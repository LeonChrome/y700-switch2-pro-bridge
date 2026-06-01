# ESP32-S3 Switch 2 Bridge Firmware

This is the ESP-IDF firmware for an ESP32-S3 MCU version of the Y700 Switch 2 Pro bridge.

Observed on first hardware bring-up:

- ESP-IDF v5.3.3 build completed on the Windows host
- firmware flashed over CH343P serial
- serial `status` returned JSON
- Generic HID enumerated over native ESP32-S3 USB
- `joy.cpl` showed the generic gamepad and Button 1/A test reports
- Steam recognized the Nintendo Switch Pro/Pro2 layout through the SDL Switch 2 HIDAPI path
- ESP32-S3 BLE connected directly to the Pro2 controller once the Y700 Bluetooth bridge was disabled
- live BLE input forwarding worked in Steam, including Pro2-specific buttons
- Steam and BzzzController rumble tests produced physical Pro2 vibration
- V5 raw-like gyro passthrough worked through USB report `0x05` in Steam / Aimlabs

Current build behavior:

- Default mode is `NINTENDO_EXPERIMENT_MODE` using VID/PID `057e:2069` and the Y700-stable product string `Nintendo Switch Pro Controller`.
- `mode generic` remains available as a fallback and requires reboot/replug for USB re-enumeration.
- `rate <20..1000>` persists the HID report-loop rate in NVS; the V5 default is 250 Hz.
- `status` exposes both the configured report rate and firmware-measured actual HID send rate.
- Gyro uses the Switch 2 Pro FD2 BLE full-report motion block at bytes `48..59` and maps it into USB report `0x05` bytes `49..60` with smoothing, scaling, deadband, and auto calibration disabled by default.
- The Windows manager can control the bridge over CH343P serial, native USB HID feature report `0x7f`, or native USB WinUSB bulk fallback.
- `ble scan` starts a 15-second NimBLE active scan and logs nearby BLE advertisements over CH343P serial.
- `ble connect last|#n|addr|name` starts NimBLE connection, GATT discovery, CCCD subscription, and notify parsing for the known Y700 UUIDs.
- `ble reconnect` uses a saved BLE target when available; otherwise it scans and connects the first Nintendo/Pro2-looking candidate. Boot-time BLE autoconnect is enabled by default.
- The HID loop sends live BLE state when notify packets are active; otherwise it falls back to the selected test mode.
- `rumble config` reads runtime haptic tuning; `rumble tune 100 180 20 3` is the verified default.
- `rumble hdtest` and `rumble hold <ms>` send the verified 33-byte rumble stream to the Pro2 BLE rumble characteristic. Runtime rumble follows Steam/SDL HID OUT updates; voice, headphone audio, microphone audio, and full HD Rumble 2 audio are not implemented.

Expected board:

- ESP32-S3-N16R8
- 16MB flash
- 8MB PSRAM
- Native ESP32-S3 USB & OTG Type-C for TinyUSB HID Device
- CH343P Type-C for flashing, logs, and serial control

Host-side report-rate measurement:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\leon\Documents\Codex\y700-hid-gamepad\tools\Measure-SwitchHidRate.ps1 -Seconds 5
```

The host-side measurement reads the native Nintendo HID input interface and counts reports delivered to Windows. It is independent from the CH343P control port and is useful for confirming that `rate_hz` changes are visible to the PC.
