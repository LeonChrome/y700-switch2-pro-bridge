#include "pro2_rumble_backend.h"

#include <string.h>

#include "ble_central.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/portmacro.h"
#include "freertos/task.h"
#include "pro2_input_backend.h"

#define DS5_OUTPUT_REPORT_ID 0x02
#define DS5_OUTPUT_MIN_PAYLOAD 4
#define DS5_RUMBLE_ENABLE_MASK 0x03
#define DS5_IMPROVED_RUMBLE_OFFSET 38
#define DS5_IMPROVED_RUMBLE_MASK 0x04
#define PRO2_RUMBLE_TICK_MS 20
#define PRO2_RUMBLE_HOLD_MS 250
#define PRO2_RAW02_HOLD_MS 120
#define PRO2_RUMBLE_STOP_PACKETS 3
#define PRO2_RUMBLE_MAX_AMPLITUDE 640
#define RAW02_LEFT_FRAME_OFFSET 2
#define RAW02_RIGHT_FRAME_OFFSET 18
#define RAW02_FRAME_BYTES 5
#define RAW02_SOURCE_AMPLITUDE_MAX 65535

static const char *TAG = "v5.5_rumble";
static portMUX_TYPE s_lock = portMUX_INITIALIZER_UNLOCKED;
static bool s_task_started;
static bool s_active;
static int64_t s_active_until_us;
static uint8_t s_vibration[5];
static uint8_t s_packet_id;
static uint8_t s_stop_packets_pending;
static bool s_raw02_active;
static int64_t s_raw02_active_until_us;
static uint8_t s_raw02_left[5];
static uint8_t s_raw02_right[5];
static uint8_t s_last_right;
static uint8_t s_last_left;
static uint32_t s_updates;
static uint32_t s_writes;
static uint32_t s_errors;

static uint16_t scale_amplitude(uint8_t value)
{
    return (uint16_t)(((uint32_t)value * PRO2_RUMBLE_MAX_AMPLITUDE + 127u) /
                      255u);
}

static void build_vibration(uint8_t right_light, uint8_t left_heavy,
                            uint8_t out[5])
{
    uint16_t low_amp = scale_amplitude(left_heavy);
    uint16_t high_amp = scale_amplitude(right_light);
    uint64_t value = 0;

    value |= (uint64_t)0x0e1;
    value |= (uint64_t)(low_amp & 0x03ff) << 10;
    value |= (uint64_t)0x1e1 << 20;
    value |= (uint64_t)(high_amp & 0x03ff) << 30;

    for (size_t i = 0; i < 5; i++) {
        out[i] = (uint8_t)((value >> (8 * i)) & 0xff);
    }
}

static void build_zero_vibration(uint8_t out[5])
{
    build_vibration(0, 0, out);
}

static void write_motor_block(uint8_t *out, uint16_t offset, uint8_t packet_id,
                              const uint8_t vibration[5],
                              const uint8_t zero[5])
{
    out[offset] = (uint8_t)(0x50 | (packet_id & 0x0f));
    memcpy(out + offset + 1, vibration, 5);
    memcpy(out + offset + 6, zero, 5);
    memcpy(out + offset + 11, zero, 5);
}

static void build_packet(uint8_t packet_id, const uint8_t vibration[5],
                         uint8_t out[33])
{
    uint8_t zero[5];
    build_zero_vibration(zero);
    memset(out, 0, 33);
    out[0] = 0x00;
    write_motor_block(out, 1, packet_id, vibration, zero);
    write_motor_block(out, 17, packet_id, vibration, zero);
}


static bool raw02_frame_has_effect(const uint8_t *data, uint16_t len, uint16_t offset)
{
    if (!data || len < offset + RAW02_FRAME_BYTES) {
        return false;
    }
    for (uint16_t i = offset; i < offset + RAW02_FRAME_BYTES; i++) {
        if (data[i] != 0) {
            return true;
        }
    }
    return false;
}

static bool raw02_is_switch2_rumble_report(const uint8_t *data, uint16_t len)
{
    return len >= 7 && data[0] == 0x02 && (data[1] & 0xf0) == 0x50;
}

static int raw02_clamp_int(int value, int min_value, int max_value)
{
    if (value < min_value) {
        return min_value;
    }
    if (value > max_value) {
        return max_value;
    }
    return value;
}

