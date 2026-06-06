#include "haptic_audio_to_raw02.h"

#include <ctype.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/portmacro.h"
#include "pro2_input_backend.h"
#include "pro2_rumble_backend.h"

#define RAW02_LEFT_OFFSET 1
#define RAW02_RIGHT_OFFSET 17
#define RAW02_LOG_INTERVAL_US 100000LL
#define RAW02_DEFAULT_MAX_INTENSITY 96
#define RAW02_DEFAULT_MIN_INTERVAL_MS 50
#define RAW02_DEFAULT_SILENCE_TIMEOUT_MS 100
#define RAW02_DEFAULT_ACTIVITY_THRESHOLD 512

static const char *TAG = "v5.5_haptic_raw02";
static portMUX_TYPE s_lock = portMUX_INITIALIZER_UNLOCKED;
static haptic_raw02_status_t s_status;
static int64_t s_last_send_us;
static int64_t s_last_activity_us;
static int64_t s_next_log_us;
static bool s_stop_sent;

static uint8_t clamp_u8_int(int value)
{
    if (value < 0) {
        return 0;
    }
    if (value > 255) {
        return 255;
    }
    return (uint8_t)value;
}

static uint16_t clamp_u16_int(int value, uint16_t min_value, uint16_t max_value)
{
    if (value < (int)min_value) {
        return min_value;
    }
    if (value > (int)max_value) {
        return max_value;
    }
    return (uint16_t)value;
}

static int hex_value(char c)
{
    if (c >= '0' && c <= '9') {
        return c - '0';
    }
    if (c >= 'a' && c <= 'f') {
        return c - 'a' + 10;
    }
    if (c >= 'A' && c <= 'F') {
        return c - 'A' + 10;
    }
    return -1;
}

static bool decode_hex_exact(const char *hex, size_t hex_len, uint8_t *out, size_t out_len)
{
    if (!hex || !out || hex_len != out_len * 2 || (hex_len % 2) != 0) {
        return false;
    }
    for (size_t i = 0; i < out_len; i++) {
        int hi = hex_value(hex[i * 2]);
        int lo = hex_value(hex[i * 2 + 1]);
        if (hi < 0 || lo < 0) {
            return false;
        }
        out[i] = (uint8_t)((hi << 4) | lo);
    }
    return true;
}

static bool is_hex_string(const char *hex, size_t hex_len)
{
    if (!hex) {
        return false;
    }
    for (size_t i = 0; i < hex_len; i++) {
        if (hex_value(hex[i]) < 0) {
            return false;
        }
    }
    return true;
}

static void bytes_to_hex(const uint8_t *bytes, size_t len, char *out, size_t out_len)
{
    static const char lut[] = "0123456789abcdef";
    if (!out || out_len == 0) {
        return;
    }
    if (!bytes || out_len < (len * 2u + 1u)) {
        out[0] = 0;
        return;
    }
    for (size_t i = 0; i < len; i++) {
        out[i * 2] = lut[(bytes[i] >> 4) & 0x0f];
        out[i * 2 + 1] = lut[bytes[i] & 0x0f];
    }
    out[len * 2] = 0;
}

const char *haptic_audio_to_raw02_mode_string(haptic_raw02_mode_t mode)
{
    switch (mode) {
    case HAPTIC_RAW02_MODE_AUTO:
        return "auto";
    case HAPTIC_RAW02_MODE_TICK:
        return "tick";
    case HAPTIC_RAW02_MODE_PUNCH:
        return "punch";
    case HAPTIC_RAW02_MODE_CONTINUOUS:
        return "continuous";
    case HAPTIC_RAW02_MODE_TEXTURE:
        return "texture";
    case HAPTIC_RAW02_MODE_SILENCE:
        return "silence";
    default:
        return "auto";
    }
}

bool haptic_audio_to_raw02_parse_mode(const char *text, haptic_raw02_mode_t *out_mode)
{
    if (!text || !out_mode) {
        return false;
    }
    if (strcmp(text, "auto") == 0) {
        *out_mode = HAPTIC_RAW02_MODE_AUTO;
    } else if (strcmp(text, "tick") == 0) {
        *out_mode = HAPTIC_RAW02_MODE_TICK;
    } else if (strcmp(text, "punch") == 0) {
        *out_mode = HAPTIC_RAW02_MODE_PUNCH;
    } else if (strcmp(text, "continuous") == 0) {
        *out_mode = HAPTIC_RAW02_MODE_CONTINUOUS;
    } else if (strcmp(text, "texture") == 0) {
        *out_mode = HAPTIC_RAW02_MODE_TEXTURE;
    } else if (strcmp(text, "silence") == 0 || strcmp(text, "stop") == 0) {
        *out_mode = HAPTIC_RAW02_MODE_SILENCE;
    } else {
        return false;
    }
    return true;
}

