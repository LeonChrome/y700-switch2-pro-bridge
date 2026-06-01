#include <string.h>
#include "esp_timer.h"
#include "report_mapper.h"

static uint8_t s_nintendo_input_seq;
static bool s_nintendo_motion_passthrough = true;
static uint8_t s_nintendo_motion_offset = REPORT_MAPPER_NINTENDO_MOTION_DEFAULT_OFFSET;
static report_mapper_motion_transform_t s_motion_transform = REPORT_MAPPER_MOTION_RAW;
static report_mapper_motion_usb_test_t s_motion_usb_test = REPORT_MAPPER_MOTION_USB_TEST_OFF;
static uint16_t s_gyro_scale = 1;
static int16_t s_gyro_deadband;
static bool s_gyro_bias_valid;
static int32_t s_gyro_bias[3];
static uint16_t s_gyro_calibration_target;
static uint16_t s_gyro_calibration_remaining;
static int64_t s_gyro_calibration_sum[3];
static int32_t s_filtered_gyro[3];

#define REPORT_MAPPER_MOTION_SAMPLE_COUNT (SWITCH2_MOTION_BLOCK_SIZE / REPORT_MAPPER_NINTENDO_MOTION_SAMPLE_SIZE)

void report_mapper_set_nintendo_motion_passthrough(bool enabled)
{
    s_nintendo_motion_passthrough = enabled;
}

bool report_mapper_get_nintendo_motion_passthrough(void)
{
    return s_nintendo_motion_passthrough;
}

bool report_mapper_set_nintendo_motion_offset(uint8_t offset)
{
    if ((uint16_t)offset + REPORT_MAPPER_NINTENDO_MOTION_SAMPLE_SIZE > NINTENDO_REPORT_SIZE) {
        return false;
    }
    s_nintendo_motion_offset = offset;
    return true;
}

uint8_t report_mapper_get_nintendo_motion_offset(void)
{
    return s_nintendo_motion_offset;
}

void report_mapper_start_gyro_calibration(uint16_t samples)
{
    if (samples == 0) {
        samples = 512;
    }
    if (samples > 4000) {
        samples = 4000;
    }
    s_gyro_calibration_target = samples;
    s_gyro_calibration_remaining = samples;
    s_gyro_calibration_sum[0] = 0;
    s_gyro_calibration_sum[1] = 0;
    s_gyro_calibration_sum[2] = 0;
    s_gyro_bias_valid = false;
    s_filtered_gyro[0] = 0;
    s_filtered_gyro[1] = 0;
    s_filtered_gyro[2] = 0;
}

uint16_t report_mapper_get_gyro_calibration_remaining(void)
{
    return s_gyro_calibration_remaining;
}

bool report_mapper_get_gyro_bias(int32_t out_bias[3])
{
    if (!out_bias) {
        return false;
    }
    out_bias[0] = s_gyro_bias[0];
    out_bias[1] = s_gyro_bias[1];
    out_bias[2] = s_gyro_bias[2];
    return s_gyro_bias_valid;
}

bool report_mapper_set_gyro_scale(uint16_t scale)
{
    if (scale < 1 || scale > 512) {
        return false;
    }
    s_gyro_scale = scale;
    return true;
}

uint16_t report_mapper_get_gyro_scale(void)
{
    return s_gyro_scale;
}

bool report_mapper_set_gyro_deadband(int16_t deadband)
{
    if (deadband < 0) {
        return false;
    }
    s_gyro_deadband = deadband;
    return true;
}

int16_t report_mapper_get_gyro_deadband(void)
{
    return s_gyro_deadband;
}

bool report_mapper_set_motion_transform(report_mapper_motion_transform_t transform)
{
    if (transform > REPORT_MAPPER_MOTION_SWAP_REVERSE) {
        return false;
    }
    s_motion_transform = transform;
    return true;
}

report_mapper_motion_transform_t report_mapper_get_motion_transform(void)
{
    return s_motion_transform;
}

