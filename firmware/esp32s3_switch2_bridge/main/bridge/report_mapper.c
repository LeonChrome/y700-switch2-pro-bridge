#include <string.h>
#include "report_mapper.h"

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
        return 0;
    }
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DRIGHT)) {
        return 2;
    }
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DDOWN)) {
        return 4;
    }
    if (switch2_state_get_button(state, SWITCH2_BUTTON_DLEFT)) {
        return 6;
    }
    return 8;
}

void report_mapper_state_to_generic_report(const switch2_state_t *state, hid_gamepad_report_t *report)
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
}