static int raw02_map_switch_amp_to_ble(int value)
{
    int clamped = raw02_clamp_int(value, 0, RAW02_SOURCE_AMPLITUDE_MAX);
    int64_t scaled = (int64_t)clamped * 1023LL;
    int64_t mapped = (scaled + RAW02_SOURCE_AMPLITUDE_MAX / 2) /
                     RAW02_SOURCE_AMPLITUDE_MAX;
    return raw02_clamp_int((int)mapped, 0, 1023);
}

static void raw02_build_ble_vibration_data(uint16_t lf_freq,
                                           bool lf_tone,
                                           uint16_t lf_amp,
                                           uint16_t hf_freq,
                                           bool hf_tone,
                                           uint16_t hf_amp,
                                           uint8_t out[5])
{
    uint64_t value = 0;
    value |= (uint64_t)(lf_freq & 0x01ff);
    value |= (uint64_t)(lf_tone ? 1 : 0) << 9;
    value |= (uint64_t)(lf_amp & 0x03ff) << 10;
    value |= (uint64_t)(hf_freq & 0x01ff) << 20;
    value |= (uint64_t)(hf_tone ? 1 : 0) << 29;
    value |= (uint64_t)(hf_amp & 0x03ff) << 30;

    for (size_t i = 0; i < 5; i++) {
        out[i] = (uint8_t)((value >> (8 * i)) & 0xff);
    }
}

static void raw02_build_zero_ble_vibration(uint8_t out[5])
{
    raw02_build_ble_vibration_data(0x0e1, false, 0, 0x1e1, false, 0, out);
}

static void raw02_encode_ble_vibration_from_switch_frame(const uint8_t *report,
                                                         uint16_t len,
                                                         uint16_t offset,
                                                         uint8_t out[5])
{
    if (len < offset + 5) {
        raw02_build_zero_ble_vibration(out);
        return;
    }

    int b0 = report[offset];
    int b1 = report[offset + 1];
    int b2 = report[offset + 2];
    int b3 = report[offset + 3];
    int b4 = report[offset + 4];

    int high_freq = b0 | ((b1 & 0x03) << 8);
    int high_amp = ((b1 & 0xfc) << 4) | ((b2 & 0x0f) << 12);
    int low_freq = ((b2 & 0xf0) >> 4) | ((b3 & 0x3f) << 4);
    int low_amp = (b3 & 0xc0) | (b4 << 8);

    raw02_build_ble_vibration_data((uint16_t)low_freq,
                                   false,
                                   (uint16_t)raw02_map_switch_amp_to_ble(low_amp),
                                   (uint16_t)high_freq,
                                   false,
                                   (uint16_t)raw02_map_switch_amp_to_ble(high_amp),
                                   out);
}

static void raw02_build_pro2_packet(uint8_t packet_id,
                                    const uint8_t left[5],
                                    const uint8_t right[5],
                                    uint8_t out[33])
{
    uint8_t zero[5];
    raw02_build_zero_ble_vibration(zero);
    memset(out, 0, 33);
    out[0] = 0x00;
    write_motor_block(out, 1, packet_id, left, zero);
    write_motor_block(out, 17, packet_id, right, zero);
}

