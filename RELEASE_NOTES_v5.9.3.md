# V5.9.3 新和联胜稳定版

V5.9.3 freezes the current ESP32-S3 route as the stable V5.9 line before the
Windows-only V6.0 work begins.

## What Changed

- Added the Manager-side CH343 driver repair flow.
- Added explicit ESP32-S3 download-mode error detection and BOOT/RST guidance.
- Added serial command watchdogs so BLE actions cannot hold the UI forever.
- Made serial close/shutdown non-blocking and forced process exit on window
  close.
- Throttled and filtered UI logs while preserving full diagnostic files.

## Release Artifact

```text
release/v5.9/新和联胜版本-aio-v5.9.3.exe
sha256 4df0167eaf74c33ada0e4370304e3db5ae3320a1288bd70c8d488b0934290f7a
```

## Compatibility

V5.9.3 remains the ESP32-S3 three-mode release:

- 新和联胜 / PS5
- Pro2 / Nintendo
- Xbox / XInput

V6.0 will be a separate Windows-only route based on the VIIPER direction and
does not replace the V5.9 ESP32-S3 release.
