#include "usb_xinput_device.h"

#include <stddef.h>
#include <string.h>
#include "app_log.h"
#include "device_config.h"
#include "esp_timer.h"
#include "normalized_rumble.h"
#include "report_rate_stats.h"
#include "tusb.h"
#include "usb_descriptors.h"
#include "usb_switch2_vendor.h"

static const char *TAG = "usb_xinput";

#define XINPUT_RUMBLE_HOLD_MS 220

#ifdef XINPUT_ELITE_EXPERIMENT
#define XINPUT_OUT_MAX_LEN 64
#define GIP_PACKET_MAX_LEN 64
#define GIP_HELLO_INTERVAL_US 500000LL
#define GIP_INPUT_PACKET_LEN 18
#define GIP_METADATA_CHUNK_PAYLOAD_MAX 58

static const uint8_t s_gip_gamepad_metadata[] = {
    0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00,
    0x02, 0x00, 0x00, 0x00, 0x14, 0x03, 0x00, 0x00,
    0x9e, 0x16, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
    0x94, 0x16, 0x00, 0x00, 0x0f, 0x00, 0x00, 0x00,
    0x57, 0x69, 0x6e, 0x64, 0x6f, 0x77, 0x73, 0x2e,
    0x58, 0x62, 0x6f, 0x78, 0x2e, 0x49, 0x6e, 0x70,
    0x75, 0x74, 0x2e, 0x47, 0x61, 0x6d, 0x65, 0x70,
    0x61, 0x64, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
    0x00, 0x24, 0x03, 0x00, 0x00, 0x22, 0x00, 0x00,
    0x00, 0x56, 0x69, 0x62, 0x72, 0x61, 0x74, 0x69,
    0x6f, 0x6e, 0x2e, 0x4c, 0x65, 0x66, 0x74, 0x54,
    0x72, 0x69, 0x67, 0x67, 0x65, 0x72, 0x4d, 0x6f,
    0x74, 0x6f, 0x72, 0x00, 0x00, 0x00, 0x01, 0x00,
    0x00, 0x00, 0x25, 0x03, 0x00, 0x00, 0x23, 0x00,
    0x00, 0x00, 0x56, 0x69, 0x62, 0x72, 0x61, 0x74,
    0x69, 0x6f, 0x6e, 0x2e, 0x52, 0x69, 0x67, 0x68,
    0x74, 0x54, 0x72, 0x69, 0x67, 0x67, 0x65, 0x72,
    0x4d, 0x6f, 0x74, 0x6f, 0x72, 0x00, 0x01, 0x00,
    0x00, 0x00, 0x26, 0x03, 0x00, 0x00, 0x20, 0x00,
    0x00, 0x00, 0x56, 0x69, 0x62, 0x72, 0x61, 0x74,
    0x69, 0x6f, 0x6e, 0x2e, 0x4c, 0x65, 0x66, 0x74,
    0x48, 0x61, 0x6e, 0x64, 0x4d, 0x6f, 0x74, 0x6f,
    0x72, 0x00, 0x01, 0x00, 0x00, 0x00, 0x27, 0x03,
    0x00, 0x00, 0x21, 0x00, 0x00, 0x00, 0x56, 0x69,
    0x62, 0x72, 0x61, 0x74, 0x69, 0x6f, 0x6e, 0x2e,
    0x52, 0x69, 0x67, 0x68, 0x74, 0x48, 0x61, 0x6e,
    0x64, 0x4d, 0x6f, 0x74, 0x6f, 0x72, 0x00,
};

TU_VERIFY_STATIC(sizeof(s_gip_gamepad_metadata) == 215,
                 "unexpected GIP gamepad metadata size");
#else
#define XINPUT_INPUT_REPORT_LEN 20
#define XINPUT_OUT_MAX_LEN 32

typedef struct __attribute__((packed)) {
    uint8_t report_id;
    uint8_t report_size;
    uint8_t buttons_low;
    uint8_t buttons_high;
    uint8_t left_trigger;
    uint8_t right_trigger;
    int16_t left_x;
    int16_t left_y;
    int16_t right_x;
    int16_t right_y;
    uint8_t reserved[6];
} xinput_input_report_t;

typedef char xinput_report_size_must_be_20[
    sizeof(xinput_input_report_t) == XINPUT_INPUT_REPORT_LEN ? 1 : -1];
#endif

static uint32_t s_out_count;
static uint16_t s_last_out_len;
static uint8_t s_last_left_motor;
static uint8_t s_last_right_motor;

