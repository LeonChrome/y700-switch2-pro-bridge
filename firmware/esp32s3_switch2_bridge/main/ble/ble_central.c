#include <ctype.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "app_log.h"
#include "esp_err.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "host/ble_gap.h"
#include "host/ble_gatt.h"
#include "host/ble_hs.h"
#include "host/ble_hs_adv.h"
#include "host/ble_uuid.h"
#include "host/util/util.h"
#include "nimble/ble.h"
#include "nimble/nimble_port.h"
#include "nimble/nimble_port_freertos.h"
#include "os/os_mbuf.h"
#include "services/gap/ble_svc_gap.h"
#include "device_config.h"
#include "switch2_gatt.h"
#include "switch2_state.h"
#include "ble_central.h"

static const char *TAG = "ble";

#define BLE_SCAN_DURATION_MS 15000
#define BLE_CONNECT_TIMEOUT_MS 30000
#define BLE_SCAN_HEX_PREVIEW_BYTES 16
#define BLE_SCAN_CACHE_MAX 64
#define BLE_MAX_SERVICES 32
#define BLE_MAX_CHARS 96
#define BLE_NOTIFY_BUF_MAX 128
#define SWITCH2_INIT_COMMAND_COUNT 15
#define NINTENDO_COMPANY_ID 0x0553
#define BLE_FAST_CONN_ITVL_MIN 6
#define BLE_FAST_CONN_ITVL_MAX 6
#define BLE_FAST_CONN_LATENCY 0
#define BLE_FAST_CONN_SUPERVISION_TIMEOUT 100
#define BLE_FAST_SCAN_ITVL 16
#define BLE_FAST_SCAN_WINDOW 16

typedef enum {
    BLE_STATE_IDLE = 0,
    BLE_STATE_SCANNING,
    BLE_STATE_CONNECTING,
    BLE_STATE_CONNECTED
} ble_state_t;

typedef struct {
    bool used;
    uint32_t index;
    ble_addr_t addr;
    char name[32];
    bool candidate;
    int8_t rssi;
} scanned_device_t;

typedef struct {
    uint16_t start_handle;
    uint16_t end_handle;
    char uuid[BLE_UUID_STR_LEN];
} discovered_service_t;

typedef struct {
    uint16_t def_handle;
    uint16_t val_handle;
    uint16_t end_handle;
    uint16_t cccd_handle;
    uint8_t properties;
    int service_index;
    char uuid[BLE_UUID_STR_LEN];
    bool notify_target;
    bool ack_target;
    bool post_init_notify_target;
    bool command_target;
    bool rumble_target;
    bool subscribed;
} discovered_char_t;

typedef enum {
    BLE_SUBSCRIBE_PHASE_ACK = 0,
    BLE_SUBSCRIBE_PHASE_POST_INIT
} ble_subscribe_phase_t;

typedef struct {
    const char *name;
    const uint8_t *data;
    uint16_t len;
} switch2_init_command_t;

static ble_state_t s_state = BLE_STATE_IDLE;
static bool s_host_ready;
static bool s_connected;
static uint8_t s_own_addr_type;
static uint16_t s_conn_handle;
static uint32_t s_scan_seen_count;
static scanned_device_t s_scan_cache[BLE_SCAN_CACHE_MAX];

static discovered_service_t s_services[BLE_MAX_SERVICES];
static discovered_char_t s_chars[BLE_MAX_CHARS];
static size_t s_service_count;
static size_t s_char_count;
static size_t s_disc_service_index;
static int s_desc_chr_index;
static int s_subscribe_index;
static ble_subscribe_phase_t s_subscribe_phase;
static uint16_t s_cmd_val_handle;
static bool s_cmd_write_no_rsp;
static bool s_init_started;
static bool s_init_done;
static size_t s_init_index;
static uint16_t s_rumble_val_handle;
static bool s_rumble_write_no_rsp;
static bool s_auto_scan_connect;
static bool s_auto_scan_target_valid;
static ble_addr_t s_auto_scan_target;
static char s_auto_scan_label[96];
static bool s_pending_connect_valid;
static ble_addr_t s_pending_connect_addr;
static ble_central_conn_metrics_t s_conn_metrics;
static bool s_imu_debug_enabled;
static uint32_t s_imu_debug_every = 32;
static uint32_t s_imu_debug_seen;

static const struct ble_gap_conn_params s_fast_connect_params = {
    .scan_itvl = BLE_FAST_SCAN_ITVL,
    .scan_window = BLE_FAST_SCAN_WINDOW,
    .itvl_min = BLE_FAST_CONN_ITVL_MIN,
    .itvl_max = BLE_FAST_CONN_ITVL_MAX,
    .latency = BLE_FAST_CONN_LATENCY,
    .supervision_timeout = BLE_FAST_CONN_SUPERVISION_TIMEOUT,
    .min_ce_len = 0,
    .max_ce_len = 0,
};

static const struct ble_gap_upd_params s_fast_update_params = {
    .itvl_min = BLE_FAST_CONN_ITVL_MIN,
    .itvl_max = BLE_FAST_CONN_ITVL_MAX,
    .latency = BLE_FAST_CONN_LATENCY,
    .supervision_timeout = BLE_FAST_CONN_SUPERVISION_TIMEOUT,
    .min_ce_len = 0,
    .max_ce_len = 0,
};

