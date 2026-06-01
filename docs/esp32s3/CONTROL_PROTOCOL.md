# ESP32-S3 Serial Control Protocol

Status: Steam Nintendo Switch Pro/Pro2 layout, BLE input forwarding, raw-like gyro passthrough, and Pro2 rumble are verified for V5.0.0 on 2026-06-01.

Transports:

- CH343P USB serial: one command per line, one JSON reply per line.
- Native USB HID feature report `0x7f`: manager control over the same HID interface used for input.
- Native USB WinUSB bulk private frame: fallback only; Steam may hold this interface exclusively.

## Rules

- One command per line.
- One JSON reply per line.
- Logs are prefixed with `[LOG]`.
- JSON replies start with `{`.
- Unknown or unimplemented commands return `ok:false`.

Native USB HID feature control uses report ID `0x7f`. The manager sends `Y7HID1` + ASCII command in `SET_FEATURE`, then reads chunked `GET_FEATURE` replies starting with `Y7HRS1`.

Native USB bulk control uses `Y7CTL1` + ASCII command on bulk OUT and replies with `Y7RSP1` + length + JSON on bulk IN. This is useful when Steam is not holding the WinUSB interface.

## Commands

```text
status
mode generic
mode nintendo
start
stop
rate 250
reboot
loglevel debug
loglevel info
ble scan
ble connect last
ble connect #3
ble connect aa:bb:cc:dd:ee:ff/1
ble reconnect
ble auto on
ble auto off
ble target aa:bb:cc:dd:ee:ff/0
ble disconnect
rumble config
rumble tune 100 180 20 3
rumble hdtest
rumble hold 3000
rumble stop
hid test_a
hid neutral
hid auto_a
version
```

## Example Replies

```json
{"ok":true,"cmd":"status","mode":"nintendo","usb":"mounted","ble":"connected","hid":"running","test_mode":"neutral","rate_hz":250,"report_actual_hz":249,"report_actual_mhz":249000,"report_sent":372,"report_failed":0,"report_last_gap_us":4000,"report_max_gap_us":5000,"live":"active","live_updates":512,"live_age_ms":2,"version":"5.0.0"}
{"ok":true,"cmd":"mode","mode":"nintendo","experimental":true,"note":"replug native USB may be required"}
{"ok":true,"cmd":"rate","rate_hz":250,"saved":true}
{"ok":true,"cmd":"ble connect","ble":"connecting"}
{"ok":true,"cmd":"ble reconnect","ble":"connecting"}
{"ok":true,"cmd":"rumble tune","scale_percent":100,"hold_ms":180,"tick_ms":20,"stop_packets":3}
{"ok":true,"cmd":"rumble hold","rumble":"active","mode":"hd_stream_hold","hold_ms":3000}
```

## Notes

`mode nintendo` changes firmware state, but USB descriptors are normally read during enumeration. A native USB replug may be required after switching identity mode.

`rate <20..1000>` persists the USB HID report loop rate in NVS. The V5 default is 250 Hz because it is the current gyro-stability recommendation; the manager exposes 60, 125, 250, 500, and 1000 Hz presets. Treat 1000 Hz as an optional experimental USB output cadence on ESP32-S3.

`status` reports both configured and measured report-rate fields:

- `rate_hz`: configured target loop rate.
- `report_actual_hz`: firmware-measured HID input reports per second, rounded to whole Hz.
- `report_actual_mhz`: same value in millihertz for UI display, for example `122500` means `122.5 Hz`.
- `report_sent` / `report_failed`: successful and failed HID input report sends since boot.
- `report_last_gap_us` / `report_max_gap_us`: last and recent max gap between actual HID input sends.

V5 gyro data uses the Switch 2 Pro FD2 BLE full-report motion block at bytes `48..59` and maps it into USB report `0x05` bytes `49..60`. The default path is raw-like: no smoothing, no scaling, no deadband, and no auto calibration.

Host-side verification can be done without the CH343P control port:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\leon\Documents\Codex\y700-hid-gamepad\tools\Measure-SwitchHidRate.ps1 -Seconds 5
```

`loglevel info` keeps NimBLE packet-level logs at WARN so rumble streaming does not flood serial output. `loglevel debug` re-enables verbose packet tracing.

`ble scan` starts a 15-second NimBLE active scan and emits `[LOG] BLE scan ...` lines with MAC/type, RSSI, advertised name, appearance, UUIDs, and manufacturer-data preview. `ble connect` accepts `last`, a scan index such as `#3`, an address copied from scan logs, or a name substring. The firmware starts connection, service discovery, descriptor discovery, CCCD subscription, the Pro2 init command chain, and live notify parsing for the known Y700 UUIDs.

`ble reconnect` uses the saved BLE target when available, otherwise it scans and connects the first candidate that looks like a Nintendo/Pro2 controller. Successful connects persist `ble_target` in NVS. `ble auto on/off` controls boot-time reconnect behavior; the default is on.

`rumble config` returns the current runtime haptic tuning. `rumble tune <scale_percent> <hold_ms> <tick_ms> <stop_packets>` changes tuning without reflashing. The stable default is `rumble tune 100 180 20 3`. V5 rumble tracks Steam/SDL HID OUT rumble updates and keeps a Pro2 BLE rumble stream alive; it is not just one fixed preset, but it is also not a full HD Rumble 2 audio/voice implementation.

- `scale_percent`: `10..250`, applies to decoded Switch HID OUT amplitude before clamping to the Pro2 BLE 10-bit amplitude range.
- `hold_ms`: `50..1000`, keeps the 33-byte HD stream alive after the latest Steam/SDL HID OUT rumble update.
- `tick_ms`: `5..50`, controls the HD stream write cadence. The verified default is 20 ms.
- `stop_packets`: `1..8`, sends neutral HD packets after rumble stops. The verified default is 3.

`rumble hdtest` starts a direct Pro2 HD stream self-test using the same 33-byte BLE write shape as Steam rumble. `rumble hold <ms 100..10000>` keeps that stream active for a chosen duration. `rumble stop` queues neutral stop packets.

First-board observed behavior:

- `status` returns `test_mode`.
- `status` returns `rate_hz`, live BLE state, live update count, live age, rumble counters, and rumble tuning fields.
- `hid neutral` forces a neutral Generic HID report.
- `hid test_a` holds Button 1/A.
- `hid auto_a` restores the A-button toggle fallback when no live BLE notify stream is active.
- `stop` stops the bridge loop and repeatedly sends neutral reports so buttons are released.
- `start` resumes HID output using live BLE state when available, otherwise the current `test_mode`.

CH343P RX reaches the firmware command parser on the first board. Windows Manager builds and publishes a self-contained exe, and the manager-facing commands above are verified against COM12.