const char *report_mapper_motion_transform_string(report_mapper_motion_transform_t transform)
{
    switch (transform) {
    case REPORT_MAPPER_MOTION_RAW:
        return "raw";
    case REPORT_MAPPER_MOTION_SWAP_HALVES:
        return "swap";
    case REPORT_MAPPER_MOTION_REVERSE_SAMPLES:
        return "rev";
    case REPORT_MAPPER_MOTION_SWAP_REVERSE:
        return "swaprev";
    default:
        return "unknown";
    }
}

bool report_mapper_set_motion_usb_test(report_mapper_motion_usb_test_t mode)
{
    if (mode > REPORT_MAPPER_MOTION_USB_TEST_ALL_AXES) {
        return false;
    }
    s_motion_usb_test = mode;
    return true;
}

report_mapper_motion_usb_test_t report_mapper_get_motion_usb_test(void)
{
    return s_motion_usb_test;
}

const char *report_mapper_motion_usb_test_string(report_mapper_motion_usb_test_t mode)
{
    switch (mode) {
    case REPORT_MAPPER_MOTION_USB_TEST_OFF:
        return "off";
    case REPORT_MAPPER_MOTION_USB_TEST_GYRO_SECOND:
        return "gyro2";
    case REPORT_MAPPER_MOTION_USB_TEST_GYRO_FIRST:
        return "gyro1";
    case REPORT_MAPPER_MOTION_USB_TEST_ALL_AXES:
        return "all";
    default:
        return "unknown";
    }
}

static void write_i16_le(uint8_t *dst, int16_t value)
{
    dst[0] = (uint8_t)((uint16_t)value & 0xff);
    dst[1] = (uint8_t)(((uint16_t)value >> 8) & 0xff);
}

static int16_t read_i16_le(const uint8_t *src)
{
    return (int16_t)((uint16_t)src[0] | ((uint16_t)src[1] << 8));
}

static void make_motion_usb_test(uint8_t *dst)
{
    int64_t now_us = esp_timer_get_time();
    int phase = (int)((now_us / 250000LL) & 3);
    int16_t x = phase == 0 ? 6000 : phase == 2 ? -6000 : 0;
    int16_t y = phase == 1 ? 5000 : phase == 3 ? -5000 : 0;
    int16_t z = (phase < 2) ? 3500 : -3500;

    memset(dst, 0, REPORT_MAPPER_NINTENDO_MOTION_SAMPLE_SIZE);

    if (s_motion_usb_test == REPORT_MAPPER_MOTION_USB_TEST_ALL_AXES) {
        write_i16_le(dst + 0, x);
        write_i16_le(dst + 2, y);
        write_i16_le(dst + 4, z);
        write_i16_le(dst + 6, x);
        write_i16_le(dst + 8, y);
        write_i16_le(dst + 10, z);
    } else if (s_motion_usb_test == REPORT_MAPPER_MOTION_USB_TEST_GYRO_SECOND) {
        write_i16_le(dst + 6, x);
        write_i16_le(dst + 8, y);
        write_i16_le(dst + 10, z);
    } else if (s_motion_usb_test == REPORT_MAPPER_MOTION_USB_TEST_GYRO_FIRST) {
        write_i16_le(dst + 0, x);
        write_i16_le(dst + 2, y);
        write_i16_le(dst + 4, z);
    }
}

static void copy_motion_sample(uint8_t *dst, const uint8_t *src, bool swap_halves)
{
    if (swap_halves) {
        memcpy(dst, src + 6, 6);
        memcpy(dst + 6, src, 6);
    } else {
        memcpy(dst, src, 12);
    }
}

static void copy_motion_latest_sample(uint8_t *dst, const uint8_t *src)
{
    bool swap_halves = s_motion_transform == REPORT_MAPPER_MOTION_SWAP_HALVES ||
                       s_motion_transform == REPORT_MAPPER_MOTION_SWAP_REVERSE;
    bool reverse_samples = s_motion_transform == REPORT_MAPPER_MOTION_REVERSE_SAMPLES ||
                           s_motion_transform == REPORT_MAPPER_MOTION_SWAP_REVERSE;
    uint8_t src_sample = reverse_samples ? 0 : (uint8_t)(REPORT_MAPPER_MOTION_SAMPLE_COUNT - 1);

    copy_motion_sample(dst, src + src_sample * REPORT_MAPPER_NINTENDO_MOTION_SAMPLE_SIZE, swap_halves);
}

