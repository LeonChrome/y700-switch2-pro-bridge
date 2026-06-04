# V5.0.0 Release Notes

Date: 2026-06-01
Manager asset refreshed: 2026-06-04

V5.0.0 is the first formal all-in-one ESP32-S3 Switch 2 Pro bridge release. It promotes the previous Manager preview into the main recommended path and bundles V5 firmware instead of the older V4 payload.

## Recommended Assets

```text
Y700Switch2Manager-aio-v5.0.0.exe
esp32s3-pro2-bridge-v5.0.0-20260601.zip
SHA256SUMS-v5.0.0.txt
```

## What Changed Since V4

- USB input report ID changed to `0x05` and the HID descriptor was updated so Steam can use the full Nintendo extended input report path.
- Pro2 BLE motion parsing now uses the FD2 full report motion block at `bytes 48..59`.
- USB `0x05` motion output is filled at report bytes `49..60`.
- The firmware subscribes to the FD2 notify stream for live input/motion instead of allowing compact C0F8 notify to steal the active stream.
- Gyro passthrough is raw-like by default: no smoothing, no scale change, no deadband, no auto-calibration.
- Default USB report loop changed from `125 Hz` to `250 Hz` for better gyro stability.
- `1000 Hz` USB output remains available as an optional experimental setting in both Manager and serial commands.
- Manager V5 bundles firmware `5.0.0` and exposes the `250` gyro recommendation while keeping the `1000` experimental button.
- Manager BLE panel now exposes explicit last-address auto-reconnect on/off buttons; startup auto-assist respects the saved off state.

## Current Verified Behavior

- BLE connects to a real Switch 2 Pro Controller.
- BLE input is around the `133 Hz` class in the tested environment.
- Steam / Windows use the Nintendo Switch Pro / Pro2-style path.
- Buttons, sticks, triggers, system buttons, and Pro2-specific buttons are mapped.
- Gyro axes and raw-like motion path have been validated in Steam and Aimlabs.
- Rumble bridge produces usable physical feedback and follows Steam/SDL HID OUT rumble updates.
- Manager can flash, erase/reflash, detect CH343/CH340/WCH ports, show status, reconnect BLE, set USB report rate, and expose diagnostics.

## Rumble Scope

The V5 rumble bridge is more than a single fixed preset: it consumes host rumble updates and maintains a Pro2 BLE rumble stream with runtime tuning. It is still not a full HD Rumble 2 audio/voice implementation. Voice, headphone audio, microphone audio, and audio-over-rumble reproduction are not implemented.

## Known Limits

- `1000 Hz` USB report output does not mean the physical controller produces 1000 new BLE samples per second; BLE input remains around the `133 Hz` class on tested hardware.
- Gyro feel depends on Steam input settings, game mouse handling, USB cable quality, BLE environment, board revision, and controller firmware.
- macOS Generic USB HID mode, Android OTG Generic HID mode, dual-controller mode, and full HD Rumble 2 audio/voice are not part of this stable release.

## Flashing

All-in-one EXE:

1. Open `Y700Switch2Manager-aio-v5.0.0.exe`.
2. Select the CH343P COM port.
3. Flash the bundled firmware.
4. Replug native USB if Windows / Steam does not refresh enumeration.

Zip package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

## Checksums

```text
0B19786FF4342B6093F8EF13C4836B4976BED43FA628933AC48BFF969D53022C  Y700Switch2Manager-aio-v5.0.0.exe
E6571DBBDFDD988A0FD02EBF53A8EB9FB53674F7AB335B6C534A95A8A1D8A824  esp32s3-pro2-bridge-v5.0.0-20260601.zip
7652EAB05BADCF627F6CB5B2FD7A635BF9FF15F780C025960D3A22B5965BA125  firmware/esp32s3_switch2_bridge/build/esp32s3_switch2_bridge.bin
```