#ifdef XINPUT_ELITE_EXPERIMENT
static uint8_t s_gip_seq;
static int64_t s_last_hello_us;
static bool s_gip_host_seen;
static bool s_gip_active;
static bool s_gip_metadata_pending;
static bool s_gip_metadata_complete_pending;
static uint16_t s_gip_metadata_offset;
static uint8_t s_gip_metadata_seq;
#endif

static bool xinput_mode(void)
{
    return device_config_get_mode() == XINPUT_EXPERIMENT_MODE;
}

void usb_xinput_device_init(void)
{
#ifdef XINPUT_ELITE_EXPERIMENT
    s_gip_seq = 0;
    s_last_hello_us = 0;
    s_gip_host_seen = false;
    s_gip_active = false;
    s_gip_metadata_pending = false;
    s_gip_metadata_complete_pending = false;
    s_gip_metadata_offset = 0;
    s_gip_metadata_seq = 0;
#endif
}

bool usb_xinput_device_ready(void)
{
    return xinput_mode() && tud_vendor_mounted() && !tud_suspended();
}

static void set_button_bit(const internal_gamepad_state_t *state,
                           internal_gamepad_button_t button,
                           uint8_t *byte,
                           uint8_t mask)
{
    if (internal_gamepad_state_get_button(state, button)) {
        *byte |= mask;
    }
}

static int16_t axis_to_xinput(uint16_t value, bool invert)
{
    value = internal_gamepad_state_snap_axis_center(value);
    int32_t centered = (int32_t)value - INTERNAL_GAMEPAD_AXIS_CENTER;
    int32_t scaled = centered >= 0 ?
        (centered * 32767) / 2047 :
        (centered * 32768) / 2048;
    if (invert) {
        scaled = -scaled;
    }
    if (scaled < -32768) {
        return -32768;
    }
    if (scaled > 32767) {
        return 32767;
    }
    return (int16_t)scaled;
}

#ifdef XINPUT_ELITE_EXPERIMENT
static void put_u16_le(uint8_t *dst, uint16_t value)
{
    dst[0] = (uint8_t)(value & 0xffu);
    dst[1] = (uint8_t)(value >> 8);
}

static void put_i16_le(uint8_t *dst, int16_t value)
{
    put_u16_le(dst, (uint16_t)value);
}

static void put_u64_le(uint8_t *dst, uint64_t value)
{
    for (size_t i = 0; i < 8; i++) {
        dst[i] = (uint8_t)(value >> (8u * i));
    }
}

static uint8_t gip_next_seq(void)
{
    s_gip_seq = (uint8_t)(s_gip_seq + 1u);
    if (s_gip_seq == 0) {
        s_gip_seq = 1;
    }
    return s_gip_seq;
}

static bool gip_write_bytes(const uint8_t *packet, size_t len)
{
    if (!packet || len == 0 || len > GIP_PACKET_MAX_LEN || !usb_xinput_device_ready()) {
        return false;
    }

    if (tud_vendor_write_available() < len) {
        tud_vendor_write_flush();
        if (tud_vendor_write_available() < len) {
            return false;
        }
    }

    uint32_t written = tud_vendor_write(packet, len);
    tud_vendor_write_flush();
    return written == len;
}

static bool gip_write_message(uint8_t command, uint8_t flags, uint8_t seq,
                              const uint8_t *payload, uint8_t payload_len)
{
    if (payload_len > GIP_PACKET_MAX_LEN - 4) {
        return false;
    }

    uint8_t packet[GIP_PACKET_MAX_LEN];
    packet[0] = command;
    packet[1] = flags;
    packet[2] = seq;
    packet[3] = payload_len;
    if (payload_len > 0 && payload) {
        memcpy(packet + 4, payload, payload_len);
    }
    return gip_write_bytes(packet, (size_t)payload_len + 4u);
}

static size_t gip_encode_chunk_value(uint8_t *dst, uint16_t value)
{
    size_t count = 0;
    do {
        uint8_t byte = (uint8_t)(value & 0x7fu);
        value >>= 7;
        if (value != 0) {
            byte |= 0x80u;
        }
        dst[count++] = byte;
    } while (value != 0);

    if (count == 1) {
        dst[0] |= 0x80u;
        dst[1] = 0x00;
        count = 2;
    }

    return count;
}

