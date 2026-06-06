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
#define PRO2_RUMBLE_STOP_PACKETS 3
#define PRO2_RUMBLE_MAX_AMPLITUDE 640

static const char *TAG = "v5.5_rumble";
static portMUX_TYPE s_lock = portMUX_INITIALIZER_UNLOCKED;
static bool s_task_started;
static bool s_active;
static int64_t s_active_until_us;
static uint8_t s_vibration[5];
static uint8_t s_packet_id;
static uint8_t s_stop_packets_pending;
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
        bool active;
        bool send_stop = false;
        int64_t now_us = esp_timer_get_time();

        portENTER_CRITICAL(&s_lock);
        active = s_active && now_us <= s_active_until_us;
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

        if (active || send_stop) {
            uint8_t packet[33];
            build_packet(packet_id, vibration, packet);
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

            if (err == ESP_OK && active && now_us >= next_log_us) {
                next_log_us = now_us + 500000LL;
                ESP_LOGI(TAG,
                         "[DS5_RUMBLE] tick=true writes=%lu errors=%lu data=%02x%02x%02x%02x%02x",
                         (unsigned long)writes,
                         (unsigned long)errors,
                         vibration[0],
                         vibration[1],
                         vibration[2],
                         vibration[3],
                         vibration[4]);
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
