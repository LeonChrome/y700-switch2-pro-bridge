#pragma once

#include "esp_err.h"

void ble_central_init(void);
esp_err_t ble_central_start_scan(void);
esp_err_t ble_central_connect(const char *address_or_name);
void ble_central_disconnect(void);
const char *ble_central_state_string(void);
