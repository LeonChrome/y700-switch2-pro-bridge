#pragma once

#include <stdbool.h>
#include "esp_err.h"
#include "hid_report.h"

esp_err_t usb_hid_device_init(void);
bool usb_hid_device_ready(void);
const char *usb_hid_device_state_string(void);
esp_err_t usb_hid_device_send_generic_report(const hid_gamepad_report_t *report);
esp_err_t usb_hid_device_send_nintendo_report(const uint8_t report[NINTENDO_REPORT_SIZE]);
