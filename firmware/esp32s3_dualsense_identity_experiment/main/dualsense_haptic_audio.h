#pragma once

#include <stdbool.h>
#include <stdint.h>

#define DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE 48000
#ifndef DS5_AUDIO_CHANNELS
#define DS5_AUDIO_CHANNELS 4
#endif
#if DS5_AUDIO_CHANNELS < 2
#define DUALSENSE_HAPTIC_AUDIO_CHANNELS 4
#else
#define DUALSENSE_HAPTIC_AUDIO_CHANNELS DS5_AUDIO_CHANNELS
#endif
#define DUALSENSE_HAPTIC_AUDIO_BYTES_PER_SAMPLE 2

typedef struct {
    uint32_t packet_count;
    uint32_t active_packet_count;
    uint32_t silence_packet_count;
    uint32_t frame_count;
    uint32_t overrun_count;
    uint16_t last_packet_len;
    uint16_t rms_l;
    uint16_t rms_r;
    uint16_t peak_l;
    uint16_t peak_r;
    uint16_t mean_abs_l;
    uint16_t mean_abs_r;
    uint16_t envelope_l;
    uint16_t envelope_r;
    uint16_t transient_l;
    uint16_t transient_r;
    bool activity;
    bool transient;
    bool streaming;
    uint8_t alt_setting;
    uint8_t source_channels;
} dualsense_haptic_audio_features_t;

void dualsense_haptic_audio_init(void);
void dualsense_haptic_audio_set_streaming(bool streaming, uint8_t alt_setting);
void dualsense_haptic_audio_process_packet(const uint8_t *data,
                                           uint16_t len,
                                           uint8_t channels,
                                           int64_t now_us);
bool dualsense_haptic_audio_snapshot(dualsense_haptic_audio_features_t *out);
