# ESP32-S3 Switch 2 Bridge Firmware

This is an ESP-IDF skeleton for an ESP32-S3 MCU version of the Y700 Switch 2 Pro bridge.

PENDING_HARDWARE_TEST:

- build pending verification
- flash pending verification
- USB HID enumeration pending verification
- joy.cpl recognition pending verification
- Steam recognition pending verification
- BLE scan/connect/notify pending verification

Default mode is `GENERIC_HID_MODE`. `NINTENDO_EXPERIMENT_MODE` is present only for later compatibility experiments and must not be described as official support.

Expected board:

- ESP32-S3-N16R8
- 16MB flash
- 8MB PSRAM
- Native ESP32-S3 USB & OTG Type-C for TinyUSB HID Device
- CH343P Type-C for flashing, logs, and serial control