void haptic_audio_to_raw02_defaults(void)
{
    portENTER_CRITICAL(&s_lock);
    memset(&s_status, 0, sizeof(s_status));
    s_status.dry_run = true;
    s_status.live_forwarding = false;
    s_status.ble_required = true;
    s_status.max_intensity = RAW02_DEFAULT_MAX_INTENSITY;
    s_status.gain = 1.0f;
    s_status.transient_gain = 0.65f;
    s_status.min_interval_ms = RAW02_DEFAULT_MIN_INTERVAL_MS;
    s_status.silence_timeout_ms = RAW02_DEFAULT_SILENCE_TIMEOUT_MS;
    s_status.activity_threshold = RAW02_DEFAULT_ACTIVITY_THRESHOLD;
    s_status.mode = HAPTIC_RAW02_MODE_AUTO;
    snprintf(s_status.last_mode, sizeof(s_status.last_mode), "%s", "auto");
    snprintf(s_status.last_error, sizeof(s_status.last_error), "%s", "none");
    portEXIT_CRITICAL(&s_lock);
    s_last_send_us = 0;
    s_last_activity_us = 0;
    s_next_log_us = 0;
    s_stop_sent = true;
}

void haptic_audio_to_raw02_init(void)
{
    haptic_audio_to_raw02_defaults();
    ESP_LOGI(TAG,
             "[HAPTIC_TO_RAW02] dry_run=true live_forwarding=false max_intensity=%u min_interval_ms=%u",
             RAW02_DEFAULT_MAX_INTENSITY,
             RAW02_DEFAULT_MIN_INTERVAL_MS);
}

static haptic_raw02_mode_t choose_effect_mode(const dualsense_haptic_audio_features_t *features,
                                              haptic_raw02_mode_t requested)
{
    if (requested != HAPTIC_RAW02_MODE_AUTO) {
        return requested;
    }
    if (!features || !features->activity) {
        return HAPTIC_RAW02_MODE_SILENCE;
    }
    if (features->transient) {
        return HAPTIC_RAW02_MODE_PUNCH;
    }
    if (features->envelope_l > 5000 || features->envelope_r > 5000) {
        return HAPTIC_RAW02_MODE_CONTINUOUS;
    }
    if (features->peak_l > 3000 || features->peak_r > 3000) {
        return HAPTIC_RAW02_MODE_TEXTURE;
    }
    return HAPTIC_RAW02_MODE_TICK;
}

static uint8_t calculate_intensity(uint16_t envelope,
                                   uint16_t transient,
                                   const haptic_raw02_status_t *config)
{
    float env_part = ((float)envelope / 32768.0f) * 255.0f * config->gain;
    float transient_part = ((float)transient / 32768.0f) * 255.0f * config->transient_gain;
    int value = (int)(env_part + transient_part + 0.5f);
    if (value > config->max_intensity) {
        value = config->max_intensity;
    }
    return clamp_u8_int(value);
}

static void build_side(uint8_t intensity,
                       haptic_raw02_mode_t mode,
                       uint8_t out[HAPTIC_RAW02_SIDE_BYTES])
{
    memset(out, 0, HAPTIC_RAW02_SIDE_BYTES);
    out[0] = 0x50;
    if (mode == HAPTIC_RAW02_MODE_SILENCE || intensity == 0) {
        return;
    }

    uint8_t shaped = intensity;
    if (mode == HAPTIC_RAW02_MODE_TICK && shaped > 48) {
        shaped = 48;
    } else if (mode == HAPTIC_RAW02_MODE_TEXTURE && shaped < 18) {
        shaped = 18;
    } else if (mode == HAPTIC_RAW02_MODE_PUNCH && shaped < 32) {
        shaped = 32;
    }

    out[1] = (uint8_t)(0x80 | ((shaped >> 2) & 0x3f));
    out[2] = mode == HAPTIC_RAW02_MODE_PUNCH ? 0x2a :
             (mode == HAPTIC_RAW02_MODE_TEXTURE ? 0x1b : 0x15);
    out[3] = (uint8_t)(0x20 | ((shaped >> 3) & 0x1f));
    out[4] = (uint8_t)(0x40 | ((shaped >> 1) & 0x3f));
    out[5] = mode == HAPTIC_RAW02_MODE_PUNCH ? 0x7f :
             (mode == HAPTIC_RAW02_MODE_CONTINUOUS ? 0x78 : 0x71);
}

