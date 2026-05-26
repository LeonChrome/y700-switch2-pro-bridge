#include <string.h>
#include "hid_report.h"

void hid_report_make_neutral(hid_gamepad_report_t *report)
{
    memset(report, 0, sizeof(*report));
    report->hat = 8;
}

void hid_report_set_a(hid_gamepad_report_t *report, int pressed)
{
    if (pressed) {
        report->buttons |= 0x0001;
    } else {
        report->buttons &= (uint16_t)~0x0001;
    }
}

void hid_report_make_nintendo_neutral(uint8_t report[NINTENDO_REPORT_SIZE])
{
    memset(report, 0, NINTENDO_REPORT_SIZE);
    report[0] = NINTENDO_INPUT_REPORT_ID;
    report[2] = 0x20;
    report[11] = 0x00;
    report[12] = 0x80;
    report[13] = 0x00;
    report[14] = 0x00;
    report[15] = 0x80;
    report[16] = 0x00;
}
