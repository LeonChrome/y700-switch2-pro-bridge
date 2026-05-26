#pragma once

#include <stdint.h>
#include "hid_report.h"
#include "switch2_state.h"

void report_mapper_state_to_generic_report(const switch2_state_t *state, hid_gamepad_report_t *report);
void report_mapper_state_to_nintendo_report(const switch2_state_t *state, uint8_t report[NINTENDO_REPORT_SIZE]);