static int16_t clamp_i16(int32_t value)
{
    if (value < -32768) {
        return -32768;
    }
    if (value > 32767) {
        return 32767;
    }
    return (int16_t)value;
}

static int32_t scale_gyro_value(int32_t value)
{
    if (s_gyro_deadband > 0 && value > -s_gyro_deadband && value < s_gyro_deadband) {
        return 0;
    }

    int32_t scale = s_gyro_scale == 0 ? 1 : s_gyro_scale;
    if (value >= 0) {
        return (value + scale / 2) / scale;
    }
    return -((-value + scale / 2) / scale);
}

static void finish_gyro_calibration(void)
{
    uint16_t target = s_gyro_calibration_target == 0 ? 1 : s_gyro_calibration_target;
    s_gyro_bias[0] = (int32_t)(s_gyro_calibration_sum[0] / target);
    s_gyro_bias[1] = (int32_t)(s_gyro_calibration_sum[1] / target);
    s_gyro_bias[2] = (int32_t)(s_gyro_calibration_sum[2] / target);
    s_gyro_bias_valid = true;
    s_filtered_gyro[0] = 0;
    s_filtered_gyro[1] = 0;
    s_filtered_gyro[2] = 0;
}

static bool gyro_filter_active(void)
{
    return s_gyro_calibration_remaining > 0 ||
           s_gyro_bias_valid ||
           s_gyro_deadband > 0 ||
           s_gyro_scale != 1;
}

static void write_filtered_motion_sample(uint8_t *dst, const uint8_t *src)
{
    uint8_t sample[REPORT_MAPPER_NINTENDO_MOTION_SAMPLE_SIZE];
    copy_motion_latest_sample(sample, src);

    if (!gyro_filter_active()) {
        memcpy(dst, sample, REPORT_MAPPER_NINTENDO_MOTION_SAMPLE_SIZE);
        return;
    }

    memcpy(dst, sample, 6);

    int32_t gyro[3] = {
        read_i16_le(sample + 6),
        read_i16_le(sample + 8),
        read_i16_le(sample + 10),
    };

    if (s_gyro_calibration_remaining > 0) {
        s_gyro_calibration_sum[0] += gyro[0];
        s_gyro_calibration_sum[1] += gyro[1];
        s_gyro_calibration_sum[2] += gyro[2];
        s_gyro_calibration_remaining--;
        if (s_gyro_calibration_remaining == 0) {
            finish_gyro_calibration();
        }
        write_i16_le(dst + 6, 0);
        write_i16_le(dst + 8, 0);
        write_i16_le(dst + 10, 0);
        return;
    }

    for (uint8_t i = 0; i < 3; i++) {
        int32_t value = gyro[i];
        if (s_gyro_bias_valid) {
            value -= s_gyro_bias[i];
        }
        value = scale_gyro_value(value);
        s_filtered_gyro[i] += (value - s_filtered_gyro[i]) / 4;
    }

    write_i16_le(dst + 6, clamp_i16(s_filtered_gyro[0]));
    write_i16_le(dst + 8, clamp_i16(s_filtered_gyro[1]));
    write_i16_le(dst + 10, clamp_i16(s_filtered_gyro[2]));
}

static void write_sensor_timestamp(uint8_t *report)
{
    uint32_t now_us = (uint32_t)esp_timer_get_time();
    report[REPORT_MAPPER_NINTENDO_MOTION_TIMESTAMP_OFFSET] = (uint8_t)(now_us & 0xff);
    report[REPORT_MAPPER_NINTENDO_MOTION_TIMESTAMP_OFFSET + 1] = (uint8_t)((now_us >> 8) & 0xff);
    report[REPORT_MAPPER_NINTENDO_MOTION_TIMESTAMP_OFFSET + 2] = (uint8_t)((now_us >> 16) & 0xff);
    report[REPORT_MAPPER_NINTENDO_MOTION_TIMESTAMP_OFFSET + 3] = (uint8_t)((now_us >> 24) & 0xff);
}

static bool motion_sample_fits(void)
{
    return (uint16_t)s_nintendo_motion_offset + REPORT_MAPPER_NINTENDO_MOTION_SAMPLE_SIZE <= NINTENDO_REPORT_SIZE;
}

