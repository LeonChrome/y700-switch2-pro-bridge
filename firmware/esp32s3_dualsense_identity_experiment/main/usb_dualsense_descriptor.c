#include "usb_dualsense_descriptor.h"

#include "dualsense_report.h"

#ifndef DS5_ENABLE_USB_AUDIO
#define DS5_ENABLE_USB_AUDIO 0
#endif

#ifndef DS5_PROFILE_SERIAL
#define DS5_PROFILE_SERIAL "V55UNKNOWN"
#endif

#define DS5_USB_VID 0x054c
#define DS5_USB_PID 0x0ce6

#if DS5_ENABLE_USB_AUDIO
#define DS5_AUDIO_CONTROL_INTERFACE 0
#define DS5_AUDIO_STREAMING_INTERFACE 1
#define DS5_HID_INTERFACE 2
#else
#define DS5_HID_INTERFACE 0
#endif

#define DS5_HID_EP_OUT 0x01
#define DS5_HID_EP_IN 0x81
#define DS5_HID_EP_SIZE 64
#define DS5_HID_POLL_MS 1

#if DS5_ENABLE_USB_AUDIO
#define DS5_AUDIO_EP_OUT 0x02
#define DS5_AUDIO_SAMPLE_RATE 48000
#define DS5_AUDIO_CHANNELS 4
#define DS5_AUDIO_BYTES_PER_SAMPLE 2
#define DS5_AUDIO_BITS_PER_SAMPLE 16
#define DS5_AUDIO_EP_OUT_SIZE \
    TUD_AUDIO_EP_SIZE(DS5_AUDIO_SAMPLE_RATE, \
                      DS5_AUDIO_BYTES_PER_SAMPLE, \
                      DS5_AUDIO_CHANNELS)
#define DS5_AUDIO_ENTITY_INPUT_TERMINAL 0x01
#define DS5_AUDIO_ENTITY_FEATURE_UNIT 0x02
#define DS5_AUDIO_ENTITY_OUTPUT_TERMINAL 0x03
#define DS5_AUDIO_ENTITY_CLOCK 0x04
#define DS5_AUDIO_CONTROL_DESC_LEN \
    (TUD_AUDIO_DESC_CLK_SRC_LEN + TUD_AUDIO_DESC_INPUT_TERM_LEN + \
     TUD_AUDIO_DESC_FEATURE_UNIT_FOUR_CHANNEL_LEN + \
     TUD_AUDIO_DESC_OUTPUT_TERM_LEN)
#define DS5_AUDIO_RENDER_DESC_LEN \
    (TUD_AUDIO_DESC_IAD_LEN + TUD_AUDIO_DESC_STD_AC_LEN + \
     TUD_AUDIO_DESC_CS_AC_LEN + TUD_AUDIO_DESC_CLK_SRC_LEN + \
     TUD_AUDIO_DESC_INPUT_TERM_LEN + \
     TUD_AUDIO_DESC_FEATURE_UNIT_FOUR_CHANNEL_LEN + \
     TUD_AUDIO_DESC_OUTPUT_TERM_LEN + TUD_AUDIO_DESC_STD_AS_INT_LEN + \
     TUD_AUDIO_DESC_STD_AS_INT_LEN + TUD_AUDIO_DESC_CS_AS_INT_LEN + \
     TUD_AUDIO_DESC_TYPE_I_FORMAT_LEN + TUD_AUDIO_DESC_STD_AS_ISO_EP_LEN + \
     TUD_AUDIO_DESC_CS_AS_ISO_EP_LEN)
#define DS5_CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + DS5_AUDIO_RENDER_DESC_LEN + 32)
#else
#define DS5_CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + 32)
#endif

