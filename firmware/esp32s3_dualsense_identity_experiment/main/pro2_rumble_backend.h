#pragma once

#include <stdbool.h>
#include <stdint.h>

void pro2_rumble_backend_init(void);
bool pro2_rumble_backend_handle_dualsense_output(
    uint8_t report_id,
    const uint8_t *buffer,
    uint16_t len);
