# ESP32-S3 Bridge Track

This directory documents the current ESP32-S3 mainline. As of V5.0.0, the ESP32-S3 can connect directly to a real Switch 2 Pro Controller over BLE and expose a Nintendo Switch Pro / Pro2-style USB HID device to Windows / Steam.

## Verified Status

- CH343P serial flashing, logging, and control.
- Native USB enumerates as `VID_057E PID_2069` Nintendo Switch Pro Controller plus the project bulk/control interface.
- Steam Nintendo Switch Pro / Pro2 input path works.
- BLE GATT discovery, FD2 notify subscription, input parsing, and auto-reconnect.
- BLE interval request `6`, meaning `7.5 ms`, with about `133 Hz` input in the tested environment.
- USB input report ID `0x05` full extended report.
- Gyro uses FD2 motion bytes `48..59` and USB report `0x05` bytes `49..60` with raw-like defaults.
- USB report loop defaults to `250 Hz`; `1000 Hz` remains available as an experimental option.
- Rumble produces physical feedback from Steam/SDL HID OUT updates.
- Windows Manager can flash bundled firmware, show status, reconnect BLE, and send control commands.

## Not Implemented

- Voice.
- Headphone audio.
- Microphone audio.
- Full HD Rumble 2 audio reproduction.
- Dual-controller mode.
- Stable macOS/Android generic profiles.

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

## Current Deliverables

- ESP-IDF firmware.
- TinyUSB Nintendo HID + vendor bulk descriptors.
- BLE Central scan/connect/GATT discovery/FD2 notify pipeline.
- Pro2 input, raw-like gyro passthrough, and rumble bridge.
- Runtime HID report-rate control persisted in NVS.
- Windows .NET 8 WPF Manager with serial, HID feature, and bulk control transports.
- Flash/build/monitor scripts.
- V5 release package and all-in-one Manager EXE.

## Build

```powershell
.\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

Build and publishing were observed with ESP-IDF v5.3.3 and .NET 8 on 2026-06-01.

## Manager EXE

The self-contained manager publish output is:

```text
windows\manager_app\bin\Release\net8.0-windows\win-x64\publish\Y700Switch2Manager.exe
```

It is built with .NET 8 and published by:

```powershell
.\windows\manager_app\publish_self_contained.ps1
```

## Flash And Serial Notes

Use the CH343P Type-C port for build/flash/log/control:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

Board-specific note from first bring-up: serial-line behavior differed by host library. Python/pyserial and .NET both worked after the correct reset/release sequence, and .NET/PowerShell reads worked with `DTR=False, RTS=False`. If a generic serial reader opens the COM port and sees no output, check DTR/RTS handling before assuming the firmware is not running.

If flashing fails at high speed, retry with `-Baud 115200`, then `-NoStub -Baud 115200`.
