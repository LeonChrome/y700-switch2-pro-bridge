#include <string.h>
#include "switch2_state.h"

#define CENTER_12BIT 2048

void switch2_state_init(void)
{
}

void switch2_state_reset(switch2_state_t *state)
{
    memset(state, 0, sizeof(*state));
    state->lx = CENTER_12BIT;
    state->ly = CENTER_12BIT;
    state->rx = CENTER_12BIT;
    state->ry = CENTER_12BIT;
}

void switch2_state_set_button(switch2_state_t *state, switch2_button_t button, bool pressed)
{
    if (button >= SWITCH2_BUTTON_COUNT) {
        return;
    }
    uint32_t mask = 1u << button;
    if (pressed) {
        state->buttons |= mask;
    } else {
        state->buttons &= ~mask;
    }
}

bool switch2_state_get_button(const switch2_state_t *state, switch2_button_t button)
{
    if (button >= SWITCH2_BUTTON_COUNT) {
        return false;
    }
    return (state->buttons & (1u << button)) != 0;
}

void switch2_state_update_from_legacy_bytes(switch2_state_t *state, uint8_t b2, uint8_t b3, uint8_t b4)
{
    switch2_state_reset(state);
    switch2_state_set_button(state, SWITCH2_BUTTON_B, (b2 & 0x01) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_A, (b2 & 0x02) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_Y, (b2 & 0x04) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_X, (b2 & 0x08) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_R, (b2 & 0x10) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_ZR, (b2 & 0x20) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_PLUS, (b2 & 0x40) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_RSTICK, (b2 & 0x80) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DDOWN, (b3 & 0x01) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DRIGHT, (b3 & 0x02) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DLEFT, (b3 & 0x04) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DUP, (b3 & 0x08) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_L, (b3 & 0x10) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_ZL, (b3 & 0x20) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_MINUS, (b3 & 0x40) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_LSTICK, (b3 & 0x80) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_HOME, (b4 & 0x01) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_CAPTURE, (b4 & 0x02) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_GR, (b4 & 0x04) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_GL, (b4 & 0x08) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_C, (b4 & 0x10) != 0);
}

void switch2_state_update_from_fd2_buttons(switch2_state_t *state, uint32_t buttons)
{
    switch2_state_reset(state);
    switch2_state_set_button(state, SWITCH2_BUTTON_Y, (buttons & 0x00000001) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_X, (buttons & 0x00000002) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_B, (buttons & 0x00000004) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_A, (buttons & 0x00000008) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_R, (buttons & 0x00000040) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_ZR, (buttons & 0x00000080) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_MINUS, (buttons & 0x00000100) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_PLUS, (buttons & 0x00000200) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_RSTICK, (buttons & 0x00000400) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_LSTICK, (buttons & 0x00000800) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_HOME, (buttons & 0x00001000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_CAPTURE, (buttons & 0x00002000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_C, (buttons & 0x00004000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DDOWN, (buttons & 0x00010000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DUP, (buttons & 0x00020000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DRIGHT, (buttons & 0x00040000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_DLEFT, (buttons & 0x00080000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_L, (buttons & 0x00400000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_ZL, (buttons & 0x00800000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_GR, (buttons & 0x01000000) != 0);
    switch2_state_set_button(state, SWITCH2_BUTTON_GL, (buttons & 0x02000000) != 0);
}
