#include <stdbool.h>
#include <stdint.h>

#include "class/audio/audio.h"
#include "device/usbd_pvt.h"
#include "esp_log.h"
#include "tusb.h"
#include "usb_dualsense_descriptor.h"

#ifndef DS5_ENABLE_UAC1_AUDIO
#define DS5_ENABLE_UAC1_AUDIO 0
#endif
#ifndef DS5_ENABLE_UAC1_CONTROL_ONLY
#define DS5_ENABLE_UAC1_CONTROL_ONLY 0
#endif
#ifndef DS5_ENABLE_UAC1_STREAMING_ALT0
#define DS5_ENABLE_UAC1_STREAMING_ALT0 0
#endif

#if DS5_ENABLE_UAC1_AUDIO || DS5_ENABLE_UAC1_CONTROL_ONLY || \
    DS5_ENABLE_UAC1_STREAMING_ALT0

#define DS5_UAC1_AC_INTERFACE 0
#define DS5_UAC1_AS_INTERFACE 1
#define DS5_UAC1_ALT_IDLE 0
#define DS5_UAC1_ALT_STREAMING 1

static const char *TAG = "v5.5_uac1";
static uint8_t s_alt_setting;
#if DS5_ENABLE_UAC1_AUDIO
static uint32_t s_packet_count;
static uint8_t s_audio_out_buffer[DUALSENSE_USB_UAC1_2CH_PACKET_SIZE] TU_ATTR_ALIGNED(4);

static const tusb_desc_endpoint_t s_uac1_audio_out_ep = {
    .bLength = sizeof(tusb_desc_endpoint_t),
    .bDescriptorType = TUSB_DESC_ENDPOINT,
    .bEndpointAddress = DUALSENSE_USB_AUDIO_EP_OUT,
    .bmAttributes = {
        .xfer = TUSB_XFER_ISOCHRONOUS,
        .sync = (TUSB_ISO_EP_ATT_ADAPTIVE >> 2),
        .usage = (TUSB_ISO_EP_ATT_DATA >> 4),
    },
    .wMaxPacketSize = DUALSENSE_USB_UAC1_2CH_PACKET_SIZE,
    .bInterval = 1,
};
#endif

static void uac1_init(void)
{
    s_alt_setting = DS5_UAC1_ALT_IDLE;
#if DS5_ENABLE_UAC1_AUDIO
    s_packet_count = 0;
#endif
}

static bool uac1_deinit(void)
{
    return true;
}

static void uac1_reset(uint8_t rhport)
{
    (void)rhport;
    s_alt_setting = DS5_UAC1_ALT_IDLE;
#if DS5_ENABLE_UAC1_AUDIO
    s_packet_count = 0;
#endif
}

static uint16_t uac1_open(uint8_t rhport,
                          tusb_desc_interface_t const *itf_desc,
                          uint16_t max_len)
{
    (void)rhport;
    if (itf_desc->bInterfaceClass != TUSB_CLASS_AUDIO ||
        itf_desc->bInterfaceProtocol != AUDIO_INT_PROTOCOL_CODE_UNDEF) {
        return 0;
    }

    if (itf_desc->bInterfaceNumber == DS5_UAC1_AC_INTERFACE &&
        itf_desc->bInterfaceSubClass == AUDIO_SUBCLASS_CONTROL &&
        itf_desc->bAlternateSetting == 0) {
#if DS5_ENABLE_UAC1_CONTROL_ONLY
        const uint16_t descriptor_len = DUALSENSE_USB_UAC1_CONTROL_ONLY_AC_LEN;
#elif DS5_ENABLE_UAC1_STREAMING_ALT0
        const uint16_t descriptor_len = DUALSENSE_USB_UAC1_STREAMING_ALT0_AC_LEN;
#else
        const uint16_t descriptor_len = DUALSENSE_USB_UAC1_2CH_AC_LEN;
#endif
        if (max_len < descriptor_len) {
            return 0;
        }
        ESP_LOGI(TAG,
                 "[DS5_UAC1] open=true section=audio_control desc_len=%u",
                 (unsigned)descriptor_len);
        return descriptor_len;
    }

#if DS5_ENABLE_UAC1_STREAMING_ALT0 || DS5_ENABLE_UAC1_AUDIO
    if (itf_desc->bInterfaceNumber == DS5_UAC1_AS_INTERFACE &&
        itf_desc->bInterfaceSubClass == AUDIO_SUBCLASS_STREAMING &&
        itf_desc->bAlternateSetting == 0) {
#if DS5_ENABLE_UAC1_STREAMING_ALT0
        const uint16_t descriptor_len = DUALSENSE_USB_UAC1_STREAMING_ALT0_AS_LEN;
#else
        const uint16_t descriptor_len = DUALSENSE_USB_UAC1_2CH_AS_LEN;
#endif
        if (max_len < descriptor_len) {
            return 0;
        }
        ESP_LOGI(TAG,
                 "[DS5_UAC1] open=true section=audio_streaming desc_len=%u",
                 (unsigned)descriptor_len);
        return descriptor_len;
    }
#endif

    return 0;
}

