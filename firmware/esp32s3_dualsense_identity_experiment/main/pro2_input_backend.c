#include "pro2_input_backend.h"

#include "app_log.h"
#include "ble_central.h"
#include "device_config.h"
#include "esp_err.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "v5.5_pro2";

static void autoconnect_task(void *arg)
{
    (void)arg;
    vTaskDelay(pdMS_TO_TICKS(2500));

    for (int attempt = 1; attempt <= 10; attempt++) {
        esp_err_t err = ble_central_reconnect_saved_or_scan();
        if (err == ESP_OK) {
            ESP_LOGI(TAG, "[PRO2_INPUT] autoconnect_started=true attempt=%d", attempt);
            vTaskDelete(NULL);
            return;
        }

        ESP_LOGW(TAG,
                 "[PRO2_INPUT] autoconnect_started=false attempt=%d err=%s",
                 attempt,
                 esp_err_to_name(err));
        vTaskDelay(pdMS_TO_TICKS(1000));
    }

    ESP_LOGE(TAG, "[PRO2_INPUT] autoconnect_failed=true");
    vTaskDelete(NULL);
}

void pro2_input_backend_init(void)
{
    app_log_init();
    device_config_init();
    switch2_state_init();
    ble_central_init();

    BaseType_t created = xTaskCreate(autoconnect_task,
                                     "pro2_autoconnect",
                                     4096,
                                     NULL,
                                     4,
                                     NULL);
    ESP_ERROR_CHECK(created == pdPASS ? ESP_OK : ESP_FAIL);
}

bool pro2_input_backend_get_live(switch2_state_t *state,
                                 uint32_t *updates,
                                 int64_t *age_us)
{
    return switch2_state_get_live(state, updates, age_us);
}

const char *pro2_input_backend_state(void)
{
    return ble_central_state_string();
}
