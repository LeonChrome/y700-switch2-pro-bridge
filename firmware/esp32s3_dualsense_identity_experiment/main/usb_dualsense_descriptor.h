#pragma once

#include "tusb.h"

const tusb_desc_device_t *dualsense_usb_device_descriptor(void);
const uint8_t *dualsense_usb_configuration_descriptor(void);
const char **dualsense_usb_string_descriptors(void);
int dualsense_usb_string_descriptor_count(void);
