#include <string.h>
#include "esp_err.h"
#include "esp_log.h"
#include "tinyusb.h"
#include "tusb.h"
#include "app_log.h"
#include "ble_central.h"
#include "control_protocol.h"
#include "device_config.h"
#include "report_rate_stats.h"
#include "usb_descriptors.h"
#include "usb_hid_device.h"
#include "usb_switch2_vendor.h"

static const char *TAG = "usb";
static bool s_mounted;
static bool s_suspended;
static uint32_t s_hid_out_count;
static uint8_t s_hid_out_last_report_id;
static uint8_t s_hid_out_last_effective_report_id;
static uint8_t s_hid_out_last_type;
static uint16_t s_hid_out_last_len;
static uint8_t s_hid_out_last_first_byte;
static uint32_t s_hid_get_count;
static uint8_t s_hid_get_last_report_id;
static uint8_t s_hid_get_last_type;
static uint16_t s_hid_get_last_req_len;
static uint16_t s_hid_get_last_resp_len;
static char s_feature_reply[3072];
static uint16_t s_feature_reply_len;
static uint16_t s_feature_reply_offset;

#define HID_INSTANCE_GENERIC 0
#define HID_INSTANCE_NINTENDO 0
#define MANAGER_FEATURE_SET_MAGIC "Y7HID1"
#define MANAGER_FEATURE_REPLY_MAGIC "Y7HRS1"
#define MANAGER_FEATURE_MAGIC_LEN 6
#define MANAGER_FEATURE_REPLY_HEADER_LEN 11

static bool usb_hid_device_instance_ready(uint8_t instance)
{
    return s_mounted && !s_suspended && tud_hid_n_ready(instance);
}

esp_err_t usb_hid_device_init(void)
{
    APP_LOGI(TAG, "initializing TinyUSB HID mode=%s", device_mode_to_string(device_config_get_mode()));
    APP_LOGI(TAG, "USB identity VID=0x%04x PID=0x%04x product=%s",
             usb_descriptors_current_vid(),
             usb_descriptors_current_pid(),
             usb_descriptors_current_product());
    APP_LOGI(TAG, "USB path: Nintendo/Steam HID and vendor bulk init are verified on ESP32-S3");
    usb_switch2_vendor_init();

    const tinyusb_config_t tusb_cfg = {
        .device_descriptor = usb_descriptors_current_device(),
        .string_descriptor = usb_descriptors_current_strings(),
        .string_descriptor_count = usb_descriptors_current_string_count(),
        .external_phy = false,
        .configuration_descriptor = usb_descriptors_current_configuration(),
    };
    return tinyusb_driver_install(&tusb_cfg);
}

bool usb_hid_device_ready(void)
{
    return usb_hid_device_instance_ready(HID_INSTANCE_GENERIC);
}

const char *usb_hid_device_state_string(void)
{
    if (!s_mounted) {
        return "not_mounted";
    }
    return s_suspended ? "suspended" : "mounted";
}

uint32_t usb_hid_device_out_count(void)
{
    return s_hid_out_count;
}

uint8_t usb_hid_device_last_out_report_id(void)
{
    return s_hid_out_last_report_id;
}

uint8_t usb_hid_device_last_out_effective_report_id(void)
{
    return s_hid_out_last_effective_report_id;
}

uint8_t usb_hid_device_last_out_type(void)
{
    return s_hid_out_last_type;
}

uint16_t usb_hid_device_last_out_len(void)
{
    return s_hid_out_last_len;
}

uint8_t usb_hid_device_last_out_first_byte(void)
{
    return s_hid_out_last_first_byte;
}

uint32_t usb_hid_device_get_count(void)
{
    return s_hid_get_count;
}

uint8_t usb_hid_device_last_get_report_id(void)
{
    return s_hid_get_last_report_id;
}

uint8_t usb_hid_device_last_get_type(void)
{
    return s_hid_get_last_type;
}

uint16_t usb_hid_device_last_get_req_len(void)
{
    return s_hid_get_last_req_len;
}

uint16_t usb_hid_device_last_get_resp_len(void)
{
    return s_hid_get_last_resp_len;
}

esp_err_t usb_hid_device_send_generic_report(const bridge_hid_gamepad_report_t *report)
{
    if (!usb_hid_device_instance_ready(HID_INSTANCE_GENERIC)) {
        return ESP_ERR_INVALID_STATE;
    }
    bool ok = tud_hid_n_report(HID_INSTANCE_GENERIC, GENERIC_HID_REPORT_ID, report, sizeof(*report));
    report_rate_stats_record(ok);
    return ok ? ESP_OK : ESP_FAIL;
}

