# V5.2 ns2pro VIIPER Integration Plan

Date: 2026-06-06

## Scope

This remains an experimental V5.2 route. It must not become the default output
path, must not alter the V5.1 Manager GUI, and must not change the stable bridge
behavior unless the user explicitly runs the Phase 3 raw02 send command.

Proposed opt-in mode:

```text
output_mode = pro2 | ps4 | ns2pro_viiper
default = pro2
```

Mode meanings:

```text
pro2          = default ESP32-S3 Switch 2 Pro bridge path
ps4           = V5.1 DS4/raw compatibility path
ns2pro_viiper = V5.2 experimental VIIPER virtual Switch 2 Pro HD rumble capture + raw02 forwarding path
```

`ns2pro_viiper` must be shown as `Experimental` anywhere it appears. It is not
the default and must not silently replace the stable Pro2 path.

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

The raw02 chain is implemented and has now passed host/firmware real-send
validation on a BLE-connected Pro2. The user has confirmed physical vibration,
buttons, and gyro on the real controller, with no BLE abnormal disconnect.

```text
blocked_by_real_pro2=false
real_send_low=true
real_send_medium=true
real_send_captured_viiper=true
physical_vibration=true
button_input=true
gyro_input=true
rumble_writes=49
rumble_errors=0
ble_disconnect=false
next=keep ns2pro_viiper as opt-in experimental mode; do not block V5.2 on Steam/SDL natural HD rumble
```

## Experimental Output Mode Boundary

If physical vibration is confirmed, the next design target is an opt-in mode:

```text
output_mode=ns2pro_viiper
```

Rules before implementation:

- Do not enable it by default.
- Label it `Experimental` anywhere it is exposed.
- Keep V5.1 Pro2/PS4 behavior unchanged.
- Start only when usbip-win2 and VIIPER are available.
- Start only when the real Pro2 BLE state is connected.
- Refuse to forward if the target serial port is missing or busy.
- Keep `-MaxPackets 1` style safety for probes; runtime forwarding needs a
  separate rate limiter and stop path.
- Keep the stable raw Pro2 bridge usable without VIIPER.

Required dependencies:

```text
usbip-win2=true
VIIPER=true
ESP32 bridge firmware raw02=true
real Pro2 BLE connected=true
serial control port available=true
```

Error cases the mode should surface clearly:

```text
missing usbip-win2
missing VIIPER
VIIPER attach failed
Pro2 not connected
raw02 unsupported firmware
serial port missing or busy
no game HD rumble source
Steam/SDL ordinary rumble does not map to ns2pro HD 0x02
```

V5.2 conclusion:

```text
ns2pro_viiper experimental mode ready for documented opt-in
native Steam game HD rumble support remains game/input-stack dependent
do not claim all games support HD rumble
do not claim PS5/DualSense haptic support
```

Open interface questions:

- Whether the runtime forwarder should live in the Manager or remain a separate
  experimental script first.
- Whether VIIPER attach should be started by the app or require an already
  running monitor.
- How to surface `rumble_errors`, BLE disconnects, and usbip-win2 driver state
  without confusing V5.1 users.

## Frozen Route Boundary

As of V5.5 planning, this V5.2 route is frozen and preserved:

```text
output_identity=pro2_ns2_viiper
status=stable_long_term_experimental_route
default_changed=false
```

The verified buttons, gyro, VIIPER 16+16 capture, raw02 forwarding, and real
Pro2 vibration logic must not be rewritten for the DualSense experiment.

V5.5 adds a separate identity:

```text
output_identity=dualsense_esp32s3_experimental
```

It uses a PC-facing wired DualSense contract and the existing Pro2 BLE/raw02
capability as a backend. It does not replace the Pure Pro2 / VIIPER route, does
not make VIIPER the default, and does not enter the V5.1 GUI during probing.
