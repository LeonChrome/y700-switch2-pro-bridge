# V5.2 Night Probe Summary

Date: 2026-06-06 10:10:44 +08:00

Log: .\logs\v5_2\night_probe_20260606_100934.log
idf_path: C:\Espressif\v5.3.3\esp-idf
recommended_idf_path_example: C:\Espressif\v5.3.3\esp-idf

## Step Results

- firmware_build: passed (exit=0) ESP32-S3 firmware build completed
- check_viiper_env: passed (exit=0)
- viiper_ns2pro_probe: passed (exit=0)
- viiper_ns2pro_hid_rumble_probe: passed (exit=0)
- viiper_to_real_pro2_phase3: blocked (exit=0)
- sdl_ns2pro_rumble_test: blocked (exit=0) SDL ordinary rumble/raw effect unsupported for current descriptor
- check_dualsense_env: blocked (exit=0)
- dualsense_hid_output_probe: blocked (exit=0)
- dualsense_haptic_audio_probe: blocked (exit=0)

## Key Signals

- firmware_build: passed
- usbip-win2 installed: true
- VIIPER ns2pro attach: true
- synthetic input: true
- HID 0x02 nonzero 16+16: true
- Pro2 dry-run payload: true
- real Pro2 send: false
- SDL3 runtime: true
- SDL gamepad recognition: false
- SDL rumble/effect nonzero route: false
- DualSense HID detected: false
- DualSense audio endpoint detected: false

## Current Blockers

- Steam Controller Test still requires manual UI action while the VIIPER monitor is online.
- Real Pro2 HD send requires flashed raw02 firmware, ESP32-S3 control port, and a real Pro2 BLE connection.
- SDL 3.4.10 currently treats VIIPER `VID_057E&PID_2069&MI_00` as a low-level HID joystick, not a rumble-capable Switch gamepad.
- DualSense route is blocked on this machine by missing real DualSense HID/audio endpoint.

## Manual Commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1 -MonitorOnly -Seconds 300
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -SendToRealPro2 -Port COM12
```
