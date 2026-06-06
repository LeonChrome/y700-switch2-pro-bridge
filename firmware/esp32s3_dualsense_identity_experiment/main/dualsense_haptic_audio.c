#include "dualsense_haptic_audio.h"

#include <string.h>

#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/portmacro.h"
#include "freertos/task.h"
#include "haptic_audio_to_raw02.h"
#include "tusb.h"

#ifndef DS5_ENABLE_UAC2_AUDIO
#define DS5_ENABLE_UAC2_AUDIO 0
#endif

#define DS5_AUDIO_ENTITY_INPUT_TERMINAL 0x01
#define DS5_AUDIO_ENTITY_FEATURE_UNIT 0x02
#define DS5_AUDIO_ENTITY_CLOCK 0x04
#define DS5_AUDIO_FRAME_BYTES(channels) \
    ((channels) * DUALSENSE_HAPTIC_AUDIO_BYTES_PER_SAMPLE)
#define DS5_AUDIO_READ_BUFFER_BYTES 512
#define DS5_AUDIO_ACTIVITY_ENV_THRESHOLD 512
#define DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD 2048
#define DS5_AUDIO_TRANSIENT_ENV_THRESHOLD 900
#define DS5_AUDIO_TRANSIENT_PEAK_THRESHOLD 4096
#define DS5_AUDIO_LOG_INTERVAL_ACTIVE_US 100000LL
#define DS5_AUDIO_LOG_INTERVAL_IDLE_US 250000LL

static const char *TAG = "v5.5_audio";
static portMUX_TYPE s_lock = portMUX_INITIALIZER_UNLOCKED;
static bool s_task_started;
#if DS5_ENABLE_UAC2_AUDIO
static uint32_t s_sample_rate = DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE;
static uint8_t s_clock_valid = 1;
#endif
static int8_t s_mute[DUALSENSE_HAPTIC_AUDIO_CHANNELS + 1];
static int16_t s_volume[DUALSENSE_HAPTIC_AUDIO_CHANNELS + 1];
static dualsense_haptic_audio_features_t s_last_features;
static int64_t s_next_feature_log_us;

static uint32_t isqrt_u64(uint64_t value)
{
    uint64_t bit = 1ULL << 62;
    uint32_t result = 0;

    while (bit > value) {
        bit >>= 2;
    }
    while (bit != 0) {
        if (value >= (uint64_t)result + bit) {
            value -= (uint64_t)result + bit;
            result = (uint32_t)(((uint64_t)result >> 1) + bit);
        } else {
            result >>= 1;
        }
        bit >>= 2;
    }
    return result;
}

static uint16_t abs_i16(int16_t value)
{
    if (value == INT16_MIN) {
        return 32768u;
    }
    return (uint16_t)(value < 0 ? -value : value);
}

static int16_t read_i16_le(const uint8_t *data)
{
    return (int16_t)((uint16_t)data[0] | ((uint16_t)data[1] << 8));
}

static uint16_t smooth_envelope(uint16_t previous, uint16_t value)
{
    uint32_t mixed;
    if (value > previous) {
        mixed = ((uint32_t)previous * 2u + (uint32_t)value * 6u + 4u) / 8u;
    } else {
        mixed = ((uint32_t)previous * 7u + (uint32_t)value + 4u) / 8u;
    }
    return (uint16_t)(mixed > UINT16_MAX ? UINT16_MAX : mixed);
}

static uint16_t positive_delta(uint16_t value, uint16_t previous)
{
    return value > previous ? (uint16_t)(value - previous) : 0;
}

static uint16_t max_u16(uint16_t a, uint16_t b)
{
    return a > b ? a : b;
}

