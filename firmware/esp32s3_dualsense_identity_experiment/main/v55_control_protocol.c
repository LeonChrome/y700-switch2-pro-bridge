#include "v55_control_protocol.h"

#include <ctype.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "app_log.h"
#include "ble_central.h"
#include "device_config.h"
#include "dualsense_haptic_audio.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_system.h"
#include "haptic_audio_to_raw02.h"
#include "pro2_input_backend.h"
#include "switch2_gatt.h"

#ifndef DS5_PROFILE_NAME
#define DS5_PROFILE_NAME "unknown"
#endif

static const char *TAG = "v5.5_control";

static void trim(char *text)
{
    int len = (int)strlen(text);
    while (len > 0 && isspace((unsigned char)text[len - 1])) {
        text[--len] = 0;
    }
    int start = 0;
    while (text[start] && isspace((unsigned char)text[start])) {
        start++;
    }
    if (start > 0) {
        memmove(text, text + start, strlen(text + start) + 1);
    }
}

static bool copy_trimmed_arg(const char *src, char *out, size_t out_len)
{
    if (!src || !out || out_len == 0) {
        return false;
    }
    while (*src && isspace((unsigned char)*src)) {
        src++;
    }
    size_t len = strlen(src);
    while (len > 0 && isspace((unsigned char)src[len - 1])) {
        len--;
    }
    if (len >= out_len) {
        return false;
    }
    memcpy(out, src, len);
    out[len] = 0;
    return true;
}

static bool parse_long_arg(const char *text, long *out)
{
    if (!text || !out) {
        return false;
    }
    while (*text && isspace((unsigned char)*text)) {
        text++;
    }
    char *end = NULL;
    long value = strtol(text, &end, 10);
    if (end == text) {
        return false;
    }
    while (*end && isspace((unsigned char)*end)) {
        end++;
    }
    if (*end) {
        return false;
    }
    *out = value;
    return true;
}

static bool parse_float_arg(const char *text, float *out)
{
    if (!text || !out) {
        return false;
    }
    while (*text && isspace((unsigned char)*text)) {
        text++;
    }
    char *end = NULL;
    float value = strtof(text, &end);
    if (end == text) {
        return false;
    }
    while (*end && isspace((unsigned char)*end)) {
        end++;
    }
    if (*end) {
        return false;
    }
    *out = value;
    return true;
}

static esp_err_t json_ok(char *reply, int reply_len, const char *cmd, const char *extra)
{
    if (extra && extra[0]) {
        snprintf(reply, reply_len, "{\"ok\":true,\"cmd\":\"%s\",%s}", cmd, extra);
    } else {
        snprintf(reply, reply_len, "{\"ok\":true,\"cmd\":\"%s\"}", cmd);
    }
    printf("%s\n", reply);
    return ESP_OK;
}

static esp_err_t json_error(char *reply, int reply_len, const char *cmd, const char *error)
{
    snprintf(reply, reply_len, "{\"ok\":false,\"cmd\":\"%s\",\"error\":\"%s\"}", cmd, error);
    printf("%s\n", reply);
    return ESP_FAIL;
}

