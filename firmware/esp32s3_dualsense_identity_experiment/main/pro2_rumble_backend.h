#pragma once

#include <stdbool.h>
#include <stdint.h>
#include "esp_err.h"

void pro2_rumble_backend_init(void);
esp_err_t pro2_rumble_backend_send_raw02_payload(const uint8_t *payload, uint16_t len);
bool pro2_rumble_backend_handle_dualsense_output(
    uint8_t report_id,
    const uint8_t *buffer,
    uint16_t len);
