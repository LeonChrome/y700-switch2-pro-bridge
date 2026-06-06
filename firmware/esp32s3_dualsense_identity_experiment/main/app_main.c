#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "dualsense_report.h"
#include "esp_err.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "tinyusb.h"
#include "tusb.h"
#include "usb_dualsense_descriptor.h"

static const char *TAG = "v5.5_ds5";
static volatile bool s_mounted;
static volatile bool s_suspended;
static uint32_t s_report_count;
static uint32_t s_output_count;

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
    ESP_LOGI(TAG,
             "[DS5_OUTPUT] report_id=0x%02x effective_report_id=0x%02x type=%u len=%u count=%lu",
             report_id,
             effective_report_id,
             (unsigned)report_type,
             (unsigned)bufsize,
             (unsigned long)s_output_count);
}

static void neutral_report_task(void *arg)
{
    (void)arg;
    uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE];
    dualsense_report_make_neutral(report);

    while (true) {
        if (s_mounted && !s_suspended && tud_hid_n_ready(0)) {
            bool sent = tud_hid_n_report(0,
                                         DUALSENSE_INPUT_REPORT_ID,
                                         report,
                                         sizeof(report));
            if (sent) {
                s_report_count++;
                if (s_report_count == 1 || (s_report_count % 250) == 0) {
                    ESP_LOGI(TAG,
                             "[DS5_REPORT] sent=true report_id=0x%02x len=%u count=%lu",
                             DUALSENSE_INPUT_REPORT_ID,
                             (unsigned)sizeof(report),
                             (unsigned long)s_report_count);
                }
            }
        }
        vTaskDelay(pdMS_TO_TICKS(4));
    }
}

void app_main(void)
{
    ESP_LOGI(TAG, "[DS5_IDENTITY] enabled=true mode=dualsense_experimental");
    ESP_LOGI(TAG,
             "[DS5_IDENTITY] vid=0x054c pid=0x0ce6 product=DualSense Wireless Controller");
    ESP_LOGI(TAG, "[DS5_IDENTITY] audio=false ble=false raw02=false");

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
    ESP_ERROR_CHECK(xTaskCreate(neutral_report_task,
                                "ds5_neutral",
                                3072,
                                NULL,
                                5,
                                NULL) == pdPASS ? ESP_OK : ESP_FAIL);
}