static int8_t axis12_to_i8(uint16_t value)
{
    int32_t centered = (int32_t)value - 2048;
    int32_t scaled = centered / 16;
    if (scaled < -127) {
        return -127;
    }
    if (scaled > 127) {
        return 127;
    }
    return (int8_t)scaled;
}

static uint8_t map_hat(const switch2_state_t *state)
{
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DUP)) {
        return 1;
    }
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DRIGHT)) {
        return 3;
    }
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DDOWN)) {
        return 5;
    }
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DLEFT)) {
        return 7;
    }
    return 0;
}

void report_mapper_state_to_generic_report(const switch2_state_t *state, bridge_hid_gamepad_report_t *report)
{
    hid_report_make_neutral(report);
    report->hat = map_hat(state);
    report->x = axis12_to_i8(state->lx);
    report->y = axis12_to_i8(state->ly);
    report->z = axis12_to_i8(state->rx);
    report->rz = axis12_to_i8(state->ry);

    if (switch2_state_get_button(state, SWITCH2_BUTTON_A)) report->buttons |= 0x0001;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_B)) report->buttons |= 0x0002;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_X)) report->buttons |= 0x0004;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_Y)) report->buttons |= 0x0008;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_L)) report->buttons |= 0x0010;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_R)) report->buttons |= 0x0020;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_ZL)) report->buttons |= 0x0040;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_ZR)) report->buttons |= 0x0080;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_MINUS)) report->buttons |= 0x0100;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_PLUS)) report->buttons |= 0x0200;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_LSTICK)) report->buttons |= 0x0400;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_RSTICK)) report->buttons |= 0x0800;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_C)) report->buttons |= 0x1000;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_GL)) report->buttons |= 0x2000;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_GR)) report->buttons |= 0x4000;
}

static void pack12_pair(uint8_t *out, int offset, uint16_t x, uint16_t y)
{
    out[offset] = (uint8_t)(x & 0xff);
    out[offset + 1] = (uint8_t)(((x >> 8) & 0x0f) | ((y & 0x0f) << 4));
    out[offset + 2] = (uint8_t)((y >> 4) & 0xff);
}

void report_mapper_state_to_nintendo_report(const switch2_state_t *state, uint8_t report[NINTENDO_REPORT_SIZE])
{
    hid_report_make_nintendo_neutral(report);
    report[1] = s_nintendo_input_seq++;

    if (switch2_state_get_button(state, SWITCH2_BUTTON_Y)) report[5] |= 0x01;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_X)) report[5] |= 0x02;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_B)) report[5] |= 0x04;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_A)) report[5] |= 0x08;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_R)) report[5] |= 0x40;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_ZR)) report[5] |= 0x80;

    if (switch2_state_get_button(state, SWITCH2_BUTTON_MINUS)) report[6] |= 0x01;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_PLUS)) report[6] |= 0x02;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_RSTICK)) report[6] |= 0x04;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_LSTICK)) report[6] |= 0x08;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_HOME)) report[6] |= 0x10;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_CAPTURE)) report[6] |= 0x20;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_C)) report[6] |= 0x40;

    if (switch2_state_get_button(state, SWITCH2_BUTTON_DDOWN)) report[7] |= 0x01;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DUP)) report[7] |= 0x02;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DRIGHT)) report[7] |= 0x04;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DLEFT)) report[7] |= 0x08;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_L)) report[7] |= 0x40;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_ZL)) report[7] |= 0x80;

    if (switch2_state_get_button(state, SWITCH2_BUTTON_GR)) report[8] |= 0x01;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_GL)) report[8] |= 0x02;

    pack12_pair(report, 11, state->lx, state->ly);
    pack12_pair(report, 14, state->rx, state->ry);

    write_sensor_timestamp(report);

    if (motion_sample_fits() && s_motion_usb_test != REPORT_MAPPER_MOTION_USB_TEST_OFF) {
        make_motion_usb_test(report + s_nintendo_motion_offset);
    } else if (motion_sample_fits() && s_nintendo_motion_passthrough && state->motion_valid) {
        write_filtered_motion_sample(report + s_nintendo_motion_offset, state->motion);
    }
}
