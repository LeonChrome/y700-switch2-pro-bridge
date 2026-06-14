# V6.0.0-preview VIIPER Windows-only

This is the first V6.0 preview line. It is separate from the V5.9 ESP32-S3
release.

## Scope

The preview app can:

- connect to a VIIPER server on `localhost:3242`;
- scan and open Windows-paired Pro2/Switch Pro HID input devices;
- create virtual USB devices for:
  - 新和联胜 / PS5 (`dualsense`)
  - Pro2 / Nintendo (`ns2pro`)
  - Xbox / XInput (`xbox360`)
- feed live Pro2 input reports when available, with neutral fallback;
- read and log host feedback packets, including rumble output.
- start the bundled VIIPER v0.7.0 Windows server, extracting the embedded
  runtime when no repo-side `tools/viiper` copy exists.

The preview app does not yet:

- install `usbip-win2`;
- pair the real Pro2 controller in Windows by itself;
- write host rumble feedback back to the real controller over Bluetooth;
- perform production haptic arbitration.

## Release Artifact

```text
release/v6.0/新和联胜VIIPER版本-aio-v6.0.0-preview.exe
sha256 916503457bf2ad55e1aa0cde73bf0308bc812d57003bcf415bece23064306ade
```

## Required External Runtime

Windows users need:

- `usbip-win2`
- VIIPER server

See [docs/V6_0_VIIPER_WINDOWS_ONLY.md](docs/V6_0_VIIPER_WINDOWS_ONLY.md).
