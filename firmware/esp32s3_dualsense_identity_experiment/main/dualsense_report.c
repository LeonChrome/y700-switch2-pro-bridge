#include "dualsense_report.h"

#include <string.h>

void dualsense_report_make_neutral(uint8_t report[DUALSENSE_INPUT_PAYLOAD_SIZE])
{
    memset(report, 0, DUALSENSE_INPUT_PAYLOAD_SIZE);

    // Four sticks centered, two analog triggers released.
    report[0] = 0x80;
    report[1] = 0x80;
    report[2] = 0x80;
    report[3] = 0x80;
    report[4] = 0x00;
    report[5] = 0x00;

    // Low nibble is the hat. Value 8 is the descriptor's null position.
    report[7] = 0x08;

    // Both touch contacts are not touching.
    report[32] = 0x80;
    report[36] = 0x80;

    // Fixed 80%/complete battery and wired USB data state for Phase 2.
    report[52] = 0x28;
    report[53] = 0x08;
}

size_t dualsense_report_feature_size(uint8_t report_id)
{
    switch (report_id) {
    case 0x05:
        return 40;
    case 0x08:
        return 47;
    case 0x09:
        return 19;
    case 0x20:
        return 63;
    default:
        return 0;
    }
}

void dualsense_report_make_feature(uint8_t report_id, uint8_t *buffer, size_t len)
{
    (void)report_id;
    if (buffer && len > 0) {
        memset(buffer, 0, len);
    }
}
