#include <string.h>
#include "esp_err.h"
#include "app_log.h"
#include "switch2_gatt.h"
#include "ble_central.h"

static const char *TAG = "ble";

typedef enum {
    BLE_STATE_IDLE = 0,
    BLE_STATE_SCANNING,
    BLE_STATE_CONNECTING,
    BLE_STATE_CONNECTED
} ble_state_t;

static ble_state_t s_state = BLE_STATE_IDLE;

void ble_central_init(void)
{
    s_state = BLE_STATE_IDLE;
    APP_LOGI(TAG, "BLE Central skeleton initialized");
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: BLE scan/connect/notify are not verified without ESP32-S3 hardware");
}

esp_err_t ble_central_start_scan(void)
{
    s_state = BLE_STATE_SCANNING;
    APP_LOGI(TAG, "BLE scan requested");
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: implement NimBLE active scan and log name/MAC/RSSI on hardware");
    return ESP_OK;
}

esp_err_t ble_central_connect(const char *address_or_name)
{
    (void)address_or_name;
    s_state = BLE_STATE_CONNECTING;
    APP_LOGI(TAG, "BLE connect requested target=%s", address_or_name ? address_or_name : "<none>");
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: connection/service discovery/notify subscription not implemented yet");
    return ESP_ERR_NOT_SUPPORTED;
}

void ble_central_disconnect(void)
{
    s_state = BLE_STATE_IDLE;
    APP_LOGI(TAG, "BLE disconnect requested");
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
