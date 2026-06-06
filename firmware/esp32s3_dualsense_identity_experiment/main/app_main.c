#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "dualsense_haptic_audio.h"
#include "dualsense_report.h"
#include "dualsense_report_mapper.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "nvs_flash.h"
#include "pro2_input_backend.h"
#include "pro2_rumble_backend.h"
#include "tinyusb.h"
#include "tusb.h"
#include "usb_dualsense_descriptor.h"

static const char *TAG = "v5.5_ds5";
static volatile bool s_mounted;
static volatile bool s_suspended;
static uint32_t s_report_count;
static uint32_t s_output_count;

#define PRO2_INPUT_STALE_US 1000000LL

void tud_mount_cb(void)
{
    s_mounted = true;
    s_suspended = false;
    ESP_LOGI(TAG, "[DS5_USB] mounted=true");
}

void tud_umount_cb(void)
{
    s_mounted = false;
    s_suspended = false;
    ESP_LOGI(TAG, "[DS5_USB] mounted=false");
}

void tud_suspend_cb(bool remote_wakeup_en)
{
    (void)remote_wakeup_en;
    s_suspended = true;
    ESP_LOGI(TAG, "[DS5_USB] suspended=true");
}

void tud_resume_cb(void)
{
    s_suspended = false;
    ESP_LOGI(TAG, "[DS5_USB] suspended=false");
}

uint16_t tud_hid_get_report_cb(uint8_t instance,
                               uint8_t report_id,
                               hid_report_type_t report_type,
                               uint8_t *buffer,
                               uint16_t reqlen)
{
    (void)instance;
    if (!buffer || reqlen == 0) {
        return 0;
    }

    if (report_type == HID_REPORT_TYPE_INPUT &&
        (report_id == 0 || report_id == DUALSENSE_INPUT_REPORT_ID)) {
        uint8_t neutral[DUALSENSE_INPUT_PAYLOAD_SIZE];
        dualsense_report_make_neutral(neutral);
        uint16_t length = reqlen < sizeof(neutral) ? reqlen : sizeof(neutral);
        memcpy(buffer, neutral, length);
        return length;
    }

    if (report_type == HID_REPORT_TYPE_FEATURE) {
        size_t feature_size = dualsense_report_feature_size(report_id);
        if (feature_size == 0) {
            ESP_LOGW(TAG, "[DS5_FEATURE] report_id=0x%02x supported=false", report_id);
            return 0;
        }
        uint16_t length = reqlen < feature_size ? reqlen : (uint16_t)feature_size;
        dualsense_report_make_feature(report_id, buffer, length);
        ESP_LOGI(TAG, "[DS5_FEATURE] report_id=0x%02x len=%u placeholder=true",
                 report_id,
                 (unsigned)length);
        return length;
    }

    memset(buffer, 0, reqlen);
    return reqlen;
}

void tud_hid_set_report_cb(uint8_t instance,
                           uint8_t report_id,
                           hid_report_type_t report_type,
                           uint8_t const *buffer,
                           uint16_t bufsize)
{
    (void)instance;
    uint8_t effective_report_id = report_id;
    if (effective_report_id == 0 && buffer && bufsize > 0) {
        effective_report_id = buffer[0];
    }

    s_output_count++;
    bool rumble_handled = pro2_rumble_backend_handle_dualsense_output(
        report_id,
        buffer,
        bufsize);
    ESP_LOGI(TAG,
             "[DS5_OUTPUT] report_id=0x%02x effective_report_id=0x%02x type=%u len=%u count=%lu rumble_handled=%s",
             report_id,
             effective_report_id,
             (unsigned)report_type,
             (unsigned)bufsize,
             (unsigned long)s_output_count,
             rumble_handled ? "true" : "false");
}

