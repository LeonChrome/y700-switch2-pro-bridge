#pragma once

#include <stdint.h>
#include "esp_err.h"
#include "switch2_state.h"

#define SWITCH2_NOTIFY_FD2_UUID "ab7de9be-89fe-49ad-828f-118f09df7fd2"
#define SWITCH2_NOTIFY_LEGACY_UUID "7492866c-ec3e-4619-8258-32755ffcc0f8"
#define SWITCH2_RUMBLE_CC48_UUID "cc483f51-9258-427d-a939-630c31f72b05"

esp_err_t switch2_gatt_handle_notify(const char *uuid, const uint8_t *data, uint16_t len, switch2_state_t *out_state);
esp_err_t switch2_gatt_send_rumble_stub(const uint8_t *data, uint16_t len);
