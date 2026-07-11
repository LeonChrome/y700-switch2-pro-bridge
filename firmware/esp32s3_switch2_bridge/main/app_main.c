#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <unistd.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "nvs_flash.h"
#include "app_log.h"
#include "ble_central.h"
#include "ble_dual_probe.h"
#include "control_protocol.h"
#include "device_config.h"
#include "hid_report.h"
#include "internal_gamepad_state.h"
#include "report_mapper.h"
#include "report_rate_stats.h"
#include "switch2_state.h"
#include "usb_hid_device.h"
#include "usb_xinput_device.h"
#include "xbox_paddle_mapper.h"

static const char *TAG = "app";

#define BLE_LIVE_STALE_US 1000000LL
#define AUTO_A_TOGGLE_US 500000LL
#define CONTROL_LINE_MAX 192

static bool usb_current_mode_ready(void)
{
    if (device_config_get_mode() == XINPUT_EXPERIMENT_MODE) {
        return usb_xinput_device_ready();
    }
    if (device_config_get_mode() == DUAL_PRO2_EXPERIMENT_MODE) {
        return usb_hid_device_dual_ready();
    }
    return usb_hid_device_ready();
}

static esp_err_t send_usb_state_report(const switch2_state_t *state)
{
    internal_gamepad_state_t internal;
    switch2_state_to_internal(state, &internal);

    if (device_config_get_mode() == DUAL_PRO2_EXPERIMENT_MODE) {
        uint8_t slot_a[SWITCH_LEGACY_REPORT_SIZE];
        uint8_t slot_b[SWITCH_LEGACY_REPORT_SIZE];
        report_mapper_internal_to_switch_legacy_report(&internal, slot_a);
        report_mapper_internal_to_switch_legacy_report(&internal, slot_b);
        return usb_hid_device_send_dual_switch_legacy_reports(slot_a, slot_b);
    }

    if (device_config_get_mode() == NINTENDO_EXPERIMENT_MODE) {
        uint8_t nintendo_report[NINTENDO_REPORT_SIZE];
        report_mapper_internal_to_nintendo_report(&internal, nintendo_report);
        return usb_hid_device_send_nintendo_report(nintendo_report);
    }

    if (device_config_get_mode() == XINPUT_EXPERIMENT_MODE) {
        return usb_xinput_device_send_report(&internal);
    }

    bridge_hid_gamepad_report_t report;
    report_mapper_internal_to_generic_report(&internal, &report);
    return usb_hid_device_send_generic_report(&report);
}

static void control_task(void *arg)
{
    (void)arg;
    char line[CONTROL_LINE_MAX];
    uint8_t rx[64];
    size_t line_len = 0;
    bool overflow = false;
    static char reply[16384];

    while (true) {
        int rx_len = read(STDIN_FILENO, rx, sizeof(rx));
        if (rx_len <= 0) {
            vTaskDelay(pdMS_TO_TICKS(20));
            continue;
        }

        for (int i = 0; i < rx_len; i++) {
            uint8_t ch = rx[i];
            if (ch == '\r' || ch == '\n') {
                if (overflow) {
                    APP_LOGW(TAG, "serial control line too long; discarded");
                    printf("{\"ok\":false,\"cmd\":\"serial\",\"error\":\"command line too long\"}\n");
                    overflow = false;
                    line_len = 0;
                    continue;
                }
                if (line_len > 0) {
                    line[line_len] = 0;
                    control_protocol_handle_line(line, reply, sizeof(reply));
                    line_len = 0;
                }
                continue;
            }

            if (overflow) {
                continue;
            }
            if (line_len + 1 >= sizeof(line)) {
                overflow = true;
                line_len = 0;
                continue;
            }

            line[line_len++] = (char)ch;
        }
    }
}

static uint32_t report_delay_ms(void)
{
    uint16_t rate_hz = device_config_get_report_rate_hz();
    uint32_t delay_ms = (1000u + rate_hz - 1u) / rate_hz;
    return delay_ms == 0 ? 1 : delay_ms;
}

static bool make_test_state(switch2_state_t *state, bool *pressed, int64_t now_us)
{
    static int64_t next_toggle_us;

    hid_test_mode_t test_mode = device_config_get_hid_test_mode();
    switch2_state_reset(state);

    if (test_mode == HID_TEST_AUTO_A && now_us >= next_toggle_us) {
        *pressed = !*pressed;
        next_toggle_us = now_us + AUTO_A_TOGGLE_US;
    }

    bool a_pressed = test_mode == HID_TEST_A_HELD ||
                     (test_mode == HID_TEST_AUTO_A && *pressed);
    if (a_pressed) {
        switch2_state_set_button(state, SWITCH2_BUTTON_A, true);
    }
    return a_pressed;
}

