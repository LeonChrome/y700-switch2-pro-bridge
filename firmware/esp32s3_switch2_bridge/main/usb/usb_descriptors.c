#include "device_config.h"
#include "hid_report.h"
#include "usb_descriptors.h"

#define EPNUM_HID_OUT 0x01
#define EPNUM_HID 0x81
#define EPNUM_VENDOR_OUT 0x02
#define EPNUM_VENDOR_IN 0x82
#define ITF_NUM_HID 0
#define ITF_NUM_VENDOR USB_SWITCH2_VENDOR_INTERFACE
#define ITF_NUM_TOTAL_GENERIC 1
#define ITF_NUM_TOTAL_NINTENDO 2
#define CONFIG_TOTAL_LEN_GENERIC (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN)
#define CONFIG_TOTAL_LEN_NINTENDO (TUD_CONFIG_DESC_LEN + TUD_HID_INOUT_DESC_LEN + TUD_VENDOR_DESC_LEN)
#define HID_POLL_INTERVAL_MS 1
#define VENDOR_BULK_PACKET_SIZE 64
#define CONFIG_ATTR_NINTENDO 0
#define CONFIG_POWER_MA_NINTENDO 500

#define TUD_VENDOR_INOUT_DESCRIPTOR(_itfnum, _stridx, _epin, _epout, _epsize) \
    9, TUSB_DESC_INTERFACE, _itfnum, 0, 2, TUSB_CLASS_VENDOR_SPECIFIC, 0x00, 0x00, _stridx, \
    7, TUSB_DESC_ENDPOINT, _epin, TUSB_XFER_BULK, U16_TO_U8S_LE(_epsize), 0, \
    7, TUSB_DESC_ENDPOINT, _epout, TUSB_XFER_BULK, U16_TO_U8S_LE(_epsize), 0

#define TUD_HID_Y700_INOUT_DESCRIPTOR(_itfnum, _stridx, _boot_protocol, _report_desc_len, _epin, _epout, _epsize, _ep_interval) \
    9, TUSB_DESC_INTERFACE, _itfnum, 0, 2, TUSB_CLASS_HID, (uint8_t)((_boot_protocol) ? (uint8_t)HID_SUBCLASS_BOOT : 0), _boot_protocol, _stridx, \
    9, HID_DESC_TYPE_HID, U16_TO_U8S_LE(0x0101), 0, 1, HID_DESC_TYPE_REPORT, U16_TO_U8S_LE(_report_desc_len), \
    7, TUSB_DESC_ENDPOINT, _epin, TUSB_XFER_INTERRUPT, U16_TO_U8S_LE(_epsize), _ep_interval, \
    7, TUSB_DESC_ENDPOINT, _epout, TUSB_XFER_INTERRUPT, U16_TO_U8S_LE(_epsize), _ep_interval

enum {
    STRID_LANGID = 0,
    STRID_MANUFACTURER,
    STRID_PRODUCT,
    STRID_SERIAL,
    STRID_CONFIG,
    STRID_HID_INTERFACE,
    STRID_EMPTY,
    STRID_VENDOR_INTERFACE,
};

const uint8_t desc_hid_report_generic[] = {
    TUD_HID_REPORT_DESC_GAMEPAD(HID_REPORT_ID(GENERIC_HID_REPORT_ID))
};

const uint8_t desc_hid_report_nintendo_experiment[] = {
    // Vendor/raw HID copied from Y700 v3:
    // input report 0x05 + 63 payload bytes, output report 0x02 + 63 payload bytes.
    // feature report 0x7f is a manager-only control channel and is ignored by Steam.
    0x06, 0x00, 0xff, 0x09, 0x01, 0xa1, 0x01, 0x15, 0x00, 0x26, 0xff, 0x00,
    0x75, 0x08, 0x85, NINTENDO_INPUT_REPORT_ID, 0x95, 0x3f, 0x09, 0x01, 0x81, 0x02, 0x85, 0x02,
    0x95, 0x3f, 0x09, 0x01, 0x91, 0x02, 0x85, MANAGER_FEATURE_REPORT_ID,
    0x95, 0x3f, 0x09, 0x01, 0xb1, 0x02, 0xc0,
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
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL_GENERIC, 0, CONFIG_TOTAL_LEN_GENERIC, 0, 100),
    TUD_HID_DESCRIPTOR(ITF_NUM_HID, 0, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report_generic), EPNUM_HID, sizeof(bridge_hid_gamepad_report_t) + 1, HID_POLL_INTERVAL_MS),
};

static const uint8_t desc_configuration_nintendo[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL_NINTENDO, STRID_CONFIG, CONFIG_TOTAL_LEN_NINTENDO, CONFIG_ATTR_NINTENDO, CONFIG_POWER_MA_NINTENDO),
    TUD_HID_Y700_INOUT_DESCRIPTOR(ITF_NUM_HID, STRID_HID_INTERFACE, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report_nintendo_experiment), EPNUM_HID, EPNUM_HID_OUT, NINTENDO_REPORT_SIZE, HID_POLL_INTERVAL_MS),
    TUD_VENDOR_INOUT_DESCRIPTOR(ITF_NUM_VENDOR, STRID_VENDOR_INTERFACE, EPNUM_VENDOR_IN, EPNUM_VENDOR_OUT, VENDOR_BULK_PACKET_SIZE),
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
    "HA2F83JF",
    "Nintendo Switch Pro Controller",
    "HID Interface",
    "",
    "Nintendo Switch 2 bulk",
};

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

const tusb_desc_device_t *usb_descriptors_current_device(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        &desc_device_nintendo : &desc_device_generic;
}

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance)
{
    (void)instance;
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        desc_hid_report_nintendo_experiment : desc_hid_report_generic;
}

const uint8_t *usb_descriptors_current_configuration(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        desc_configuration_nintendo : desc_configuration_generic;
}

const char **usb_descriptors_current_strings(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ?
        string_desc_nintendo : string_desc_generic;
}

int usb_descriptors_current_string_count(void)
{
    return device_config_get_mode() == NINTENDO_EXPERIMENT_MODE ? 8 : 4;
}
