# Y700 / ESP32-S3 Switch 2 Pro Bridge

Low-cost BLE-to-USB hardware bridge for the Switch 2 Pro Controller.

The current mainline is the ESP32-S3 receiver: a real Switch 2 Pro Controller connects to the ESP32-S3 over BLE, and the board exposes a Nintendo Switch Pro / Pro2-style USB HID controller to Windows / Steam. The older Lenovo Y700 Android USB Gadget route remains in the repository as historical research material, but new users should start with the ESP32-S3 path.

## Current Release

| Track | Status | Recommended for | Notes |
| --- | --- | --- | --- |
| V5.0.0 ESP32-S3 Pro2 Bridge | Stable / 正式版 | Normal Windows / Steam users | All-in-one Manager EXE, bundled V5 firmware, BLE input, raw-like gyro, rumble bridge, USB `0x05` full report |
| Y700 Android USB Gadget route | Legacy / 历史方案 | Research / previous Y700 users | Kept for reference; the mainline has moved to ESP32-S3 |

## V5 Highlights

- Windows / Steam use the Nintendo Switch Pro / Pro2-style path with USB input report ID `0x05`.
- Switch 2 Pro BLE input uses the FD2 full report; measured BLE input is around the `133 Hz` class on tested hardware.
- Gyro uses the Pro2 BLE FD2 motion block at `bytes 48..59` and maps it into the USB `0x05` motion block with a raw-like path. Default smoothing, scaling, deadband, and auto calibration are off.
- USB report loop defaults to `250 Hz`, which is the current gyro-stability recommendation. `1000 Hz` is still available in the Manager and serial command path as an optional experimental mode.
- Buttons, D-pad, sticks, stick clicks, triggers, `+`, `-`, `Home`, `Capture`, `C`, `GL`, `GR`, BLE auto-reconnect, Steam init guard, and manager status/control paths are integrated.
- Rumble produces usable physical feedback and is not a single fixed preset; it tracks Steam/SDL HID OUT rumble updates and drives the Pro2 BLE rumble stream.
- Voice, headphone audio, microphone audio, and full HD Rumble 2 audio reproduction are not implemented.

## Release Downloads

Normal users should download release assets from GitHub Releases rather than picking binaries manually from the repository tree.

```text
Y700Switch2Manager-aio-v5.0.0.exe
esp32s3-pro2-bridge-v5.0.0-20260601.zip
SHA256SUMS-v5.0.0.txt
```

The all-in-one Manager EXE is the simplest path: it includes the V5 firmware payload, flasher, driver hints, BLE scan list, status panel, report-rate controls, gyro-friendly `250 Hz` default, and optional `1000 Hz` command.

## Hardware

Current tested board shape:

- ESP32-S3-N16R8.
- 16MB flash.
- 8MB PSRAM.
- CH343P Type-C for flashing, logs, and serial control.
- ESP32-S3 native USB & OTG Type-C for USB HID output to Windows / Steam.

Other board ports are welcome, but they should document the board model, SDK version, USB path, BLE path, and measured behavior.

## Quick Start

### All-in-one Manager

1. Download `Y700Switch2Manager-aio-v5.0.0.exe` from GitHub Releases.
2. Connect the board's `CH343P Type-C` port.
3. Open the EXE, choose the COM port, then flash the bundled V5 firmware.
4. Connect or replug the native USB & OTG port if Windows / Steam does not refresh HID enumeration.
5. Connect the real Switch 2 Pro Controller over BLE from the Manager, or use the saved-target auto reconnect path.

### Zip Package

1. Download and extract `esp32s3-pro2-bridge-v5.0.0-20260601.zip`.
2. Connect the board's `CH343P Type-C` port.
3. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

If the CH343P port is not COM12, detect it first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

## Useful Commands

```powershell
# Query firmware status
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "status" -ReadSeconds 5

# Force reconnect to the saved Pro2 target
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 30

# Gyro-friendly default
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 250" -ReadSeconds 3

# Optional experimental USB output cadence
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 1000" -ReadSeconds 3

# Measure host-observed HID report rate on Windows
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-SwitchHidRate.ps1 -Seconds 5

# Rumble smoke test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rumble hold 3000" -ReadSeconds 5
```

## Performance Notes

- Real input freshness is represented by `BLE input Hz` / `ble_input_actual_mhz`.
- USB HID output can run faster than BLE input. When USB report rate is higher than BLE input rate, USB repeats the latest BLE controller state.
- `1000 Hz` USB output does not mean the physical controller generates 1000 new BLE samples per second.
- Host applications may show lower rates because OS/game APIs can coalesce or throttle events.
- Gyro feel depends on Steam input settings, game mouse handling, USB cable quality, BLE environment, and controller firmware.

## Documentation

- [Quickstart](QUICKSTART.md)
- [V5.0.0 Release Notes](RELEASE_NOTES_v5.0.0.md)
- [V5.0.0 Preview Notes](RELEASE_NOTES_v5.0.0-preview.md)
- [V4.0.0 Release Notes](RELEASE_NOTES_v4.0.0.md)
- [ESP32-S3 documentation](docs/esp32s3/README_ESP32S3.md)
- [Control protocol](docs/esp32s3/CONTROL_PROTOCOL.md)
- [Test matrix](docs/TEST_MATRIX.md)
- [Release packaging plan](docs/RELEASE_PACKAGING_PLAN.md)
- [Contributing](CONTRIBUTING.md)

## Repository Layout

```text
firmware/esp32s3_switch2_bridge/   ESP-IDF firmware for the ESP32-S3 bridge
windows/manager_app/               .NET 8 WPF Manager and all-in-one flasher source
tools/esp32s3/                     Build, flash, monitor, and serial command scripts
tools/                             HID, Steam, haptic, and rate-test helper tools
docs/esp32s3/                      ESP32-S3 protocol, design, and troubleshooting docs
release/                           Local packaged artifacts; public downloads should use GitHub Releases
src/                               Historical Y700 Android bridge/responder sources
```

## Disclaimer

This project is not affiliated with, endorsed by, or sponsored by Nintendo, Valve, Microsoft, Apple, Google, Espressif, or any other hardware/software vendor. Nintendo Switch, Switch Pro Controller, Steam, Windows, macOS, Android, ESP32, and related names are trademarks of their respective owners.

This is an experimental research project. Results may vary with different ESP32-S3 boards, Windows builds, Steam versions, USB cables, BLE environments, games, and controller firmware.

## License

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.
