#include <stdio.h>
#include <string.h>
#include "app_log.h"
#include "switch2_gatt.h"

static const char *TAG = "switch2_gatt";

static uint32_t read_le32(const uint8_t *p)
{
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

esp_err_t switch2_gatt_handle_notify(const char *uuid, const uint8_t *data, uint16_t len, switch2_state_t *out_state)
{
    APP_LOGI(TAG, "notify uuid=%s len=%u", uuid ? uuid : "<unknown>", (unsigned)len);
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: compare raw notify hex with Y700 logs before trusting parser");

    if (!uuid || !data || !out_state) {
        return ESP_ERR_INVALID_ARG;
    }

    if (strcmp(uuid, SWITCH2_NOTIFY_FD2_UUID) == 0 && len >= 8) {
        switch2_state_update_from_fd2_buttons(out_state, read_le32(&data[4]));
        return ESP_OK;
    }

    if (strcmp(uuid, SWITCH2_NOTIFY_LEGACY_UUID) == 0 && len >= 5) {
        switch2_state_update_from_legacy_bytes(out_state, data[2], data[3], data[4]);
        return ESP_OK;
    }

    return ESP_ERR_NOT_FOUND;
}

esp_err_t switch2_gatt_send_rumble_stub(const uint8_t *data, uint16_t len)
{
    (void)data;
    (void)len;
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: rumble reverse path is reserved but not implemented");
    return ESP_ERR_NOT_SUPPORTED;
}
