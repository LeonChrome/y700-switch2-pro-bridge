#include "device_config.h"
#include "hid_report.h"
#include "usb_descriptors.h"

#define EPNUM_HID 0x81
#define ITF_NUM_HID 0
#define ITF_NUM_TOTAL 1
#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN)

enum {
    STRID_LANGID = 0,
    STRID_MANUFACTURER,
    STRID_PRODUCT,
    STRID_SERIAL,
};

const uint8_t desc_hid_report_generic[] = {
    TUD_HID_REPORT_DESC_GAMEPAD(HID_REPORT_ID(GENERIC_HID_REPORT_ID))
};

const uint8_t desc_hid_report_nintendo_experiment[] = {
    // Vendor/raw HID copied from Y700 v3:
    // input report 0x09 + 63 payload bytes, output report 0x02 + 63 payload bytes.
    // PENDING_HARDWARE_TEST: host parsing and Steam path on ESP32-S3 are not verified.
    0x06, 0x00, 0xff, 0x09, 0x01, 0xa1, 0x01, 0x15, 0x00, 0x26, 0xff, 0x00,
    0x75, 0x08, 0x85, 0x09, 0x95, 0x3f, 0x09, 0x01, 0x81, 0x02, 0x85, 0x02,
    0x95, 0x3f, 0x09, 0x01, 0x91, 0x02, 0xc0
};

static const tusb_desc_device_t desc_device_generic = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = USB_VID_GENERIC,
    .idProduct = USB_PID_GENERIC,
    .bcdDevice = 0x0100,
    .iManufacturer = STRID_MANUFACTURER,
    .iProduct = STRID_PRODUCT,
    .iSerialNumber = STRID_SERIAL,
    .bNumConfigurations = 0x01,
};

static const tusb_desc_device_t desc_device_nintendo = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = USB_VID_NINTENDO_EXPERIMENT,
    .idProduct = USB_PID_NINTENDO_EXPERIMENT,
    .bcdDevice = 0x0104,
    .iManufacturer = STRID_MANUFACTURER,
    .iProduct = STRID_PRODUCT,
    .iSerialNumber = STRID_SERIAL,
    .bNumConfigurations = 0x01,
};

static const uint8_t desc_configuration_generic[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, 0, 100),
    TUD_HID_DESCRIPTOR(ITF_NUM_HID, 0, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report_generic), EPNUM_HID, sizeof(hid_gamepad_report_t) + 1, 10),
};

static const uint8_t desc_configuration_nintendo[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, 0, 100),
    TUD_HID_DESCRIPTOR(ITF_NUM_HID, 0, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report_nintendo_experiment), EPNUM_HID, NINTENDO_REPORT_SIZE, 10),
};

static const char *string_desc_generic[] = {
    "",
    "LeonChrome",
    "ESP32-S3 Generic HID Gamepad",
    "ESP32S3-GENERIC",
};

static const char *string_desc_nintendo[] = {
    "",
    "Nintendo Co., Ltd.",
    "Nintendo Switch Pro Controller",
    "ESP32S3-NIN-EXP",
};

static uint16_t _desc_str[32];

uint16_t usb_descriptors_current_vid(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ? USB_VID_NINTENDO_EXPERIMENT : USB_VID_GENERIC;
}

uint16_t usb_descriptors_current_pid(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ? USB_PID_NINTENDO_EXPERIMENT : USB_PID_GENERIC;
}

const char *usb_descriptors_current_product(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        "Nintendo Switch Pro Controller" : "ESP32-S3 Generic HID Gamepad";
}

const char *usb_descriptors_current_manufacturer(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ? "Nintendo Co., Ltd." : "LeonChrome";
}

uint8_t const *tud_descriptor_device_cb(void)
{
    return (uint8_t const *)(device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        &desc_device_nintendo : &desc_device_generic);
}

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance)
{
    (void)instance;
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        desc_hid_report_nintendo_experiment : desc_hid_report_generic;
}

uint8_t const *tud_descriptor_configuration_cb(uint8_t index)
{
    (void)index;
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        desc_configuration_nintendo : desc_configuration_generic;
}

uint16_t const *tud_descriptor_string_cb(uint8_t index, uint16_t langid)
{
    (void)langid;

    const char **strings = device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        string_desc_nintendo : string_desc_generic;
    const uint8_t count = 4;

    if (index >= count) {
        return NULL;
    }

    uint8_t chr_count;
    if (index == 0) {
        _desc_str[1] = 0x0409;
        chr_count = 1;
    } else {
        const char *str = strings[index];
        chr_count = 0;
        while (str[chr_count] && chr_count < 31) {
            _desc_str[1 + chr_count] = str[chr_count];
            chr_count++;
        }
    }

    _desc_str[0] = (uint16_t)((TUSB_DESC_STRING << 8) | (2 * chr_count + 2));
    return _desc_str;
}