static const uint8_t s_init_cmd_0[] = {0x03, 0x91, 0x01, 0x0d, 0x00, 0x08, 0x00, 0x00, 0x01, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff};
static const uint8_t s_init_cmd_1[] = {0x07, 0x91, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_2[] = {0x16, 0x91, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_3[] = {0x15, 0x91, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_4[] = {0x0c, 0x91, 0x01, 0x02, 0x00, 0x04, 0x00, 0x00, 0xff, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_5[] = {0x11, 0x91, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_6[] = {0x0a, 0x91, 0x01, 0x08, 0x00, 0x14, 0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x35, 0x00, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_7[] = {0x0c, 0x91, 0x01, 0x04, 0x00, 0x04, 0x00, 0x00, 0xff, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_8[] = {0x03, 0x91, 0x01, 0x0a, 0x00, 0x04, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_9[] = {0x10, 0x91, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_10[] = {0x01, 0x91, 0x01, 0x0c, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_11[] = {0x01, 0x91, 0x01, 0x01, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_12[] = {0x09, 0x91, 0x01, 0x07, 0x00, 0x08, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
static const uint8_t s_init_cmd_13[] = {0x02, 0x91, 0x01, 0x04, 0x00, 0x08, 0x00, 0x00, 0x09, 0x7e, 0x00, 0x00, 0xa8, 0x30, 0x01, 0x00};
static const uint8_t s_init_cmd_14[] = {0x02, 0x91, 0x01, 0x04, 0x00, 0x08, 0x00, 0x00, 0x09, 0x7e, 0x00, 0x00, 0xe8, 0x30, 0x01, 0x00};

static const switch2_init_command_t s_init_commands[SWITCH2_INIT_COMMAND_COUNT] = {
    {"INIT", s_init_cmd_0, sizeof(s_init_cmd_0)},
    {"CMD_07", s_init_cmd_1, sizeof(s_init_cmd_1)},
    {"CMD_16", s_init_cmd_2, sizeof(s_init_cmd_2)},
    {"CMD_15_03", s_init_cmd_3, sizeof(s_init_cmd_3)},
    {"FEATSEL_SET_MASK", s_init_cmd_4, sizeof(s_init_cmd_4)},
    {"CMD_11", s_init_cmd_5, sizeof(s_init_cmd_5)},
    {"VIBRATE_CFG", s_init_cmd_6, sizeof(s_init_cmd_6)},
    {"FEATSEL_ENABLE", s_init_cmd_7, sizeof(s_init_cmd_7)},
    {"SELECT_REPORT", s_init_cmd_8, sizeof(s_init_cmd_8)},
    {"FW_INFO_GET", s_init_cmd_9, sizeof(s_init_cmd_9)},
    {"CMD_01_0C", s_init_cmd_10, sizeof(s_init_cmd_10)},
    {"RUMBLE_ENABLE", s_init_cmd_11, sizeof(s_init_cmd_11)},
    {"SET_PLAYER_LED", s_init_cmd_12, sizeof(s_init_cmd_12)},
    {"CALIB_LEFT", s_init_cmd_13, sizeof(s_init_cmd_13)},
    {"CALIB_RIGHT", s_init_cmd_14, sizeof(s_init_cmd_14)},
};

static int ble_gap_event(struct ble_gap_event *event, void *arg);
static esp_err_t ble_central_connect_addr(const ble_addr_t *target, const char *label);

static uint32_t conn_interval_us(uint16_t interval_units)
{
    return (uint32_t)interval_units * 1250u;
}

static void clear_conn_metrics(void)
{
    memset(&s_conn_metrics, 0, sizeof(s_conn_metrics));
    s_conn_metrics.last_update_start_rc = -1;
    s_conn_metrics.last_update_event_status = -1;
}

static void update_conn_metrics_from_desc(uint16_t conn_handle, const char *reason)
{
    struct ble_gap_conn_desc desc;
    int rc = ble_gap_conn_find(conn_handle, &desc);
    if (rc != 0) {
        APP_LOGW(TAG, "BLE conn metrics read failed reason=%s handle=%u rc=%d",
                 reason,
                 conn_handle,
                 rc);
        return;
    }

    s_conn_metrics.connected = true;
    s_conn_metrics.conn_handle = desc.conn_handle;
    s_conn_metrics.interval_units = desc.conn_itvl;
    s_conn_metrics.latency = desc.conn_latency;
    s_conn_metrics.supervision_timeout = desc.supervision_timeout;
    APP_LOGI(TAG, "BLE conn params reason=%s handle=%u interval_units=%u interval_us=%lu latency=%u supervision=%u",
             reason,
             desc.conn_handle,
             desc.conn_itvl,
             (unsigned long)conn_interval_us(desc.conn_itvl),
             desc.conn_latency,
             desc.supervision_timeout);
}

static esp_err_t request_fast_conn_params_internal(const char *reason)
{
    if (!s_connected) {
        return ESP_ERR_INVALID_STATE;
    }

    s_conn_metrics.update_request_count++;
    int rc = ble_gap_update_params(s_conn_handle, &s_fast_update_params);
    s_conn_metrics.last_update_start_rc = rc;
    APP_LOGI(TAG, "BLE fast conn request reason=%s handle=%u interval=%u..%u units (%lu..%lu us) latency=%u supervision=%u rc=%d",
             reason,
             s_conn_handle,
             s_fast_update_params.itvl_min,
             s_fast_update_params.itvl_max,
             (unsigned long)conn_interval_us(s_fast_update_params.itvl_min),
             (unsigned long)conn_interval_us(s_fast_update_params.itvl_max),
             s_fast_update_params.latency,
             s_fast_update_params.supervision_timeout,
             rc);
    return rc == 0 ? ESP_OK : ESP_FAIL;
}

static void format_addr(const ble_addr_t *addr, char *out, size_t out_len)
{
    snprintf(out, out_len, "%02x:%02x:%02x:%02x:%02x:%02x/%u",
             addr->val[5], addr->val[4], addr->val[3],
             addr->val[2], addr->val[1], addr->val[0],
             addr->type);
}

static bool contains_ci(const char *haystack, const char *needle)
{
    if (!haystack || !needle || !needle[0]) {
        return false;
    }

    size_t needle_len = strlen(needle);
    for (const char *p = haystack; *p; p++) {
        size_t i = 0;
        while (i < needle_len && p[i] &&
               tolower((unsigned char)p[i]) == tolower((unsigned char)needle[i])) {
            i++;
        }
        if (i == needle_len) {
            return true;
        }
    }
    return false;
}

static void uuid_to_lower_string(const ble_uuid_t *uuid, char *out, size_t out_len)
{
    if (!uuid || !out || out_len == 0) {
        return;
    }

    ble_uuid_to_str(uuid, out);
    out[out_len - 1] = 0;
    for (char *p = out; *p; p++) {
        *p = (char)tolower((unsigned char)*p);
    }
}

static void copy_adv_name(const struct ble_hs_adv_fields *fields, char *out, size_t out_len)
{
    if (!fields->name || fields->name_len == 0 || out_len == 0) {
        if (out_len > 0) {
            out[0] = 0;
        }
        return;
    }

    size_t n = fields->name_len;
    if (n >= out_len) {
        n = out_len - 1;
    }
    memcpy(out, fields->name, n);
    out[n] = 0;
}

static void format_hex_preview(const uint8_t *data, uint8_t len, char *out, size_t out_len)
{
    if (!data || len == 0 || out_len == 0) {
        if (out_len > 0) {
            out[0] = 0;
        }
        return;
    }

    size_t used = 0;
    uint8_t n = len > BLE_SCAN_HEX_PREVIEW_BYTES ? BLE_SCAN_HEX_PREVIEW_BYTES : len;
    for (uint8_t i = 0; i < n && used + 3 < out_len; i++) {
        int written = snprintf(out + used, out_len - used, "%02x%s", data[i], i + 1 < n ? " " : "");
        if (written <= 0) {
            break;
        }
        used += (size_t)written;
    }
    if (len > n && used + 5 < out_len) {
        snprintf(out + used, out_len - used, " ...");
    }
}

static void format_hex_full(const uint8_t *data, uint16_t len, char *out, size_t out_len)
{
    if (!data || len == 0 || out_len == 0) {
        if (out_len > 0) {
            out[0] = 0;
        }
        return;
    }

    size_t used = 0;
    for (uint16_t i = 0; i < len && used + 3 < out_len; i++) {
        int written = snprintf(out + used, out_len - used, "%02x%s", data[i], i + 1 < len ? " " : "");
        if (written <= 0) {
            break;
        }
        used += (size_t)written;
    }
    out[used < out_len ? used : out_len - 1] = 0;
}

static int16_t read_i16_le(const uint8_t *data)
{
    return (int16_t)((uint16_t)data[0] | ((uint16_t)data[1] << 8));
}

static void format_imu_i16_candidate(const uint8_t *data, char *out, size_t out_len)
{
    if (!data || !out || out_len == 0) {
        return;
    }

    size_t used = 0;
    for (uint8_t sample = 0; sample < 3 && used + 1 < out_len; sample++) {
        int16_t values[6];
        for (uint8_t axis = 0; axis < 6; axis++) {
            values[axis] = read_i16_le(data + sample * 12 + axis * 2);
        }
        int written = snprintf(out + used,
                               out_len - used,
                               "%ss%u=[%d,%d,%d,%d,%d,%d]",
                               sample > 0 ? " " : "",
                               (unsigned)sample,
                               values[0],
                               values[1],
                               values[2],
                               values[3],
                               values[4],
                               values[5]);
        if (written <= 0) {
            break;
        }
        used += (size_t)written;
    }
    out[used < out_len ? used : out_len - 1] = 0;
}

static void format_imu_i16_sample(const uint8_t *data, char *out, size_t out_len)
{
    if (!data || !out || out_len == 0) {
        return;
    }

    int16_t values[6];
    for (uint8_t axis = 0; axis < 6; axis++) {
        values[axis] = read_i16_le(data + axis * 2);
    }
    snprintf(out,
             out_len,
             "accel=[%d,%d,%d] gyro=[%d,%d,%d]",
             values[0],
             values[1],
             values[2],
             values[3],
             values[4],
             values[5]);
}

static bool name_looks_like_switch_controller(const char *name)
{
    return contains_ci(name, "switch") ||
           contains_ci(name, "nintendo") ||
           contains_ci(name, "pro controller") ||
           contains_ci(name, "pro2");
}

static bool adv_event_connectable(uint8_t event_type)
{
    return event_type == BLE_HCI_ADV_RPT_EVTYPE_ADV_IND ||
           event_type == BLE_HCI_ADV_RPT_EVTYPE_DIR_IND;
}

static bool adv_has_company_id(const struct ble_hs_adv_fields *fields, uint16_t company_id)
{
    return fields->mfg_data &&
           fields->mfg_data_len >= 2 &&
           ((uint16_t)fields->mfg_data[0] | ((uint16_t)fields->mfg_data[1] << 8)) == company_id;
}

static bool is_notify_uuid(const char *uuid)
{
    /*
     * The Pro2 appears to expose one active input stream at a time: subscribing
     * the compact C0F8 stream after FD2 switches live input away from the full
     * 63-byte FD2 reports, which removes the stable motion sample at 48..59.
     */
    return uuid &&
           strcmp(uuid, SWITCH2_NOTIFY_FD2_UUID) == 0;
}

static bool is_input_uuid(const char *uuid)
{
    return uuid &&
           (strcmp(uuid, SWITCH2_NOTIFY_FD2_UUID) == 0 ||
            strcmp(uuid, SWITCH2_NOTIFY_LEGACY_UUID) == 0);
}

static bool is_ack_uuid(const char *uuid)
{
    return uuid && strcmp(uuid, SWITCH2_ACK_UUID) == 0;
}

static bool is_command_uuid(const char *uuid)
{
    return uuid && strcmp(uuid, SWITCH2_CMD_UUID) == 0;
}

static bool is_rumble_uuid(const char *uuid)
{
    return uuid && strcmp(uuid, SWITCH2_RUMBLE_CC48_UUID) == 0;
}

static void clear_gatt_cache(void)
{
    memset(s_services, 0, sizeof(s_services));
    memset(s_chars, 0, sizeof(s_chars));
    s_service_count = 0;
    s_char_count = 0;
    s_disc_service_index = 0;
    s_desc_chr_index = -1;
    s_subscribe_index = -1;
    s_subscribe_phase = BLE_SUBSCRIBE_PHASE_ACK;
    s_cmd_val_handle = 0;
    s_cmd_write_no_rsp = false;
    s_init_started = false;
    s_init_done = false;
    s_init_index = 0;
    s_rumble_val_handle = 0;
    s_rumble_write_no_rsp = false;
}

static void remember_scanned_device(uint32_t index,
                                    const ble_addr_t *addr,
                                    const char *name,
                                    bool candidate,
                                    int8_t rssi)
{
    scanned_device_t *slot = &s_scan_cache[(index - 1) % BLE_SCAN_CACHE_MAX];
    memset(slot, 0, sizeof(*slot));
    slot->used = true;
    slot->index = index;
    slot->addr = *addr;
    slot->candidate = candidate;
    slot->rssi = rssi;
    snprintf(slot->name, sizeof(slot->name), "%s", name ? name : "");
}

static void log_adv_uuids(const struct ble_hs_adv_fields *fields)
{
    for (uint8_t i = 0; i < fields->num_uuids16; i++) {
        APP_LOGI(TAG, "BLE scan uuid16=0x%04x complete=%u",
                 fields->uuids16[i].value,
                 fields->uuids16_is_complete);
    }

    for (uint8_t i = 0; i < fields->num_uuids32; i++) {
        APP_LOGI(TAG, "BLE scan uuid32=0x%08lx complete=%u",
                 (unsigned long)fields->uuids32[i].value,
                 fields->uuids32_is_complete);
    }

    for (uint8_t i = 0; i < fields->num_uuids128; i++) {
        char uuid[BLE_UUID_STR_LEN];
        uuid_to_lower_string(&fields->uuids128[i].u, uuid, sizeof(uuid));
        APP_LOGI(TAG, "BLE scan uuid128=%s complete=%u", uuid, fields->uuids128_is_complete);
    }
}

static void log_adv_report(const struct ble_gap_disc_desc *disc)
{
    struct ble_hs_adv_fields fields;
    int rc = ble_hs_adv_parse_fields(&fields, disc->data, disc->length_data);
    if (rc != 0) {
        char addr[32];
        format_addr(&disc->addr, addr, sizeof(addr));
        APP_LOGW(TAG, "BLE scan parse failed addr=%s rssi=%d event=%u len=%u rc=%d",
                 addr, disc->rssi, disc->event_type, disc->length_data, rc);
        return;
    }

    char addr[32];
    char name[32];
    char mfg[64];
    format_addr(&disc->addr, addr, sizeof(addr));
    copy_adv_name(&fields, name, sizeof(name));
    format_hex_preview(fields.mfg_data, fields.mfg_data_len, mfg, sizeof(mfg));

    bool nintendo_mfg = adv_has_company_id(&fields, NINTENDO_COMPANY_ID);
    bool candidate = adv_event_connectable(disc->event_type) &&
                     (name_looks_like_switch_controller(name) || nintendo_mfg);

    uint32_t index = ++s_scan_seen_count;
    remember_scanned_device(index, &disc->addr, name, candidate, disc->rssi);
    if (s_auto_scan_connect && candidate && !s_auto_scan_target_valid) {
        s_auto_scan_target = disc->addr;
        snprintf(s_auto_scan_label, sizeof(s_auto_scan_label), "#%lu %s %s",
                 (unsigned long)index,
                 addr,
                 name[0] ? name : "<unnamed>");
        s_auto_scan_target_valid = true;
        s_auto_scan_connect = false;
        APP_LOGI(TAG, "BLE autoconnect candidate selected target=%s", s_auto_scan_label);
        int cancel_rc = ble_gap_disc_cancel();
        if (cancel_rc != 0) {
            APP_LOGW(TAG, "BLE autoconnect scan cancel rc=%d", cancel_rc);
        }
    }

    APP_LOGI(TAG,
             "BLE scan device #%lu addr=%s rssi=%d event=%u name=\"%s\" candidate=%s nintendo_mfg=%s appearance=%s%u mfg_len=%u mfg=\"%s\"",
             (unsigned long)index,
             addr,
             disc->rssi,
             disc->event_type,
             name[0] ? name : "<none>",
             candidate ? "yes" : "no",
             nintendo_mfg ? "yes" : "no",
             fields.appearance_is_present ? "" : "<none>/",
             fields.appearance_is_present ? fields.appearance : 0,
             fields.mfg_data_len,
             mfg[0] ? mfg : "<none>");

    log_adv_uuids(&fields);
}

static bool parse_addr_text(const char *text, ble_addr_t *out, bool *out_type_set)
{
    unsigned int b[6] = {0};
    unsigned int type = 0;
    int read = sscanf(text, "%2x:%2x:%2x:%2x:%2x:%2x/%u",
                      &b[0], &b[1], &b[2], &b[3], &b[4], &b[5], &type);
    if (read < 6) {
        return false;
    }
    for (int i = 0; i < 6; i++) {
        if (b[i] > 0xff) {
            return false;
        }
    }
    if (read == 7 && type > 1) {
        return false;
    }

    out->val[0] = (uint8_t)b[5];
    out->val[1] = (uint8_t)b[4];
    out->val[2] = (uint8_t)b[3];
    out->val[3] = (uint8_t)b[2];
    out->val[4] = (uint8_t)b[1];
    out->val[5] = (uint8_t)b[0];
    out->type = read == 7 ? (uint8_t)type : BLE_ADDR_PUBLIC;
    if (out_type_set) {
        *out_type_set = read == 7;
    }
    return true;
}

static bool same_addr_value(const ble_addr_t *a, const ble_addr_t *b)
{
    return memcmp(a->val, b->val, sizeof(a->val)) == 0;
}

static bool newest_cached_device(bool candidate_only, ble_addr_t *out, char *label, size_t label_len)
{
    const scanned_device_t *best = NULL;
    for (size_t i = 0; i < BLE_SCAN_CACHE_MAX; i++) {
        const scanned_device_t *dev = &s_scan_cache[i];
        if (!dev->used || (candidate_only && !dev->candidate)) {
            continue;
        }
        if (!best || dev->index > best->index) {
            best = dev;
        }
    }

    if (!best) {
        return false;
    }

    *out = best->addr;
    char addr[32];
    format_addr(&best->addr, addr, sizeof(addr));
    snprintf(label, label_len, "#%lu %s %s",
             (unsigned long)best->index,
             addr,
             best->name[0] ? best->name : "<unnamed>");
    return true;
}

static bool find_cached_by_index(uint32_t index, ble_addr_t *out, char *label, size_t label_len)
{
    for (size_t i = 0; i < BLE_SCAN_CACHE_MAX; i++) {
        const scanned_device_t *dev = &s_scan_cache[i];
        if (dev->used && dev->index == index) {
            *out = dev->addr;
            char addr[32];
            format_addr(&dev->addr, addr, sizeof(addr));
            snprintf(label, label_len, "#%lu %s %s",
                     (unsigned long)dev->index,
                     addr,
                     dev->name[0] ? dev->name : "<unnamed>");
            return true;
        }
    }
    return false;
}

static bool find_cached_by_name(const char *name, ble_addr_t *out, char *label, size_t label_len)
{
    const scanned_device_t *best = NULL;
    for (size_t i = 0; i < BLE_SCAN_CACHE_MAX; i++) {
        const scanned_device_t *dev = &s_scan_cache[i];
        if (!dev->used || !contains_ci(dev->name, name)) {
            continue;
        }
        if (!best || dev->index > best->index) {
            best = dev;
        }
    }

    if (!best) {
        return false;
    }

    *out = best->addr;
    char addr[32];
    format_addr(&best->addr, addr, sizeof(addr));
    snprintf(label, label_len, "#%lu %s %s",
             (unsigned long)best->index,
             addr,
             best->name[0] ? best->name : "<unnamed>");
    return true;
}

static bool fix_addr_type_from_cache(ble_addr_t *addr)
{
    for (size_t i = 0; i < BLE_SCAN_CACHE_MAX; i++) {
        const scanned_device_t *dev = &s_scan_cache[i];
        if (dev->used && same_addr_value(&dev->addr, addr)) {
            addr->type = dev->addr.type;
            return true;
        }
    }
    return false;
}

static bool select_connect_target(const char *target, ble_addr_t *out, char *label, size_t label_len)
{
    if (!target || !target[0] || strcmp(target, "last") == 0) {
        if (newest_cached_device(true, out, label, label_len) ||
            newest_cached_device(false, out, label, label_len)) {
            return true;
        }
        return false;
    }

    if (target[0] == '#') {
        char *end = NULL;
        unsigned long index = strtoul(target + 1, &end, 10);
        if (end && *end == 0 && index > 0 && find_cached_by_index((uint32_t)index, out, label, label_len)) {
            return true;
        }
    }

    if (isdigit((unsigned char)target[0])) {
        char *end = NULL;
        unsigned long index = strtoul(target, &end, 10);
        if (end && *end == 0 && index > 0 && find_cached_by_index((uint32_t)index, out, label, label_len)) {
            return true;
        }
    }

    if (strchr(target, ':')) {
        bool type_set = false;
        if (parse_addr_text(target, out, &type_set)) {
            if (!type_set) {
                (void)fix_addr_type_from_cache(out);
            }
            format_addr(out, label, label_len);
            return true;
        }
    }

    return find_cached_by_name(target, out, label, label_len);
}

static void json_escape_small(const char *in, char *out, size_t out_len)
{
    if (!out || out_len == 0) {
        return;
    }
    size_t used = 0;
    for (const char *p = in ? in : ""; *p && used + 1 < out_len; p++) {
        unsigned char ch = (unsigned char)*p;
        if ((ch == '"' || ch == '\\') && used + 2 < out_len) {
            out[used++] = '\\';
            out[used++] = (char)ch;
        } else if (ch >= 0x20) {
            out[used++] = (char)ch;
        }
    }
    out[used] = 0;
}

void ble_central_format_scan_results_json(char *out, size_t out_len)
{
    if (!out || out_len == 0) {
        return;
    }

    size_t used = (size_t)snprintf(out, out_len,
                                   "\"scan_seen\":%lu,\"devices\":[",
                                   (unsigned long)s_scan_seen_count);
    if (used >= out_len) {
        out[out_len - 1] = 0;
        return;
    }

    uint32_t below_index = UINT32_MAX;
    bool first = true;
    for (int item = 0; item < 12; item++) {
        const scanned_device_t *best = NULL;
        for (size_t i = 0; i < BLE_SCAN_CACHE_MAX; i++) {
            const scanned_device_t *dev = &s_scan_cache[i];
            if (!dev->used || dev->index >= below_index) {
                continue;
            }
            if (!best ||
                (dev->candidate && !best->candidate) ||
                (dev->candidate == best->candidate && dev->index > best->index)) {
                best = dev;
            }
        }
        if (!best) {
            break;
        }

        below_index = best->index;
        char addr[32];
        char name[64];
        format_addr(&best->addr, addr, sizeof(addr));
        json_escape_small(best->name, name, sizeof(name));

        int written = snprintf(out + used,
                               out_len - used,
                               "%s{\"index\":%lu,\"target\":\"#%lu\",\"addr\":\"%s\",\"name\":\"%s\",\"rssi\":%d,\"candidate\":%s}",
                               first ? "" : ",",
                               (unsigned long)best->index,
                               (unsigned long)best->index,
                               addr,
                               name[0] ? name : "<unnamed>",
                               best->rssi,
                               best->candidate ? "true" : "false");
        if (written < 0 || (size_t)written >= out_len - used) {
            out[out_len - 1] = 0;
            return;
        }
        used += (size_t)written;
        first = false;
    }

    snprintf(out + used, out_len - used, "]");
}

static discovered_char_t *find_chr_by_value_handle(uint16_t value_handle)
{
    for (size_t i = 0; i < s_char_count; i++) {
        if (s_chars[i].val_handle == value_handle) {
            return &s_chars[i];
        }
    }
    return NULL;
}

static void finalize_character_end_handles(void)
{
    for (size_t i = 0; i < s_char_count; i++) {
        uint16_t end_handle = s_services[s_chars[i].service_index].end_handle;
        for (size_t j = 0; j < s_char_count; j++) {
            if (i == j || s_chars[j].service_index != s_chars[i].service_index) {
                continue;
            }
            if (s_chars[j].def_handle > s_chars[i].def_handle &&
                s_chars[j].def_handle - 1 < end_handle) {
                end_handle = s_chars[j].def_handle - 1;
            }
        }
        s_chars[i].end_handle = end_handle;
    }
}

static int gatt_subscribe_write_cb(uint16_t conn_handle,
                                   const struct ble_gatt_error *error,
                                   struct ble_gatt_attr *attr,
                                   void *arg);

static void start_post_init_subscriptions(void);
static void send_current_init_command(void);
static void subscribe_next_target(void);

static volatile bool s_subscribe_task_pending;

static void subscribe_next_task(void *arg)
{
    (void)arg;
    vTaskDelay(pdMS_TO_TICKS(1));
    s_subscribe_task_pending = false;
    subscribe_next_target();
    vTaskDelete(NULL);
}

static void schedule_subscribe_next(void)
{
    if (s_subscribe_task_pending) {
        return;
    }

    s_subscribe_task_pending = true;
    BaseType_t ok = xTaskCreate(subscribe_next_task,
                                "ble_sub_next",
                                4096,
                                NULL,
                                5,
                                NULL);
    if (ok != pdPASS) {
        s_subscribe_task_pending = false;
        APP_LOGE(TAG, "BLE subscribe scheduler failed");
    }
}

static bool subscribe_target_for_phase(const discovered_char_t *chr)
{
    if (s_subscribe_phase == BLE_SUBSCRIBE_PHASE_ACK) {
        return chr->ack_target;
    }
    return chr->post_init_notify_target;
}

static void subscribe_next_target(void)
{
    uint8_t enable_notify[2] = {0x01, 0x00};

    for (int i = s_subscribe_index + 1; i < (int)s_char_count; i++) {
        discovered_char_t *chr = &s_chars[i];
        if (!subscribe_target_for_phase(chr)) {
            continue;
        }

        uint16_t cccd_handle = chr->cccd_handle ? chr->cccd_handle : chr->val_handle + 1;
        s_subscribe_index = i;
        int rc = ble_gattc_write_flat(s_conn_handle,
                                      cccd_handle,
                                      enable_notify,
                                      sizeof(enable_notify),
                                      gatt_subscribe_write_cb,
                                      NULL);
        if (rc != 0) {
            APP_LOGW(TAG, "BLE subscribe start failed uuid=%s cccd=0x%04x rc=%d",
                     chr->uuid,
                     cccd_handle,
                     rc);
            continue;
        }

        APP_LOGI(TAG, "BLE subscribe start uuid=%s value=0x%04x cccd=0x%04x",
                 chr->uuid,
                 chr->val_handle,
                 cccd_handle);
        return;
    }

    if (s_subscribe_phase == BLE_SUBSCRIBE_PHASE_ACK) {
        APP_LOGI(TAG, "BLE ACK notification setup complete cmd_handle=0x%04x", s_cmd_val_handle);
        if (s_cmd_val_handle == 0) {
            APP_LOGW(TAG, "BLE init skipped; command characteristic missing");
            start_post_init_subscriptions();
            return;
        }
        s_init_started = true;
        s_init_done = false;
        s_init_index = 0;
        APP_LOGI(TAG, "BLE init starting commands=%u", (unsigned)SWITCH2_INIT_COMMAND_COUNT);
        send_current_init_command();
        return;
    }

    unsigned subscribed = 0;
    for (size_t i = 0; i < s_char_count; i++) {
        if (s_chars[i].subscribed) {
            subscribed++;
        }
    }
    APP_LOGI(TAG, "BLE GATT ready subscribed=%u rumble_value_handle=0x%04x",
             subscribed,
             s_rumble_val_handle);
}

static void start_post_init_subscriptions(void)
{
    s_subscribe_phase = BLE_SUBSCRIBE_PHASE_POST_INIT;
    s_subscribe_index = -1;
    APP_LOGI(TAG, "BLE post-init notification setup start");
    schedule_subscribe_next();
}

static void send_current_init_command(void)
{
    if (!s_connected || s_cmd_val_handle == 0) {
        APP_LOGW(TAG, "BLE init stopped; connected=%s cmd_handle=0x%04x",
                 s_connected ? "yes" : "no",
                 s_cmd_val_handle);
        return;
    }

    if (s_init_index >= SWITCH2_INIT_COMMAND_COUNT) {
        s_init_done = true;
        APP_LOGI(TAG, "BLE init complete; enabling input notifications");
        start_post_init_subscriptions();
        return;
    }

    const switch2_init_command_t *cmd = &s_init_commands[s_init_index];
    int rc;
    if (s_cmd_write_no_rsp) {
        rc = ble_gattc_write_no_rsp_flat(s_conn_handle, s_cmd_val_handle, cmd->data, cmd->len);
    } else {
        rc = ble_gattc_write_flat(s_conn_handle, s_cmd_val_handle, cmd->data, cmd->len, NULL, NULL);
    }

    if (rc != 0) {
        APP_LOGW(TAG, "BLE init send failed index=%u name=%s handle=0x%04x len=%u no_rsp=%s rc=%d",
                 (unsigned)s_init_index,
                 cmd->name,
                 s_cmd_val_handle,
                 (unsigned)cmd->len,
                 s_cmd_write_no_rsp ? "yes" : "no",
                 rc);
        return;
    }

    APP_LOGI(TAG, "BLE init send index=%u/%u name=%s len=%u no_rsp=%s",
             (unsigned)s_init_index,
             (unsigned)SWITCH2_INIT_COMMAND_COUNT,
             cmd->name,
             (unsigned)cmd->len,
             s_cmd_write_no_rsp ? "yes" : "no");
}

static void advance_ble_init_from_ack(const uint8_t *data, uint16_t len)
{
    if (!s_init_started || s_init_done) {
        return;
    }

    APP_LOGI(TAG, "BLE init ACK index=%u len=%u first=0x%02x",
             (unsigned)s_init_index,
             (unsigned)len,
             len > 0 ? data[0] : 0);
    s_init_index++;
    send_current_init_command();
}

static int gatt_subscribe_write_cb(uint16_t conn_handle,
                                   const struct ble_gatt_error *error,
                                   struct ble_gatt_attr *attr,
                                   void *arg)
{
    (void)conn_handle;
    (void)attr;
    (void)arg;

    if (s_subscribe_index >= 0 && s_subscribe_index < (int)s_char_count) {
        discovered_char_t *chr = &s_chars[s_subscribe_index];
        if (error->status == 0) {
            chr->subscribed = true;
            APP_LOGI(TAG, "BLE subscribe ok uuid=%s", chr->uuid);
        } else {
            APP_LOGW(TAG, "BLE subscribe failed uuid=%s status=%d", chr->uuid, error->status);
        }
    }

    schedule_subscribe_next();
    return 0;
}

static void start_next_descriptor_discovery(void);

static int gatt_dsc_cb(uint16_t conn_handle,
                       const struct ble_gatt_error *error,
                       uint16_t chr_val_handle,
                       const struct ble_gatt_dsc *dsc,
                       void *arg)
{
    (void)conn_handle;
    (void)chr_val_handle;
    (void)arg;

    if (s_desc_chr_index < 0 || s_desc_chr_index >= (int)s_char_count) {
        return 0;
    }

    discovered_char_t *chr = &s_chars[s_desc_chr_index];
    if (error->status == 0) {
        char uuid[BLE_UUID_STR_LEN];
        uuid_to_lower_string(&dsc->uuid.u, uuid, sizeof(uuid));
        APP_LOGI(TAG, "BLE descriptor chr=%s handle=0x%04x uuid=%s",
                 chr->uuid,
                 dsc->handle,
                 uuid);
        if (dsc->uuid.u.type == BLE_UUID_TYPE_16 &&
            ble_uuid_u16(&dsc->uuid.u) == BLE_GATT_DSC_CLT_CFG_UUID16) {
            chr->cccd_handle = dsc->handle;
        }
        return 0;
    }

    if (error->status == BLE_HS_EDONE) {
        start_next_descriptor_discovery();
        return 0;
    }

    APP_LOGW(TAG, "BLE descriptor discovery failed chr=%s status=%d", chr->uuid, error->status);
    start_next_descriptor_discovery();
    return 0;
}

static void start_next_descriptor_discovery(void)
{
    for (int i = s_desc_chr_index + 1; i < (int)s_char_count; i++) {
        discovered_char_t *chr = &s_chars[i];
        if (!chr->notify_target || chr->end_handle <= chr->val_handle) {
            continue;
        }

        s_desc_chr_index = i;
        int rc = ble_gattc_disc_all_dscs(s_conn_handle,
                                         chr->val_handle,
                                         chr->end_handle,
                                         gatt_dsc_cb,
                                         NULL);
        if (rc != 0) {
            APP_LOGW(TAG, "BLE descriptor discovery start failed chr=%s rc=%d", chr->uuid, rc);
            continue;
        }

        APP_LOGI(TAG, "BLE descriptor discovery start chr=%s range=0x%04x..0x%04x",
                 chr->uuid,
                 chr->val_handle,
                 chr->end_handle);
        return;
    }

    s_subscribe_index = -1;
    schedule_subscribe_next();
}

static void start_next_characteristic_discovery(void);

static int gatt_chr_cb(uint16_t conn_handle,
                       const struct ble_gatt_error *error,
                       const struct ble_gatt_chr *chr,
                       void *arg)
{
    (void)conn_handle;
    (void)arg;

    if (error->status == 0) {
        if (s_char_count >= BLE_MAX_CHARS) {
            APP_LOGW(TAG, "BLE characteristic cache full; skipping value=0x%04x", chr->val_handle);
            return 0;
        }

        discovered_char_t *out = &s_chars[s_char_count++];
        memset(out, 0, sizeof(*out));
        out->def_handle = chr->def_handle;
        out->val_handle = chr->val_handle;
        out->properties = chr->properties;
        out->service_index = (int)s_disc_service_index;
        uuid_to_lower_string(&chr->uuid.u, out->uuid, sizeof(out->uuid));
        out->ack_target = is_ack_uuid(out->uuid);
        out->post_init_notify_target = is_notify_uuid(out->uuid);
        out->notify_target = out->ack_target || out->post_init_notify_target;
        out->command_target = is_command_uuid(out->uuid);
        if (out->command_target) {
            s_cmd_val_handle = out->val_handle;
            s_cmd_write_no_rsp = (out->properties & BLE_GATT_CHR_F_WRITE_NO_RSP) != 0;
        }
        out->rumble_target = is_rumble_uuid(out->uuid);
        if (out->rumble_target) {
            s_rumble_val_handle = out->val_handle;
            s_rumble_write_no_rsp = (out->properties & BLE_GATT_CHR_F_WRITE_NO_RSP) != 0;
        }

        APP_LOGI(TAG, "BLE char svc=%u def=0x%04x value=0x%04x props=0x%02x uuid=%s target=%s",
                 (unsigned)s_disc_service_index,
                 out->def_handle,
                 out->val_handle,
                 out->properties,
                 out->uuid,
                 out->ack_target ? "ack" :
                 (out->post_init_notify_target ? "notify" :
                 (out->command_target ? "cmd" :
                 (out->rumble_target ? "rumble" : "no"))));
        return 0;
    }

    if (error->status == BLE_HS_EDONE) {
        s_disc_service_index++;
        start_next_characteristic_discovery();
        return 0;
    }

    APP_LOGW(TAG, "BLE characteristic discovery failed status=%d", error->status);
    s_disc_service_index++;
    start_next_characteristic_discovery();
    return 0;
}

static void start_next_characteristic_discovery(void)
{
    while (s_disc_service_index < s_service_count) {
        discovered_service_t *svc = &s_services[s_disc_service_index];
        if (svc->end_handle <= svc->start_handle) {
            s_disc_service_index++;
            continue;
        }

        int rc = ble_gattc_disc_all_chrs(s_conn_handle,
                                         svc->start_handle,
                                         svc->end_handle,
                                         gatt_chr_cb,
                                         NULL);
        if (rc != 0) {
            APP_LOGW(TAG, "BLE characteristic discovery start failed svc=%s rc=%d", svc->uuid, rc);
            s_disc_service_index++;
            continue;
        }

        APP_LOGI(TAG, "BLE characteristic discovery start svc=%s range=0x%04x..0x%04x",
                 svc->uuid,
                 svc->start_handle,
                 svc->end_handle);
        return;
    }

    finalize_character_end_handles();
    APP_LOGI(TAG, "BLE characteristic discovery complete chars=%u", (unsigned)s_char_count);
    s_desc_chr_index = -1;
    start_next_descriptor_discovery();
}

static int gatt_svc_cb(uint16_t conn_handle,
                       const struct ble_gatt_error *error,
                       const struct ble_gatt_svc *service,
                       void *arg)
{
    (void)conn_handle;
    (void)arg;

    if (error->status == 0) {
        if (s_service_count >= BLE_MAX_SERVICES) {
            APP_LOGW(TAG, "BLE service cache full; skipping start=0x%04x", service->start_handle);
            return 0;
        }

        discovered_service_t *out = &s_services[s_service_count++];
        out->start_handle = service->start_handle;
        out->end_handle = service->end_handle;
        uuid_to_lower_string(&service->uuid.u, out->uuid, sizeof(out->uuid));
        APP_LOGI(TAG, "BLE service start=0x%04x end=0x%04x uuid=%s",
                 out->start_handle,
                 out->end_handle,
                 out->uuid);
        return 0;
    }

    if (error->status == BLE_HS_EDONE) {
        APP_LOGI(TAG, "BLE service discovery complete services=%u", (unsigned)s_service_count);
        s_disc_service_index = 0;
        start_next_characteristic_discovery();
        return 0;
    }

    APP_LOGW(TAG, "BLE service discovery failed status=%d", error->status);
    return 0;
}

static void start_service_discovery(uint16_t conn_handle)
{
    int rc = ble_gattc_disc_all_svcs(conn_handle, gatt_svc_cb, NULL);
    if (rc != 0) {
        APP_LOGE(TAG, "BLE service discovery start failed rc=%d", rc);
    } else {
        APP_LOGI(TAG, "BLE service discovery start");
    }
}

static int gatt_mtu_cb(uint16_t conn_handle,
                       const struct ble_gatt_error *error,
                       uint16_t mtu,
                       void *arg)
{
    (void)conn_handle;
    (void)arg;
    if (error->status == 0) {
        APP_LOGI(TAG, "BLE MTU exchange ok mtu=%u", mtu);
    } else {
        APP_LOGW(TAG, "BLE MTU exchange failed status=%d", error->status);
    }
    start_service_discovery(conn_handle);
    return 0;
}

static void start_gatt_discovery(uint16_t conn_handle)
{
    clear_gatt_cache();

    int rc = ble_gattc_exchange_mtu(conn_handle, gatt_mtu_cb, NULL);
    if (rc != 0) {
        APP_LOGW(TAG, "BLE MTU exchange start failed rc=%d", rc);
        start_service_discovery(conn_handle);
    } else {
        APP_LOGI(TAG, "BLE MTU exchange start");
    }
}

static void handle_notify_rx(const struct ble_gap_event *event)
{
    uint16_t len = OS_MBUF_PKTLEN(event->notify_rx.om);
    uint16_t copy_len = len > BLE_NOTIFY_BUF_MAX ? BLE_NOTIFY_BUF_MAX : len;
    uint8_t data[BLE_NOTIFY_BUF_MAX];

    int rc = os_mbuf_copydata(event->notify_rx.om, 0, copy_len, data);
    if (rc != 0) {
        APP_LOGW(TAG, "BLE notify copy failed attr=0x%04x len=%u rc=%d",
                 event->notify_rx.attr_handle,
                 len,
                 rc);
        return;
    }

    discovered_char_t *chr = find_chr_by_value_handle(event->notify_rx.attr_handle);
    const char *uuid = chr ? chr->uuid : "<unknown>";

    if (s_imu_debug_enabled && chr && is_input_uuid(uuid) && copy_len >= 51) {
        s_imu_debug_seen++;
        uint32_t every = s_imu_debug_every == 0 ? 32 : s_imu_debug_every;
        if (every <= 1 || (s_imu_debug_seen % every) == 0) {
            static char imu_hex[128];
            static char motion_hex[128];
            static char imu_i16[192];
            static char motion_i16[192];
            static char fd2_motion_hex[48];
            static char fd2_motion_i16[96];
            static char raw_hex[384];
            format_hex_full(data + 15, 36, imu_hex, sizeof(imu_hex));
            format_imu_i16_candidate(data + 15, imu_i16, sizeof(imu_i16));
            if (copy_len >= 55 && data[14] == 0x28) {
                format_hex_full(data + 19, 36, motion_hex, sizeof(motion_hex));
                format_imu_i16_candidate(data + 19, motion_i16, sizeof(motion_i16));
            } else {
                snprintf(motion_hex, sizeof(motion_hex), "n/a");
                snprintf(motion_i16, sizeof(motion_i16), "n/a");
            }
            if (strcmp(uuid, SWITCH2_NOTIFY_FD2_UUID) == 0 && copy_len >= 60) {
                format_hex_full(data + 48, 12, fd2_motion_hex, sizeof(fd2_motion_hex));
                format_imu_i16_sample(data + 48, fd2_motion_i16, sizeof(fd2_motion_i16));
            } else {
                snprintf(fd2_motion_hex, sizeof(fd2_motion_hex), "n/a");
                snprintf(fd2_motion_i16, sizeof(fd2_motion_i16), "n/a");
            }
            format_hex_full(data, copy_len, raw_hex, sizeof(raw_hex));
            APP_LOGI(TAG,
                     "IMU_DEBUG notify=%lu uuid=%s len=%u sub_len=0x%02x imu15_50=\"%s\" imu_i16=\"%s\" motion19_54=\"%s\" motion_i16=\"%s\" fd2_motion48_59=\"%s\" fd2_i16=\"%s\" raw=\"%s\"",
                     (unsigned long)s_imu_debug_seen,
                     uuid,
                     (unsigned)copy_len,
                     copy_len > 14 ? data[14] : 0,
                     imu_hex,
                     imu_i16,
                     motion_hex,
                     motion_i16,
                     fd2_motion_hex,
                     fd2_motion_i16,
                     raw_hex);
        }
    }

    if (chr && chr->ack_target) {
        advance_ble_init_from_ack(data, copy_len);
        return;
    }

    if (chr && !is_input_uuid(uuid)) {
        if (app_log_debug_enabled()) {
            APP_LOGD(TAG, "BLE notify side-channel uuid=%s attr=0x%04x len=%u",
                     uuid,
                     event->notify_rx.attr_handle,
                     len);
        }
        return;
    }

    switch2_state_t state;
    if (!switch2_state_get_live(&state, NULL, NULL)) {
        switch2_state_reset(&state);
    }
    esp_err_t err = switch2_gatt_handle_notify(uuid, data, copy_len, &state);
    if (err == ESP_OK) {
        switch2_state_store_live(&state);
        uint32_t updates = 0;
        (void)switch2_state_get_live(NULL, &updates, NULL);
        if ((updates & 0x1f) == 1) {
            APP_LOGI(TAG, "BLE notify parsed uuid=%s len=%u updates=%lu buttons=0x%08lx",
                     uuid,
                     len,
                     (unsigned long)updates,
                     (unsigned long)state.buttons);
        }
    } else if (app_log_debug_enabled()) {
        APP_LOGD(TAG, "BLE notify ignored uuid=%s attr=0x%04x len=%u err=%d",
                 uuid,
                 event->notify_rx.attr_handle,
                 len,
                 (int)err);
    }
}

static int ble_gap_event(struct ble_gap_event *event, void *arg)
{
    (void)arg;

    switch (event->type) {
    case BLE_GAP_EVENT_DISC:
        log_adv_report(&event->disc);
        return 0;

    case BLE_GAP_EVENT_DISC_COMPLETE:
        APP_LOGI(TAG, "BLE scan complete reason=%d seen=%lu",
                 event->disc_complete.reason,
                 (unsigned long)s_scan_seen_count);
        if (!s_connected && s_state == BLE_STATE_SCANNING) {
            s_state = BLE_STATE_IDLE;
        }
        if (s_auto_scan_target_valid) {
            ble_addr_t target = s_auto_scan_target;
            char label[sizeof(s_auto_scan_label)];
            snprintf(label, sizeof(label), "%s", s_auto_scan_label);
            s_auto_scan_target_valid = false;
            s_auto_scan_connect = false;
            APP_LOGI(TAG, "BLE autoconnect scan complete; connecting target=%s", label);
            (void)ble_central_connect_addr(&target, label);
        } else {
            s_auto_scan_connect = false;
        }
        return 0;

    case BLE_GAP_EVENT_CONNECT:
        if (event->connect.status == 0) {
            s_connected = true;
            s_conn_handle = event->connect.conn_handle;
            s_state = BLE_STATE_CONNECTED;
            APP_LOGI(TAG, "BLE connected handle=%u", event->connect.conn_handle);
            update_conn_metrics_from_desc(event->connect.conn_handle, "connect");
            (void)request_fast_conn_params_internal("connect");
            if (s_pending_connect_valid) {
                char addr[32];
                format_addr(&s_pending_connect_addr, addr, sizeof(addr));
                (void)device_config_save_ble_target(addr);
                s_pending_connect_valid = false;
            }
            start_gatt_discovery(event->connect.conn_handle);
        } else {
            s_connected = false;
            s_conn_handle = 0;
            s_state = BLE_STATE_IDLE;
            s_pending_connect_valid = false;
            clear_conn_metrics();
            APP_LOGW(TAG, "BLE connect failed status=%d", event->connect.status);
        }
        return 0;

    case BLE_GAP_EVENT_CONN_UPDATE:
        s_conn_metrics.last_update_event_status = event->conn_update.status;
        APP_LOGI(TAG, "BLE conn update event handle=%u status=%d",
                 event->conn_update.conn_handle,
                 event->conn_update.status);
        update_conn_metrics_from_desc(event->conn_update.conn_handle, "update");
        return 0;

    case BLE_GAP_EVENT_CONN_UPDATE_REQ:
        APP_LOGI(TAG, "BLE conn update req handle=%u peer_interval=%u..%u latency=%u supervision=%u; requesting fast interval=%u..%u latency=%u supervision=%u",
                 event->conn_update_req.conn_handle,
                 event->conn_update_req.peer_params ? event->conn_update_req.peer_params->itvl_min : 0,
                 event->conn_update_req.peer_params ? event->conn_update_req.peer_params->itvl_max : 0,
                 event->conn_update_req.peer_params ? event->conn_update_req.peer_params->latency : 0,
                 event->conn_update_req.peer_params ? event->conn_update_req.peer_params->supervision_timeout : 0,
                 s_fast_update_params.itvl_min,
                 s_fast_update_params.itvl_max,
                 s_fast_update_params.latency,
                 s_fast_update_params.supervision_timeout);
        if (event->conn_update_req.self_params) {
            *event->conn_update_req.self_params = s_fast_update_params;
        }
        return 0;

    case BLE_GAP_EVENT_DISCONNECT:
        s_connected = false;
        s_conn_handle = 0;
        s_state = BLE_STATE_IDLE;
        clear_gatt_cache();
        switch2_state_clear_live();
        clear_conn_metrics();
        APP_LOGI(TAG, "BLE disconnected reason=%d", event->disconnect.reason);
        return 0;

    case BLE_GAP_EVENT_NOTIFY_RX:
        handle_notify_rx(event);
        return 0;

    case BLE_GAP_EVENT_MTU:
        APP_LOGI(TAG, "BLE MTU event conn=%u mtu=%u",
                 event->mtu.conn_handle,
                 event->mtu.value);
        return 0;

    default:
        return 0;
    }
}

static void ble_on_reset(int reason)
{
    s_host_ready = false;
    s_connected = false;
    s_state = BLE_STATE_IDLE;
    s_auto_scan_connect = false;
    s_auto_scan_target_valid = false;
    s_pending_connect_valid = false;
    clear_gatt_cache();
    switch2_state_clear_live();
    clear_conn_metrics();
    APP_LOGW(TAG, "NimBLE reset reason=%d", reason);
}

static void ble_on_sync(void)
{
    int rc = ble_hs_util_ensure_addr(0);
    if (rc != 0) {
        APP_LOGE(TAG, "NimBLE cannot ensure identity addr rc=%d", rc);
        return;
    }

    rc = ble_hs_id_infer_auto(0, &s_own_addr_type);
    if (rc != 0) {
        APP_LOGE(TAG, "NimBLE cannot infer own addr type rc=%d", rc);
        return;
    }

    s_host_ready = true;
    APP_LOGI(TAG, "NimBLE host ready own_addr_type=%u", s_own_addr_type);
}

static void ble_host_task(void *param)
{
    (void)param;
    APP_LOGI(TAG, "NimBLE host task started");
    nimble_port_run();
    nimble_port_freertos_deinit();
}

void ble_central_init(void)
{
    s_state = BLE_STATE_IDLE;
    s_host_ready = false;
    s_connected = false;
    s_conn_handle = 0;
    s_scan_seen_count = 0;
    s_auto_scan_connect = false;
    s_auto_scan_target_valid = false;
    s_pending_connect_valid = false;
    memset(s_scan_cache, 0, sizeof(s_scan_cache));
    clear_gatt_cache();
    clear_conn_metrics();

    esp_err_t err = nimble_port_init();
    if (err != ESP_OK) {
        APP_LOGE(TAG, "NimBLE init failed err=%d", (int)err);
        return;
    }

    ble_hs_cfg.reset_cb = ble_on_reset;
    ble_hs_cfg.sync_cb = ble_on_sync;

    int rc = ble_svc_gap_device_name_set("esp32s3-switch2-bridge");
    if (rc != 0) {
        APP_LOGW(TAG, "NimBLE device-name set failed rc=%d", rc);
    }

    nimble_port_freertos_init(ble_host_task);
    APP_LOGI(TAG, "BLE Central initialized; use 'ble scan' then 'ble connect last|#n|addr|name'");
}

static esp_err_t ble_central_connect_addr(const ble_addr_t *target, const char *label)
{
    if (!target) {
        return ESP_ERR_INVALID_ARG;
    }

    if (ble_gap_disc_active()) {
        int cancel_rc = ble_gap_disc_cancel();
        if (cancel_rc != 0) {
            APP_LOGW(TAG, "BLE scan cancel before connect rc=%d", cancel_rc);
        }
    }

    if (ble_gap_conn_active()) {
        int cancel_rc = ble_gap_conn_cancel();
        if (cancel_rc != 0) {
            APP_LOGW(TAG, "BLE pending connect cancel rc=%d", cancel_rc);
        }
    }

    if (s_connected) {
        int term_rc = ble_gap_terminate(s_conn_handle, BLE_ERR_REM_USER_CONN_TERM);
        if (term_rc != 0) {
            APP_LOGW(TAG, "BLE existing connection terminate rc=%d", term_rc);
        }
    }

    clear_gatt_cache();
    switch2_state_clear_live();
    s_pending_connect_addr = *target;
    s_pending_connect_valid = true;
    s_state = BLE_STATE_CONNECTING;
    APP_LOGI(TAG, "BLE connect start target=%s timeout_ms=%d",
             label ? label : "<addr>",
             BLE_CONNECT_TIMEOUT_MS);

    int rc = ble_gap_connect(s_own_addr_type, target, BLE_CONNECT_TIMEOUT_MS, &s_fast_connect_params, ble_gap_event, NULL);
    if (rc != 0) {
        s_state = BLE_STATE_IDLE;
        s_pending_connect_valid = false;
        APP_LOGE(TAG, "BLE connect start failed rc=%d", rc);
        return ESP_FAIL;
    }

    return ESP_OK;
}

static esp_err_t ble_central_start_scan_internal(bool auto_connect)
{
    if (!s_host_ready) {
        APP_LOGW(TAG, "BLE scan requested before NimBLE sync");
        return ESP_ERR_INVALID_STATE;
    }

    if (ble_gap_disc_active()) {
        int cancel_rc = ble_gap_disc_cancel();
        if (cancel_rc != 0) {
            APP_LOGW(TAG, "BLE previous scan cancel rc=%d", cancel_rc);
        }
    }

    struct ble_gap_disc_params disc_params = {0};
    disc_params.filter_duplicates = 1;
    disc_params.passive = 0;
    disc_params.itvl = 0;
    disc_params.window = 0;
    disc_params.filter_policy = 0;
    disc_params.limited = 0;

    s_scan_seen_count = 0;
    memset(s_scan_cache, 0, sizeof(s_scan_cache));
    s_auto_scan_connect = auto_connect;
    s_auto_scan_target_valid = false;
    s_state = BLE_STATE_SCANNING;
    APP_LOGI(TAG, "BLE active scan started duration_ms=%d auto_connect=%s",
             BLE_SCAN_DURATION_MS,
             auto_connect ? "yes" : "no");

    int rc = ble_gap_disc(s_own_addr_type, BLE_SCAN_DURATION_MS, &disc_params, ble_gap_event, NULL);
    if (rc != 0) {
        s_state = BLE_STATE_IDLE;
        s_auto_scan_connect = false;
        s_auto_scan_target_valid = false;
        APP_LOGE(TAG, "BLE scan start failed rc=%d", rc);
        return ESP_FAIL;
    }

    return ESP_OK;
}

esp_err_t ble_central_start_scan(void)
{
    return ble_central_start_scan_internal(false);
}

esp_err_t ble_central_connect(const char *address_or_name)
{
    if (!s_host_ready) {
        APP_LOGW(TAG, "BLE connect requested before NimBLE sync");
        return ESP_ERR_INVALID_STATE;
    }

    ble_addr_t target;
    char label[96];
    if (!select_connect_target(address_or_name, &target, label, sizeof(label))) {
        APP_LOGW(TAG, "BLE connect target not found target=%s", address_or_name ? address_or_name : "<last>");
        return ESP_ERR_NOT_FOUND;
    }

    return ble_central_connect_addr(&target, label);
}

esp_err_t ble_central_reconnect_saved_or_scan(void)
{
    const char *saved_target = device_config_get_ble_target();
    if (saved_target && saved_target[0]) {
        APP_LOGI(TAG, "BLE reconnect using saved target=%s", saved_target);
        return ble_central_connect(saved_target);
    }

    APP_LOGI(TAG, "BLE reconnect has no saved target; scanning for first candidate");
    return ble_central_start_scan_internal(true);
}

void ble_central_disconnect(void)
{
    s_auto_scan_connect = false;
    s_auto_scan_target_valid = false;
    s_pending_connect_valid = false;
    if (ble_gap_disc_active()) {
        (void)ble_gap_disc_cancel();
    }
    if (ble_gap_conn_active()) {
        (void)ble_gap_conn_cancel();
    }
    if (s_connected) {
        (void)ble_gap_terminate(s_conn_handle, BLE_ERR_REM_USER_CONN_TERM);
    }
    s_state = BLE_STATE_IDLE;
    APP_LOGI(TAG, "BLE disconnect requested");
}

esp_err_t ble_central_request_fast_params(void)
{
    return request_fast_conn_params_internal("control");
}

void ble_central_get_conn_metrics(ble_central_conn_metrics_t *out_metrics)
{
    if (!out_metrics) {
        return;
    }
    *out_metrics = s_conn_metrics;
}

void ble_central_set_imu_debug(bool enabled, uint32_t every)
{
    s_imu_debug_enabled = enabled;
    s_imu_debug_every = every == 0 ? 32 : every;
    s_imu_debug_seen = 0;
    APP_LOGI(TAG, "IMU debug %s every=%lu",
             enabled ? "enabled" : "disabled",
             (unsigned long)s_imu_debug_every);
}

bool ble_central_get_imu_debug(uint32_t *out_every)
{
    if (out_every) {
        *out_every = s_imu_debug_every;
    }
    return s_imu_debug_enabled;
}

esp_err_t ble_central_send_command(const uint8_t *data, uint16_t len)
{
    if (!data || len == 0) {
        return ESP_ERR_INVALID_ARG;
    }
    if (!s_connected || s_cmd_val_handle == 0) {
        return ESP_ERR_INVALID_STATE;
    }

    int rc = s_cmd_write_no_rsp ?
        ble_gattc_write_no_rsp_flat(s_conn_handle, s_cmd_val_handle, data, len) :
        ble_gattc_write_flat(s_conn_handle, s_cmd_val_handle, data, len, NULL, NULL);
    if (rc != 0) {
        APP_LOGW(TAG, "BLE command write start failed handle=0x%04x len=%u no_rsp=%s rc=%d",
                 s_cmd_val_handle,
                 (unsigned)len,
                 s_cmd_write_no_rsp ? "yes" : "no",
                 rc);
        return ESP_FAIL;
    }
    APP_LOGD(TAG, "BLE command write handle=0x%04x len=%u no_rsp=%s",
             s_cmd_val_handle,
             (unsigned)len,
             s_cmd_write_no_rsp ? "yes" : "no");
    return ESP_OK;
}

esp_err_t ble_central_send_rumble(const uint8_t *data, uint16_t len)
{
    if (!data || len == 0) {
        return ESP_ERR_INVALID_ARG;
    }
    if (!s_connected || s_rumble_val_handle == 0) {
        return ESP_ERR_INVALID_STATE;
    }

    int rc = s_rumble_write_no_rsp ?
        ble_gattc_write_no_rsp_flat(s_conn_handle, s_rumble_val_handle, data, len) :
        ble_gattc_write_flat(s_conn_handle, s_rumble_val_handle, data, len, NULL, NULL);
    if (rc != 0) {
        APP_LOGW(TAG, "BLE rumble write start failed handle=0x%04x len=%u no_rsp=%s rc=%d",
                 s_rumble_val_handle,
                 (unsigned)len,
                 s_rumble_write_no_rsp ? "yes" : "no",
                 rc);
        return ESP_FAIL;
    }
    APP_LOGD(TAG, "BLE rumble write handle=0x%04x len=%u no_rsp=%s",
             s_rumble_val_handle,
             (unsigned)len,
             s_rumble_write_no_rsp ? "yes" : "no");
    return ESP_OK;
}

const char *ble_central_state_string(void)
{
    switch (s_state) {
    case BLE_STATE_IDLE:
        return "idle";
    case BLE_STATE_SCANNING:
        return "scanning";
    case BLE_STATE_CONNECTING:
        return "connecting";
    case BLE_STATE_CONNECTED:
        return "connected";
    default:
        return "unknown";
    }
}
