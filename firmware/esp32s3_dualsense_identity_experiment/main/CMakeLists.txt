idf_component_register(
    SRCS
        "app_main.c"
        "usb_dualsense_descriptor.c"
        "dualsense_report.c"
        "dualsense_report_mapper.c"
        "dualsense_haptic_audio.c"
        "haptic_audio_to_raw02.c"
        "pro2_input_backend.c"
        "pro2_rumble_backend.c"
    INCLUDE_DIRS
        "."
        "tinyusb_config"
    REQUIRES
        esp_tinyusb
        pro2_bridge_reuse
        nvs_flash
        esp_timer
)

idf_component_get_property(tinyusb_lib espressif__tinyusb COMPONENT_LIB)
if(tinyusb_lib)
    target_include_directories(${tinyusb_lib} BEFORE PRIVATE
        "${CMAKE_CURRENT_LIST_DIR}/tinyusb_config")
endif()