static void format_status_extra(char *out, size_t out_len)
{
    dualsense_haptic_audio_features_t audio;
    haptic_raw02_status_t raw02;
    ble_central_conn_metrics_t ble;
    dualsense_haptic_audio_snapshot(&audio);
    haptic_audio_to_raw02_snapshot(&raw02);
    ble_central_get_conn_metrics(&ble);

    snprintf(out,
             out_len,
             "\"mode\":\"dualsense\",\"profile\":\"%s\",\"usb_audio\":\"uac1_4ch\",\"ble\":\"%s\",\"ble_auto\":\"%s\",\"ble_target\":\"%s\",\"ble_conn_interval_units\":%u,\"ble_conn_interval_us\":%lu,\"audio_streaming\":%s,\"audio_alt\":%u,\"audio_packets\":%lu,\"audio_active\":%lu,\"audio_silence\":%lu,\"audio_parser\":\"%s\",\"audio_pair\":\"%s\",\"hd_candidate\":%s,\"front_rms_l\":%u,\"front_rms_r\":%u,\"rear_rms_l\":%u,\"rear_rms_r\":%u,\"front_peak_l\":%u,\"front_peak_r\":%u,\"rear_peak_l\":%u,\"rear_peak_r\":%u,\"front_env_l\":%u,\"front_env_r\":%u,\"rear_env_l\":%u,\"rear_env_r\":%u,\"transient_l\":%u,\"transient_r\":%u,\"haptic\":\"%s\",\"haptic_live\":%s,\"haptic_dry_run\":%s,\"haptic_mode\":\"%s\",\"haptic_source\":\"%s\",\"haptic_max\":%u,\"haptic_gain\":%.3f,\"haptic_transient_gain\":%.3f,\"haptic_interval_ms\":%u,\"haptic_activity_threshold\":%u,\"haptic_silence_timeout_ms\":%u,\"raw02_hd_candidate_packets\":%lu,\"raw02_dry_packets\":%lu,\"raw02_live_packets\":%lu,\"raw02_dropped_rate\":%lu,\"raw02_dropped_no_ble\":%lu,\"raw02_dropped_silence\":%lu,\"raw02_dropped_pcm\":%lu,\"raw02_ble_writes\":%lu,\"raw02_ble_errors\":%lu,\"raw02_last_mode\":\"%s\",\"raw02_left\":\"%s\",\"raw02_right\":\"%s\",\"raw02_error\":\"%s\",\"version\":\"v5.9.0-dualsense\"",
             DS5_PROFILE_NAME,
             pro2_input_backend_state(),
             device_config_get_ble_autoconnect() ? "on" : "off",
             device_config_get_ble_target(),
             (unsigned)ble.interval_units,
             (unsigned long)ble.interval_units * 1250UL,
             audio.streaming ? "true" : "false",
              (unsigned)audio.alt_setting,
              (unsigned long)audio.packet_count,
              (unsigned long)audio.active_packet_count,
              (unsigned long)audio.silence_packet_count,
              dualsense_haptic_audio_parser_string((dualsense_haptic_audio_parser_t)audio.parser_mode),
              audio.selected_front_pair ? "front" : "rear",
              audio.hd_candidate ? "true" : "false",
             audio.front_rms_l,
             audio.front_rms_r,
             audio.rms_l,
             audio.rms_r,
             audio.front_peak_l,
             audio.front_peak_r,
             audio.peak_l,
             audio.peak_r,
             audio.front_envelope_l,
             audio.front_envelope_r,
             audio.envelope_l,
             audio.envelope_r,
             audio.transient_l,
             audio.transient_r,
             raw02.live_forwarding ? (raw02.dry_run ? "dry" : "live") : "off",
             raw02.live_forwarding ? "true" : "false",
             raw02.dry_run ? "true" : "false",
             haptic_audio_to_raw02_mode_string(raw02.mode),
             haptic_audio_to_raw02_source_string(raw02.source),
             (unsigned)raw02.max_intensity,
             (double)raw02.gain,
             (double)raw02.transient_gain,
             (unsigned)raw02.min_interval_ms,
             (unsigned)raw02.activity_threshold,
             (unsigned)raw02.silence_timeout_ms,
             (unsigned long)raw02.hd_candidate_packets,
             (unsigned long)raw02.raw02_dry_packets,
             (unsigned long)raw02.raw02_live_packets,
             (unsigned long)raw02.dropped_rate,
             (unsigned long)raw02.dropped_no_ble,
             (unsigned long)raw02.dropped_silence,
             (unsigned long)raw02.dropped_pcm,
             (unsigned long)raw02.ble_writes,
             (unsigned long)raw02.ble_errors,
             raw02.last_mode,
             raw02.last_left_hex,
             raw02.last_right_hex,
             raw02.last_error);
}

