# V5.5 ESP32-S3 DualSense Identity Experiment Plan

Date: 2026-06-06

## Rules

- Preserve V5.2 `pro2_ns2_viiper`.
- Keep `dualsense_esp32s3_experimental` opt-in.
- Do not add V5.5 to the V5.1 GUI during probing.
- Default haptic translation to dry-run.
- Do not commit third-party DS5Dongle source.

## Phase 0: DS5Dongle Reference Study

Actions:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\fetch_v5_5_ds5dongle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\analyze_v5_5_ds5dongle.ps1
```

Exit:

- commit, branch, and license recorded,
- descriptor/audio/output/input/backend paths documented,
- generated symbol scan committed,
- upstream checkout remains ignored.

## Phase 1: Minimal DualSense HID Identity

Target:

- Windows recognizes `DualSense Wireless Controller` / `Wireless Controller`,
- Steam recognizes a wired DualSense,
- neutral report `0x01` remains stable,
- no USB audio,
- no raw02 forwarding.

Measurements:

- Device Manager state,
- VID/PID/interfaces/endpoints,
- GET_REPORT/SET_REPORT IDs and lengths,
- USB reconnect and suspend/resume,
- report cadence and errors.

Exit:

```text
windows_recognition=true
steam_recognition=true
neutral_input_stable=true
feature_request_stall=false
```

## Phase 2: Pro2 Input To DualSense Input

Target:

- real Pro2 drives PC-visible DualSense input,
- buttons, D-pad, sticks, triggers, gyro, and accelerometer work,
- missing touch/mic fields stay neutral.

Work:

- define `Pro2BleInputBackend`,
- define `DualSenseInputReportBuilder`,
- map axis ranges/signs,
- implement fixed calibration feature data,
- map battery and wired connection state.

Exit:

```text
buttons=true
sticks=true
triggers=true
gyro=true
accel=true
touch_neutral=true
```

## Phase 3: DualSense Output Capture

Target:

- capture report `0x02`,
- classify ordinary rumble, adaptive trigger, lightbar, player, and mute fields,
- log only; no real-controller send.

Test sources:

- Steam controller test,
- SDL/native DualSense test,
- one native DualSense game with Steam Input off,
- repeat with Steam Input on.

Exit:

```text
output_report_capture=true
ordinary_rumble_classified=true
adaptive_trigger_classified=true
real_send=false
```

## Phase 4: USB Audio Interface

Target:

- Windows exposes a DualSense-like audio endpoint,
- four-channel 48 kHz, 16-bit OUT opens,
- samples arrive without destabilizing HID or Pro2 BLE,
- samples may be discarded in this phase.

Measurements:

- endpoint name and format,
- alternate-setting callbacks,
- packet size/jitter,
- callback overrun/drop count,
- BLE input rate,
- HID input rate,
- CPU/internal RAM/PSRAM usage.

Exit:

```text
audio_endpoint=true
four_channel_out=true
sample_activity=true
hid_regression=false
ble_regression=false
```

## Phase 5: Haptic Audio To raw02 Preview

Target:

```text
channels 2/3
-> feature windows
-> event classification
-> raw02 payload
-> dry-run only
```

Initial policy:

- 10 to 25 ms windows,
- RMS, peak, transient, low-frequency energy, stereo balance,
- 20 to 50 ms minimum output interval,
- silence timeout produces stop,
- no high-intensity sustained pattern.

Synthetic validation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_dualsense_to_pro2_pipeline.ps1 -Synthetic -Event impact -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_4_hybrid_haptic_probe.ps1
```

Exit:

```text
nonzero_haptic_source=true
raw02_preview=true
rate_bounded=true
silence_stop=true
real_send=false
```

## Phase 6: Live Forwarding

Target:

```text
DualSense haptic audio/output
-> translator
-> Pro2Raw02Backend
-> real Pro2 BLE
```

Required guards:

- explicit enable,
- low initial gain,
- maximum packets per second,
- minimum interval,
- stale frame timeout,
- queue overflow counter,
- BLE write error counter,
- USB suspend stop,
- BLE disconnect stop,
- serial `rumble stop`.

Exit:

```text
physical_vibration=true
latency_measured=true
rumble_errors=0_or_explained
ble_disconnect=false
emergency_stop=true
```

## Hardware

Must have now:

- existing ESP32-S3-N16R8 board,
- real Switch 2 Pro Controller,
- native USB OTG cable,
- CH343P control connection.

Required later:

- real DualSense for reference and feature-report comparison,
- one or more native DualSense PC games.

Optional:

- USB protocol analyzer,
- second ESP32-S3/Pico2W for dual-board fallback,
- oscilloscope or logic analyzer for latency markers.
