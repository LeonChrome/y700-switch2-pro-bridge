#pragma once

#include <stdint.h>

#include "dualsense_haptic_audio.h"

void haptic_audio_to_raw02_init(void);
void haptic_audio_to_raw02_process_features(
    const dualsense_haptic_audio_features_t *features,
    int64_t now_us);
