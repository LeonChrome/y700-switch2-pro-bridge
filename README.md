# PRO2 Wireless Receiver Control Board

Current release: **V5.9.3 新和联胜版本**

Next development line: **V6.0 VIIPER Windows-only route**. V6.0 is separate
from the ESP32-S3 V5.9 series and targets a no-ESP32 Windows Bluetooth feeder
using VIIPER virtual USB devices. The current preview does not require Windows
Bluetooth HID pairing: the EXE scans and connects the real Pro2 over BLE/GATT,
subscribes to FD2 input, feeds the three VIIPER virtual modes, and converts
host rumble back to the Pro2 BLE cc48 rumble stream.

This repository contains the ESP32-S3 firmware and Windows Manager for a three-mode wireless receiver bridge for the real Switch 2 Pro / Pro2 controller.

## Download

Use the all-in-one Windows Manager:

[release/v5.9/新和联胜版本-aio-v5.9.3.exe](release/v5.9/%E6%96%B0%E5%92%8C%E8%81%94%E8%83%9C%E7%89%88%E6%9C%AC-aio-v5.9.3.exe)

SHA256:

```text
4df0167eaf74c33ada0e4370304e3db5ae3320a1288bd70c8d488b0934290f7a
```

The EXE bundles:

- V5.9 firmware profiles
- esptool
- Windows flashing flow
- BLE scan/connect controls
- USB identity checks
- Pro2 rumble tools
- XInput rumble probe
- First-pair, saved-controller reconnect, and controller-replacement guidance

## What It Does

An ESP32-S3 board connects to the real controller over BLE, then exposes one of three USB controller identities to Windows / Steam:

| Mode | USB identity | Main use |
| --- | --- | --- |
| 新和联胜 / PS5 | DualSense-compatible HID/audio, `054C:0CE6` | PS5 identity, ordinary rumble, and four-channel HD haptic audio |
| Pro2 / Nintendo | Nintendo Switch Pro style HID, `057E:2069` | Steam Input and native Pro2-style layout |
| Xbox / XInput | Xbox 360 style, `045E:028E` | Games that prefer XInput |

V5.9 focuses on preserving controller feel:

- Pro2 / Nintendo mode keeps raw HID report `0x02` rumble authoritative.
- 新和联胜 and Xbox / XInput preserve left/right ordinary motor routing and strength, then shape Pro2 output frequency dynamically.
- Left and right stick Y axes use host-expected polarity.
- BLE auto reconnect keeps daily use working after controller sleep/disconnect.
- The Manager avoids UI freezes when both native USB and CH343P control USB are connected.
- 新和联胜 gates HID IN submissions immediately on TinyUSB bus reset, preventing reports from racing endpoint reconfiguration.
- Diagnostics distinguish HID submit failures from completed-transfer failures and record USB bus resets/configuration resets.
- The Manager separates first pairing, reconnecting a saved controller, and replacing the saved controller.
- Pro2 and Xbox release profiles lock their USB identity, so stale NVS mode
  values cannot override a successful flash.
- Flashing logs the active serial driver and blocks the reproduced Windows
  26300 + WCH `2.1.2025.7` kernel-hang combination before esptool starts.
- V5.9.3 adds a Manager-side CH343 driver repair button, serial command
  watchdogs, shutdown cleanup, and throttled UI logs for BLE-heavy sessions.

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
4. Click the target mode card: 新和联胜 / PS5, Pro2 / Nintendo, or Xbox / XInput.
5. Wait for flashing to finish.
6. Replug the ESP32-S3 native USB / OTG gamepad port.
7. Click USB check in the Manager.
8. For a new controller, click `首次连接`; for a saved controller, wake it or click `重连已配对`.

See [docs/USER_GUIDE.md](docs/USER_GUIDE.md) for the full user flow.

## Repository Layout

```text
firmware/
  esp32s3_switch2_bridge/              Pro2/Nintendo and Xbox/XInput bridge firmware
  esp32s3_dualsense_identity_experiment/ 新和联胜 / PS5 firmware

windows/
  v55_manager_app/                     Final V5.9 Windows Manager
  v60_viiper_app/                      V6.0 Windows-only VIIPER Manager preview

tools/
  esp32s3/                             ESP-IDF build/flash helpers
  package_v5_9_manager.ps1             Final all-in-one package script
  package_v6_0_viiper_manager.ps1      V6.0 VIIPER preview package script
  viiper/                              Bundled VIIPER runtime notes and exe

release/
  v5.9/                                Final EXE and SHA256
  v6.0/                                V6.0 VIIPER Windows-only preview EXE

docs/
  USER_GUIDE.md
  TROUBLESHOOTING.md
  TECHNICAL_NOTES.md
  V6_0_VIIPER_WINDOWS_ONLY.md
```

## Build

For normal users, building is not required. Use the release EXE.

Install the project-local ESP-IDF 5.4.2 environment once:

```powershell
.\tools\setup_dev_environment.ps1
```

Build all firmware profiles:

```powershell
.\tools\build_all.ps1
```

Build all profiles and package the Manager:

```powershell
.\tools\build_all.ps1 -Package
```

To package from already-built firmware:

```powershell
.\tools\package_v5_9_manager.ps1 -SkipFirmwareBuild
```

See [docs/DEVELOPMENT_ENVIRONMENT.md](docs/DEVELOPMENT_ENVIRONMENT.md) for
toolchain details and [docs/PS5_PRO2_ROADMAP.md](docs/PS5_PRO2_ROADMAP.md) for
the DualSense compatibility and stability roadmap.

## Notes

- This is an independent experimental project.
- It is not affiliated with Nintendo, Sony, Microsoft, Valve, Espressif, or WCH.
- Brand names and USB identities are used only to describe compatibility behavior.

## License

Apache License 2.0.