static void build_payload(const uint8_t left[HAPTIC_RAW02_SIDE_BYTES],
                          const uint8_t right[HAPTIC_RAW02_SIDE_BYTES],
                          uint8_t payload[HAPTIC_RAW02_PAYLOAD_BYTES])
{
    memset(payload, 0, HAPTIC_RAW02_PAYLOAD_BYTES);
    payload[0] = 0x02;
    memcpy(payload + RAW02_LEFT_OFFSET, left, HAPTIC_RAW02_SIDE_BYTES);
    memcpy(payload + RAW02_RIGHT_OFFSET, right, HAPTIC_RAW02_SIDE_BYTES);
}

static bool ble_connected(void)
{
    return strcmp(pro2_input_backend_state(), "connected") == 0;
}

static esp_err_t maybe_send_payload(const uint8_t payload[HAPTIC_RAW02_PAYLOAD_BYTES],
                                    const haptic_raw02_status_t *config,
                                    bool stop_packet)
{
    if (config->dry_run || !config->live_forwarding) {
        return ESP_OK;
    }
    if (config->ble_required && !ble_connected()) {
        return ESP_ERR_INVALID_STATE;
    }
    esp_err_t err = pro2_rumble_backend_send_raw02_payload(payload,
                                                            HAPTIC_RAW02_PAYLOAD_BYTES);
    if (err != ESP_OK && !stop_packet) {
        haptic_audio_to_raw02_set_live_forwarding(false);
    }
    return err;
}

