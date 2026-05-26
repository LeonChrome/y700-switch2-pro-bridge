# ESP32-S3 Bridge Track

This directory documents the planned ESP32-S3 MCU version of the Y700 Switch 2 Pro Controller bridge.

Current status: PENDING_HARDWARE_TEST.

No real ESP32-S3 board has been flashed or tested yet. Do not treat this firmware as validated.

## Goal

Replace the rooted Y700 bridge role with an ESP32-S3 device:

```text
real Switch 2 Pro Controller BLE notify
-> ESP32-S3 BLE Central
-> Switch2 state mapper
-> TinyUSB HID Device
-> Windows / Steam HID path
```

## Hardware Assumption

- ESP32-S3-N16R8
- 16MB flash
- 8MB PSRAM
- CH343P Type-C for flashing, logs, and serial control
- ESP32-S3 native USB & OTG Type-C for TinyUSB HID Device

## Current Offline Deliverables

- ESP-IDF project skeleton
- TinyUSB HID descriptor and report scaffolding
- Generic HID mode
- Nintendo experimental identity mode
- Serial JSON control protocol
- BLE Central skeleton
- Windows .NET 8 WPF Manager skeleton
- Flash/build/monitor scripts
- Hardware test checklist

## Build

```powershell
.\tools\esp32s3\build.ps1
```

PENDING_HARDWARE_TEST: build has not been verified against the target board.
