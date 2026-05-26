#include "device_config.h"

static device_mode_t s_mode = GENERIC_HID_MODE;
static bool s_bridge_running = true;

void device_config_init(void)
{
    s_mode = GENERIC_HID_MODE;
    s_bridge_running = true;
}

device_mode_t device_config_get_mode(void)
{
    return s_mode;
}

void device_config_set_mode(device_mode_t mode)
{
    s_mode = mode;
}

const char *device_mode_to_string(device_mode_t mode)
{
    switch (mode) {
    case GENERIC_HID_MODE:
        return "generic";
    case NINTENDO_EXPERIMENT_MODE:
        return "nintendo";
    default:
        return "unknown";
    }
}

bool device_config_bridge_running(void)
{
    return s_bridge_running;
}

void device_config_set_bridge_running(bool running)
{
    s_bridge_running = running;
}

const char *device_config_get_version(void)
{
    return "0.1.0";
}