void dualsense_haptic_audio_process_packet(const uint8_t *data,
                                           uint16_t len,
                                           uint8_t channels,
                                           int64_t now_us)
{
    if (!data || channels < 2) {
        portENTER_CRITICAL(&s_lock);
        s_last_features.overrun_count++;
        portEXIT_CRITICAL(&s_lock);
        return;
    }

    uint16_t frame_bytes = DS5_AUDIO_FRAME_BYTES(channels);
    if (frame_bytes == 0) {
        return;
    }
    uint16_t frames = len / frame_bytes;
    if (frames == 0) {
        return;
    }

    uint16_t aligned_len = (uint16_t)(frames * frame_bytes);
    bool partial_frame = aligned_len != len;
    uint8_t left_offset = channels >= 4 ? 4 : 0;
    uint8_t right_offset = channels >= 4 ? 6 : 2;
    uint64_t sum_sq_l = 0;
    uint64_t sum_sq_r = 0;
    uint64_t sum_abs_l = 0;
    uint64_t sum_abs_r = 0;
    uint16_t peak_l = 0;
    uint16_t peak_r = 0;

    for (uint16_t frame = 0; frame < frames; frame++) {
        const uint8_t *base = data + (frame * frame_bytes);
        uint16_t abs_l = abs_i16(read_i16_le(base + left_offset));
        uint16_t abs_r = abs_i16(read_i16_le(base + right_offset));
        sum_abs_l += abs_l;
        sum_abs_r += abs_r;
        sum_sq_l += (uint64_t)abs_l * abs_l;
        sum_sq_r += (uint64_t)abs_r * abs_r;
        if (abs_l > peak_l) {
            peak_l = abs_l;
        }
        if (abs_r > peak_r) {
            peak_r = abs_r;
        }
    }

    dualsense_haptic_audio_features_t features;
    dualsense_haptic_audio_features_t previous;

    portENTER_CRITICAL(&s_lock);
    previous = s_last_features;
    features = previous;
    features.packet_count++;
    features.frame_count += frames;
    features.last_packet_len = len;
    features.source_channels = channels;
    features.rms_l = (uint16_t)isqrt_u64(sum_sq_l / frames);
    features.rms_r = (uint16_t)isqrt_u64(sum_sq_r / frames);
    features.mean_abs_l = (uint16_t)(sum_abs_l / frames);
    features.mean_abs_r = (uint16_t)(sum_abs_r / frames);
    features.peak_l = peak_l;
    features.peak_r = peak_r;
    features.envelope_l = smooth_envelope(previous.envelope_l, features.mean_abs_l);
    features.envelope_r = smooth_envelope(previous.envelope_r, features.mean_abs_r);
    uint16_t env_delta_l = positive_delta(features.envelope_l, previous.envelope_l);
    uint16_t env_delta_r = positive_delta(features.envelope_r, previous.envelope_r);
    uint16_t peak_delta_l = positive_delta(features.peak_l, previous.peak_l);
    uint16_t peak_delta_r = positive_delta(features.peak_r, previous.peak_r);
    features.transient_l = max_u16(env_delta_l, peak_delta_l);
    features.transient_r = max_u16(env_delta_r, peak_delta_r);
    features.activity = features.envelope_l >= DS5_AUDIO_ACTIVITY_ENV_THRESHOLD ||
                        features.envelope_r >= DS5_AUDIO_ACTIVITY_ENV_THRESHOLD ||
                        peak_l >= DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD ||
                        peak_r >= DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD;
    features.transient = env_delta_l >= DS5_AUDIO_TRANSIENT_ENV_THRESHOLD ||
                         env_delta_r >= DS5_AUDIO_TRANSIENT_ENV_THRESHOLD ||
                         peak_delta_l >= DS5_AUDIO_TRANSIENT_PEAK_THRESHOLD ||
                         peak_delta_r >= DS5_AUDIO_TRANSIENT_PEAK_THRESHOLD;
    if (features.activity) {
        features.active_packet_count++;
    } else {
        features.silence_packet_count++;
    }
    if (partial_frame) {
        features.overrun_count++;
    }
    s_last_features = features;
    portEXIT_CRITICAL(&s_lock);

    haptic_audio_to_raw02_process_features(&features, now_us);

    int64_t interval_us = features.activity ?
        DS5_AUDIO_LOG_INTERVAL_ACTIVE_US : DS5_AUDIO_LOG_INTERVAL_IDLE_US;
    if (now_us >= s_next_feature_log_us &&
        (features.activity || features.packet_count == 1 ||
         (features.packet_count % 1000u) == 0u)) {
        s_next_feature_log_us = now_us + interval_us;
        ESP_LOGI(TAG,
                 "[DS5_HAPTIC_AUDIO] packet=%lu len=%u channels=%u frames=%lu active=%lu silence=%lu rms_l=%u rms_r=%u mean_abs_l=%u mean_abs_r=%u peak_l=%u peak_r=%u env_l=%u env_r=%u transient_l=%u transient_r=%u activity=%s transient=%s overrun=%lu",
                 (unsigned long)features.packet_count,
                 (unsigned)features.last_packet_len,
                 (unsigned)features.source_channels,
                 (unsigned long)features.frame_count,
                 (unsigned long)features.active_packet_count,
                 (unsigned long)features.silence_packet_count,
                 features.rms_l,
                 features.rms_r,
                 features.mean_abs_l,
                 features.mean_abs_r,
                 features.peak_l,
                 features.peak_r,
                 features.envelope_l,
                 features.envelope_r,
                 features.transient_l,
                 features.transient_r,
                 features.activity ? "true" : "false",
                 features.transient ? "true" : "false",
                 (unsigned long)features.overrun_count);
    }
}

