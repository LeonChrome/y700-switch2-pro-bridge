# V5.2 Real Pro2 HD Rumble Probe Results

Date: 2026-06-06

## Summary

```text
VIIPER 16+16 source: available
raw02 firmware/control command: implemented
host helper: implemented
default mode: dry_run
real Pro2 send attempted: false
real Pro2 verified: false
blocked_by_real_pro2: true
V5.1 GUI changed: false
```

Phase 3 now has the minimal raw02 forwarding path:

```text
VIIPER LeftRumble[16] + RightRumble[16]
-> raw02 payload builder
-> rumble raw02 <hex>
-> ESP32-S3 control protocol
-> Pro2 BLE rumble characteristic
```

The code path is implemented, but physical Pro2 vibration has not been verified
in this run because no real Pro2 send was attempted.

## raw02 Command

Firmware/control command:

```text
rumble raw02 <hex>
```

`<hex>` supports two shapes:

```text
64 hex chars  = LeftRumble[16] + RightRumble[16]
128 hex chars = full 64-byte HID OUT payload, starting with report_id 0x02
```

For 64 hex chars, firmware automatically builds:

```text
0x02 + LeftRumble[16] + RightRumble[16] + zero padding to 64 bytes
```

For 128 hex chars, firmware validates that byte 0 is `0x02` and sends the full
payload shape.

Safety checks:

- odd hex length is rejected
- non-hex characters are rejected
- unsupported length is rejected
- full payload with report ID other than `0x02` is rejected
- BLE/rumble send failure returns an error

Firmware logs:

```text
[RUMBLE_RAW02] mode=left_right_16 or full_payload
[RUMBLE_RAW02] left=...
[RUMBLE_RAW02] right=...
[RUMBLE_RAW02] payload=...
[RUMBLE_RAW02] sent=true/false error=...
```

## Host Helper

Dry-run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset captured -DryRun
```

Real send:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset low -Send -Port COM12
```

Manual hex:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Hex "<64-or-128-hex>" -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Hex "<64-or-128-hex>" -Send -Port COM12
```

The helper defaults to dry-run unless `-Send` is passed. In send mode it prints
the target port and sends `rumble stop` after a short delay.

Stop command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rumble stop"
```

## Safe Payload Samples

Low pulse, 64 hex chars:

```text
5087012011000000000000000000000050870120110000000000000000000000
```

Medium pulse, 64 hex chars:

```text
5087112440330000000000000000000050871124403300000000000000000000
```

Captured VIIPER sample, 64 hex chars:

```text
5087152751710000000000000000000050871527517100000000000000000000
```

Captured VIIPER sample as full raw02 payload:

```text
02508715275171000000000000000000005087152751710000000000000000000000000000000000000000000000000000000000000000000000000000000000
```

## VIIPER Capture To raw02

Capture the first non-zero VIIPER output packet and dry-run the raw02 payload:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -DryRun -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

Observed:

```text
[VIIPER_HD_OUTPUT] capture_viiper=true timeout_seconds=35 max_packets=1 min_interval_ms=100
[VIIPER_HD_OUTPUT] left_rumble_hex=50871527517100000000000000000000 right_rumble_hex=50871527517100000000000000000000
[PRO2_HD_RUMBLE] payload_0x02=02508715275171000000000000000000005087152751710000000000000000000000000000000000000000000000000000000000000000000000000000000000
[PRO2_RAW02] dry_run=true
[PRO2_RAW02] sent=false
```

Real send, after flashing the new firmware and connecting the real Pro2:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -SendToRealPro2 -Port COM12 -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

## Current Verification

Completed:

- raw02 dry-run with `low`, `medium`, and `captured` presets
- Phase 3 default dry-run
- VIIPER capture to raw02 dry-run
- host helper dry-run

Not completed:

- real Pro2 physical vibration
- flashed-firmware serial command on hardware

Current blocker:

```text
blocked_by_real_pro2=true
reason=needs firmware flash plus real Pro2 connected before -Send
```
