#pragma once

#include <stdbool.h>
#include <stdint.h>

#define DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE 48000
#define DUALSENSE_HAPTIC_AUDIO_CHANNELS 4
#define DUALSENSE_HAPTIC_AUDIO_BYTES_PER_SAMPLE 2

typedef struct {
    uint32_t packet_count;
    uint32_t frame_count;
    uint32_t overrun_count;
    uint16_t last_packet_len;
    uint16_t rms_l;
    uint16_t rms_r;
    uint16_t peak_l;
    uint16_t peak_r;
    bool activity;
    bool transient;
    uint8_t alt_setting;
} dualsense_haptic_audio_features_t;

void dualsense_haptic_audio_init(void);
bool dualsense_haptic_audio_snapshot(dualsense_haptic_audio_features_t *out);
