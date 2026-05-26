#include <string.h>
#include "esp_err.h"
#include "esp_log.h"
#include "tinyusb.h"
#include "tusb.h"
#include "app_log.h"
#include "device_config.h"
#include "usb_hid_device.h"

static const char *TAG = "usb";
static bool s_mounted;
static bool s_suspended;

esp_err_t usb_hid_device_init(void)
{
    APP_LOGI(TAG, "initializing TinyUSB HID mode=%s", device_mode_to_string(device_config_get_mode()));
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: USB HID enumeration has not been verified on ESP32-S3");

    const tinyusb_config_t tusb_cfg = {
        .device_descriptor = NULL,
        .string_descriptor = NULL,
        .external_phy = false,
        .configuration_descriptor = NULL,
    };
    return tinyusb_driver_install(&tusb_cfg);
}

bool usb_hid_device_ready(void)
{
    return s_mounted && !s_suspended && tud_hid_ready();
}

const char *usb_hid_device_state_string(void)
{
    if (!s_mounted) {
        return "not_mounted";
    }
    return s_suspended ? "suspended" : "mounted";
}

esp_err_t usb_hid_device_send_generic_report(const hid_gamepad_report_t *report)
{
    if (!usb_hid_device_ready()) {
        return ESP_ERR_INVALID_STATE;
    }
    bool ok = tud_hid_report(GENERIC_HID_REPORT_ID, report, sizeof(*report));
    return ok ? ESP_OK : ESP_FAIL;
}

esp_err_t usb_hid_device_send_nintendo_report(const uint8_t report[NINTENDO_REPORT_SIZE])
{
    if (!usb_hid_device_ready()) {
        return ESP_ERR_INVALID_STATE;
    }
    bool ok = tud_hid_report(NINTENDO_INPUT_REPORT_ID, report + 1, NINTENDO_REPORT_SIZE - 1);
    return ok ? ESP_OK : ESP_FAIL;
}

void tud_mount_cb(void)
{
    s_mounted = true;
    s_suspended = false;
    APP_LOGI(TAG, "USB mounted");
}

void tud_umount_cb(void)
{
    s_mounted = false;
    s_suspended = false;
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
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)reqlen;
    return 0;
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                           uint8_t const *buffer, uint16_t bufsize)
{
    (void)instance;
    APP_LOGI(TAG, "HID OUT report_id=0x%02x type=%d size=%u", report_id, report_type, (unsigned)bufsize);
    if (device_config_get_mode() == NINTENDO_EXPERIMENT_MODE && report_id == NINTENDO_OUTPUT_REPORT_ID) {
        APP_LOGI(TAG, "PENDING_HARDWARE_TEST: Nintendo output/rumble report received, BLE reverse path not implemented yet");
    }
    (void)buffer;
}