esp_err_t usb_hid_device_send_nintendo_report(const uint8_t report[NINTENDO_REPORT_SIZE])
{
    if (usb_switch2_vendor_hid_guard_active()) {
        return ESP_OK;
    }
    if (!usb_hid_device_instance_ready(HID_INSTANCE_NINTENDO)) {
        return ESP_ERR_INVALID_STATE;
    }
    bool ok = tud_hid_n_report(HID_INSTANCE_NINTENDO, NINTENDO_INPUT_REPORT_ID, report + 1, NINTENDO_REPORT_SIZE - 1);
    report_rate_stats_record(ok);
    return ok ? ESP_OK : ESP_FAIL;
}

static bool manager_feature_set_command(uint8_t const *payload, uint16_t payload_size)
{
    return payload && payload_size > MANAGER_FEATURE_MAGIC_LEN &&
           memcmp(payload, MANAGER_FEATURE_SET_MAGIC, MANAGER_FEATURE_MAGIC_LEN) == 0;
}

static void manager_feature_handle_set(uint8_t const *payload, uint16_t payload_size)
{
    char command[128];
    size_t command_len = payload_size > MANAGER_FEATURE_MAGIC_LEN ?
        payload_size - MANAGER_FEATURE_MAGIC_LEN : 0;

    while (command_len > 0 &&
           (payload[MANAGER_FEATURE_MAGIC_LEN + command_len - 1] == 0 ||
            payload[MANAGER_FEATURE_MAGIC_LEN + command_len - 1] == '\r' ||
            payload[MANAGER_FEATURE_MAGIC_LEN + command_len - 1] == '\n')) {
        command_len--;
    }
    if (command_len >= sizeof(command)) {
        command_len = sizeof(command) - 1;
    }

    memcpy(command, payload + MANAGER_FEATURE_MAGIC_LEN, command_len);
    command[command_len] = 0;

    control_protocol_handle_line(command, s_feature_reply, sizeof(s_feature_reply));
    s_feature_reply_len = (uint16_t)strlen(s_feature_reply);
    s_feature_reply_offset = 0;
    APP_LOGI(TAG, "manager HID feature command handled cmd=%s reply_len=%u",
             command,
             (unsigned)s_feature_reply_len);
}

static uint16_t manager_feature_get_chunk(uint8_t *buffer, uint16_t reqlen)
{
    if (!buffer || reqlen == 0) {
        return 0;
    }

    memset(buffer, 0, reqlen);
    if (reqlen < MANAGER_FEATURE_REPLY_HEADER_LEN) {
        return reqlen;
    }

    uint16_t offset = s_feature_reply_offset;
    uint16_t remaining = offset < s_feature_reply_len ? (uint16_t)(s_feature_reply_len - offset) : 0;
    uint16_t chunk = (uint16_t)(reqlen - MANAGER_FEATURE_REPLY_HEADER_LEN);
    if (chunk > remaining) {
        chunk = remaining;
    }

    memcpy(buffer, MANAGER_FEATURE_REPLY_MAGIC, MANAGER_FEATURE_MAGIC_LEN);
    buffer[6] = (uint8_t)(s_feature_reply_len & 0xff);
    buffer[7] = (uint8_t)((s_feature_reply_len >> 8) & 0xff);
    buffer[8] = (uint8_t)(offset & 0xff);
    buffer[9] = (uint8_t)((offset >> 8) & 0xff);
    buffer[10] = (uint8_t)chunk;
    if (chunk > 0) {
        memcpy(buffer + MANAGER_FEATURE_REPLY_HEADER_LEN, s_feature_reply + offset, chunk);
        s_feature_reply_offset = (uint16_t)(offset + chunk);
    }

    return reqlen;
}

void tud_mount_cb(void)
{
    s_mounted = true;
    s_suspended = false;
    usb_switch2_vendor_reset_hid_guard();
    APP_LOGI(TAG, "USB mounted");
}

void tud_umount_cb(void)
{
    s_mounted = false;
    s_suspended = false;
    usb_switch2_vendor_reset_hid_guard();
    APP_LOGI(TAG, "USB unmounted");
}

void tud_suspend_cb(bool remote_wakeup_en)
{
    (void)remote_wakeup_en;
    s_suspended = true;
    APP_LOGI(TAG, "USB suspended");
}

