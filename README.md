# PRO2 Wireless Receiver Control Board

Final release: **V5.9.0**

This repository contains the final ESP32-S3 firmware and Windows Manager for a three-mode wireless receiver bridge for the real Switch 2 Pro / Pro2 controller.

The project is archived as a finished personal hardware/software build. No further feature updates are planned.

## Download

Use the all-in-one Windows Manager:

[release/v5.9/PRO2手柄无线接收器控制板-aio-v5.9.0.exe](release/v5.9/PRO2%E6%89%8B%E6%9F%84%E6%97%A0%E7%BA%BF%E6%8E%A5%E6%94%B6%E5%99%A8%E6%8E%A7%E5%88%B6%E6%9D%BF-aio-v5.9.0.exe)

SHA256:

```text
4dcbf9c19ba9f493b316bb35aba3b994ff555a876f7246ff42ba43090cd84137
```

The EXE bundles:

- V5.9 firmware profiles
- esptool
- Windows flashing flow
- BLE scan/connect controls
- USB identity checks
- Pro2 rumble tools
- XInput rumble probe

## What It Does

An ESP32-S3 board connects to the real controller over BLE, then exposes one of three USB controller identities to Windows / Steam:

| Mode | USB identity | Main use |
| --- | --- | --- |
| Pro2 / Nintendo | Nintendo Switch Pro style HID, `057E:2069` | Best default mode for Steam Input and Pro2-style layout |
| Xbox / XInput | Xbox 360 style, `045E:028E` | Games that prefer XInput |
| DualSense-like | DualSense-style HID/audio experiment, `054C:0CE6` | Compatibility and haptic-audio experiments |

V5.9 focuses on preserving controller feel:

- Pro2 / Nintendo mode keeps raw HID report `0x02` rumble authoritative.
- Xbox / XInput and DualSense-like modes preserve left/right motor routing and strength, then shape Pro2 output frequency dynamically.
- Left and right stick Y axes use host-expected polarity.
- BLE auto reconnect keeps daily use working after controller sleep/disconnect.
- The Manager avoids UI freezes when both native USB and CH343P control USB are connected.

## Hardware

Tested target:

- ESP32-S3 N16R8
- 16 MB flash
- 8 MB Octal PSRAM
- CH343P / WCH USB serial control port
- ESP32-S3 native USB / OTG gamepad port

The release firmware uses the safer N16R8 PSRAM profile:

```text
CONFIG_SPIRAM_USE_MEMMAP=y
# CONFIG_SPIRAM_USE_MALLOC is not set
# CONFIG_SPIRAM_MEMTEST is not set
```

## Basic Use

1. Connect the CH343P control USB port.
2. Open the V5.9 Manager EXE.
3. Select or refresh the COM port.
4. Click the target mode card: Pro2 / Nintendo, Xbox / XInput, or DualSense-like.
5. Wait for flashing to finish.
6. Replug the ESP32-S3 native USB / OTG gamepad port.
7. Click USB check in the Manager.
8. Connect the real controller over BLE.

See [docs/USER_GUIDE.md](docs/USER_GUIDE.md) for the full user flow.

## Repository Layout

```text
firmware/
  esp32s3_switch2_bridge/              Pro2/Nintendo and Xbox/XInput bridge firmware
  esp32s3_dualsense_identity_experiment/ DualSense-like firmware

windows/
  v55_manager_app/                     Final V5.9 Windows Manager

tools/
  esp32s3/                             ESP-IDF build/flash helpers
  package_v5_9_manager.ps1             Final all-in-one package script

release/
  v5.9/                                Final EXE and SHA256

docs/
  USER_GUIDE.md
  TROUBLESHOOTING.md
  TECHNICAL_NOTES.md
```

## Build

For normal users, building is not required. Use the release EXE.

For source builds:

```powershell
.\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
.\tools\package_v5_9_manager.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

To package from already-built firmware:

```powershell
.\tools\package_v5_9_manager.ps1 -SkipFirmwareBuild
```

## Notes

- This is an independent experimental project.
- It is not affiliated with Nintendo, Sony, Microsoft, Valve, Espressif, or WCH.
- Brand names and USB identities are used only to describe compatibility behavior.

## License

Apache License 2.0.
