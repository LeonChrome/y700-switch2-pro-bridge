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
#define DS5_AUDIO_HD_FRONT_ENV_MAX 192
#define DS5_AUDIO_HD_FRONT_PEAK_MAX 768
#define DS5_AUDIO_HD_REAR_FRONT_RATIO 4
#define DS5_AUDIO_HD_REAR_ENV_MIN 1024
#define DS5_AUDIO_LOG_INTERVAL_ACTIVE_US 500000LL
#define DS5_AUDIO_LOG_INTERVAL_IDLE_US 1000000LL

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

const char *dualsense_haptic_audio_parser_string(dualsense_haptic_audio_parser_t mode)
{
    switch (mode) {
    case DUALSENSE_HAPTIC_AUDIO_PARSER_FRONT:
        return "front";
    case DUALSENSE_HAPTIC_AUDIO_PARSER_STRONGEST:
        return "strongest";
    case DUALSENSE_HAPTIC_AUDIO_PARSER_REAR:
    default:
        return "rear";
    }
}

bool dualsense_haptic_audio_parse_parser(const char *text,
                                         dualsense_haptic_audio_parser_t *out)
{
    if (!text || !out) {
        return false;
    }
    if (strcmp(text, "rear") == 0 || strcmp(text, "ch2_ch3") == 0) {
        *out = DUALSENSE_HAPTIC_AUDIO_PARSER_REAR;
        return true;
    }
    if (strcmp(text, "front") == 0 || strcmp(text, "ch0_ch1") == 0) {
        *out = DUALSENSE_HAPTIC_AUDIO_PARSER_FRONT;
        return true;
    }
    if (strcmp(text, "strongest") == 0 || strcmp(text, "auto_pair") == 0) {
        *out = DUALSENSE_HAPTIC_AUDIO_PARSER_STRONGEST;
        return true;
    }
    return false;
}