static bool gip_send_hello_if_due(bool force)
{
    int64_t now = esp_timer_get_time();
    if (!force && s_last_hello_us != 0 &&
        now - s_last_hello_us < GIP_HELLO_INTERVAL_US) {
        return true;
    }

    uint8_t payload[28];
    memset(payload, 0, sizeof(payload));
    put_u64_le(payload + 0, 0x45535033474c5032ULL);
    put_u16_le(payload + 8, USB_VID_XINPUT_EXPERIMENT);
    put_u16_le(payload + 10, USB_PID_XINPUT_ELITE_EXPERIMENT);
    put_u16_le(payload + 12, 5);
    put_u16_le(payload + 14, 9);
    put_u16_le(payload + 16, 1);
    put_u16_le(payload + 18, 0);
    put_u16_le(payload + 20, 1);
    put_u16_le(payload + 22, 0);
    put_u16_le(payload + 24, 0);
    put_u16_le(payload + 26, 0);

    bool ok = gip_write_message(0x02, 0x20, gip_next_seq(), payload, sizeof(payload));
    if (ok) {
        s_last_hello_us = now;
    }
    return ok;
}

static void gip_start_metadata_transfer(void)
{
    s_gip_metadata_pending = true;
    s_gip_metadata_complete_pending = false;
    s_gip_metadata_offset = 0;
    s_gip_metadata_seq = gip_next_seq();
    s_gip_active = true;
    APP_LOGI(TAG, "GIP metadata transfer requested len=%u",
             (unsigned)sizeof(s_gip_gamepad_metadata));
}

static bool gip_send_metadata_step(void)
{
    if (s_gip_metadata_complete_pending) {
        if (!gip_write_message(0x04, 0xa0, s_gip_metadata_seq, NULL, 0)) {
            return false;
        }
        s_gip_metadata_complete_pending = false;
        APP_LOGI(TAG, "GIP metadata transfer complete");
        return true;
    }

    if (!s_gip_metadata_pending) {
        return true;
    }

    uint16_t total = (uint16_t)sizeof(s_gip_gamepad_metadata);
    uint16_t remaining = (uint16_t)(total - s_gip_metadata_offset);
    uint8_t payload_len = remaining > GIP_METADATA_CHUNK_PAYLOAD_MAX ?
        GIP_METADATA_CHUNK_PAYLOAD_MAX : (uint8_t)remaining;

    uint8_t packet[GIP_PACKET_MAX_LEN];
    packet[0] = 0x04;
    packet[1] = s_gip_metadata_offset == 0 ? 0xf0 : 0xa0;
    packet[2] = s_gip_metadata_seq;
    packet[3] = payload_len;
    size_t header_len = 4;
    header_len += gip_encode_chunk_value(packet + header_len,
                                         s_gip_metadata_offset == 0 ?
                                             total : s_gip_metadata_offset);
    memcpy(packet + header_len, s_gip_gamepad_metadata + s_gip_metadata_offset,
           payload_len);

    if (!gip_write_bytes(packet, header_len + payload_len)) {
        return false;
    }

    s_gip_metadata_offset = (uint16_t)(s_gip_metadata_offset + payload_len);
    if (s_gip_metadata_offset >= total) {
        s_gip_metadata_pending = false;
        s_gip_metadata_complete_pending = true;
    }
    return true;
}

static bool gip_send_ack_for(const uint8_t *data, uint16_t len)
{
    if (!data || len < 4 || (data[1] & 0x10u) == 0) {
        return true;
    }

    uint8_t payload[9] = {
        0x00,
        data[0],
        (uint8_t)(data[1] & 0xf0u),
        data[3],
        0x00, 0x00, 0x00, 0x00, 0x00,
    };
    return gip_write_message(0x01, 0x20, gip_next_seq(), payload, sizeof(payload));
}

static uint16_t trigger_to_gip(uint16_t value, bool pressed)
{
    if (value == 0 && pressed) {
        return 1023;
    }

    uint32_t scaled = ((uint32_t)value * 1023u + (INTERNAL_GAMEPAD_TRIGGER_MAX / 2u)) /
        INTERNAL_GAMEPAD_TRIGGER_MAX;
    return scaled > 1023u ? 1023u : (uint16_t)scaled;
}