#if DS5_ENABLE_UAC1_AUDIO
static bool uac1_start_stream(uint8_t rhport)
{
    bool opened = usbd_edpt_open(rhport, &s_uac1_audio_out_ep);
    if (!opened) {
        ESP_LOGW(TAG, "[DS5_UAC1] open_ep=false ep=0x%02x", DUALSENSE_USB_AUDIO_EP_OUT);
        return false;
    }
    bool armed = usbd_edpt_xfer(rhport,
                                DUALSENSE_USB_AUDIO_EP_OUT,
                                s_audio_out_buffer,
                                sizeof(s_audio_out_buffer));
    ESP_LOGI(TAG,
             "[DS5_UAC1] streaming=true ep=0x%02x armed=%s",
             DUALSENSE_USB_AUDIO_EP_OUT,
             armed ? "true" : "false");
    return armed;
}
#endif

static bool uac1_control_xfer_cb(uint8_t rhport,
                                 uint8_t stage,
                                 tusb_control_request_t const *request)
{
    if (stage != CONTROL_STAGE_SETUP) {
        return true;
    }
    if (request->bmRequestType_bit.type != TUSB_REQ_TYPE_STANDARD ||
        request->bmRequestType_bit.recipient != TUSB_REQ_RCPT_INTERFACE) {
        return false;
    }

    uint8_t itf = tu_u16_low(request->wIndex);
    if (itf != DS5_UAC1_AS_INTERFACE ||
        (!DS5_ENABLE_UAC1_STREAMING_ALT0 && !DS5_ENABLE_UAC1_AUDIO)) {
        return false;
    }

    if (request->bRequest == TUSB_REQ_GET_INTERFACE) {
        return tud_control_xfer(rhport, request, &s_alt_setting, sizeof(s_alt_setting));
    }

    if (request->bRequest == TUSB_REQ_SET_INTERFACE) {
#if DS5_ENABLE_UAC1_STREAMING_ALT0 || DS5_ENABLE_UAC1_AUDIO
        uint8_t alt = tu_u16_low(request->wValue);
#if DS5_ENABLE_UAC1_STREAMING_ALT0
        if (alt != DS5_UAC1_ALT_IDLE) {
            return false;
        }
        s_alt_setting = alt;
#elif DS5_ENABLE_UAC1_AUDIO
        if (alt > DS5_UAC1_ALT_STREAMING) {
            return false;
        }

        if (s_alt_setting == DS5_UAC1_ALT_STREAMING && alt == DS5_UAC1_ALT_IDLE) {
            usbd_edpt_close(rhport, DUALSENSE_USB_AUDIO_EP_OUT);
        }
        s_alt_setting = alt;
        if (alt == DS5_UAC1_ALT_STREAMING) {
            if (!uac1_start_stream(rhport)) {
                return false;
            }
        }
#else
        return false;
#endif

        ESP_LOGI(TAG, "[DS5_UAC1] set_interface=%u", (unsigned)s_alt_setting);
        return tud_control_status(rhport, request);
#else
        return false;
#endif
    }

    return false;
}

static bool uac1_xfer_cb(uint8_t rhport,
                         uint8_t ep_addr,
                         xfer_result_t result,
                         uint32_t xferred_bytes)
{
#if DS5_ENABLE_UAC1_AUDIO
    if (ep_addr != DUALSENSE_USB_AUDIO_EP_OUT) {
        return false;
    }
    if (result == XFER_RESULT_SUCCESS) {
        s_packet_count++;
        if (s_packet_count == 1 || (s_packet_count % 500) == 0) {
            ESP_LOGI(TAG,
                     "[DS5_UAC1] out_packet len=%lu count=%lu",
                     (unsigned long)xferred_bytes,
                     (unsigned long)s_packet_count);
        }
    }

    return usbd_edpt_xfer(rhport,
                          DUALSENSE_USB_AUDIO_EP_OUT,
                          s_audio_out_buffer,
                          sizeof(s_audio_out_buffer));
#else
    (void)rhport;
    (void)ep_addr;
    (void)result;
    (void)xferred_bytes;
    return false;
#endif
}

static usbd_class_driver_t const s_uac1_driver[] = {{
    .name = "v5.5_uac1",
    .init = uac1_init,
    .deinit = uac1_deinit,
    .reset = uac1_reset,
    .open = uac1_open,
    .control_xfer_cb = uac1_control_xfer_cb,
    .xfer_cb = uac1_xfer_cb,
    .xfer_isr = NULL,
    .sof = NULL,
}};

usbd_class_driver_t const *usbd_app_driver_get_cb(uint8_t *driver_count)
{
    *driver_count = 1;
    return s_uac1_driver;
}

#endif
