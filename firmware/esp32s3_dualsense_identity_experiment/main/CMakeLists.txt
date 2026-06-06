if(NOT DEFINED V5_5_DS5_PROFILE)
    set(V5_5_DS5_PROFILE "hid_audio_uac2")
endif()

set(DS5_ENABLE_USB_AUDIO 0)
set(DS5_ENABLE_UAC2_AUDIO 0)
set(DS5_PROFILE_SERIAL "V55HIDONLY")
set(main_sources
    "app_main.c"
    "usb_dualsense_descriptor.c"
    "dualsense_report.c"
    "dualsense_report_mapper.c"
    "pro2_input_backend.c"
    "pro2_rumble_backend.c"
)

if(V5_5_DS5_PROFILE STREQUAL "hid_audio_uac2")
    set(DS5_ENABLE_USB_AUDIO 1)
    set(DS5_ENABLE_UAC2_AUDIO 1)
    set(DS5_PROFILE_SERIAL "V55PHASE3")
    list(APPEND main_sources
        "dualsense_haptic_audio.c"
        "haptic_audio_to_raw02.c"
    )
elseif(V5_5_DS5_PROFILE STREQUAL "hid_only")
    set(DS5_PROFILE_SERIAL "V55HIDONLY")
elseif(V5_5_DS5_PROFILE STREQUAL "hid_audio_uac1_fallback")
    set(DS5_PROFILE_SERIAL "V55UAC1FB")
else()
    message(FATAL_ERROR "Unknown V5_5_DS5_PROFILE: ${V5_5_DS5_PROFILE}")
endif()

idf_component_register(
    SRCS
        ${main_sources}
    INCLUDE_DIRS
        "."
        "tinyusb_config"
    REQUIRES
        esp_tinyusb
        pro2_bridge_reuse
        nvs_flash
        esp_timer
)

target_compile_definitions(${COMPONENT_LIB} PRIVATE
    DS5_PROFILE_NAME=\"${V5_5_DS5_PROFILE}\"
    DS5_PROFILE_SERIAL=\"${DS5_PROFILE_SERIAL}\"
    DS5_ENABLE_USB_AUDIO=${DS5_ENABLE_USB_AUDIO}
    DS5_ENABLE_UAC2_AUDIO=${DS5_ENABLE_UAC2_AUDIO}
)

idf_component_get_property(tinyusb_lib espressif__tinyusb COMPONENT_LIB)
if(tinyusb_lib)
    target_include_directories(${tinyusb_lib} BEFORE PRIVATE
        "${CMAKE_CURRENT_LIST_DIR}/tinyusb_config")
    target_compile_definitions(${tinyusb_lib} PRIVATE
        DS5_ENABLE_USB_AUDIO=${DS5_ENABLE_USB_AUDIO}
        DS5_ENABLE_UAC2_AUDIO=${DS5_ENABLE_UAC2_AUDIO}
    )
endif()
