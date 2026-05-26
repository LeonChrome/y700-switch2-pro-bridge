#pragma once

#include <stdint.h>

#define GENERIC_HID_REPORT_ID 0x01
#define NINTENDO_INPUT_REPORT_ID 0x09
#define NINTENDO_OUTPUT_REPORT_ID 0x02
#define NINTENDO_REPORT_SIZE 64

typedef struct __attribute__((packed)) {
    uint16_t buttons;
    uint8_t hat;
    int8_t x;
    int8_t y;
    int8_t z;
    int8_t rz;
    uint8_t reserved;
} hid_gamepad_report_t;

void hid_report_make_neutral(hid_gamepad_report_t *report);
void hid_report_set_a(hid_gamepad_report_t *report, int pressed);
void hid_report_make_nintendo_neutral(uint8_t report[NINTENDO_REPORT_SIZE]);
