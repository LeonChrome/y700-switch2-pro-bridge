#pragma once

#include <stdbool.h>

typedef enum {
    GENERIC_HID_MODE = 0,
    NINTENDO_EXPERIMENT_MODE = 1
} device_mode_t;

void device_config_init(void);
device_mode_t device_config_get_mode(void);
void device_config_set_mode(device_mode_t mode);
const char *device_mode_to_string(device_mode_t mode);
bool device_config_bridge_running(void);
void device_config_set_bridge_running(bool running);
const char *device_config_get_version(void);