static void hid_report_task(void *arg)
{
    (void)arg;
    bool pressed = false;
    int64_t next_log_us = 0;

    while (true) {
        int64_t now_us = esp_timer_get_time();
        uint32_t delay_ms = report_delay_ms();
        switch2_state_t state;
        const char *source = "test";
        uint32_t live_updates = 0;
        int64_t live_age_us = 0;
        hid_test_mode_t test_mode = device_config_get_hid_test_mode();

        usb_xinput_device_poll_out();

        if (!device_config_bridge_running()) {
            switch2_state_reset(&state);

            if (usb_current_mode_ready()) {
                esp_err_t err = send_usb_state_report(&state);
                if (err == ESP_OK && now_us >= next_log_us) {
                    APP_LOGI(TAG, "USB stopped; neutral report sent mode=%s",
                             device_mode_to_string(device_config_get_mode()));
                    next_log_us = now_us + 1000000LL;
                } else if (err != ESP_OK && now_us >= next_log_us) {
                    APP_LOGW(TAG, "USB stopped; neutral report failed mode=%s err=%d",
                             device_mode_to_string(device_config_get_mode()),
                             (int)err);
                    next_log_us = now_us + 1000000LL;
                }
            } else if (now_us >= next_log_us) {
                APP_LOGI(TAG, "USB stopped; USB not ready state=%s mode=%s",
                         usb_hid_device_state_string(),
                         device_mode_to_string(device_config_get_mode()));
                next_log_us = now_us + 1000000LL;
            }
            vTaskDelay(pdMS_TO_TICKS(delay_ms));
            continue;
        }

        bool live_valid = switch2_state_get_live(&state, &live_updates, &live_age_us);
        bool using_live = live_valid && live_age_us <= BLE_LIVE_STALE_US;
        bool a_pressed = false;
        if (using_live) {
            source = "ble";
        } else {
            a_pressed = make_test_state(&state, &pressed, now_us);
        }

        if (usb_current_mode_ready()) {
            esp_err_t err = send_usb_state_report(&state);
            if (err == ESP_OK && now_us >= next_log_us) {
                APP_LOGI(TAG, "report loop usb_mode=%s source=%s rate_hz=%u live_updates=%lu live_age_ms=%lld test_mode=%s test_a=%s",
                         device_mode_to_string(device_config_get_mode()),
                         source,
                         (unsigned)device_config_get_report_rate_hz(),
                         (unsigned long)live_updates,
                         using_live ? (long long)(live_age_us / 1000) : -1LL,
                         hid_test_mode_to_string(test_mode),
                         a_pressed ? "pressed" : "released");
                next_log_us = now_us + 1000000LL;
            } else if (err != ESP_OK && now_us >= next_log_us) {
                APP_LOGW(TAG, "report send failed err=%d", (int)err);
                next_log_us = now_us + 1000000LL;
            }
        } else if (now_us >= next_log_us) {
            APP_LOGI(TAG, "USB not ready state=%s; firmware alive mode=%s",
                     usb_hid_device_state_string(),
                     device_mode_to_string(device_config_get_mode()));
            next_log_us = now_us + 1000000LL;
        }

        vTaskDelay(pdMS_TO_TICKS(delay_ms));
    }
}

void app_main(void)
{
    app_log_init();
    APP_LOGI(TAG, "ESP32-S3 Switch 2 bridge firmware 5.9.15 starting");
    APP_LOGI(TAG, "Stable path: Steam Switch Pro/Pro2 layout, BLE input, raw-like gyro, rumble, and boot autoconnect are verified");
    APP_LOGI(TAG, "Experimental path: dual Pro2 BLE probe mode measures two-controller connection capacity before dual-HID work");

    esp_err_t nvs_err = nvs_flash_init();
    if (nvs_err == ESP_ERR_NVS_NO_FREE_PAGES || nvs_err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        nvs_err = nvs_flash_init();
    }
    ESP_ERROR_CHECK(nvs_err);

    device_config_init();
    xbox_paddle_mapper_init();
    switch2_state_init();
    report_rate_stats_init();
    usb_xinput_device_init();

    ESP_ERROR_CHECK(usb_hid_device_init());
    control_protocol_init();
    ble_central_init();

    xTaskCreate(control_task, "control_task", 6144, NULL, 6, NULL);
    xTaskCreate(hid_report_task, "hid_report_task", 4096, NULL, 5, NULL);
}



