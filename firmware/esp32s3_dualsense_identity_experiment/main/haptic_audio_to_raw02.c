#include "haptic_audio_to_raw02.h"

#include <stdbool.h>
#include <string.h>

#include "esp_log.h"

#define RAW02_SIDE_BYTES 16
#define RAW02_LOG_INTERVAL_US 100000LL
#define RAW02_MAX_INTENSITY 192

static const char *TAG = "v5.5_raw02dry";
static int64_t s_next_log_us;

static uint8_t clamp_intensity(uint32_t value)
{
    if (value > RAW02_MAX_INTENSITY) {
        return RAW02_MAX_INTENSITY;
    }
    return (uint8_t)value;
}

static uint8_t feature_intensity(uint16_t rms, uint16_t peak)
{
    uint32_t mixed = ((uint32_t)rms * 3u + peak) / 512u;
    return clamp_intensity(mixed);
}

static const char *choose_template(const dualsense_haptic_audio_features_t *f)
{
    if (f->transient) {
        return "punch";
    }
    if (f->rms_l > 4000 || f->rms_r > 4000) {
        return "continuous";
    }
    if (f->peak_l > 3000 || f->peak_r > 3000) {
        return "texture";
    }
    return "tick";
}

static void build_side(uint8_t intensity, bool transient, uint8_t out[RAW02_SIDE_BYTES])
{
    memset(out, 0, RAW02_SIDE_BYTES);
    out[0] = 0x50;
    out[1] = (uint8_t)(0x80 | ((intensity >> 2) & 0x3f));
    out[2] = transient ? 0x2a : 0x15;
    out[3] = (uint8_t)(0x20 | ((intensity >> 3) & 0x1f));
    out[4] = (uint8_t)(0x40 | ((intensity >> 1) & 0x3f));
    out[5] = transient ? 0x7f : 0x71;
}

static void hex_side(const uint8_t data[RAW02_SIDE_BYTES], char out[RAW02_SIDE_BYTES * 2 + 1])
{
    static const char hex[] = "0123456789abcdef";
    for (size_t i = 0; i < RAW02_SIDE_BYTES; i++) {
        out[i * 2] = hex[data[i] >> 4];
        out[i * 2 + 1] = hex[data[i] & 0x0f];
    }
    out[RAW02_SIDE_BYTES * 2] = '\0';
}

void haptic_audio_to_raw02_init(void)
{
    s_next_log_us = 0;
    ESP_LOGI(TAG, "[HAPTIC_TO_RAW02] dry_run=true live_forwarding=false");
}

void haptic_audio_to_raw02_process_features(
    const dualsense_haptic_audio_features_t *features,
    int64_t now_us)
{
    if (!features || !features->activity || now_us < s_next_log_us) {
        return;
    }

    uint8_t left[RAW02_SIDE_BYTES];
    uint8_t right[RAW02_SIDE_BYTES];
    uint8_t intensity_l = feature_intensity(features->rms_l, features->peak_l);
    uint8_t intensity_r = feature_intensity(features->rms_r, features->peak_r);
    char left_hex[RAW02_SIDE_BYTES * 2 + 1];
    char right_hex[RAW02_SIDE_BYTES * 2 + 1];
    const char *template_name = choose_template(features);

    build_side(intensity_l, features->transient, left);
    build_side(intensity_r, features->transient, right);
    hex_side(left, left_hex);
    hex_side(right, right_hex);

    ESP_LOGI(TAG,
             "[HAPTIC_TO_RAW02] dry_run=true template=%s intensity_l=%u intensity_r=%u left=%s right=%s",
             template_name,
             intensity_l,
             intensity_r,
             left_hex,
             right_hex);

    s_next_log_us = now_us + RAW02_LOG_INTERVAL_US;
}
