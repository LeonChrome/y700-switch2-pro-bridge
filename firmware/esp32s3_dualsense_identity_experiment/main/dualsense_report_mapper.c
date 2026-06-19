#include "dualsense_report_mapper.h"

#include <string.h>
#include "gamepad_axis_math.h"

static uint8_t s_sequence;
static uint32_t s_sensor_timestamp;

#define DS5_SENSOR_TICKS_PER_USB_REPORT 12000u

static uint8_t map_hat(const internal_gamepad_state_t *state)
{
    bool up = internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_DPAD_UP);
    bool right = internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_DPAD_RIGHT);
    bool down = internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_DPAD_DOWN);
    bool left = internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_DPAD_LEFT);

    if (up && right) return 1;
    if (right && down) return 3;
    if (down && left) return 5;
    if (left && up) return 7;
    if (up) return 0;
    if (right) return 2;
    if (down) return 4;
    if (left) return 6;
    return 8;
}

static void write_i16_le(uint8_t *dst, int16_t value)
{
    dst[0] = (uint8_t)((uint16_t)value & 0xff);
    dst[1] = (uint8_t)(((uint16_t)value >> 8) & 0xff);
}

static void write_u32_le(uint8_t *dst, uint32_t value)
{
    dst[0] = (uint8_t)(value & 0xff);
    dst[1] = (uint8_t)((value >> 8) & 0xff);
    dst[2] = (uint8_t)((value >> 16) & 0xff);
    dst[3] = (uint8_t)((value >> 24) & 0xff);
}

static int16_t negate_i16(int16_t value)
{
    return value == INT16_MIN ? INT16_MAX : (int16_t)-value;
}

static void apply_sequence_and_timing(
    uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE])
{
    report[6] = s_sequence++;
    write_u32_le(report + 27, s_sensor_timestamp);
    s_sensor_timestamp += DS5_SENSOR_TICKS_PER_USB_REPORT;
}

void dualsense_report_mapper_init(void)
{
    s_sequence = 0;
    // SDL-compatible initial threshold observed in DS5 references.
    s_sensor_timestamp = 10200000u;
}

void dualsense_report_mapper_neutral(
    uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE])
{
    dualsense_report_make_neutral(report);
    apply_sequence_and_timing(report);
}

void dualsense_report_mapper_from_internal(
    const internal_gamepad_state_t *state,
    uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE],
    dualsense_input_debug_t *debug)
{
    dualsense_report_mapper_neutral(report);
    if (!state) {
        return;
    }

    uint8_t lx = gamepad_axis_12bit_to_u8(state->lx, false);
    uint8_t ly = gamepad_axis_12bit_to_u8(state->ly, true);
    uint8_t rx = gamepad_axis_12bit_to_u8(state->rx, false);
    uint8_t ry = gamepad_axis_12bit_to_u8(state->ry, true);
    uint8_t l2 = (uint8_t)((state->l2 * 255u + INTERNAL_GAMEPAD_TRIGGER_MAX / 2u) /
                           INTERNAL_GAMEPAD_TRIGGER_MAX);
    uint8_t r2 = (uint8_t)((state->r2 * 255u + INTERNAL_GAMEPAD_TRIGGER_MAX / 2u) /
                           INTERNAL_GAMEPAD_TRIGGER_MAX);
    uint8_t hat = map_hat(state);

    report[0] = lx;
    report[1] = ly;
    report[2] = rx;
    report[3] = ry;
    report[4] = l2;
    report[5] = r2;
    report[7] = hat;

    // Physical-position mapping: south/cross, east/circle, west/square,
    // north/triangle.
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_WEST)) report[7] |= 1u << 4;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_SOUTH)) report[7] |= 1u << 5;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_EAST)) report[7] |= 1u << 6;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_NORTH)) report[7] |= 1u << 7;

    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_L1)) report[8] |= 1u << 0;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_R1)) report[8] |= 1u << 1;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_L2)) report[8] |= 1u << 2;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_R2)) report[8] |= 1u << 3;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_BACK)) report[8] |= 1u << 4;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_START)) report[8] |= 1u << 5;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_LSTICK)) report[8] |= 1u << 6;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_RSTICK)) report[8] |= 1u << 7;

    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_HOME)) report[9] |= 1u << 0;
    if (internal_gamepad_state_get_button(state, INTERNAL_GAMEPAD_BUTTON_CAPTURE)) report[9] |= 1u << 1;

    int16_t accel[3] = {0, 0, 0};
    int16_t gyro[3] = {0, 0, 0};
    int16_t ds5_accel[3] = {0, 0, 0};
    int16_t ds5_gyro[3] = {0, 0, 0};
    if (state->accel_valid || state->gyro_valid) {
        accel[0] = state->accel_valid ? state->accel[0] : 0;
        accel[1] = state->accel_valid ? state->accel[1] : 0;
        accel[2] = state->accel_valid ? state->accel[2] : 0;
        gyro[0] = state->gyro_valid ? state->gyro[0] : 0;
        gyro[1] = state->gyro_valid ? state->gyro[1] : 0;
        gyro[2] = state->gyro_valid ? state->gyro[2] : 0;

        ds5_gyro[0] = negate_i16(gyro[0]);
        ds5_gyro[1] = gyro[2];
        ds5_gyro[2] = negate_i16(gyro[1]);
        ds5_accel[0] = negate_i16(accel[0]);
        ds5_accel[1] = accel[2];
        ds5_accel[2] = negate_i16(accel[1]);

        write_i16_le(report + 15, ds5_gyro[0]);
        write_i16_le(report + 17, ds5_gyro[1]);
        write_i16_le(report + 19, ds5_gyro[2]);
        write_i16_le(report + 21, ds5_accel[0]);
        write_i16_le(report + 23, ds5_accel[1]);
        write_i16_le(report + 25, ds5_accel[2]);
    }

    if (debug) {
        memset(debug, 0, sizeof(*debug));
        debug->raw_lx = state->lx;
        debug->raw_ly = state->ly;
        debug->raw_rx = state->rx;
        debug->raw_ry = state->ry;
        debug->lx = lx;
        debug->ly = ly;
        debug->rx = rx;
        debug->ry = ry;
        debug->l2 = l2;
        debug->r2 = r2;
        debug->hat = hat;
        debug->buttons = (uint16_t)report[7] | ((uint16_t)report[8] << 8);
        debug->motion_valid = state->accel_valid || state->gyro_valid;
        memcpy(debug->gyro, ds5_gyro, sizeof(ds5_gyro));
        memcpy(debug->accel, ds5_accel, sizeof(ds5_accel));
    }
}

void dualsense_report_mapper_from_pro2(const switch2_state_t *state,
                                       uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE],
                                       dualsense_input_debug_t *debug)
{
    internal_gamepad_state_t internal;
    switch2_state_to_internal(state, &internal);
    dualsense_report_mapper_from_internal(&internal, report, debug);
}
