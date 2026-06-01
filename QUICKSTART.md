# Quickstart

This is the shortest path for the V5 ESP32-S3 Switch 2 Pro bridge.

## Requirements

- ESP32-S3-N16R8 board with CH343P Type-C and native USB & OTG Type-C.
- Windows PC.
- Real Switch 2 Pro Controller.
- V5 release asset: `Y700Switch2Manager-aio-v5.0.0.exe` or `esp32s3-pro2-bridge-v5.0.0-20260601.zip`.

## Option A: All-in-one Manager

1. Download `Y700Switch2Manager-aio-v5.0.0.exe`.
2. Connect the board's `CH343P Type-C` port.
3. Open the EXE, select the CH343P COM port, then flash the bundled V5 firmware.
4. Connect or replug the native USB & OTG Type-C port.
5. In the Manager, confirm:

```text
usb=mounted
bulk=mounted
version=5.0.0
rate_hz=250
ble=connected
live=active
BLE input Hz ~= 133 Hz
```

The Manager keeps `250 Hz` as the gyro-friendly default and still exposes `1000 Hz` as an optional experimental USB report rate.

## Option B: Zip Package

Extract `esp32s3-pro2-bridge-v5.0.0-20260601.zip`. From the extracted folder, flash over the CH343P port:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

If the port is not COM12:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

Replace `COM12` with the detected CH343P port.

## Connect Native USB

After flashing, connect or replug the native USB & OTG Type-C port. Windows should enumerate the Nintendo-style HID path:

```text
VID_057E PID_2069
Nintendo Switch Pro Controller
Nintendo Switch 2 bulk
```

## Connect The Pro2 Controller

BLE auto-reconnect is enabled by default. If a Pro2 address was saved before, the firmware connects automatically after boot. You can also click reconnect in the Manager or run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 30
```

## Recommended Steam/Gyro Baseline

- Keep USB report rate at `250 Hz` for the current best gyro stability.
- Use `1000 Hz` only when intentionally testing high USB output cadence.
- Gyro is mapped close to raw from BLE FD2 motion bytes `48..59` into USB report `0x05`.
- Voice, headphone audio, microphone audio, and full HD Rumble 2 audio are not implemented.

## Useful Tests

```powershell
# Query firmware status
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "status" -ReadSeconds 5

# Set recommended USB report rate
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 250" -ReadSeconds 3

# Optional 1000 Hz test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 1000" -ReadSeconds 3

# Measure host-observed HID report rate
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-SwitchHidRate.ps1 -Seconds 5
```