static void format_status_lite_extra(char *out, size_t out_len)
{
    dualsense_haptic_audio_features_t audio;
    haptic_raw02_status_t raw02;
    switch2_live_stats_t input_stats;
    switch2_state_t input_state;
    uint32_t input_updates = 0;
    int64_t input_age_us = INT64_MAX;
    bool input_live = pro2_input_backend_get_live(&input_state,
                                                  &input_updates,
                                                  &input_age_us);
    dualsense_haptic_audio_snapshot(&audio);
    haptic_audio_to_raw02_snapshot(&raw02);
    switch2_state_get_live_stats(&input_stats);

    snprintf(out,
             out_len,
             "\"mode\":\"dualsense\",\"profile\":\"%s\",\"ble\":\"%s\",\"audio_streaming\":%s,\"audio_alt\":%u,\"audio_packets\":%lu,\"audio_active\":%lu,\"audio_silence\":%lu,\"audio_parser\":\"%s\",\"audio_pair\":\"%s\",\"hd_candidate\":%s,\"front_env_l\":%u,\"front_env_r\":%u,\"rear_env_l\":%u,\"rear_env_r\":%u,\"front_peak_l\":%u,\"front_peak_r\":%u,\"rear_peak_l\":%u,\"rear_peak_r\":%u,\"haptic\":\"%s\",\"haptic_live\":%s,\"haptic_dry_run\":%s,\"haptic_mode\":\"%s\",\"haptic_source\":\"%s\",\"raw02_hd_candidate_packets\":%lu,\"raw02_live_packets\":%lu,\"raw02_dropped_rate\":%lu,\"raw02_dropped_silence\":%lu,\"raw02_dropped_pcm\":%lu,\"raw02_ble_writes\":%lu,\"raw02_ble_errors\":%lu,\"raw02_last_mode\":\"%s\",\"raw02_left\":\"%s\",\"raw02_right\":\"%s\",\"raw02_error\":\"%s\",\"input_live\":%s,\"input_updates\":%lu,\"input_age_ms\":%lld,\"input_rate_millihz\":%lu,\"input_last_gap_us\":%lu,\"input_max_gap_us\":%lu,\"input_lx\":%u,\"input_ly\":%u,\"input_rx\":%u,\"input_ry\":%u,\"version\":\"v5.9.0-dualsense\"",
             DS5_PROFILE_NAME,
             pro2_input_backend_state(),
             audio.streaming ? "true" : "false",
              (unsigned)audio.alt_setting,
              (unsigned long)audio.packet_count,
              (unsigned long)audio.active_packet_count,
              (unsigned long)audio.silence_packet_count,
              dualsense_haptic_audio_parser_string((dualsense_haptic_audio_parser_t)audio.parser_mode),
              audio.selected_front_pair ? "front" : "rear",
              audio.hd_candidate ? "true" : "false",
             audio.front_envelope_l,
             audio.front_envelope_r,
             audio.envelope_l,
             audio.envelope_r,
             audio.front_peak_l,
             audio.front_peak_r,
             audio.peak_l,
             audio.peak_r,
             raw02.live_forwarding ? (raw02.dry_run ? "dry" : "live") : "off",
             raw02.live_forwarding ? "true" : "false",
             raw02.dry_run ? "true" : "false",
             haptic_audio_to_raw02_mode_string(raw02.mode),
             haptic_audio_to_raw02_source_string(raw02.source),
             (unsigned long)raw02.hd_candidate_packets,
             (unsigned long)raw02.raw02_live_packets,
             (unsigned long)raw02.dropped_rate,
             (unsigned long)raw02.dropped_silence,
             (unsigned long)raw02.dropped_pcm,
             (unsigned long)raw02.ble_writes,
             (unsigned long)raw02.ble_errors,
             raw02.last_mode,
             raw02.last_left_hex,
             raw02.last_right_hex,
             raw02.last_error,
             input_live ? "true" : "false",
             (unsigned long)input_updates,
             (long long)(input_age_us == INT64_MAX ? -1 : input_age_us / 1000),
             (unsigned long)input_stats.actual_millihz,
             (unsigned long)input_stats.last_gap_us,
             (unsigned long)input_stats.max_gap_us,
             (unsigned)input_state.lx,
             (unsigned)input_state.ly,
             (unsigned)input_state.rx,
             (unsigned)input_state.ry);
}