esp_err_t pro2_rumble_backend_send_raw02_payload(const uint8_t *payload, uint16_t len)
{
    if (!payload || len != 64) {
        ESP_LOGW(TAG,
                 "[RUMBLE_RAW02] sent=false error=invalid_payload_len len=%u",
                 (unsigned)len);
        return ESP_ERR_INVALID_ARG;
    }
    if (!raw02_is_switch2_rumble_report(payload, len)) {
        ESP_LOGW(TAG,
                 "[RUMBLE_RAW02] sent=false error=invalid_report first=%02x/%02x",
                 payload[0],
                 payload[1]);
        return ESP_ERR_INVALID_ARG;
    }

    uint8_t left[5];
    uint8_t right[5];
    bool active = raw02_frame_has_effect(payload, len, RAW02_LEFT_FRAME_OFFSET) ||
                  raw02_frame_has_effect(payload, len, RAW02_RIGHT_FRAME_OFFSET);
    if (active) {
        raw02_encode_ble_vibration_from_switch_frame(
            payload, len, RAW02_LEFT_FRAME_OFFSET, left);
        raw02_encode_ble_vibration_from_switch_frame(
            payload, len, RAW02_RIGHT_FRAME_OFFSET, right);
    } else {
        raw02_build_zero_ble_vibration(left);
        raw02_build_zero_ble_vibration(right);
    }

    uint8_t packet[33];
    uint8_t packet_id;
    portENTER_CRITICAL(&s_lock);
    packet_id = s_packet_id++ & 0x0f;
    s_active = false;
    s_active_until_us = 0;
    if (active) {
        memcpy(s_raw02_left, left, sizeof(s_raw02_left));
        memcpy(s_raw02_right, right, sizeof(s_raw02_right));
        s_raw02_active = true;
        s_raw02_active_until_us =
            esp_timer_get_time() + (int64_t)PRO2_RAW02_HOLD_MS * 1000LL;
        s_stop_packets_pending = 0;
    } else {
        s_raw02_active = false;
        s_raw02_active_until_us = 0;
        s_stop_packets_pending = PRO2_RUMBLE_STOP_PACKETS;
    }
    portEXIT_CRITICAL(&s_lock);
    raw02_build_pro2_packet(packet_id, left, right, packet);

    esp_err_t err = ble_central_send_rumble(packet, sizeof(packet));
    portENTER_CRITICAL(&s_lock);
    if (err == ESP_OK) {
        s_writes++;
    } else if (err != ESP_ERR_INVALID_STATE) {
        s_errors++;
    }
    portEXIT_CRITICAL(&s_lock);

    if (err == ESP_OK) {
        ESP_LOGI(TAG,
                 "[RUMBLE_RAW02] sent=true active=%s hold_ms=%u left=%02x%02x%02x%02x%02x right=%02x%02x%02x%02x%02x",
                 active ? "true" : "false",
                 active ? PRO2_RAW02_HOLD_MS : 0,
                 left[0], left[1], left[2], left[3], left[4],
                 right[0], right[1], right[2], right[3], right[4]);
    } else {
        ESP_LOGW(TAG, "[RUMBLE_RAW02] sent=false error=%s", esp_err_to_name(err));
    }
    return err;
}
static void rumble_task(void *arg)
{
    (void)arg;
    int64_t next_log_us = 0;

    while (true) {
        if (strcmp(pro2_input_backend_state(), "connected") != 0) {
            vTaskDelay(pdMS_TO_TICKS(PRO2_RUMBLE_TICK_MS));
            continue;
        }

        uint8_t vibration[5];
        uint8_t raw02_left[5];
        uint8_t raw02_right[5];
        bool active;
        bool raw02_active;
        bool send_stop = false;
        int64_t now_us = esp_timer_get_time();

        portENTER_CRITICAL(&s_lock);
        raw02_active = s_raw02_active && now_us <= s_raw02_active_until_us;
        memcpy(raw02_left, s_raw02_left, sizeof(raw02_left));
        memcpy(raw02_right, s_raw02_right, sizeof(raw02_right));
        if (s_raw02_active && !raw02_active) {
            s_raw02_active = false;
            s_stop_packets_pending = PRO2_RUMBLE_STOP_PACKETS;
        }
        active = !raw02_active && s_active && now_us <= s_active_until_us;
        memcpy(vibration, s_vibration, sizeof(vibration));
        if (s_active && !active) {
            s_active = false;
            s_stop_packets_pending = PRO2_RUMBLE_STOP_PACKETS;
        }
        if (!active && s_stop_packets_pending > 0) {
            s_stop_packets_pending--;
            send_stop = true;
            build_zero_vibration(vibration);
        }
        uint8_t packet_id = s_packet_id++ & 0x0f;
        portEXIT_CRITICAL(&s_lock);

        if (raw02_active || active || send_stop) {
            uint8_t packet[33];
            if (raw02_active) {
                raw02_build_pro2_packet(packet_id, raw02_left, raw02_right, packet);
            } else {
                build_packet(packet_id, vibration, packet);
            }
            esp_err_t err = ble_central_send_rumble(packet, sizeof(packet));

            portENTER_CRITICAL(&s_lock);
            if (err == ESP_OK) {
                s_writes++;
            } else if (err != ESP_ERR_INVALID_STATE) {
                s_errors++;
            }
            uint32_t writes = s_writes;
            uint32_t errors = s_errors;
            portEXIT_CRITICAL(&s_lock);

            if (err == ESP_OK && (raw02_active || active) && now_us >= next_log_us) {
                next_log_us = now_us + 500000LL;
                ESP_LOGI(TAG,
                         "[DS5_RUMBLE] tick=true source=%s writes=%lu errors=%lu data=%02x%02x%02x%02x%02x",
                         raw02_active ? "raw02" : "ordinary",
                         (unsigned long)writes,
                         (unsigned long)errors,
                         raw02_active ? raw02_left[0] : vibration[0],
                         raw02_active ? raw02_left[1] : vibration[1],
                         raw02_active ? raw02_left[2] : vibration[2],
                         raw02_active ? raw02_left[3] : vibration[3],
                         raw02_active ? raw02_left[4] : vibration[4]);
            } else if (err != ESP_OK && err != ESP_ERR_INVALID_STATE) {
                ESP_LOGW(TAG,
                         "[DS5_RUMBLE] tick=false error=%s active=%s stop=%s",
                         esp_err_to_name(err),
                         active ? "true" : "false",
                         send_stop ? "true" : "false");
            }
        }

        vTaskDelay(pdMS_TO_TICKS(PRO2_RUMBLE_TICK_MS));
    }
}

