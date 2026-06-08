#include "dualsense_report_mapper.h"

#include <string.h>

static uint8_t s_sequence;
static uint32_t s_report_counter;
static uint32_t s_sensor_timestamp;

#define DS5_AXIS_CENTER_12BIT INTERNAL_GAMEPAD_AXIS_CENTER
#define DS5_AXIS_OUTPUT_DEADZONE INTERNAL_GAMEPAD_AXIS_CENTER_DEADBAND

static bool axis12_is_centered(uint16_t value)
{
    int32_t delta = (int32_t)value - DS5_AXIS_CENTER_12BIT;
    if (delta < 0) {
        delta = -delta;
    }
    return delta <= DS5_AXIS_OUTPUT_DEADZONE;
}

static uint8_t axis12_to_u8(uint16_t value)
{
    value = internal_gamepad_state_snap_axis_center(value);
    if (axis12_is_centered(value)) {
        return 0x80;
    }
    uint32_t clamped = value > 4095 ? 4095 : value;
    return (uint8_t)((clamped * 255u + 2047u) / 4095u);
}

static uint8_t axis12_to_u8_inverted(uint16_t value)
{
    if (axis12_is_centered(value)) {
        return 0x80;
    }
    return (uint8_t)(0xffu - axis12_to_u8(value));
}

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

static void apply_sequence_and_timing(
    uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE])
{
    report[6] = s_sequence++;
    write_u32_le(report + 11, s_report_counter++);
    write_u32_le(report + 27, s_sensor_timestamp);
    write_u32_le(report + 48, s_sensor_timestamp);
    s_sensor_timestamp += 4000u;
}

void dualsense_report_mapper_init(void)
{
    s_sequence = 0;
    s_report_counter = 0;
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

    uint8_t lx = axis12_to_u8(state->lx);
    uint8_t ly = axis12_to_u8_inverted(state->ly);
    uint8_t rx = axis12_to_u8(state->rx);
    uint8_t ry = axis12_to_u8_inverted(state->ry);
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
    if (state->accel_valid || state->gyro_valid) {
        accel[0] = state->accel_valid ? state->accel[0] : 0;
        accel[1] = state->accel_valid ? state->accel[1] : 0;
        accel[2] = state->accel_valid ? state->accel[2] : 0;
        gyro[0] = state->gyro_valid ? state->gyro[0] : 0;
        gyro[1] = state->gyro_valid ? state->gyro[1] : 0;
        gyro[2] = state->gyro_valid ? state->gyro[2] : 0;

        write_i16_le(report + 15, gyro[0]);
        write_i16_le(report + 17, gyro[2]);
        write_i16_le(report + 19, gyro[1]);
        write_i16_le(report + 21, accel[0]);
        write_i16_le(report + 23, accel[1]);
        write_i16_le(report + 25, accel[2]);
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
        memcpy(debug->gyro, gyro, sizeof(gyro));
        memcpy(debug->accel, accel, sizeof(accel));
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