static void make_gip_input_packet(const internal_gamepad_state_t *state,
                                  uint8_t packet[GIP_INPUT_PACKET_LEN])
{
    memset(packet, 0, GIP_INPUT_PACKET_LEN);
    packet[0] = 0x20;
    packet[1] = 0x00;
    packet[2] = gip_next_seq();
    packet[3] = 0x0e;

    if (!state) {
        return;
    }

    uint8_t *payload = packet + 4;
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_START, &payload[0], 0x04);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_BACK, &payload[0], 0x08);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_SOUTH, &payload[0], 0x10);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_EAST, &payload[0], 0x20);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_WEST, &payload[0], 0x40);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_NORTH, &payload[0], 0x80);

    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_UP, &payload[1], 0x01);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_DOWN, &payload[1], 0x02);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_LEFT, &payload[1], 0x04);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_RIGHT, &payload[1], 0x08);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_L1, &payload[1], 0x10);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_R1, &payload[1], 0x20);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_LSTICK, &payload[1], 0x40);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_RSTICK, &payload[1], 0x80);

    put_u16_le(payload + 2, trigger_to_gip(
                   state->l2,
                   internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_L2)));
    put_u16_le(payload + 4, trigger_to_gip(
                   state->r2,
                   internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_R2)));
    put_i16_le(payload + 6, axis_to_xinput(state->lx, false));
    put_i16_le(payload + 8, axis_to_xinput(state->ly, false));
    put_i16_le(payload + 10, axis_to_xinput(state->rx, false));
    put_i16_le(payload + 12, axis_to_xinput(state->ry, false));
}

static bool gip_service(void)
{
    if (s_gip_metadata_pending || s_gip_metadata_complete_pending) {
        return gip_send_metadata_step();
    }

    if (!s_gip_active || !s_gip_host_seen) {
        return gip_send_hello_if_due(false);
    }

    return true;
}

static bool parse_gip_rumble_out(const uint8_t *data, uint16_t len,
                                 uint8_t *left_heavy, uint8_t *right_light)
{
    if (!data || !left_heavy || !right_light || len < 13 || data[0] != 0x09) {
        return false;
    }

    const uint8_t *payload = data + 4;
    *left_heavy = payload[4];
    *right_light = payload[5];
    return true;
}
#else
static uint8_t trigger_to_xinput(uint16_t value, bool pressed)
{
    if (value == 0 && pressed) {
        return 255;
    }
    return (uint8_t)(((uint32_t)value * 255u + (INTERNAL_GAMEPAD_TRIGGER_MAX / 2u)) /
                     INTERNAL_GAMEPAD_TRIGGER_MAX);
}

static void make_report(const internal_gamepad_state_t *state,
                        xinput_input_report_t *report)
{
    memset(report, 0, sizeof(*report));
    report->report_id = 0x00;
    report->report_size = XINPUT_INPUT_REPORT_LEN;

    if (!state) {
        report->left_x = 0;
        report->left_y = 0;
        report->right_x = 0;
        report->right_y = 0;
        return;
    }

    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_UP, &report->buttons_low, 0x01);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_DOWN, &report->buttons_low, 0x02);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_LEFT, &report->buttons_low, 0x04);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_DPAD_RIGHT, &report->buttons_low, 0x08);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_START, &report->buttons_low, 0x10);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_BACK, &report->buttons_low, 0x20);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_LSTICK, &report->buttons_low, 0x40);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_RSTICK, &report->buttons_low, 0x80);

    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_L1, &report->buttons_high, 0x01);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_R1, &report->buttons_high, 0x02);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_HOME, &report->buttons_high, 0x04);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_SOUTH, &report->buttons_high, 0x10);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_EAST, &report->buttons_high, 0x20);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_WEST, &report->buttons_high, 0x40);
    set_button_bit(state, INTERNAL_GAMEPAD_BUTTON_NORTH, &report->buttons_high, 0x80);

    report->left_trigger = trigger_to_xinput(
        state->l2,
        internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_L2));
    report->right_trigger = trigger_to_xinput(
        state->r2,
        internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_R2));
    report->left_x = axis_to_xinput(state->lx, false);
    report->left_y = axis_to_xinput(state->ly, false);
    report->right_x = axis_to_xinput(state->rx, false);
    report->right_y = axis_to_xinput(state->ry, false);
}

static bool parse_rumble_out(const uint8_t *data, uint16_t len,
                             uint8_t *left_heavy, uint8_t *right_light)
{
    if (!data || !left_heavy || !right_light || len < 4) {
        return false;
    }

    if (len >= 5 && data[1] == 0x08) {
        *left_heavy = data[3];
        *right_light = data[4];
        return true;
    }

    *left_heavy = data[2];
    *right_light = data[3];
    return true;
}
#endif

