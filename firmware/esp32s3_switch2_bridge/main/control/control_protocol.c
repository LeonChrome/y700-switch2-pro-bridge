#include <ctype.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "esp_system.h"
#include "app_log.h"
#include "ble_central.h"
#include "device_config.h"
#include "hid_report.h"
#include "report_rate_stats.h"
#include "switch2_state.h"
#include "usb_hid_device.h"
#include "usb_switch2_vendor.h"
#include "control_protocol.h"

static const char *TAG = "control";

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

static bool parse_long_token(const char **cursor, long *value)
{
    const char *p = *cursor;
    while (*p && isspace((unsigned char)*p)) {
        p++;
    }
    if (!*p) {
        return false;
    }

    char *end = NULL;
    long parsed = strtol(p, &end, 10);
    if (end == p) {
        return false;
    }
    while (*end && isspace((unsigned char)*end)) {
        end++;
    }

    *cursor = end;
    *value = parsed;
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

void control_protocol_init(void)
{
    APP_LOGI(TAG, "serial control protocol ready on CH343P console");
    APP_LOGI(TAG, "manager integration ready: status, rate, BLE reconnect, and rumble test commands are available");
}

esp_err_t control_protocol_handle_line(const char *line, char *reply, int reply_len)
{
    char cmd[96];
    snprintf(cmd, sizeof(cmd), "%s", line ? line : "");
    trim(cmd);
    APP_LOGI(TAG, "command: %s", cmd);

    if (strcmp(cmd, "status") == 0) {
        uint32_t live_updates = 0;
        int64_t live_age_us = 0;
        uint16_t rumble_scale_percent = 0;
        uint16_t rumble_hold_ms = 0;
        uint16_t rumble_tick_ms = 0;
        uint8_t rumble_stop_packets = 0;
        report_rate_stats_snapshot_t report_stats;
        switch2_live_stats_t live_stats;
        ble_central_conn_metrics_t ble_conn;
        bool live_valid = switch2_state_get_live(NULL, &live_updates, &live_age_us);
        report_rate_stats_get(&report_stats);
        switch2_state_get_live_stats(&live_stats);
        ble_central_get_conn_metrics(&ble_conn);
        usb_switch2_vendor_get_hd_rumble_tuning(&rumble_scale_percent,
                                                &rumble_hold_ms,
                                                &rumble_tick_ms,
                                                &rumble_stop_packets);
        static char extra[3000];
        snprintf(extra, sizeof(extra),
                 "\"mode\":\"%s\",\"usb\":\"%s\",\"hid_out\":%lu,\"hid_out_last\":\"%02x/%02x/%02x/%02x/%u\",\"hid_get\":%lu,\"hid_get_last\":\"%02x/%02x/%u/%u\",\"bulk\":\"%s\",\"bulk_rx\":%lu,\"bulk_tx\":%lu,\"bulk_tx_done\":%lu,\"bulk_tx_sent\":%lu,\"bulk_last\":\"%02x/%02x\",\"bulk_addr\":\"%08lx\",\"bulk_rx_len\":%u,\"bulk_tx_len\":%u,\"bulk_pending\":\"%u/%u\",\"hid_guard\":\"%s\",\"ble\":\"%s\",\"ble_auto\":\"%s\",\"ble_target\":\"%s\",\"ble_conn_interval_units\":%u,\"ble_conn_interval_us\":%lu,\"ble_conn_latency\":%u,\"ble_conn_supervision\":%u,\"ble_conn_update_start_rc\":%d,\"ble_conn_update_status\":%d,\"ble_conn_update_requests\":%lu,\"ble_input_actual_hz\":%lu,\"ble_input_actual_mhz\":%lu,\"ble_input_last_gap_us\":%lu,\"ble_input_max_gap_us\":%lu,\"hid\":\"%s\",\"test_mode\":\"%s\",\"rate_hz\":%u,\"report_actual_hz\":%lu,\"report_actual_mhz\":%lu,\"report_sent\":%lu,\"report_failed\":%lu,\"report_last_gap_us\":%lu,\"report_max_gap_us\":%lu,\"live\":\"%s\",\"live_updates\":%lu,\"live_age_ms\":%lld,\"rumble\":\"%s\",\"rumble_updates\":%lu,\"rumble_writes\":%lu,\"rumble_stops\":%lu,\"rumble_errors\":%lu,\"rumble_scale_percent\":%u,\"rumble_hold_ms\":%u,\"rumble_tick_ms\":%u,\"rumble_stop_packets\":%u,\"version\":\"%s\"",
                 device_mode_to_string(device_config_get_mode()),
                 usb_hid_device_state_string(),
                 (unsigned long)usb_hid_device_out_count(),
                 usb_hid_device_last_out_report_id(),
                 usb_hid_device_last_out_effective_report_id(),
                 usb_hid_device_last_out_type(),
                 usb_hid_device_last_out_first_byte(),
                 (unsigned)usb_hid_device_last_out_len(),
                 (unsigned long)usb_hid_device_get_count(),
                 usb_hid_device_last_get_report_id(),
                 usb_hid_device_last_get_type(),
                 (unsigned)usb_hid_device_last_get_req_len(),
                 (unsigned)usb_hid_device_last_get_resp_len(),
                 usb_switch2_vendor_mounted() ? "mounted" : "not_mounted",
                 (unsigned long)usb_switch2_vendor_rx_count(),
                 (unsigned long)usb_switch2_vendor_tx_count(),
                 (unsigned long)usb_switch2_vendor_tx_done_count(),
                 (unsigned long)usb_switch2_vendor_last_sent_bytes(),
                 usb_switch2_vendor_last_cmd(),
                 usb_switch2_vendor_last_arg(),
                 (unsigned long)usb_switch2_vendor_last_address(),
                 (unsigned)usb_switch2_vendor_last_rx_len(),
                 (unsigned)usb_switch2_vendor_last_tx_len(),
                 (unsigned)usb_switch2_vendor_pending_offset(),
                 (unsigned)usb_switch2_vendor_pending_len(),
                 usb_switch2_vendor_hid_guard_state(),
                 ble_central_state_string(),
                 device_config_get_ble_autoconnect() ? "on" : "off",
                 device_config_get_ble_target(),
                 (unsigned)ble_conn.interval_units,
                 (unsigned long)ble_conn.interval_units * 1250UL,
                 (unsigned)ble_conn.latency,
                 (unsigned)ble_conn.supervision_timeout,
                 ble_conn.last_update_start_rc,
                 ble_conn.last_update_event_status,
                 (unsigned long)ble_conn.update_request_count,
                 (unsigned long)((live_stats.actual_millihz + 500u) / 1000u),
                 (unsigned long)live_stats.actual_millihz,
                 (unsigned long)live_stats.last_gap_us,
                 (unsigned long)live_stats.max_gap_us,
                 device_config_bridge_running() ? "running" : "stopped",
                 hid_test_mode_to_string(device_config_get_hid_test_mode()),
                 (unsigned)device_config_get_report_rate_hz(),
                 (unsigned long)((report_stats.actual_millihz + 500u) / 1000u),
                 (unsigned long)report_stats.actual_millihz,
                 (unsigned long)report_stats.sent_total,
                 (unsigned long)report_stats.failed_total,
                 (unsigned long)report_stats.last_gap_us,
                 (unsigned long)report_stats.max_gap_us,
                 live_valid ? "active" : "none",
                 (unsigned long)live_updates,
                 live_valid ? (long long)(live_age_us / 1000) : -1LL,
                 usb_switch2_vendor_hd_rumble_active() ? "active" : "idle",
                 (unsigned long)usb_switch2_vendor_hd_rumble_update_count(),
                 (unsigned long)usb_switch2_vendor_hd_rumble_write_count(),
                 (unsigned long)usb_switch2_vendor_hd_rumble_stop_count(),
                 (unsigned long)usb_switch2_vendor_hd_rumble_error_count(),
                 (unsigned)rumble_scale_percent,
                 (unsigned)rumble_hold_ms,
                 (unsigned)rumble_tick_ms,
                 (unsigned)rumble_stop_packets,
                 device_config_get_version());
        return json_ok(reply, reply_len, "status", extra);
    }
    if (strcmp(cmd, "hidguard arm") == 0) {
        usb_switch2_vendor_arm_hid_guard();
        return json_ok(reply, reply_len, "hidguard", "\"state\":\"active\"");
    }
    if (strcmp(cmd, "hidguard release") == 0) {
        usb_switch2_vendor_release_hid_guard();
        return json_ok(reply, reply_len, "hidguard", "\"state\":\"done\"");
    }
    if (strcmp(cmd, "mode generic") == 0) {
        esp_err_t err = device_config_save_mode(GENERIC_HID_MODE);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "mode", "failed to save generic mode");
        }
        return json_ok(reply, reply_len, "mode", "\"mode\":\"generic\",\"saved\":true,\"reboot_required\":true,\"note\":\"run reboot, then replug native USB if needed\"");
    }
    if (strcmp(cmd, "mode nintendo") == 0) {
        esp_err_t err = device_config_save_mode(NINTENDO_EXPERIMENT_MODE);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "mode", "failed to save nintendo mode");
        }
        return json_ok(reply, reply_len, "mode", "\"mode\":\"nintendo\",\"saved\":true,\"experimental\":true,\"reboot_required\":true,\"note\":\"run reboot, then replug native USB if needed\"");
    }
    if (strcmp(cmd, "start") == 0) {
        device_config_set_bridge_running(true);
        return json_ok(reply, reply_len, "start", "\"hid\":\"running\"");
    }
    if (strcmp(cmd, "stop") == 0) {
        device_config_set_bridge_running(false);
        return json_ok(reply, reply_len, "stop", "\"hid\":\"stopped\"");
    }
    if (strncmp(cmd, "rate ", 5) == 0 || strncmp(cmd, "report rate ", 12) == 0) {
        const char *rate_text = cmd[0] == 'r' && cmd[4] == ' ' ? cmd + 5 : cmd + 12;
        char *end = NULL;
        long rate_hz = strtol(rate_text, &end, 10);
        while (end && *end && isspace((unsigned char)*end)) {
            end++;
        }
        if (rate_text == end || rate_hz < 20 || rate_hz > 1000 || (end && *end)) {
            return json_error(reply, reply_len, "rate", "usage: rate <20..1000>");
        }
        esp_err_t err = device_config_save_report_rate_hz((uint16_t)rate_hz);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "rate", "failed to save report rate");
        }
        char extra[64];
        snprintf(extra, sizeof(extra), "\"rate_hz\":%u,\"saved\":true", (unsigned)device_config_get_report_rate_hz());
        return json_ok(reply, reply_len, "rate", extra);
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
    if (strcmp(cmd, "ble scan") == 0) {
        return ble_central_start_scan() == ESP_OK ?
            json_ok(reply, reply_len, "ble scan", "\"ble\":\"scanning\"") :
            json_error(reply, reply_len, "ble scan", "scan start failed");
    }
    if (strncmp(cmd, "ble connect", 11) == 0) {
        char target[96];
        snprintf(target, sizeof(target), "%s", cmd + 11);
        trim(target);
        esp_err_t err = ble_central_connect(target[0] ? target : NULL);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "ble connect", "connect start failed; run ble scan and use ble connect last, ble connect <addr>, or ble connect <name>");
        }
        return json_ok(reply, reply_len, "ble connect", "\"ble\":\"connecting\"");
    }
    if (strcmp(cmd, "ble reconnect") == 0 || strcmp(cmd, "ble auto connect") == 0) {
        esp_err_t err = ble_central_reconnect_saved_or_scan();
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "ble reconnect", "reconnect start failed");
        }
        return json_ok(reply, reply_len, "ble reconnect", "\"ble\":\"connecting\"");
    }
    if (strcmp(cmd, "ble fast") == 0 || strcmp(cmd, "ble interval fast") == 0) {
        esp_err_t err = ble_central_request_fast_params();
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "ble fast", "fast connection-parameter request failed or BLE is not connected");
        }
        return json_ok(reply, reply_len, "ble fast", "\"ble_conn_request\":\"fast\"");
    }
    if (strcmp(cmd, "ble auto on") == 0 || strcmp(cmd, "ble autoconnect on") == 0) {
        esp_err_t err = device_config_save_ble_autoconnect(true);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "ble auto", "failed to save BLE autoconnect");
        }
        return json_ok(reply, reply_len, "ble auto", "\"ble_auto\":\"on\"");
    }
    if (strcmp(cmd, "ble auto off") == 0 || strcmp(cmd, "ble autoconnect off") == 0) {
        esp_err_t err = device_config_save_ble_autoconnect(false);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "ble auto", "failed to save BLE autoconnect");
        }
        return json_ok(reply, reply_len, "ble auto", "\"ble_auto\":\"off\"");
    }
    if (strncmp(cmd, "ble target ", 11) == 0) {
        char target[96];
        snprintf(target, sizeof(target), "%s", cmd + 11);
        trim(target);
        esp_err_t err = device_config_save_ble_target(target);
        if (err != ESP_OK) {
            return json_error(reply, reply_len, "ble target", "failed to save BLE target");
        }
        char extra[96];
        snprintf(extra, sizeof(extra), "\"ble_target\":\"%s\"", device_config_get_ble_target());
        return json_ok(reply, reply_len, "ble target", extra);
    }
    if (strcmp(cmd, "ble disconnect") == 0) {
        ble_central_disconnect();
        return json_ok(reply, reply_len, "ble disconnect", "\"ble\":\"idle\"");
    }
    if (strcmp(cmd, "rumble config") == 0 || strcmp(cmd, "rumble tune") == 0 ||
        strncmp(cmd, "rumble tune ", 12) == 0) {
        uint16_t scale_percent = 0;
        uint16_t hold_ms = 0;
        uint16_t tick_ms = 0;
        uint8_t stop_packets = 0;
        usb_switch2_vendor_get_hd_rumble_tuning(&scale_percent, &hold_ms, &tick_ms, &stop_packets);

        if (strncmp(cmd, "rumble tune ", 12) == 0) {
            const char *p = cmd + 12;
            long scale_arg = 0;
            long hold_arg = 0;
            long tick_arg = 0;
            long stop_arg = 0;
            if (!parse_long_token(&p, &scale_arg) ||
                !parse_long_token(&p, &hold_arg) ||
                !parse_long_token(&p, &tick_arg) ||
                !parse_long_token(&p, &stop_arg) ||
                *p ||
                scale_arg < 10 || scale_arg > 250 ||
                hold_arg < 50 || hold_arg > 1000 ||
                tick_arg < 5 || tick_arg > 50 ||
                stop_arg < 1 || stop_arg > 8) {
                return json_error(reply, reply_len, "rumble tune",
                                  "usage: rumble tune <scale_percent 10..250> <hold_ms 50..1000> <tick_ms 5..50> <stop_packets 1..8>");
            }
            usb_switch2_vendor_set_hd_rumble_tuning((uint16_t)scale_arg,
                                                    (uint16_t)hold_arg,
                                                    (uint16_t)tick_arg,
                                                    (uint8_t)stop_arg);
            usb_switch2_vendor_get_hd_rumble_tuning(&scale_percent, &hold_ms, &tick_ms, &stop_packets);
        }

        char extra[128];
        snprintf(extra, sizeof(extra),
                 "\"scale_percent\":%u,\"hold_ms\":%u,\"tick_ms\":%u,\"stop_packets\":%u",
                 (unsigned)scale_percent,
                 (unsigned)hold_ms,
                 (unsigned)tick_ms,
                 (unsigned)stop_packets);
        return json_ok(reply, reply_len, "rumble tune", extra);
    }
    if (strcmp(cmd, "rumble hdtest") == 0 || strcmp(cmd, "rumble test") == 0) {
        usb_switch2_vendor_start_hd_rumble_self_test();
        return json_ok(reply, reply_len, "rumble hdtest", "\"rumble\":\"active\",\"mode\":\"hd_stream_self_test\"");
    }
    if (strncmp(cmd, "rumble hold ", 12) == 0) {
        const char *p = cmd + 12;
        long hold_ms = 0;
        if (!parse_long_token(&p, &hold_ms) || *p || hold_ms < 100 || hold_ms > 10000) {
            return json_error(reply, reply_len, "rumble hold", "usage: rumble hold <ms 100..10000>");
        }
        usb_switch2_vendor_start_hd_rumble_self_test_ms((uint16_t)hold_ms);
        char extra[80];
        snprintf(extra, sizeof(extra), "\"rumble\":\"active\",\"mode\":\"hd_stream_hold\",\"hold_ms\":%ld", hold_ms);
        return json_ok(reply, reply_len, "rumble hold", extra);
    }
    if (strcmp(cmd, "rumble stop") == 0) {
        usb_switch2_vendor_stop_hd_rumble();
        return json_ok(reply, reply_len, "rumble stop", "\"rumble\":\"stopping\"");
    }
    if (strcmp(cmd, "hid test_a") == 0) {
        device_config_set_hid_test_mode(HID_TEST_A_HELD);
        return json_ok(reply, reply_len, "hid test_a", "\"test_mode\":\"a_held\"");
    }
    if (strcmp(cmd, "hid neutral") == 0) {
        device_config_set_hid_test_mode(HID_TEST_NEUTRAL);
        return json_ok(reply, reply_len, "hid neutral", "\"test_mode\":\"neutral\"");
    }
    if (strcmp(cmd, "hid auto_a") == 0) {
        device_config_set_hid_test_mode(HID_TEST_AUTO_A);
        return json_ok(reply, reply_len, "hid auto_a", "\"test_mode\":\"auto_a\"");
    }
    if (strcmp(cmd, "version") == 0) {
        char extra[64];
        snprintf(extra, sizeof(extra), "\"version\":\"%s\"", device_config_get_version());
        return json_ok(reply, reply_len, "version", extra);
    }

    return json_error(reply, reply_len, cmd[0] ? cmd : "empty", "unknown command");
}