static esp_err_t handle_haptic_command(const char *cmd, char *reply, int reply_len)
{
    if (strcmp(cmd, "haptic status lite") == 0 || strcmp(cmd, "haptic lite") == 0) {
        static char extra[1600];
        format_status_lite_extra(extra, sizeof(extra));
        return json_ok(reply, reply_len, "haptic status lite", extra);
    }
    if (strcmp(cmd, "haptic status") == 0 || strcmp(cmd, "haptic") == 0) {
        static char extra[3200];
        format_status_extra(extra, sizeof(extra));
        return json_ok(reply, reply_len, "haptic status", extra);
    }
    if (strcmp(cmd, "haptic raw02 on") == 0) {
        haptic_audio_to_raw02_set_live_forwarding(true);
        return json_ok(reply, reply_len, "haptic raw02", "\"live_forwarding\":true");
    }
    if (strcmp(cmd, "haptic raw02 off") == 0) {
        haptic_audio_to_raw02_set_live_forwarding(false);
        return json_ok(reply, reply_len, "haptic raw02", "\"live_forwarding\":false");
    }
    if (strcmp(cmd, "haptic dryrun on") == 0 || strcmp(cmd, "haptic dry-run on") == 0) {
        haptic_audio_to_raw02_set_dry_run(true);
        return json_ok(reply, reply_len, "haptic dryrun", "\"dry_run\":true");
    }
    if (strcmp(cmd, "haptic dryrun off") == 0 || strcmp(cmd, "haptic dry-run off") == 0) {
        haptic_audio_to_raw02_set_dry_run(false);
        return json_ok(reply, reply_len, "haptic dryrun", "\"dry_run\":false");
    }
    if (strncmp(cmd, "haptic max ", 11) == 0) {
        long value = 0;
        if (!parse_long_arg(cmd + 11, &value) || value < 0 || value > 255) {
            return json_error(reply, reply_len, "haptic max", "usage: haptic max <0..255>");
        }
        haptic_audio_to_raw02_set_max_intensity((uint8_t)value);
        char extra[48];
        snprintf(extra, sizeof(extra), "\"max_intensity\":%ld", value);
        return json_ok(reply, reply_len, "haptic max", extra);
    }
    if (strncmp(cmd, "haptic gain ", 12) == 0) {
        float value = 0.0f;
        if (!parse_float_arg(cmd + 12, &value) || value < 0.0f || value > 8.0f) {
            return json_error(reply, reply_len, "haptic gain", "usage: haptic gain <0.0..8.0>");
        }
        haptic_audio_to_raw02_set_gain(value);
        char extra[48];
        snprintf(extra, sizeof(extra), "\"gain\":%.3f", (double)value);
        return json_ok(reply, reply_len, "haptic gain", extra);
    }
    if (strncmp(cmd, "haptic transient_gain ", 22) == 0) {
        float value = 0.0f;
        if (!parse_float_arg(cmd + 22, &value) || value < 0.0f || value > 8.0f) {
            return json_error(reply, reply_len, "haptic transient_gain", "usage: haptic transient_gain <0.0..8.0>");
        }
        haptic_audio_to_raw02_set_transient_gain(value);
        char extra[64];
        snprintf(extra, sizeof(extra), "\"transient_gain\":%.3f", (double)value);
        return json_ok(reply, reply_len, "haptic transient_gain", extra);
    }
    if (strncmp(cmd, "haptic interval ", 16) == 0) {
        long value = 0;
        if (!parse_long_arg(cmd + 16, &value) || value < 10 || value > 250) {
            return json_error(reply, reply_len, "haptic interval", "usage: haptic interval <10..250>");
        }
        haptic_audio_to_raw02_set_min_interval_ms((uint16_t)value);
        char extra[64];
        snprintf(extra, sizeof(extra), "\"min_interval_ms\":%ld", value);
        return json_ok(reply, reply_len, "haptic interval", extra);
    }
    if (strncmp(cmd, "haptic silence ", 15) == 0) {
        long value = 0;
        if (!parse_long_arg(cmd + 15, &value) || value < 20 || value > 1000) {
            return json_error(reply, reply_len, "haptic silence", "usage: haptic silence <20..1000>");
        }
        haptic_audio_to_raw02_set_silence_timeout_ms((uint16_t)value);
        char extra[64];
        snprintf(extra, sizeof(extra), "\"silence_timeout_ms\":%ld", value);
        return json_ok(reply, reply_len, "haptic silence", extra);
    }
    if (strncmp(cmd, "haptic threshold ", 17) == 0 ||
        strncmp(cmd, "haptic activity ", 16) == 0) {
        const char *arg = strncmp(cmd, "haptic threshold ", 17) == 0 ? cmd + 17 : cmd + 16;
        long value = 0;
        if (!parse_long_arg(arg, &value) || value < 1 || value > 32767) {
            return json_error(reply, reply_len, "haptic threshold", "usage: haptic threshold <1..32767>");
        }
        haptic_audio_to_raw02_set_activity_threshold((uint16_t)value);
        char extra[64];
        snprintf(extra, sizeof(extra), "\"activity_threshold\":%ld", value);
        return json_ok(reply, reply_len, "haptic threshold", extra);
    }
    if (strncmp(cmd, "haptic mode ", 12) == 0) {
        haptic_raw02_mode_t mode;
        if (!haptic_audio_to_raw02_parse_mode(cmd + 12, &mode)) {
            return json_error(reply, reply_len, "haptic mode", "usage: haptic mode auto|tick|punch|continuous|texture");
        }
        haptic_audio_to_raw02_set_mode(mode);
        char extra[64];
        snprintf(extra, sizeof(extra), "\"mode\":\"%s\"", haptic_audio_to_raw02_mode_string(mode));
        return json_ok(reply, reply_len, "haptic mode", extra);
    }
    if (strncmp(cmd, "haptic source ", 14) == 0) {
        haptic_raw02_source_t source;
        if (!haptic_audio_to_raw02_parse_source(cmd + 14, &source)) {
            return json_error(reply, reply_len, "haptic source", "usage: haptic source hd_only|pcm");
        }
        haptic_audio_to_raw02_set_source(source);
        char extra[64];
        snprintf(extra, sizeof(extra), "\"source\":\"%s\"", haptic_audio_to_raw02_source_string(source));
        return json_ok(reply, reply_len, "haptic source", extra);
    }
    if (strncmp(cmd, "haptic test live ", 17) == 0) {
        char name[24];
        if (!copy_trimmed_arg(cmd + 17, name, sizeof(name))) {
            return json_error(reply, reply_len, "haptic test live", "test name is too long");
        }
        esp_err_t err = haptic_audio_to_raw02_send_test(name, true);
        char extra[112];
        snprintf(extra, sizeof(extra), "\"test\":\"%s\",\"force_live\":true,\"sent\":%s,\"error\":\"%s\"",
                 name,
                 err == ESP_OK ? "true" : "false",
                 err == ESP_OK ? "none" : esp_err_to_name(err));
        return err == ESP_OK ?
            json_ok(reply, reply_len, "haptic test live", extra) :
            json_error(reply, reply_len, "haptic test live", esp_err_to_name(err));
    }
    if (strncmp(cmd, "haptic test ", 12) == 0) {
        char name[24];
        if (!copy_trimmed_arg(cmd + 12, name, sizeof(name))) {
            return json_error(reply, reply_len, "haptic test", "test name is too long");
        }
        esp_err_t err = haptic_audio_to_raw02_send_test(name, false);
        char extra[96];
        snprintf(extra, sizeof(extra), "\"test\":\"%s\",\"sent\":%s,\"error\":\"%s\"",
                 name,
                 err == ESP_OK ? "true" : "false",
                 err == ESP_OK ? "none" : esp_err_to_name(err));
        return json_ok(reply, reply_len, "haptic test", extra);
    }
    if (strcmp(cmd, "haptic default") == 0 || strcmp(cmd, "haptic defaults") == 0) {
        haptic_audio_to_raw02_defaults();
        return json_ok(reply, reply_len, "haptic defaults", "\"restored\":true");
    }
    return json_error(reply, reply_len, "haptic", "unknown haptic command");
}