#if DS5_ENABLE_USB_AUDIO
#define DS5_AUDIO_RENDER_DESCRIPTOR(_stridx, _epout, _epsize) \
    TUD_AUDIO_DESC_IAD(DS5_AUDIO_CONTROL_INTERFACE, 0x02, 0x00), \
    TUD_AUDIO_DESC_STD_AC(DS5_AUDIO_CONTROL_INTERFACE, 0x00, _stridx), \
    TUD_AUDIO_DESC_CS_AC(0x0200, AUDIO_FUNC_DESKTOP_SPEAKER, \
                         DS5_AUDIO_CONTROL_DESC_LEN, \
                         AUDIO_CS_AS_INTERFACE_CTRL_LATENCY_POS), \
    TUD_AUDIO_DESC_CLK_SRC(DS5_AUDIO_ENTITY_CLOCK, \
                           AUDIO_CLOCK_SOURCE_ATT_INT_FIX_CLK, \
                           (AUDIO_CTRL_R << AUDIO_CLOCK_SOURCE_CTRL_CLK_FRQ_POS) | \
                               (AUDIO_CTRL_R << AUDIO_CLOCK_SOURCE_CTRL_CLK_VAL_POS), \
                           0x00, 0x00), \
    TUD_AUDIO_DESC_INPUT_TERM(DS5_AUDIO_ENTITY_INPUT_TERMINAL, \
                              AUDIO_TERM_TYPE_USB_STREAMING, 0x00, \
                              DS5_AUDIO_ENTITY_CLOCK, DS5_AUDIO_CHANNELS, \
                              AUDIO_CHANNEL_CONFIG_NON_PREDEFINED, 0x00, 0x0000, 0x00), \
    TUD_AUDIO_DESC_FEATURE_UNIT_FOUR_CHANNEL( \
        DS5_AUDIO_ENTITY_FEATURE_UNIT, DS5_AUDIO_ENTITY_INPUT_TERMINAL, \
        (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_MUTE_POS) | \
            (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_VOLUME_POS), \
        (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_MUTE_POS) | \
            (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_VOLUME_POS), \
        (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_MUTE_POS) | \
            (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_VOLUME_POS), \
        (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_MUTE_POS) | \
            (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_VOLUME_POS), \
        (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_MUTE_POS) | \
            (AUDIO_CTRL_RW << AUDIO_FEATURE_UNIT_CTRL_VOLUME_POS), \
        0x00), \
    TUD_AUDIO_DESC_OUTPUT_TERM(DS5_AUDIO_ENTITY_OUTPUT_TERMINAL, \
                               AUDIO_TERM_TYPE_OUT_HEADPHONES, \
                               0x00, \
                               DS5_AUDIO_ENTITY_FEATURE_UNIT, \
                               DS5_AUDIO_ENTITY_CLOCK, 0x0000, 0x00), \
    TUD_AUDIO_DESC_STD_AS_INT(DS5_AUDIO_STREAMING_INTERFACE, 0x00, 0x00, \
                              _stridx), \
    TUD_AUDIO_DESC_STD_AS_INT(DS5_AUDIO_STREAMING_INTERFACE, 0x01, 0x01, \
                              _stridx), \
    TUD_AUDIO_DESC_CS_AS_INT(DS5_AUDIO_ENTITY_INPUT_TERMINAL, AUDIO_CTRL_NONE, \
                             AUDIO_FORMAT_TYPE_I, AUDIO_DATA_FORMAT_TYPE_I_PCM, \
                             DS5_AUDIO_CHANNELS, \
                             AUDIO_CHANNEL_CONFIG_NON_PREDEFINED, 0x00), \
    TUD_AUDIO_DESC_TYPE_I_FORMAT(DS5_AUDIO_BYTES_PER_SAMPLE, \
                                 DS5_AUDIO_BITS_PER_SAMPLE), \
    TUD_AUDIO_DESC_STD_AS_ISO_EP( \
        _epout, \
        (uint8_t)((uint8_t)TUSB_XFER_ISOCHRONOUS | \
                  (uint8_t)TUSB_ISO_EP_ATT_ADAPTIVE | \
                  (uint8_t)TUSB_ISO_EP_ATT_DATA), \
        _epsize, 0x01), \
    TUD_AUDIO_DESC_CS_AS_ISO_EP( \
        AUDIO_CS_AS_ISO_DATA_EP_ATT_NON_MAX_PACKETS_OK, AUDIO_CTRL_NONE, \
        AUDIO_CS_AS_ISO_DATA_EP_LOCK_DELAY_UNIT_MILLISEC, 0x0001)
#endif

enum {
    STRID_LANGID = 0,
    STRID_MANUFACTURER,
    STRID_PRODUCT,
    STRID_SERIAL,
#if DS5_ENABLE_USB_AUDIO
    STRID_AUDIO_INTERFACE,
#endif
    STRID_HID_INTERFACE,
};

// Phase 1/2 keeps the DS5 input/output shape while declaring only the feature
// reports needed for early identity and input-mapping experiments.
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
#if DS5_ENABLE_USB_AUDIO
    .bDeviceClass = TUSB_CLASS_MISC,
    .bDeviceSubClass = MISC_SUBCLASS_COMMON,
    .bDeviceProtocol = MISC_PROTOCOL_IAD,
#else
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
#endif
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
#if DS5_ENABLE_USB_AUDIO
    0x03, 0x01, 0x00,
#else
    0x01, 0x01, 0x00,
#endif
    0x80, 0x32,

#if DS5_ENABLE_USB_AUDIO
    DS5_AUDIO_RENDER_DESCRIPTOR(STRID_AUDIO_INTERFACE,
                                DS5_AUDIO_EP_OUT,
                                DS5_AUDIO_EP_OUT_SIZE),
#endif

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
    DS5_PROFILE_SERIAL,
#if DS5_ENABLE_USB_AUDIO
    "DualSense Haptic Audio",
#endif
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