static void remember_last(const uint8_t left[HAPTIC_RAW02_SIDE_BYTES],
                          const uint8_t right[HAPTIC_RAW02_SIDE_BYTES],
                          const char *mode,
                          const char *error)
{
    portENTER_CRITICAL(&s_lock);
    bytes_to_hex(left, HAPTIC_RAW02_SIDE_BYTES,
                 s_status.last_left_hex, sizeof(s_status.last_left_hex));
    bytes_to_hex(right, HAPTIC_RAW02_SIDE_BYTES,
                 s_status.last_right_hex, sizeof(s_status.last_right_hex));
    snprintf(s_status.last_mode, sizeof(s_status.last_mode), "%s", mode ? mode : "auto");
    snprintf(s_status.last_error, sizeof(s_status.last_error), "%s", error ? error : "none");
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_process_features(
    const dualsense_haptic_audio_features_t *features,
    int64_t now_us)
{
    if (!features) {
        return;
    }

    haptic_raw02_status_t config;
    portENTER_CRITICAL(&s_lock);
    s_status.feature_packets++;
    if (features->activity) {
        s_status.active_packets++;
    } else {
        s_status.silence_packets++;
    }
    config = s_status;
    portEXIT_CRITICAL(&s_lock);

    if (!features->activity ||
        (features->envelope_l < config.activity_threshold &&
         features->envelope_r < config.activity_threshold &&
         features->peak_l < config.activity_threshold &&
         features->peak_r < config.activity_threshold)) {
        portENTER_CRITICAL(&s_lock);
        s_status.dropped_silence++;
        portEXIT_CRITICAL(&s_lock);
        if (!s_stop_sent && s_last_activity_us > 0 &&
            now_us - s_last_activity_us >= (int64_t)config.silence_timeout_ms * 1000LL) {
            uint8_t side[HAPTIC_RAW02_SIDE_BYTES];
            uint8_t payload[HAPTIC_RAW02_PAYLOAD_BYTES];
            build_side(0, HAPTIC_RAW02_MODE_SILENCE, side);
            build_payload(side, side, payload);
            esp_err_t err = maybe_send_payload(payload, &config, true);
            if (!config.dry_run && config.live_forwarding && err == ESP_OK) {
                portENTER_CRITICAL(&s_lock);
                s_status.raw02_live_packets++;
                s_status.ble_writes++;
                portEXIT_CRITICAL(&s_lock);
            }
            remember_last(side, side, "silence", err == ESP_OK ? "none" : esp_err_to_name(err));
            s_stop_sent = true;
        }
        return;
    }

    if (now_us - s_last_send_us < (int64_t)config.min_interval_ms * 1000LL) {
        portENTER_CRITICAL(&s_lock);
        s_status.dropped_rate++;
        portEXIT_CRITICAL(&s_lock);
        return;
    }

    haptic_raw02_mode_t mode = choose_effect_mode(features, config.mode);
    uint8_t intensity_l = calculate_intensity(features->envelope_l,
                                              features->transient_l,
                                              &config);
    uint8_t intensity_r = calculate_intensity(features->envelope_r,
                                              features->transient_r,
                                              &config);
    uint8_t left[HAPTIC_RAW02_SIDE_BYTES];
    uint8_t right[HAPTIC_RAW02_SIDE_BYTES];
    uint8_t payload[HAPTIC_RAW02_PAYLOAD_BYTES];

    build_side(intensity_l, mode, left);
    build_side(intensity_r, mode, right);
    build_payload(left, right, payload);

    const char *error = "none";
    bool live_attempt = config.live_forwarding && !config.dry_run;
    esp_err_t err = ESP_OK;
    if (live_attempt && config.ble_required && !ble_connected()) {
        err = ESP_ERR_INVALID_STATE;
        error = "no_ble";
        portENTER_CRITICAL(&s_lock);
        s_status.dropped_no_ble++;
        portEXIT_CRITICAL(&s_lock);
    } else {
        err = maybe_send_payload(payload, &config, false);
        if (err != ESP_OK) {
            error = esp_err_to_name(err);
        }
    }

    portENTER_CRITICAL(&s_lock);
    if (live_attempt && err == ESP_OK) {
        s_status.raw02_live_packets++;
        s_status.ble_writes++;
    } else if (live_attempt && err != ESP_OK) {
        s_status.ble_errors++;
    } else {
        s_status.raw02_dry_packets++;
    }
    portEXIT_CRITICAL(&s_lock);

    remember_last(left, right, haptic_audio_to_raw02_mode_string(mode), error);
    s_last_send_us = now_us;
    s_last_activity_us = now_us;
    s_stop_sent = false;

    if (now_us >= s_next_log_us || err != ESP_OK || mode == HAPTIC_RAW02_MODE_PUNCH) {
        s_next_log_us = now_us + RAW02_LOG_INTERVAL_US;
        char left_hex[HAPTIC_RAW02_SIDE_BYTES * 2 + 1];
        char right_hex[HAPTIC_RAW02_SIDE_BYTES * 2 + 1];
        bytes_to_hex(left, HAPTIC_RAW02_SIDE_BYTES, left_hex, sizeof(left_hex));
        bytes_to_hex(right, HAPTIC_RAW02_SIDE_BYTES, right_hex, sizeof(right_hex));
        ESP_LOGI(TAG,
                 "[HAPTIC_TO_RAW02] dry_run=%s live_forwarding=%s mode=%s intensity_l=%u intensity_r=%u left=%s right=%s raw02_packets_dry=%lu raw02_packets_live=%lu error=%s",
                 config.dry_run ? "true" : "false",
                 config.live_forwarding ? "true" : "false",
                 haptic_audio_to_raw02_mode_string(mode),
                 intensity_l,
                 intensity_r,
                 left_hex,
                 right_hex,
                 (unsigned long)s_status.raw02_dry_packets,
                 (unsigned long)s_status.raw02_live_packets,
                 error);
    }
}

void haptic_audio_to_raw02_note_audio_stopped(int64_t now_us)
{
    haptic_raw02_status_t config;
    portENTER_CRITICAL(&s_lock);
    config = s_status;
    portEXIT_CRITICAL(&s_lock);

    uint8_t side[HAPTIC_RAW02_SIDE_BYTES];
    uint8_t payload[HAPTIC_RAW02_PAYLOAD_BYTES];
    build_side(0, HAPTIC_RAW02_MODE_SILENCE, side);
    build_payload(side, side, payload);
    esp_err_t err = maybe_send_payload(payload, &config, true);
    remember_last(side, side, "silence", err == ESP_OK ? "none" : esp_err_to_name(err));
    s_stop_sent = true;
    s_last_send_us = now_us;
    ESP_LOGI(TAG,
             "[HAPTIC_TO_RAW02] audio_stopped=true dry_run=%s live_forwarding=%s sent=%s error=%s",
             config.dry_run ? "true" : "false",
             config.live_forwarding ? "true" : "false",
             err == ESP_OK ? "true" : "false",
             err == ESP_OK ? "none" : esp_err_to_name(err));
}

void haptic_audio_to_raw02_snapshot(haptic_raw02_status_t *out)
{
    if (!out) {
        return;
    }
    portENTER_CRITICAL(&s_lock);
    *out = s_status;
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_set_live_forwarding(bool enabled)
{
    portENTER_CRITICAL(&s_lock);
    s_status.live_forwarding = enabled;
    portEXIT_CRITICAL(&s_lock);
    if (!enabled) {
        haptic_audio_to_raw02_note_audio_stopped(esp_timer_get_time());
    }
}

void haptic_audio_to_raw02_set_dry_run(bool enabled)
{
    portENTER_CRITICAL(&s_lock);
    s_status.dry_run = enabled;
    portEXIT_CRITICAL(&s_lock);
    if (enabled) {
        haptic_audio_to_raw02_note_audio_stopped(esp_timer_get_time());
    }
}

void haptic_audio_to_raw02_set_max_intensity(uint8_t value)
{
    portENTER_CRITICAL(&s_lock);
    s_status.max_intensity = value;
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_set_gain(float value)
{
    if (value < 0.0f) {
        value = 0.0f;
    }
    if (value > 8.0f) {
        value = 8.0f;
    }
    portENTER_CRITICAL(&s_lock);
    s_status.gain = value;
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_set_transient_gain(float value)
{
    if (value < 0.0f) {
        value = 0.0f;
    }
    if (value > 8.0f) {
        value = 8.0f;
    }
    portENTER_CRITICAL(&s_lock);
    s_status.transient_gain = value;
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_set_min_interval_ms(uint16_t value)
{
    portENTER_CRITICAL(&s_lock);
    s_status.min_interval_ms = clamp_u16_int(value, 10, 250);
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_set_silence_timeout_ms(uint16_t value)
{
    portENTER_CRITICAL(&s_lock);
    s_status.silence_timeout_ms = clamp_u16_int(value, 20, 1000);
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_set_activity_threshold(uint16_t value)
{
    portENTER_CRITICAL(&s_lock);
    s_status.activity_threshold = clamp_u16_int(value, 1, 32767);
    portEXIT_CRITICAL(&s_lock);
}

void haptic_audio_to_raw02_set_mode(haptic_raw02_mode_t mode)
{
    portENTER_CRITICAL(&s_lock);
    s_status.mode = mode;
    portEXIT_CRITICAL(&s_lock);
}

static esp_err_t send_named_payload(const char *name,
                                    bool force_live,
                                    bool stop_only)
{
    uint8_t left[HAPTIC_RAW02_SIDE_BYTES];
    uint8_t right[HAPTIC_RAW02_SIDE_BYTES];
    uint8_t payload[HAPTIC_RAW02_PAYLOAD_BYTES];
    haptic_raw02_status_t config;
    portENTER_CRITICAL(&s_lock);
    config = s_status;
    portEXIT_CRITICAL(&s_lock);

    haptic_raw02_mode_t mode = HAPTIC_RAW02_MODE_TICK;
    uint8_t intensity = 32;
    if (name && strcmp(name, "punch") == 0) {
        mode = HAPTIC_RAW02_MODE_PUNCH;
        intensity = config.max_intensity > 64 ? 64 : config.max_intensity;
    } else if (name && strcmp(name, "texture") == 0) {
        mode = HAPTIC_RAW02_MODE_TEXTURE;
        intensity = 40;
    } else if (name && strcmp(name, "continuous") == 0) {
        mode = HAPTIC_RAW02_MODE_CONTINUOUS;
        intensity = 48;
    } else if (name && strcmp(name, "stop") == 0) {
        mode = HAPTIC_RAW02_MODE_SILENCE;
        intensity = 0;
        stop_only = true;
    }
    if (intensity > config.max_intensity) {
        intensity = config.max_intensity;
    }

    build_side(intensity, mode, left);
    build_side(intensity, mode, right);
    build_payload(left, right, payload);
    if (force_live) {
        config.live_forwarding = true;
        config.dry_run = false;
    }
    esp_err_t err = maybe_send_payload(payload, &config, stop_only);
    if (!force_live || config.dry_run || !config.live_forwarding) {
        portENTER_CRITICAL(&s_lock);
        s_status.raw02_dry_packets++;
        portEXIT_CRITICAL(&s_lock);
    } else if (err == ESP_OK) {
        portENTER_CRITICAL(&s_lock);
        s_status.raw02_live_packets++;
        s_status.ble_writes++;
        portEXIT_CRITICAL(&s_lock);
    }
    remember_last(left, right, haptic_audio_to_raw02_mode_string(mode), err == ESP_OK ? "none" : esp_err_to_name(err));
    ESP_LOGI(TAG,
             "[HAPTIC_TO_RAW02] test=%s dry_run=%s live_forwarding=%s sent=%s error=%s",
             name ? name : "tick",
             config.dry_run ? "true" : "false",
             config.live_forwarding ? "true" : "false",
             err == ESP_OK ? "true" : "false",
             err == ESP_OK ? "none" : esp_err_to_name(err));
    return err;
}

esp_err_t haptic_audio_to_raw02_send_test(const char *name, bool force_live)
{
    return send_named_payload(name ? name : "tick",
                              force_live,
                              name && strcmp(name, "stop") == 0);
}

esp_err_t haptic_audio_to_raw02_send_raw_hex(const char *hex,
                                             bool force_live,
                                             char *payload_hex,
                                             size_t payload_hex_len)
{
    if (!hex) {
        return ESP_ERR_INVALID_ARG;
    }
    while (*hex && isspace((unsigned char)*hex)) {
        hex++;
    }
    size_t hex_len = strlen(hex);
    while (hex_len > 0 && isspace((unsigned char)hex[hex_len - 1])) {
        hex_len--;
    }
    if (hex_len != HAPTIC_RAW02_HEX_LEFT_RIGHT_LEN &&
        hex_len != HAPTIC_RAW02_HEX_FULL_LEN) {
        return ESP_ERR_INVALID_SIZE;
    }
    if (!is_hex_string(hex, hex_len)) {
        return ESP_ERR_INVALID_ARG;
    }

    uint8_t payload[HAPTIC_RAW02_PAYLOAD_BYTES];
    if (hex_len == HAPTIC_RAW02_HEX_LEFT_RIGHT_LEN) {
        uint8_t left_right[HAPTIC_RAW02_SIDE_BYTES * 2];
        if (!decode_hex_exact(hex, hex_len, left_right, sizeof(left_right))) {
            return ESP_ERR_INVALID_ARG;
        }
        memset(payload, 0, sizeof(payload));
        payload[0] = 0x02;
        memcpy(payload + RAW02_LEFT_OFFSET, left_right, HAPTIC_RAW02_SIDE_BYTES);
        memcpy(payload + RAW02_RIGHT_OFFSET, left_right + HAPTIC_RAW02_SIDE_BYTES,
               HAPTIC_RAW02_SIDE_BYTES);
    } else {
        int report_id = (hex_value(hex[0]) << 4) | hex_value(hex[1]);
        if (report_id != 0x02) {
            return ESP_ERR_INVALID_ARG;
        }
        if (!decode_hex_exact(hex, hex_len, payload, sizeof(payload))) {
            return ESP_ERR_INVALID_ARG;
        }
    }

    haptic_raw02_status_t config;
    portENTER_CRITICAL(&s_lock);
    config = s_status;
    portEXIT_CRITICAL(&s_lock);
    if (force_live) {
        config.live_forwarding = true;
        config.dry_run = false;
    }

    esp_err_t err = maybe_send_payload(payload, &config, false);
    if (payload_hex && payload_hex_len > 0) {
        bytes_to_hex(payload, sizeof(payload), payload_hex, payload_hex_len);
    }
    remember_last(payload + RAW02_LEFT_OFFSET,
                  payload + RAW02_RIGHT_OFFSET,
                  "raw_hex",
                  err == ESP_OK ? "none" : esp_err_to_name(err));
    return err;
}