void dualsense_haptic_audio_set_streaming(bool streaming, uint8_t alt_setting)
{
    int64_t now_us = esp_timer_get_time();
    portENTER_CRITICAL(&s_lock);
    s_last_features.streaming = streaming;
    s_last_features.alt_setting = alt_setting;
    if (!streaming) {
        s_last_features.activity = false;
        s_last_features.transient = false;
        s_last_features.envelope_l = 0;
        s_last_features.envelope_r = 0;
        s_last_features.transient_l = 0;
        s_last_features.transient_r = 0;
    }
    portEXIT_CRITICAL(&s_lock);

    if (!streaming) {
        haptic_audio_to_raw02_note_audio_stopped(now_us);
    }
    ESP_LOGI(TAG,
             "[DS5_HAPTIC_AUDIO] streaming=%s alt=%u",
             streaming ? "true" : "false",
             (unsigned)alt_setting);
}

#if DS5_ENABLE_UAC2_AUDIO
static void process_audio_packet_uac2(const uint8_t *data, uint16_t len)
{
    dualsense_haptic_audio_process_packet(data,
                                          len,
                                          DUALSENSE_HAPTIC_AUDIO_CHANNELS,
                                          esp_timer_get_time());
}

static void haptic_audio_task(void *arg)
{
    (void)arg;
    uint8_t buffer[DS5_AUDIO_READ_BUFFER_BYTES];

    while (true) {
        uint16_t available = tud_audio_available();
        while (available >= DS5_AUDIO_FRAME_BYTES(DUALSENSE_HAPTIC_AUDIO_CHANNELS)) {
            uint16_t read_len = available > sizeof(buffer) ? sizeof(buffer) : available;
            read_len = (uint16_t)(read_len - (read_len % DS5_AUDIO_FRAME_BYTES(DUALSENSE_HAPTIC_AUDIO_CHANNELS)));
            if (read_len == 0) {
                break;
            }
            uint16_t bytes_read = tud_audio_read(buffer, read_len);
            if (bytes_read == 0) {
                break;
            }
            if (available > sizeof(buffer)) {
                portENTER_CRITICAL(&s_lock);
                s_last_features.overrun_count++;
                portEXIT_CRITICAL(&s_lock);
            }
            process_audio_packet_uac2(buffer, bytes_read);
            available = tud_audio_available();
        }

        vTaskDelay(pdMS_TO_TICKS(1));
    }
}
#endif

void dualsense_haptic_audio_init(void)
{
    memset(s_mute, 0, sizeof(s_mute));
    memset(s_volume, 0, sizeof(s_volume));
    haptic_audio_to_raw02_init();

#if DS5_ENABLE_UAC2_AUDIO
    if (!s_task_started) {
        BaseType_t created = xTaskCreate(haptic_audio_task,
                                         "ds5_audio",
                                         4096,
                                         NULL,
                                         4,
                                         NULL);
        ESP_ERROR_CHECK(created == pdPASS ? ESP_OK : ESP_FAIL);
        s_task_started = true;
    }
#else
    (void)s_task_started;
#endif

    ESP_LOGI(TAG,
             "[DS5_AUDIO] enabled=true sample_rate=%u channels=%u bytes_per_sample=%u parser=ch2_ch3",
             DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE,
             DUALSENSE_HAPTIC_AUDIO_CHANNELS,
             DUALSENSE_HAPTIC_AUDIO_BYTES_PER_SAMPLE);
}

bool dualsense_haptic_audio_snapshot(dualsense_haptic_audio_features_t *out)
{
    if (!out) {
        return false;
    }
    portENTER_CRITICAL(&s_lock);
    *out = s_last_features;
    portEXIT_CRITICAL(&s_lock);
    return true;
}

#if DS5_ENABLE_UAC2_AUDIO
bool tud_audio_set_itf_cb(uint8_t rhport, tusb_control_request_t const *request)
{
    (void)rhport;
    uint8_t itf = tu_u16_low(tu_le16toh(request->wIndex));
    uint8_t alt = tu_u16_low(tu_le16toh(request->wValue));

    dualsense_haptic_audio_set_streaming(alt != 0, alt);

    ESP_LOGI(TAG,
             "[DS5_AUDIO] mounted=true interface=%u alt=%u sample_rate=%lu channels=%u",
             itf,
             alt,
             (unsigned long)s_sample_rate,
             DUALSENSE_HAPTIC_AUDIO_CHANNELS);
    return true;
}

bool tud_audio_set_itf_close_ep_cb(uint8_t rhport,
                                   tusb_control_request_t const *request)
{
    (void)rhport;
    (void)request;
    dualsense_haptic_audio_set_streaming(false, 0);
    ESP_LOGI(TAG, "[DS5_AUDIO] mounted=false");
    return true;
}