esp_err_t usb_xinput_device_send_report(const internal_gamepad_state_t *state)
{
    if (!usb_xinput_device_ready()) {
        return ESP_ERR_INVALID_STATE;
    }

#ifdef XINPUT_ELITE_EXPERIMENT
    bool metadata_busy = s_gip_metadata_pending || s_gip_metadata_complete_pending;
    bool serviced = gip_service();
    if (metadata_busy) {
        report_rate_stats_record(serviced);
        return serviced ? ESP_OK : ESP_FAIL;
    }
    uint8_t report[GIP_INPUT_PACKET_LEN];
    make_gip_input_packet(state, report);
    bool ok = gip_write_bytes(report, sizeof(report));
#else
    xinput_input_report_t report;
    make_report(state, &report);
    uint32_t written = tud_vendor_write(&report, sizeof(report));
    tud_vendor_write_flush();
    bool ok = written == sizeof(report);
#endif
    report_rate_stats_record(ok);
    return ok ? ESP_OK : ESP_FAIL;
}

void usb_xinput_device_poll_out(void)
{
    if (!xinput_mode() || !tud_vendor_mounted()) {
        return;
    }

    while (tud_vendor_available() > 0) {
        uint8_t data[XINPUT_OUT_MAX_LEN];
        uint32_t read = tud_vendor_read(data, sizeof(data));
        if (read == 0) {
            return;
        }

        s_out_count++;
        s_last_out_len = (uint16_t)read;

#ifdef XINPUT_ELITE_EXPERIMENT
        s_gip_host_seen = true;
        if (read >= 4) {
            if (data[0] == 0x04) {
                gip_start_metadata_transfer();
            } else if (data[0] == 0x05) {
                s_gip_active = true;
                APP_LOGI(TAG, "GIP device state/config command len=%u",
                         (unsigned)read);
            }
            (void)gip_send_ack_for(data, (uint16_t)read);
        }

        uint8_t left_heavy = 0;
        uint8_t right_light = 0;
        if (!parse_gip_rumble_out(data, (uint16_t)read, &left_heavy, &right_light)) {
            APP_LOGD(TAG, "GIP OUT len=%u cmd=0x%02x flags=0x%02x",
                     (unsigned)read,
                     read >= 1 ? data[0] : 0,
                     read >= 2 ? data[1] : 0);
            continue;
        }
#else
        uint8_t left_heavy = 0;
        uint8_t right_light = 0;
        if (!parse_rumble_out(data, (uint16_t)read, &left_heavy, &right_light)) {
            APP_LOGD(TAG, "XInput OUT ignored len=%u", (unsigned)read);
            continue;
        }
#endif

        s_last_left_motor = left_heavy;
        s_last_right_motor = right_light;
        if (left_heavy == 0 && right_light == 0) {
            usb_switch2_vendor_stop_hd_rumble();
#ifdef XINPUT_ELITE_EXPERIMENT
            APP_LOGI(TAG, "GIP OUT rumble stop len=%u", (unsigned)read);
#else
            APP_LOGI(TAG, "XInput OUT rumble stop len=%u", (unsigned)read);
#endif
            continue;
        }

        normalized_rumble_t rumble;
        normalized_rumble_from_dualsense_motors(
            right_light,
            left_heavy,
            XINPUT_RUMBLE_HOLD_MS,
            &rumble);
#ifdef XINPUT_ELITE_EXPERIMENT
        usb_switch2_vendor_start_normalized_rumble(&rumble, "gip-out");
        APP_LOGI(TAG, "GIP OUT rumble len=%u left_heavy=%u right_light=%u",
                 (unsigned)read,
                 (unsigned)left_heavy,
                 (unsigned)right_light);
#else
        usb_switch2_vendor_start_normalized_rumble(&rumble, "xinput-out");
        APP_LOGI(TAG, "XInput OUT rumble len=%u left_heavy=%u right_light=%u",
                 (unsigned)read,
                 (unsigned)left_heavy,
                 (unsigned)right_light);
#endif
    }
}

uint32_t usb_xinput_device_out_count(void)
{
    return s_out_count;
}

uint16_t usb_xinput_device_last_out_len(void)
{
    return s_last_out_len;
}

uint8_t usb_xinput_device_last_left_motor(void)
{
    return s_last_left_motor;
}

uint8_t usb_xinput_device_last_right_motor(void)
{
    return s_last_right_motor;
}
