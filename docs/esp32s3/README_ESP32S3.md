# ESP32-S3 Bridge Track

## 中文

这个目录记录 V4 ESP32-S3 版本。V4 已经不再只是计划或 skeleton：ESP32-S3 现在可以直接连接真实 Switch 2 Pro Controller，并通过 USB 暴露 Nintendo Switch Pro Controller 兼容设备给 Windows / Steam。

## English

This directory documents the V4 ESP32-S3 build. V4 is no longer just a plan or scaffold: the ESP32-S3 can now connect directly to a real Switch 2 Pro Controller and expose a Nintendo Switch Pro Controller-compatible USB device to Windows / Steam.

## Current Status / 当前状态

## 中文

已验证：

- CH343P 串口烧录、日志和控制。
- Native USB 枚举为 `VID_057E PID_2069` Nintendo Switch Pro Controller。
- Steam Nintendo Switch Pro / Pro2 输入路径可用。
- BLE GATT discovery、notify subscribe、输入解析和自动重连。
- BLE interval 请求 `6`，即 `7.5 ms`，输入约 `133.3 Hz`。
- USB report loop 可接近 `1000 Hz`。
- Rumble / HD rumble 有物理反馈。
- Windows Manager 可查看关键状态并发送控制命令。

## English

Verified:

- CH343P serial flashing, logging, and control.
- Native USB enumerates as `VID_057E PID_2069` Nintendo Switch Pro Controller.
- Steam Nintendo Switch Pro / Pro2 input path works.
- BLE GATT discovery, notify subscription, input parsing, and auto-reconnect.
- BLE interval request `6`, meaning `7.5 ms`, with about `133.3 Hz` input.
- USB report loop can approach `1000 Hz`.
- Rumble / HD rumble produces physical feedback.
- Windows Manager shows key status fields and sends control commands.

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

## Current Deliverables / 当前产物

- ESP-IDF firmware.
- TinyUSB Nintendo HID + vendor bulk descriptors.
- BLE Central scan/connect/GATT discovery/notify-subscribe pipeline.
- Pro2 input and rumble bridge.
- Runtime HID report-rate control persisted in NVS.
- Windows .NET 8 WPF Manager with serial, HID feature, and bulk control transports.
- Flash/build/monitor scripts.
- Release flasher package.

## Build

```powershell
.\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

Build and flashing were observed with ESP-IDF v5.3.3 on 2026-05-29.

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