static void neutral_report_task(void *arg)
{
    (void)arg;
    uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE];
    int64_t next_input_log_us = 0;
    bool last_connected = false;
    TickType_t next_wake = xTaskGetTickCount();
    dualsense_report_mapper_init();

    while (true) {
        int64_t now_us = esp_timer_get_time();
        switch2_state_t state;
        uint32_t updates = 0;
        int64_t age_us = INT64_MAX;
        bool live = pro2_input_backend_get_live(&state, &updates, &age_us);
        bool using_pro2 = live && age_us <= PRO2_INPUT_STALE_US;
        bool connected = strcmp(pro2_input_backend_state(), "connected") == 0;
        dualsense_input_debug_t debug;

        if (connected != last_connected) {
            ESP_LOGI(TAG,
                     "[PRO2_INPUT] connected=%s state=%s",
                     connected ? "true" : "false",
                     pro2_input_backend_state());
            last_connected = connected;
        }

        if (using_pro2) {
            dualsense_report_mapper_from_pro2(&state, report, &debug);
        } else {
            dualsense_report_mapper_neutral(report);
            memset(&debug, 0, sizeof(debug));
        }

        if (s_mounted && !s_suspended && tud_hid_n_ready(0)) {
            bool sent = tud_hid_n_report(0,
                                         DUALSENSE_INPUT_REPORT_ID,
                                         report,
                                         sizeof(report));
            if (sent) {
                s_report_count++;
                if (s_report_count == 1 || (s_report_count % 250) == 0) {
                    ESP_LOGI(TAG,
                             "[DS5_REPORT] source=%s sent=true report_id=0x%02x len=%u count=%lu",
                             using_pro2 ? "pro2" : "neutral",
                             DUALSENSE_INPUT_REPORT_ID,
                             (unsigned)sizeof(report),
                             (unsigned long)s_report_count);
                }
            }
        }

        if (now_us >= next_input_log_us) {
            if (using_pro2) {
                ESP_LOGI(TAG,
                         "[DS5_INPUT_MAP] buttons=0x%04x hat=%u lx=%u ly=%u rx=%u ry=%u l2=%u r2=%u updates=%lu age_ms=%lld",
                         debug.buttons,
                         debug.hat,
                         debug.lx,
                         debug.ly,
                         debug.rx,
                         debug.ry,
                         debug.l2,
                         debug.r2,
                         (unsigned long)updates,
                         (long long)(age_us / 1000));
                ESP_LOGI(TAG,
                         "[DS5_INPUT_MAP] gyro=%d,%d,%d accel=%d,%d,%d motion_valid=%s",
                         debug.gyro[0],
                         debug.gyro[1],
                         debug.gyro[2],
                         debug.accel[0],
                         debug.accel[1],
                         debug.accel[2],
                         debug.motion_valid ? "true" : "false");
            } else {
                ESP_LOGI(TAG,
                         "[DS5_INPUT] source=neutral reason=%s ble_state=%s",
                         connected ? "stale_pro2_input" : "no_pro2",
                         pro2_input_backend_state());
            }
            next_input_log_us = now_us + 1000000LL;
        }

        xTaskDelayUntil(&next_wake, pdMS_TO_TICKS(4));
    }
}

void app_main(void)
{
    ESP_LOGI(TAG, "[DS5_IDENTITY] enabled=true mode=dualsense_experimental");
    ESP_LOGI(TAG,
             "[DS5_IDENTITY] vid=0x054c pid=0x0ce6 product=DualSense Wireless Controller");
    ESP_LOGI(TAG,
             "[DS5_IDENTITY] audio=experimental ble_input=true rumble_compat=true raw02_forwarding=false");

    esp_err_t nvs_err = nvs_flash_init();
    if (nvs_err == ESP_ERR_NVS_NO_FREE_PAGES ||
        nvs_err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        nvs_err = nvs_flash_init();
    }
    ESP_ERROR_CHECK(nvs_err);

    const tinyusb_config_t tusb_config = {
        .device_descriptor = dualsense_usb_device_descriptor(),
        .string_descriptor = dualsense_usb_string_descriptors(),
        .string_descriptor_count = dualsense_usb_string_descriptor_count(),
        .external_phy = false,
        .configuration_descriptor = dualsense_usb_configuration_descriptor(),
        .self_powered = false,
        .vbus_monitor_io = 0,
    };

    ESP_ERROR_CHECK(tinyusb_driver_install(&tusb_config));
    pro2_input_backend_init();
    pro2_rumble_backend_init();
    dualsense_haptic_audio_init();
    ESP_ERROR_CHECK(xTaskCreate(neutral_report_task,
                                "ds5_input",
                                3072,
                                NULL,
                                5,
                                NULL) == pdPASS ? ESP_OK : ESP_FAIL);
}