void pro2_rumble_backend_init(void)
{
    build_zero_vibration(s_vibration);
    if (!s_task_started) {
        BaseType_t created = xTaskCreate(rumble_task,
                                         "ds5_rumble",
                                         4096,
                                         NULL,
                                         4,
                                         NULL);
        ESP_ERROR_CHECK(created == pdPASS ? ESP_OK : ESP_FAIL);
        s_task_started = true;
    }
    ESP_LOGI(TAG,
             "[DS5_RUMBLE] initialized=true mode=ordinary_compat max_amp=%u hold_ms=%u",
             PRO2_RUMBLE_MAX_AMPLITUDE,
             PRO2_RUMBLE_HOLD_MS);
}

bool pro2_rumble_backend_handle_dualsense_output(
    uint8_t report_id,
    const uint8_t *buffer,
    uint16_t len)
{
    if (!buffer || len == 0 ||
        (report_id != 0 && report_id != DS5_OUTPUT_REPORT_ID)) {
        return false;
    }

    const uint8_t *payload = buffer;
    uint16_t payload_len = len;
    if (report_id == 0) {
        if (buffer[0] != DS5_OUTPUT_REPORT_ID) {
            return false;
        }
        payload = buffer + 1;
        payload_len--;
    }
    if (payload_len < DS5_OUTPUT_MIN_PAYLOAD) {
        ESP_LOGW(TAG,
                 "[DS5_RUMBLE] handled=false error=short_payload len=%u",
                 (unsigned)payload_len);
        return false;
    }

    bool enabled = (payload[0] & DS5_RUMBLE_ENABLE_MASK) != 0;
    if (payload_len > DS5_IMPROVED_RUMBLE_OFFSET) {
        enabled = enabled ||
                  (payload[DS5_IMPROVED_RUMBLE_OFFSET] &
                   DS5_IMPROVED_RUMBLE_MASK) != 0;
    }

    uint8_t right_light = payload[2];
    uint8_t left_heavy = payload[3];
    bool active = enabled && (right_light != 0 || left_heavy != 0);
    bool changed;
    uint32_t updates;
    int64_t now_us = esp_timer_get_time();

    portENTER_CRITICAL(&s_lock);
    s_raw02_active = false;
    s_raw02_active_until_us = 0;
    changed = right_light != s_last_right ||
              left_heavy != s_last_left ||
              active != s_active;
    s_last_right = right_light;
    s_last_left = left_heavy;
    s_updates++;
    updates = s_updates;
    if (active) {
        build_vibration(right_light, left_heavy, s_vibration);
        s_active_until_us =
            now_us + (int64_t)PRO2_RUMBLE_HOLD_MS * 1000LL;
        s_active = true;
        s_stop_packets_pending = 0;
    } else {
        s_active = false;
        s_active_until_us = 0;
        s_stop_packets_pending = PRO2_RUMBLE_STOP_PACKETS;
        build_zero_vibration(s_vibration);
    }
    portEXIT_CRITICAL(&s_lock);

    if (changed || updates == 1) {
        ESP_LOGI(TAG,
                 "[DS5_RUMBLE] handled=true enabled=%s active=%s right_light=%u left_heavy=%u updates=%lu",
                 enabled ? "true" : "false",
                 active ? "true" : "false",
                 right_light,
                 left_heavy,
                 (unsigned long)updates);
    }
    return true;
}
