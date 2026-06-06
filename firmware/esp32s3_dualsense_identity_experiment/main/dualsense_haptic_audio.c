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

#define DS5_AUDIO_ENTITY_INPUT_TERMINAL 0x01
#define DS5_AUDIO_ENTITY_FEATURE_UNIT 0x02
#define DS5_AUDIO_ENTITY_CLOCK 0x04
#define DS5_AUDIO_FRAME_BYTES \
    (DUALSENSE_HAPTIC_AUDIO_CHANNELS * DUALSENSE_HAPTIC_AUDIO_BYTES_PER_SAMPLE)
#define DS5_AUDIO_READ_BUFFER_BYTES 512
#define DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD 1024
#define DS5_AUDIO_TRANSIENT_DELTA_THRESHOLD 4096

static const char *TAG = "v5.5_audio";
static portMUX_TYPE s_lock = portMUX_INITIALIZER_UNLOCKED;
static bool s_task_started;
static uint32_t s_sample_rate = DUALSENSE_HAPTIC_AUDIO_SAMPLE_RATE;
static uint8_t s_clock_valid = 1;
static int8_t s_mute[DUALSENSE_HAPTIC_AUDIO_CHANNELS + 1];
static int16_t s_volume[DUALSENSE_HAPTIC_AUDIO_CHANNELS + 1];
static dualsense_haptic_audio_features_t s_last_features;

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

static void process_audio_packet(const uint8_t *data, uint16_t len)
{
    uint16_t frames = len / DS5_AUDIO_FRAME_BYTES;
    if (frames == 0) {
        return;
    }

    uint64_t sum_l = 0;
    uint64_t sum_r = 0;
    uint16_t peak_l = 0;
    uint16_t peak_r = 0;

    for (uint16_t frame = 0; frame < frames; frame++) {
        const uint8_t *base = data + (frame * DS5_AUDIO_FRAME_BYTES);
        const uint8_t *left = base;
        const uint8_t *right = base + 2;
        if (DUALSENSE_HAPTIC_AUDIO_CHANNELS >= 4) {
            left = base + 4;
            right = base + 6;
        }
        uint16_t abs_l = abs_i16(read_i16_le(left));
        uint16_t abs_r = abs_i16(read_i16_le(right));
        sum_l += (uint64_t)abs_l * abs_l;
        sum_r += (uint64_t)abs_r * abs_r;
        if (abs_l > peak_l) {
            peak_l = abs_l;
        }
        if (abs_r > peak_r) {
            peak_r = abs_r;
        }
    }

    dualsense_haptic_audio_features_t features;
    int64_t now_us = esp_timer_get_time();

    portENTER_CRITICAL(&s_lock);
    features = s_last_features;
    features.packet_count++;
    features.frame_count += frames;
    features.last_packet_len = len;
    features.rms_l = (uint16_t)isqrt_u64(sum_l / frames);
    features.rms_r = (uint16_t)isqrt_u64(sum_r / frames);
    features.peak_l = peak_l;
    features.peak_r = peak_r;
    features.activity = peak_l >= DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD ||
                        peak_r >= DS5_AUDIO_ACTIVITY_PEAK_THRESHOLD;
    features.transient =
        (peak_l > s_last_features.peak_l + DS5_AUDIO_TRANSIENT_DELTA_THRESHOLD) ||
        (peak_r > s_last_features.peak_r + DS5_AUDIO_TRANSIENT_DELTA_THRESHOLD);
    s_last_features = features;
    portEXIT_CRITICAL(&s_lock);

    haptic_audio_to_raw02_process_features(&features, now_us);
}

static void haptic_audio_task(void *arg)
{
    (void)arg;
    uint8_t buffer[DS5_AUDIO_READ_BUFFER_BYTES];
    int64_t next_log_us = 0;

    while (true) {
        uint16_t available = tud_audio_available();
        while (available >= DS5_AUDIO_FRAME_BYTES) {
            uint16_t read_len = available > sizeof(buffer) ? sizeof(buffer) : available;
            read_len = (uint16_t)(read_len - (read_len % DS5_AUDIO_FRAME_BYTES));
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
            process_audio_packet(buffer, bytes_read);
            available = tud_audio_available();
        }

        int64_t now_us = esp_timer_get_time();
        dualsense_haptic_audio_features_t snapshot;
        if (dualsense_haptic_audio_snapshot(&snapshot) &&
            (snapshot.activity || snapshot.packet_count == 0) &&
            now_us >= next_log_us) {
            next_log_us = now_us + 500000LL;
            ESP_LOGI(TAG,
                     "[DS5_AUDIO] sample_rate=%lu channels=%u out_packet len=%u haptic_l_peak=%u haptic_r_peak=%u activity=%s",
                     (unsigned long)s_sample_rate,
                     DUALSENSE_HAPTIC_AUDIO_CHANNELS,
                     snapshot.last_packet_len,
                     snapshot.peak_l,
                     snapshot.peak_r,
                     snapshot.activity ? "true" : "false");
            ESP_LOGI(TAG,
                     "[DS5_HAPTIC_AUDIO] frames=%lu rms_l=%u rms_r=%u peak_l=%u peak_r=%u activity=%s transient=%s overrun=%lu",
                     (unsigned long)snapshot.frame_count,
                     snapshot.rms_l,
                     snapshot.rms_r,
                     snapshot.peak_l,
                     snapshot.peak_r,
                     snapshot.activity ? "true" : "false",
                     snapshot.transient ? "true" : "false",
                     (unsigned long)snapshot.overrun_count);
        }

        vTaskDelay(pdMS_TO_TICKS(1));
    }
}

void dualsense_haptic_audio_init(void)
{
    memset(s_mute, 0, sizeof(s_mute));
    memset(s_volume, 0, sizeof(s_volume));
    haptic_audio_to_raw02_init();

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

    ESP_LOGI(TAG,
             "[DS5_AUDIO] enabled=true sample_rate=%u channels=%u bytes_per_sample=%u",
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

bool tud_audio_set_itf_cb(uint8_t rhport, tusb_control_request_t const *request)
{
    (void)rhport;
    uint8_t itf = tu_u16_low(tu_le16toh(request->wIndex));
    uint8_t alt = tu_u16_low(tu_le16toh(request->wValue));

    portENTER_CRITICAL(&s_lock);
    s_last_features.alt_setting = alt;
    portEXIT_CRITICAL(&s_lock);

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
