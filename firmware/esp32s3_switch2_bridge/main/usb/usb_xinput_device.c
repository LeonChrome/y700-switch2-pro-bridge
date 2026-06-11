#include "usb_xinput_device.h"

#include <string.h>
#include "app_log.h"
#include "device_config.h"
#include "normalized_rumble.h"
#include "report_rate_stats.h"
#include "tusb.h"
#include "usb_switch2_vendor.h"

static const char *TAG = "usb_xinput";

#define XINPUT_INPUT_REPORT_LEN 20
#define XINPUT_OUT_MAX_LEN 32
#define XINPUT_RUMBLE_HOLD_MS 220

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

static uint32_t s_out_count;
static uint16_t s_last_out_len;
static uint8_t s_last_left_motor;
static uint8_t s_last_right_motor;

static bool xinput_mode(void)
{
    return device_config_get_mode() == XINPUT_EXPERIMENT_MODE;
}

void usb_xinput_device_init(void)
{
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

esp_err_t usb_xinput_device_send_report(const internal_gamepad_state_t *state)
{
    if (!usb_xinput_device_ready()) {
        return ESP_ERR_INVALID_STATE;
    }

    xinput_input_report_t report;
    make_report(state, &report);
    uint32_t written = tud_vendor_write(&report, sizeof(report));
    tud_vendor_write_flush();
    bool ok = written == sizeof(report);
    report_rate_stats_record(ok);
    return ok ? ESP_OK : ESP_FAIL;
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

        uint8_t left_heavy = 0;
        uint8_t right_light = 0;
        s_out_count++;
        s_last_out_len = (uint16_t)read;

        if (!parse_rumble_out(data, (uint16_t)read, &left_heavy, &right_light)) {
            APP_LOGD(TAG, "XInput OUT ignored len=%u", (unsigned)read);
            continue;
        }

        s_last_left_motor = left_heavy;
        s_last_right_motor = right_light;
        if (left_heavy == 0 && right_light == 0) {
            usb_switch2_vendor_stop_hd_rumble();
            APP_LOGI(TAG, "XInput OUT rumble stop len=%u", (unsigned)read);
            continue;
        }

        normalized_rumble_t rumble;
        normalized_rumble_from_dualsense_motors(
            right_light,
            left_heavy,
            XINPUT_RUMBLE_HOLD_MS,
            &rumble);
        usb_switch2_vendor_start_normalized_rumble(&rumble, "xinput-out");
        APP_LOGI(TAG, "XInput OUT rumble len=%u left_heavy=%u right_light=%u",
                 (unsigned)read,
                 (unsigned)left_heavy,
                 (unsigned)right_light);
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
