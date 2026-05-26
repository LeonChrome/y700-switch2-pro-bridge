#include <ctype.h>
#include <stdio.h>
#include <string.h>
#include "esp_system.h"
#include "app_log.h"
#include "ble_central.h"
#include "device_config.h"
#include "hid_report.h"
#include "usb_hid_device.h"
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
    APP_LOGI(TAG, "PENDING_HARDWARE_TEST: CH343P serial RX command loop must be wired and verified");
}

esp_err_t control_protocol_handle_line(const char *line, char *reply, int reply_len)
{
    char cmd[96];
    snprintf(cmd, sizeof(cmd), "%s", line ? line : "");
    trim(cmd);
    APP_LOGI(TAG, "command: %s", cmd);

    if (strcmp(cmd, "status") == 0) {
        char extra[192];
        snprintf(extra, sizeof(extra),
                 "\"mode\":\"%s\",\"usb\":\"%s\",\"ble\":\"%s\",\"hid\":\"%s\",\"version\":\"%s\"",
                 device_mode_to_string(device_config_get_mode()),
                 usb_hid_device_state_string(),
                 ble_central_state_string(),
                 device_config_bridge_running() ? "running" : "stopped",
                 device_config_get_version());
        return json_ok(reply, reply_len, "status", extra);
    }
    if (strcmp(cmd, "mode generic") == 0) {
        device_config_set_mode(GENERIC_HID_MODE);
        return json_ok(reply, reply_len, "mode", "\"mode\":\"generic\",\"note\":\"replug native USB may be required\"");
    }
    if (strcmp(cmd, "mode nintendo") == 0) {
        device_config_set_mode(NINTENDO_EXPERIMENT_MODE);
        return json_ok(reply, reply_len, "mode", "\"mode\":\"nintendo\",\"experimental\":true,\"note\":\"replug native USB may be required\"");
    }
    if (strcmp(cmd, "start") == 0) {
        device_config_set_bridge_running(true);
        return json_ok(reply, reply_len, "start", "\"hid\":\"running\"");
    }
    if (strcmp(cmd, "stop") == 0) {
        device_config_set_bridge_running(false);
        return json_ok(reply, reply_len, "stop", "\"hid\":\"stopped\"");
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
            json_ok(reply, reply_len, "ble scan", "\"ble\":\"scanning\",\"pending\":\"PENDING_HARDWARE_TEST\"") :
            json_error(reply, reply_len, "ble scan", "scan start failed");
    }
    if (strncmp(cmd, "ble connect", 11) == 0) {
        return json_error(reply, reply_len, "ble connect", "not implemented yet PENDING_HARDWARE_TEST");
    }
    if (strcmp(cmd, "ble disconnect") == 0) {
        ble_central_disconnect();
        return json_ok(reply, reply_len, "ble disconnect", "\"ble\":\"idle\"");
    }
    if (strcmp(cmd, "hid test_a") == 0) {
        return json_ok(reply, reply_len, "hid test_a", "\"queued\":true");
    }
    if (strcmp(cmd, "hid neutral") == 0) {
        return json_ok(reply, reply_len, "hid neutral", "\"queued\":true");
    }
    if (strcmp(cmd, "version") == 0) {
        char extra[64];
        snprintf(extra, sizeof(extra), "\"version\":\"%s\"", device_config_get_version());
        return json_ok(reply, reply_len, "version", extra);
    }

    return json_error(reply, reply_len, cmd[0] ? cmd : "empty", "unknown command");
}