void tud_resume_cb(void)
{
    s_suspended = false;
    APP_LOGI(TAG, "USB resumed");
}

uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                               uint8_t *buffer, uint16_t reqlen)
{
    (void)instance;

    s_hid_get_count++;
    s_hid_get_last_report_id = report_id;
    s_hid_get_last_type = (uint8_t)report_type;
    s_hid_get_last_req_len = reqlen;

    if (!buffer || reqlen == 0) {
        s_hid_get_last_resp_len = 0;
        return 0;
    }

    uint16_t resp_len = 0;
    if (report_type == HID_REPORT_TYPE_FEATURE &&
        report_id == MANAGER_FEATURE_REPORT_ID) {
        resp_len = manager_feature_get_chunk(buffer, reqlen);
    } else if (report_type == HID_REPORT_TYPE_INPUT &&
        (report_id == 0 || report_id == NINTENDO_INPUT_REPORT_ID)) {
        uint8_t report[NINTENDO_REPORT_SIZE];
        hid_report_make_nintendo_neutral(report);

        if (report_id == NINTENDO_INPUT_REPORT_ID) {
            resp_len = (uint16_t)(NINTENDO_REPORT_SIZE - 1);
            if (resp_len > reqlen) {
                resp_len = reqlen;
            }
            memcpy(buffer, report + 1, resp_len);
        } else {
            resp_len = NINTENDO_REPORT_SIZE;
            if (resp_len > reqlen) {
                resp_len = reqlen;
            }
            memcpy(buffer, report, resp_len);
        }
    } else {
        resp_len = reqlen;
        memset(buffer, 0, resp_len);
    }

    s_hid_get_last_resp_len = resp_len;
    APP_LOGI(TAG, "HID GET instance=%u report_id=0x%02x type=%d req=%u resp=%u",
             (unsigned)instance,
             report_id,
             report_type,
             (unsigned)reqlen,
             (unsigned)resp_len);
    return resp_len;
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                           uint8_t const *buffer, uint16_t bufsize)
{
    uint8_t effective_report_id = report_id;
    uint8_t const *payload = buffer;
    uint16_t payload_size = bufsize;

    if (effective_report_id == 0 && buffer && bufsize > 0) {
        effective_report_id = buffer[0];
        payload = buffer + 1;
        payload_size = (uint16_t)(bufsize - 1);
    }

    s_hid_out_count++;
    s_hid_out_last_report_id = report_id;
    s_hid_out_last_effective_report_id = effective_report_id;
    s_hid_out_last_type = (uint8_t)report_type;
    s_hid_out_last_len = bufsize;
    s_hid_out_last_first_byte = buffer && bufsize > 0 ? buffer[0] : 0;

    if (effective_report_id == NINTENDO_OUTPUT_REPORT_ID && !app_log_debug_enabled()) {
        APP_LOGD(TAG, "HID OUT instance=%u report_id=0x%02x effective=0x%02x type=%d size=%u",
                 (unsigned)instance,
                 report_id,
                 effective_report_id,
                 report_type,
                 (unsigned)bufsize);
    } else {
        APP_LOGI(TAG, "HID OUT instance=%u report_id=0x%02x effective=0x%02x type=%d size=%u",
                 (unsigned)instance,
                 report_id,
                 effective_report_id,
                 report_type,
                 (unsigned)bufsize);
    }
    if (device_config_get_mode() == NINTENDO_EXPERIMENT_MODE &&
        report_type == HID_REPORT_TYPE_FEATURE &&
        effective_report_id == MANAGER_FEATURE_REPORT_ID &&
        manager_feature_set_command(payload, payload_size)) {
        manager_feature_handle_set(payload, payload_size);
        return;
    }

    if (device_config_get_mode() == NINTENDO_EXPERIMENT_MODE &&
        effective_report_id == NINTENDO_OUTPUT_REPORT_ID) {
        uint8_t full_report[NINTENDO_REPORT_SIZE];
        full_report[0] = effective_report_id;
        uint16_t copy_len = payload_size > (NINTENDO_REPORT_SIZE - 1) ? (NINTENDO_REPORT_SIZE - 1) : payload_size;
        memcpy(full_report + 1, payload, copy_len);
        if (copy_len < NINTENDO_REPORT_SIZE - 1) {
            memset(full_report + 1 + copy_len, 0, (NINTENDO_REPORT_SIZE - 1) - copy_len);
        }
        usb_switch2_vendor_bridge_hid_output_to_ble(full_report, (uint16_t)(copy_len + 1));
    }
}
