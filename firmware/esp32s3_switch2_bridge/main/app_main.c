#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_err.h"
#include "esp_log.h"
#include "app_log.h"
#include "ble_central.h"
#include "control_protocol.h"
#include "device_config.h"
#include "hid_report.h"
#include "report_mapper.h"
#include "switch2_state.h"
#include "usb_hid_device.h"

static const char *TAG = "app";

static void control_task(void *arg)
{
    (void)arg;
    char line[128];
    char reply[256];

    while (true) {
        if (fgets(line, sizeof(line), stdin) != NULL) {
            control_protocol_handle_line(line, reply, sizeof(reply));
        } else {
            vTaskDelay(pdMS_TO_TICKS(50));
        }
    }
}

static void hid_test_task(void *arg)
{
    (void)arg;
    bool pressed = false;

    while (true) {
        switch2_state_t state;
        switch2_state_reset(&state);
        if (pressed) {
            switch2_state_set_button(&state, SWITCH2_BUTTON_A, true);
        }

        hid_gamepad_report_t report;
        report_mapper_state_to_generic_report(&state, &report);

        if (usb_hid_device_ready()) {
            esp_err_t err = usb_hid_device_send_generic_report(&report);
            if (err == ESP_OK) {
                APP_LOGI(TAG, "report sent test_a=%s", pressed ? "pressed" : "released");
            } else {
                APP_LOGW(TAG, "report send failed err=%d", (int)err);
            }
        } else {
            APP_LOGI(TAG, "USB not mounted; firmware alive mode=%s",
                     device_mode_to_string(device_config_get_mode()));
        }

        pressed = !pressed;
        vTaskDelay(pdMS_TO_TICKS(2000));
    }
}

void app_main(void)
{
    app_log_init();
    APP_LOGI(TAG, "ESP32-S3 Switch 2 bridge firmware 0.1.0 starting");
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: build/flash/USB/BLE behavior is not verified on real hardware yet");

    device_config_init();
    switch2_state_init();

    ESP_ERROR_CHECK(usb_hid_device_init());
    control_protocol_init();
    ble_central_init();

    xTaskCreate(control_task, "control_task", 4096, NULL, 6, NULL);
    xTaskCreate(hid_test_task, "hid_test_task", 4096, NULL, 5, NULL);
}
