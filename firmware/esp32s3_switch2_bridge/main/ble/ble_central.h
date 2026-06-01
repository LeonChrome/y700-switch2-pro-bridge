#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include "esp_err.h"

typedef struct {
    bool connected;
    uint16_t conn_handle;
    uint16_t interval_units;
    uint16_t latency;
    uint16_t supervision_timeout;
    int last_update_start_rc;
    int last_update_event_status;
    uint32_t update_request_count;
} ble_central_conn_metrics_t;

void ble_central_init(void);
esp_err_t ble_central_start_scan(void);
esp_err_t ble_central_connect(const char *address_or_name);
esp_err_t ble_central_reconnect_saved_or_scan(void);
void ble_central_disconnect(void);
esp_err_t ble_central_request_fast_params(void);
void ble_central_get_conn_metrics(ble_central_conn_metrics_t *out_metrics);
void ble_central_format_scan_results_json(char *out, size_t out_len);
void ble_central_set_imu_debug(bool enabled, uint32_t every);
bool ble_central_get_imu_debug(uint32_t *out_every);
esp_err_t ble_central_send_command(const uint8_t *data, uint16_t len);
esp_err_t ble_central_send_rumble(const uint8_t *data, uint16_t len);
const char *ble_central_state_string(void);
