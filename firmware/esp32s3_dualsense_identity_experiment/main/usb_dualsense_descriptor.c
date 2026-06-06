#include "usb_dualsense_descriptor.h"

#include "dualsense_report.h"

#define DS5_USB_VID 0x054c
#define DS5_USB_PID 0x0ce6
#define DS5_HID_INTERFACE 0
#define DS5_HID_EP_OUT 0x01
#define DS5_HID_EP_IN 0x81
#define DS5_HID_EP_SIZE 64
#define DS5_HID_POLL_MS 1
#define DS5_CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + 32)

enum {
    STRID_LANGID = 0,
    STRID_MANUFACTURER,
    STRID_PRODUCT,
    STRID_SERIAL,
    STRID_HID_INTERFACE,
};

// Phase 1 keeps the DS5 input/output shape while declaring only the feature
// reports needed for early host-enumeration experiments.
static const uint8_t s_ds5_hid_report_descriptor[] = {
    0x05, 0x01,       // Usage Page (Generic Desktop)
    0x09, 0x05,       // Usage (Game Pad)
    0xa1, 0x01,       // Collection (Application)
    0x85, DUALSENSE_INPUT_REPORT_ID,
    0x09, 0x30,       // X
    0x09, 0x31,       // Y
    0x09, 0x32,       // Z
    0x09, 0x35,       // Rz
    0x09, 0x33,       // Rx
    0x09, 0x34,       // Ry
    0x15, 0x00,
    0x26, 0xff, 0x00,
    0x75, 0x08,
    0x95, 0x06,
    0x81, 0x02,
    0x06, 0x00, 0xff, // Vendor byte
    0x09, 0x20,
    0x95, 0x01,
    0x81, 0x02,
    0x05, 0x01,
    0x09, 0x39,       // Hat switch
    0x15, 0x00,
    0x25, 0x07,
    0x35, 0x00,
    0x46, 0x3b, 0x01,
    0x65, 0x14,
    0x75, 0x04,
    0x95, 0x01,
    0x81, 0x42,
    0x65, 0x00,
    0x05, 0x09,       // 15 buttons
    0x19, 0x01,
    0x29, 0x0f,
    0x15, 0x00,
    0x25, 0x01,
    0x75, 0x01,
    0x95, 0x0f,
    0x81, 0x02,
    0x06, 0x00, 0xff, // Complete the packed 4-byte button/vendor block
    0x09, 0x21,
    0x95, 0x0d,
    0x81, 0x02,
    0x06, 0x00, 0xff, // Remaining DS5 input payload
    0x09, 0x22,
    0x15, 0x00,
    0x26, 0xff, 0x00,
    0x75, 0x08,
    0x95, 0x34,
    0x81, 0x02,
    0x85, DUALSENSE_OUTPUT_REPORT_ID,
    0x09, 0x23,
    0x95, DUALSENSE_OUTPUT_PAYLOAD_SIZE,
    0x91, 0x02,
    0x85, 0x05,
    0x09, 0x33,
    0x95, 0x28,
    0xb1, 0x02,
    0x85, 0x08,
    0x09, 0x34,
    0x95, 0x2f,
    0xb1, 0x02,
    0x85, 0x09,
    0x09, 0x24,
    0x95, 0x13,
    0xb1, 0x02,
    0x85, 0x20,
    0x09, 0x26,
    0x95, 0x3f,
    0xb1, 0x02,
    0xc0,
};

static const tusb_desc_device_t s_ds5_device_descriptor = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = DS5_USB_VID,
    .idProduct = DS5_USB_PID,
    .bcdDevice = 0x0100,
    .iManufacturer = STRID_MANUFACTURER,
    .iProduct = STRID_PRODUCT,
    .iSerialNumber = STRID_SERIAL,
    .bNumConfigurations = 0x01,
};

static const uint8_t s_ds5_configuration_descriptor[] = {
    0x09, TUSB_DESC_CONFIGURATION,
    U16_TO_U8S_LE(DS5_CONFIG_TOTAL_LEN),
    0x01, 0x01, 0x00,
    0x80, 0x32,

    0x09, TUSB_DESC_INTERFACE,
    DS5_HID_INTERFACE, 0x00, 0x02,
    TUSB_CLASS_HID, 0x00, 0x00, STRID_HID_INTERFACE,

    0x09, HID_DESC_TYPE_HID,
    U16_TO_U8S_LE(0x0111),
    0x00, 0x01, HID_DESC_TYPE_REPORT,
    U16_TO_U8S_LE(sizeof(s_ds5_hid_report_descriptor)),

    0x07, TUSB_DESC_ENDPOINT,
    DS5_HID_EP_IN, TUSB_XFER_INTERRUPT,
    U16_TO_U8S_LE(DS5_HID_EP_SIZE), DS5_HID_POLL_MS,

    0x07, TUSB_DESC_ENDPOINT,
    DS5_HID_EP_OUT, TUSB_XFER_INTERRUPT,
    U16_TO_U8S_LE(DS5_HID_EP_SIZE), DS5_HID_POLL_MS,
};

static const char *s_ds5_string_descriptors[] = {
    "",
    "Sony Interactive Entertainment",
    "DualSense Wireless Controller",
    "V55PHASE1",
    "Wireless Controller",
};

_Static_assert(sizeof(s_ds5_configuration_descriptor) == DS5_CONFIG_TOTAL_LEN,
               "DualSense configuration descriptor length mismatch");

const tusb_desc_device_t *dualsense_usb_device_descriptor(void)
{
    return &s_ds5_device_descriptor;
}

const uint8_t *dualsense_usb_configuration_descriptor(void)
{
    return s_ds5_configuration_descriptor;
}

const char **dualsense_usb_string_descriptors(void)
{
    return s_ds5_string_descriptors;
}

int dualsense_usb_string_descriptor_count(void)
{
    return sizeof(s_ds5_string_descriptors) / sizeof(s_ds5_string_descriptors[0]);
}

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance)
{
    (void)instance;
    return s_ds5_hid_report_descriptor;
}
