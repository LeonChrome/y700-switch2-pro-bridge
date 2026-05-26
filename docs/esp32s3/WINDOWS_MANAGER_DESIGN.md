# Windows Manager Design

Status: PENDING_HARDWARE_TEST.

Technology: C# .NET 8 WPF.

Reason: Windows 11 is the target platform, the app needs local serial access, script launching, log viewing, and a native desktop UI. WPF is a conservative fit and avoids making Python/PySide6 runtime setup part of the first ESP32-S3 bring-up.

## Scope

The manager is not a virtual gamepad driver. It does not replace the HID device. It manages the ESP32-S3 dongle.

## Pages

- Dashboard
- Device connection
- Control
- Logs
- Flash
- Recognition checks
- Settings
- About

## Serial Protocol

The manager sends one text command per line and expects JSON lines. Logs are displayed separately and can be filtered by keywords.

## Flash Page

The flash page calls:

```text
tools/esp32s3/build.ps1
tools/esp32s3/flash.ps1
tools/esp32s3/monitor.ps1
```

It must show:

```text
Flashing/logging: CH343P Type-C
HID tests: ESP32-S3 native USB & OTG Type-C
```

## Disclaimer

This project is not affiliated with, endorsed by, or sponsored by Nintendo.

This tool is intended for personal input-device compatibility research.