bool tud_audio_get_req_entity_cb(uint8_t rhport,
                                 tusb_control_request_t const *request_raw)
{
    audio_control_request_t const *request =
        (audio_control_request_t const *)request_raw;

    if (request->bEntityID == DS5_AUDIO_ENTITY_CLOCK) {
        if (request->bControlSelector == AUDIO_CS_CTRL_SAM_FREQ) {
            if (request->bRequest == AUDIO_CS_REQ_CUR) {
                audio_control_cur_4_t cur = {.bCur = (int32_t)s_sample_rate};
                return tud_audio_buffer_and_schedule_control_xfer(
                    rhport, request_raw, &cur, sizeof(cur));
            }
            if (request->bRequest == AUDIO_CS_REQ_RANGE) {
                audio_control_range_4_n_t(1) range = {
                    .wNumSubRanges = 1,
                    .subrange = {{
                        .bMin = DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE,
                        .bMax = DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE,
                        .bRes = 0,
                    }},
                };
                return tud_audio_buffer_and_schedule_control_xfer(
                    rhport, request_raw, &range, sizeof(range));
            }
        }
        if (request->bControlSelector == AUDIO_CS_CTRL_CLK_VALID &&
            request->bRequest == AUDIO_CS_REQ_CUR) {
            audio_control_cur_1_t cur = {.bCur = (int8_t)s_clock_valid};
            return tud_audio_buffer_and_schedule_control_xfer(
                rhport, request_raw, &cur, sizeof(cur));
        }
    }

    if (request->bEntityID == DS5_AUDIO_ENTITY_FEATURE_UNIT &&
        request->bChannelNumber <= DUALSENSE_HAPTIC_AUDIO_CHANNELS) {
        uint8_t channel = request->bChannelNumber;
        if (request->bControlSelector == AUDIO_FU_CTRL_MUTE &&
            request->bRequest == AUDIO_CS_REQ_CUR) {
            audio_control_cur_1_t cur = {.bCur = s_mute[channel]};
            return tud_audio_buffer_and_schedule_control_xfer(
                rhport, request_raw, &cur, sizeof(cur));
        }
        if (request->bControlSelector == AUDIO_FU_CTRL_VOLUME) {
            if (request->bRequest == AUDIO_CS_REQ_CUR) {
                audio_control_cur_2_t cur = {.bCur = s_volume[channel]};
                return tud_audio_buffer_and_schedule_control_xfer(
                    rhport, request_raw, &cur, sizeof(cur));
            }
            if (request->bRequest == AUDIO_CS_REQ_RANGE) {
                audio_control_range_2_n_t(1) range = {
                    .wNumSubRanges = 1,
                    .subrange = {{
                        .bMin = -50 * 256,
                        .bMax = 0,
                        .bRes = 256,
                    }},
                };
                return tud_audio_buffer_and_schedule_control_xfer(
                    rhport, request_raw, &range, sizeof(range));
            }
        }
    }

    ESP_LOGW(TAG,
             "[DS5_AUDIO] get_req unsupported entity=%u selector=%u request=%u",
             request->bEntityID,
             request->bControlSelector,
             request->bRequest);
    return false;
}

bool tud_audio_set_req_entity_cb(uint8_t rhport,
                                 tusb_control_request_t const *request_raw,
                                 uint8_t *buffer)
{
    (void)rhport;
    audio_control_request_t const *request =
        (audio_control_request_t const *)request_raw;

    if (request->bRequest != AUDIO_CS_REQ_CUR) {
        return false;
    }

    if (request->bEntityID == DS5_AUDIO_ENTITY_CLOCK &&
        request->bControlSelector == AUDIO_CS_CTRL_SAM_FREQ &&
        request->wLength == sizeof(audio_control_cur_4_t)) {
        uint32_t requested =
            (uint32_t)((audio_control_cur_4_t const *)buffer)->bCur;
        if (requested == DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE) {
            s_sample_rate = requested;
            ESP_LOGI(TAG, "[DS5_AUDIO] sample_rate=%lu", (unsigned long)s_sample_rate);
            return true;
        }
    }

    if (request->bEntityID == DS5_AUDIO_ENTITY_FEATURE_UNIT &&
        request->bChannelNumber <= DUALSENSE_HAPTIC_AUDIO_CHANNELS) {
        uint8_t channel = request->bChannelNumber;
        if (request->bControlSelector == AUDIO_FU_CTRL_MUTE &&
            request->wLength == sizeof(audio_control_cur_1_t)) {
            s_mute[channel] = ((audio_control_cur_1_t const *)buffer)->bCur;
            return true;
        }
        if (request->bControlSelector == AUDIO_FU_CTRL_VOLUME &&
            request->wLength == sizeof(audio_control_cur_2_t)) {
            s_volume[channel] =
                ((audio_control_cur_2_t const *)buffer)->bCur;
            return true;
        }
    }

    ESP_LOGW(TAG,
             "[DS5_AUDIO] set_req unsupported entity=%u selector=%u request=%u",
             request->bEntityID,
             request->bControlSelector,
             request->bRequest);
    return false;
}
#endif
