# V5.2 ns2pro VIIPER Integration Plan

Date: 2026-06-06

## Scope

This remains an experimental V5.2 route. It must not become the default output
path, must not alter the V5.1 Manager GUI, and must not change the stable bridge
behavior unless the user explicitly runs the Phase 3 raw02 send command.

Proposed opt-in mode:

```text
output_mode=ns2pro_viiper
```

Experimental flow:

```text
VIIPER virtual ns2pro USB
-> VIIPER output callback
-> LeftRumble[16] + RightRumble[16]
-> raw02 payload builder
-> rumble raw02 <hex>
-> ESP32-S3 control protocol
-> real Switch 2 Pro BLE rumble write
```

## Implemented Pieces

Firmware/control:

```text
rumble raw02 <hex>
```

Host helper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset captured -DryRun
```

VIIPER-to-raw02 probe:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -DryRun -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

## raw02 Hex Shapes

64 hex chars:

```text
LeftRumble[16] + RightRumble[16]
```

128 hex chars:

```text
0x02 + LeftRumble[16] + RightRumble[16] + padding to 64 bytes
```

The 64-char form is safer for manual use. The 128-char form is useful when the
host already has a full raw HID OUT payload from VIIPER or another capture.

## Safety Rules

- Default is dry-run.
- Real send requires `-Send` or `-SendToRealPro2`.
- Real send must include a target serial port.
- The helper sends one command and then `rumble stop` after a short delay.
- No looped high-intensity test is included.
- Start real hardware validation with `-Preset low`.

## Real Hardware Ladder

1. Flash the firmware containing `rumble raw02`.
2. Connect the ESP32-S3 CH343P control port.
3. Connect the real Switch 2 Pro over BLE.
4. Confirm `status` shows BLE connected and rumble state available.
5. Run the low preset:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset low -Send -Port COM12
```

6. If low is safe, run captured VIIPER dry-run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -DryRun -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

7. Then run captured VIIPER real send:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -SendToRealPro2 -Port COM12 -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

Stop command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rumble stop"
```

## Current Decision

The raw02 chain is implemented and ready for real hardware validation.

```text
blocked_by_real_pro2=true
real_pro2_verified=false
next=flash firmware, connect Pro2, run low preset real send
```