void dualsense_haptic_audio_set_parser(dualsense_haptic_audio_parser_t mode)
{
    portENTER_CRITICAL(&s_lock);
    s_last_features.parser_mode = (uint8_t)mode;
    portEXIT_CRITICAL(&s_lock);
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
    uint8_t rear_left_offset = channels >= 4 ? 4 : 0;
    uint8_t rear_right_offset = channels >= 4 ? 6 : 2;
    uint64_t sum_sq_rear_l = 0;
    uint64_t sum_sq_rear_r = 0;
    uint64_t sum_sq_front_l = 0;
    uint64_t sum_sq_front_r = 0;
    uint64_t sum_abs_rear_l = 0;
    uint64_t sum_abs_rear_r = 0;
    uint64_t sum_abs_front_l = 0;
    uint64_t sum_abs_front_r = 0;
    uint16_t rear_peak_l = 0;
    uint16_t rear_peak_r = 0;
    uint16_t front_peak_l = 0;
    uint16_t front_peak_r = 0;

    for (uint16_t frame = 0; frame < frames; frame++) {
        const uint8_t *base = data + (frame * frame_bytes);
        uint16_t abs_front_l = abs_i16(read_i16_le(base + 0));
        uint16_t abs_front_r = abs_i16(read_i16_le(base + 2));
        uint16_t abs_rear_l = abs_i16(read_i16_le(base + rear_left_offset));
        uint16_t abs_rear_r = abs_i16(read_i16_le(base + rear_right_offset));
        sum_abs_front_l += abs_front_l;
        sum_abs_front_r += abs_front_r;
        sum_abs_rear_l += abs_rear_l;
        sum_abs_rear_r += abs_rear_r;
        sum_sq_front_l += (uint64_t)abs_front_l * abs_front_l;
        sum_sq_front_r += (uint64_t)abs_front_r * abs_front_r;
        sum_sq_rear_l += (uint64_t)abs_rear_l * abs_rear_l;
        sum_sq_rear_r += (uint64_t)abs_rear_r * abs_rear_r;
        if (abs_front_l > front_peak_l) {
            front_peak_l = abs_front_l;
        }
        if (abs_front_r > front_peak_r) {
            front_peak_r = abs_front_r;
        }
        if (abs_rear_l > rear_peak_l) {
            rear_peak_l = abs_rear_l;
        }
        if (abs_rear_r > rear_peak_r) {
            rear_peak_r = abs_rear_r;
        }
    }

    dualsense_haptic_audio_features_t features;
    dualsense_haptic_audio_features_t previous;
    dualsense_haptic_audio_parser_t parser =
        DUALSENSE_HAPTIC_AUDIO_PARSER_REAR;

    portENTER_CRITICAL(&s_lock);
    previous = s_last_features;
    parser = (dualsense_haptic_audio_parser_t)previous.parser_mode;
    features = previous;
    features.packet_count++;
    features.frame_count += frames;
    features.last_packet_len = len;
    features.source_channels = channels;
    bool choose_front = channels < 4;
    if (channels >= 4) {
        if (parser == DUALSENSE_HAPTIC_AUDIO_PARSER_FRONT) {
            choose_front = true;
        } else if (parser == DUALSENSE_HAPTIC_AUDIO_PARSER_STRONGEST) {
            uint32_t front_score =
                (uint32_t)(sum_abs_front_l / frames) +
                (uint32_t)(sum_abs_front_r / frames) +
                (uint32_t)front_peak_l + (uint32_t)front_peak_r;
            uint32_t rear_score =
                (uint32_t)(sum_abs_rear_l / frames) +
                (uint32_t)(sum_abs_rear_r / frames) +
                (uint32_t)rear_peak_l + (uint32_t)rear_peak_r;
            choose_front = front_score > rear_score;
        }
    }

    uint64_t selected_sum_sq_l = choose_front ? sum_sq_front_l : sum_sq_rear_l;
    uint64_t selected_sum_sq_r = choose_front ? sum_sq_front_r : sum_sq_rear_r;
    uint64_t selected_sum_abs_l = choose_front ? sum_abs_front_l : sum_abs_rear_l;
    uint64_t selected_sum_abs_r = choose_front ? sum_abs_front_r : sum_abs_rear_r;
    uint16_t selected_peak_l = choose_front ? front_peak_l : rear_peak_l;
    uint16_t selected_peak_r = choose_front ? front_peak_r : rear_peak_r;

    features.selected_front_pair = choose_front;
    features.rms_l = (uint16_t)isqrt_u64(selected_sum_sq_l / frames);
    features.rms_r = (uint16_t)isqrt_u64(selected_sum_sq_r / frames);
    features.front_rms_l = (uint16_t)isqrt_u64(sum_sq_front_l / frames);
    features.front_rms_r = (uint16_t)isqrt_u64(sum_sq_front_r / frames);
    features.mean_abs_l = (uint16_t)(selected_sum_abs_l / frames);
    features.mean_abs_r = (uint16_t)(selected_sum_abs_r / frames);
    features.front_mean_abs_l = (uint16_t)(sum_abs_front_l / frames);
    features.front_mean_abs_r = (uint16_t)(sum_abs_front_r / frames);
    features.peak_l = selected_peak_l;
    features.peak_r = selected_peak_r;
    features.front_peak_l = front_peak_l;
    features.front_peak_r = front_peak_r;
    features.envelope_l = smooth_envelope(previous.envelope_l, features.mean_abs_l);
    features.envelope_r = smooth_envelope(previous.envelope_r, features.mean_abs_r);
    features.front_envelope_l = smooth_envelope(previous.front_envelope_l, features.front_mean_abs_l);
    features.front_envelope_r = smooth_envelope(previous.front_envelope_r, features.front_mean_abs_r);
    uint16_t env_delta_l = positive_delta(features.envelope_l, previous.envelope_l);
    uint16_t env_delta_r = positive_delta(features.envelope_r, previous.envelope_r);
    uint16_t peak_delta_l = positive_delta(features.peak_l, previous.peak_l);
    uint16_t peak_delta_r = positive_delta(features.peak_r, previous.peak_r);
    features.transient_l = max_u16(env_delta_l, peak_delta_l);
    features.transient_r = max_u16(env_delta_r, peak_delta_r);
    features.activity = features.envelope_l >= DS5_AUDIO_ACTIVITY_ENV_THRESHOLD ||
                        features.envelope_r >= DS5_AUDIO_ACTIVITY_ENV_THRESHOLD ||
                        selected_peak_l >= DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD ||
                        selected_peak_r >= DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD;
    features.transient = env_delta_l >= DS5_AUDIO_TRANSIENT_ENV_THRESHOLD ||
                         env_delta_r >= DS5_AUDIO_TRANSIENT_ENV_THRESHOLD ||
                         peak_delta_l >= DS5_AUDIO_TRANSIENT_PEAK_THRESHOLD ||
                         peak_delta_r >= DS5_AUDIO_TRANSIENT_PEAK_THRESHOLD;
    uint16_t rear_env = max_u16(features.envelope_l, features.envelope_r);
    uint16_t rear_peak = max_u16(rear_peak_l, rear_peak_r);
    uint16_t front_env = max_u16(features.front_envelope_l, features.front_envelope_r);
    uint16_t front_peak = max_u16(features.front_peak_l, features.front_peak_r);
    bool front_quiet = front_env <= DS5_AUDIO_HD_FRONT_ENV_MAX &&
                       front_peak <= DS5_AUDIO_HD_FRONT_PEAK_MAX;
    uint32_t rear_front_required =
        (uint32_t)front_env * DS5_AUDIO_HD_REAR_FRONT_RATIO +
        DS5_AUDIO_HD_REAR_ENV_MIN;
    bool rear_dominates = rear_env >= DS5_AUDIO_HD_REAR_ENV_MIN &&
                          (uint32_t)rear_env >= rear_front_required;
    features.hd_candidate = channels >= 4 && features.activity &&
                            (front_quiet || rear_dominates) &&
                            rear_peak >= DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD;
    features.pcm_like = features.activity && !features.hd_candidate;
    if (features.activity) {
        features.active_packet_count++;
    } else {
        features.silence_packet_count++;
    }
    if (partial_frame) {
        features.overrun_count++;
    }
    features.parser_mode = (uint8_t)parser;
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
                 "[DS5_HAPTIC_AUDIO] packet=%lu len=%u channels=%u parser=%s pair=%s frames=%lu active=%lu silence=%lu front_rms_l=%u front_rms_r=%u rear_rms_l=%u rear_rms_r=%u front_peak_l=%u front_peak_r=%u rear_peak_l=%u rear_peak_r=%u front_env_l=%u front_env_r=%u rear_env_l=%u rear_env_r=%u transient_l=%u transient_r=%u activity=%s transient=%s hd_candidate=%s pcm_like=%s overrun=%lu",
                 (unsigned long)features.packet_count,
                 (unsigned)features.last_packet_len,
                 (unsigned)features.source_channels,
                 dualsense_haptic_audio_parser_string(parser),
                 features.selected_front_pair ? "front" : "rear",
                 (unsigned long)features.frame_count,
                 (unsigned long)features.active_packet_count,
                 (unsigned long)features.silence_packet_count,
                 features.front_rms_l,
                 features.front_rms_r,
                 features.rms_l,
                 features.rms_r,
                 features.front_peak_l,
                 features.front_peak_r,
                 features.peak_l,
                 features.peak_r,
                 features.front_envelope_l,
                 features.front_envelope_r,
                 features.envelope_l,
                 features.envelope_r,
                 features.transient_l,
                 features.transient_r,
                 features.activity ? "true" : "false",
                 features.transient ? "true" : "false",
                 features.hd_candidate ? "true" : "false",
                 features.pcm_like ? "true" : "false",
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
        s_last_features.front_envelope_l = 0;
        s_last_features.front_envelope_r = 0;
        s_last_features.transient_l = 0;
        s_last_features.transient_r = 0;
        s_last_features.hd_candidate = false;
        s_last_features.pcm_like = false;
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
    memset(&s_last_features, 0, sizeof(s_last_features));
    s_last_features.parser_mode = (uint8_t)DUALSENSE_HAPTIC_AUDIO_PARSER_REAR;
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
             "[DS5_AUDIO] enabled=true sample_rate=%u channels=%u bytes_per_sample=%u parser=%s source_filter=hd_only",
             DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE,
             DUALSENSE_HAPTIC_AUDIO_CHANNELS,
             DUALSENSE_HAPTIC_AUDIO_BYTES_PER_SAMPLE,
             dualsense_haptic_audio_parser_string(
                 (dualsense_haptic_audio_parser_t)s_last_features.parser_mode));
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