static esp_err_t handle_audio_command(const char *cmd, char *reply, int reply_len)
{
    if (strncmp(cmd, "audio parser ", 13) == 0 ||
        strncmp(cmd, "haptic parser ", 14) == 0) {
        const char *arg = strncmp(cmd, "audio parser ", 13) == 0 ? cmd + 13 : cmd + 14;
        dualsense_haptic_audio_parser_t parser;
        if (!dualsense_haptic_audio_parse_parser(arg, &parser)) {
            return json_error(reply,
                              reply_len,
                              "audio parser",
                              "usage: audio parser rear|front|strongest");
        }
        dualsense_haptic_audio_set_parser(parser);
        char extra[64];
        snprintf(extra,
                 sizeof(extra),
                 "\"audio_parser\":\"%s\"",
                 dualsense_haptic_audio_parser_string(parser));
        return json_ok(reply, reply_len, "audio parser", extra);
    }
    return ESP_ERR_NOT_SUPPORTED;
}

void v55_control_protocol_init(void)
{
    ESP_LOGI(TAG, "[V55_CONTROL] serial control ready: status/status lite, BLE, haptic, audio parser, raw02, input recalibrate");
}

esp_err_t v55_control_protocol_handle_line(const char *line, char *reply, int reply_len)
{
    char cmd[192];
    snprintf(cmd, sizeof(cmd), "%s", line ? line : "");
    trim(cmd);
    ESP_LOGI(TAG, "[V55_CONTROL] command=%s", cmd);

    if (strcmp(cmd, "status") == 0 || strcmp(cmd, "param get") == 0) {
        static char extra[3200];
        format_status_extra(extra, sizeof(extra));
        return json_ok(reply, reply_len, "status", extra);
    }
    if (strcmp(cmd, "status lite") == 0 || strcmp(cmd, "param get lite") == 0) {
        static char extra[1600];
        format_status_lite_extra(extra, sizeof(extra));
        return json_ok(reply, reply_len, "status lite", extra);
    }
    if (strcmp(cmd, "version") == 0) {
        return json_ok(reply, reply_len, "version", "\"version\":\"v5.9.0-dualsense\",\"profile\":\"" DS5_PROFILE_NAME "\"");
    }
    if (strcmp(cmd, "mode pro2") == 0) {
        return json_ok(reply, reply_len, "mode", "\"mode\":\"pro2\",\"reflash_required\":true,\"note\":\"Flash V5.9 Pro2 / Nintendo bridge firmware, then replug native USB\"");
    }
    if (strcmp(cmd, "mode dualsense") == 0) {
        return json_ok(reply, reply_len, "mode", "\"mode\":\"dualsense\",\"reflash_required\":false,\"note\":\"Already running V5.9 DualSense-like identity\"");
    }
    if (strcmp(cmd, "reboot") == 0) {
        json_ok(reply, reply_len, "reboot", "\"note\":\"restarting\"");
        esp_restart();
        return ESP_OK;
    }
    if (strcmp(cmd, "loglevel debug") == 0) {
        app_log_set_debug(true);
        return json_ok(reply, reply_len, "loglevel", "\"level\":\"debug\"");
    }
    if (strcmp(cmd, "loglevel info") == 0) {
        app_log_set_debug(false);
        return json_ok(reply, reply_len, "loglevel", "\"level\":\"info\"");
    }
    esp_err_t audio_rc = handle_audio_command(cmd, reply, reply_len);
    if (audio_rc != ESP_ERR_NOT_SUPPORTED) {
        return audio_rc;
    }

    if (strcmp(cmd, "ble scan") == 0) {
        return ble_central_start_scan() == ESP_OK ?
            json_ok(reply, reply_len, "ble scan", "\"ble\":\"scanning\"") :
            json_error(reply, reply_len, "ble scan", "scan start failed");
    }
    if (strcmp(cmd, "ble list") == 0 || strcmp(cmd, "ble candidates") == 0) {
        static char extra[1800];
        ble_central_format_scan_results_json(extra, sizeof(extra));
        return json_ok(reply, reply_len, "ble list", extra);
    }
    if (strncmp(cmd, "ble connect", 11) == 0) {
        char target[96];
        if (!copy_trimmed_arg(cmd + 11, target, sizeof(target))) {
            return json_error(reply, reply_len, "ble connect", "BLE target is too long");
        }
        esp_err_t err = ble_central_connect(target[0] ? target : NULL);
        return err == ESP_OK ?
            json_ok(reply, reply_len, "ble connect", "\"ble\":\"connecting\"") :
            json_error(reply, reply_len, "ble connect", "connect start failed");
    }
    if (strcmp(cmd, "ble reconnect") == 0 || strcmp(cmd, "ble auto connect") == 0) {
        esp_err_t err = ble_central_reconnect_saved_or_scan();
        return err == ESP_OK ?
            json_ok(reply, reply_len, "ble reconnect", "\"ble\":\"connecting\"") :
            json_error(reply, reply_len, "ble reconnect", "reconnect start failed");
    }
    if (strcmp(cmd, "ble auto on") == 0 || strcmp(cmd, "ble autoconnect on") == 0) {
        esp_err_t err = device_config_save_ble_autoconnect(true);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "ble auto", "failed to save BLE autoconnect");
        }
        ble_central_start_auto_reconnect();
        return json_ok(reply, reply_len, "ble auto", "\"ble_auto\":\"on\"");
    }
    if (strcmp(cmd, "ble auto off") == 0 || strcmp(cmd, "ble autoconnect off") == 0) {
        esp_err_t err = device_config_save_ble_autoconnect(false);
        return err == ESP_OK ?
            json_ok(reply, reply_len, "ble auto", "\"ble_auto\":\"off\"") :
            json_error(reply, reply_len, "ble auto", "failed to save BLE autoconnect");
    }
    if (strcmp(cmd, "ble disconnect") == 0) {
        ble_central_disconnect();
        return json_ok(reply, reply_len, "ble disconnect", "\"ble\":\"idle\"");
    }
    if (strcmp(cmd, "input recalibrate") == 0 ||
        strcmp(cmd, "stick recalibrate") == 0 ||
        strcmp(cmd, "axis recalibrate") == 0) {
        switch2_gatt_reset_axis_calibration();
        switch2_state_clear_live();
        return json_ok(reply,
                       reply_len,
                       "input recalibrate",
                       "\"axis_calibration\":\"reset\",\"note\":\"keep sticks centered until BLE samples settle\"");
    }

    if (strncmp(cmd, "haptic", 6) == 0) {
        return handle_haptic_command(cmd, reply, reply_len);
    }
    if (strncmp(cmd, "rumble raw02 ", 13) == 0) {
        char payload_hex[HAPTIC_RAW02_PAYLOAD_BYTES * 2 + 1];
        esp_err_t err = haptic_audio_to_raw02_send_raw_hex(cmd + 13,
                                                           false,
                                                           payload_hex,
                                                           sizeof(payload_hex));
        char extra[220];
        snprintf(extra,
                 sizeof(extra),
                 "\"rumble\":\"raw02\",\"payload\":\"%s\",\"sent\":%s,\"error\":\"%s\"",
                 payload_hex,
                 err == ESP_OK ? "true" : "false",
                 err == ESP_OK ? "none" : esp_err_to_name(err));
        return err == ESP_OK ?
            json_ok(reply, reply_len, "rumble raw02", extra) :
            json_error(reply, reply_len, "rumble raw02", esp_err_to_name(err));
    }
    if (strcmp(cmd, "param default") == 0 || strcmp(cmd, "param defaults") == 0) {
        haptic_audio_to_raw02_defaults();
        return json_ok(reply, reply_len, "param defaults", "\"restored\":true");
    }
    if (strncmp(cmd, "param set haptic.max ", 21) == 0) {
        long value = 0;
        if (!parse_long_arg(cmd + 21, &value) || value < 0 || value > 255) {
            return json_error(reply, reply_len, "param set", "usage: param set haptic.max <0..255>");
        }
        haptic_audio_to_raw02_set_max_intensity((uint8_t)value);
        return json_ok(reply, reply_len, "param set", "\"saved\":false");
    }

    return json_error(reply, reply_len, "unknown", "unknown V5.9 command");
}
