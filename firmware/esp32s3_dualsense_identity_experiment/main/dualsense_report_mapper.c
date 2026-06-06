#include "dualsense_report_mapper.h"

#include <string.h>

static uint8_t s_sequence;
static uint32_t s_report_counter;
static uint32_t s_sensor_timestamp;

static uint8_t axis12_to_u8(uint16_t value)
{
    uint32_t clamped = value > 4095 ? 4095 : value;
    return (uint8_t)((clamped * 255u + 2047u) / 4095u);
}

static uint8_t map_hat(const switch2_state_t *state)
{
    bool up = switch2_state_get_button(state, SWITCH2_BUTTON_DUP);
    bool right = switch2_state_get_button(state, SWITCH2_BUTTON_DRIGHT);
    bool down = switch2_state_get_button(state, SWITCH2_BUTTON_DDOWN);
    bool left = switch2_state_get_button(state, SWITCH2_BUTTON_DLEFT);

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

static int16_t read_i16_le(const uint8_t *src)
{
    return (int16_t)((uint16_t)src[0] | ((uint16_t)src[1] << 8));
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

void dualsense_report_mapper_from_pro2(const switch2_state_t *state,
                                       uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE],
                                       dualsense_input_debug_t *debug)
{
    dualsense_report_mapper_neutral(report);
    if (!state) {
        return;
    }

    uint8_t lx = axis12_to_u8(state->lx);
    uint8_t ly = axis12_to_u8(state->ly);
    uint8_t rx = axis12_to_u8(state->rx);
    uint8_t ry = axis12_to_u8(state->ry);
    uint8_t l2 = switch2_state_get_button(state, SWITCH2_BUTTON_ZL) ? 0xff : 0x00;
    uint8_t r2 = switch2_state_get_button(state, SWITCH2_BUTTON_ZR) ? 0xff : 0x00;
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
    if (switch2_state_get_button(state, SWITCH2_BUTTON_Y)) report[7] |= 1u << 4;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_B)) report[7] |= 1u << 5;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_A)) report[7] |= 1u << 6;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_X)) report[7] |= 1u << 7;

    if (switch2_state_get_button(state, SWITCH2_BUTTON_L)) report[8] |= 1u << 0;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_R)) report[8] |= 1u << 1;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_ZL)) report[8] |= 1u << 2;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_ZR)) report[8] |= 1u << 3;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_MINUS)) report[8] |= 1u << 4;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_PLUS)) report[8] |= 1u << 5;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_LSTICK)) report[8] |= 1u << 6;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_RSTICK)) report[8] |= 1u << 7;

    if (switch2_state_get_button(state, SWITCH2_BUTTON_HOME)) report[9] |= 1u << 0;
    if (switch2_state_get_button(state, SWITCH2_BUTTON_CAPTURE)) report[9] |= 1u << 1;

    int16_t accel[3] = {0, 0, 0};
    int16_t gyro[3] = {0, 0, 0};
    if (state->motion_valid) {
        const uint8_t *sample = state->motion + 2 * SWITCH2_MOTION_SAMPLE_SIZE;
        accel[0] = read_i16_le(sample + 0);
        accel[1] = read_i16_le(sample + 2);
        accel[2] = read_i16_le(sample + 4);
        gyro[0] = read_i16_le(sample + 6);
        gyro[1] = read_i16_le(sample + 8);
        gyro[2] = read_i16_le(sample + 10);

        write_i16_le(report + 15, gyro[0]);
        write_i16_le(report + 17, gyro[2]);
        write_i16_le(report + 19, gyro[1]);
        write_i16_le(report + 21, accel[0]);
        write_i16_le(report + 23, accel[1]);
        write_i16_le(report + 25, accel[2]);
    }

    if (debug) {
        memset(debug, 0, sizeof(*debug));
        debug->lx = lx;
        debug->ly = ly;
        debug->rx = rx;
        debug->ry = ry;
        debug->l2 = l2;
        debug->r2 = r2;
        debug->hat = hat;
        debug->buttons = (uint16_t)report[7] | ((uint16_t)report[8] << 8);
        debug->motion_valid = state->motion_valid;
        memcpy(debug->gyro, gyro, sizeof(gyro));
        memcpy(debug->accel, accel, sizeof(accel));
    }
}
