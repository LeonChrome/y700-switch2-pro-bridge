# V5.3 DualSense To Pro2 raw02 Translation Plan

Date: 2026-06-06

## 1. Why Translate

DualSense is more likely to receive advanced haptic source data from PC games
than Switch 2 Pro in the current Windows / Steam ecosystem. The V5.2
`ns2pro_viiper` route proved that Pro2 raw02 forwarding can produce real
physical vibration, but Steam/SDL ordinary rumble is not the same as ns2pro HD
`0x02`.

Future route:

```text
PC game native DualSense haptic
-> DualSense HID/audio capture
-> haptic feature extraction
-> Pro2 raw02 / HD rumble approximation
```

This is an approximation route, not a claim of native DualSense haptic support
or native Switch 2 Pro HD rumble in every game.

## 2. Input Sources

### A. HID Ordinary Output

Ordinary HID output can carry classic small/large rumble, lightbar, mute LED,
and other state changes. It is useful for smoke tests and coarse feedback, but
it is not enough to prove advanced haptic audio.

Use it for:

- ordinary rumble intensity,
- game feedback timing,
- simple event boundaries,
- checking whether Steam Input wraps feedback into a generic path.

### B. Adaptive Trigger HID Output

Adaptive trigger output configures L2/R2 resistance or trigger effects. It can
show native DualSense-aware behavior even when haptic audio is absent.

Use it for:

- trigger resistance events,
- weapon/vehicle trigger state,
- native DualSense feature detection,
- correlating trigger output with haptic audio windows.

### C. Haptic Audio Endpoint

The audio endpoint is the most valuable V5.3 source. It may carry high-rate or
audio-like haptic information that can be analyzed by window.

Use it for:

- RMS / peak energy,
- stereo balance,
- low-frequency energy,
- transient detection,
- texture/continuous signal classification.

## 3. Output Target

The proven Pro2 target is raw02:

```text
report_id = 0x02
Left[16]
Right[16]
padding to 64-byte HID OUT shape when needed
```

Firmware/control path:

```text
rumble raw02 <hex>
-> ESP32-S3 control protocol
-> Pro2 BLE rumble write
```

The host helper already supports:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset low -Send -Port COM12
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Hex "<64-or-128-hex>" -Send -Port COM12
```

## 4. Translation Strategy

### Level 1: Ordinary Rumble Mapping

Map DualSense ordinary rumble into known safe Pro2 templates:

```text
low strength  -> Pro2 low raw02 preset
medium        -> Pro2 medium raw02 preset
strong short  -> captured-like short pulse with rate limit
stop          -> rumble stop
```

This is the first live mapping because it is simple and safe.

### Level 2: Haptic Audio Energy Mapping

Analyze haptic audio in short windows:

```text
rms_left
rms_right
peak_left
peak_right
low_frequency_energy
transient_score
stereo_balance
decay
```

Map to raw02 parameters:

```text
left intensity
right intensity
coarse/fine pattern
short pulse duration
decay curve
minimum interval
```

Initial window sizes should prefer stability over detail:

```text
window_ms=10..25
min_interval_ms=20..50
max_packets_per_second=20..50
```

### Level 3: Event Classification

Classify haptic audio into coarse events:

```text
impact
engine
texture
weapon
UI click
continuous vibration
```

Possible template mapping:

```text
impact                -> short strong pulse with fast decay
engine                -> repeated medium pulses with slow modulation
texture               -> low-intensity fast variation
weapon                -> sharp left/right pulse, short decay
UI click              -> very short low pulse
continuous vibration  -> rate-limited medium stream
```

## 5. Limits

1. Pro2 raw02 is not DualSense haptic audio.
2. This is approximate translation, not native reproduction.
3. Latency is a key risk.
4. Windows audio capture permission, endpoint naming, and loopback behavior are
   key risks.
5. Steam Input may hide or translate native DualSense output.
6. A future route may need a real DualSense pass-through, DS5Dongle-style
   bridge, or virtual DualSense HID plus virtual audio endpoint.
7. High-intensity loops must stay disabled until safety limits are proven.

## 6. Future Experiments

V5.3 Phase 1:

```text
real DualSense capture only
collect HID output and haptic audio activity
no Pro2 forwarding yet
```

V5.3 Phase 2:

```text
haptic audio feature extraction
RMS / peak / transient / balance analysis
offline logs only
```

V5.3 Phase 3:

```text
offline haptic audio -> raw02 template replay
use saved logs
send only bounded low/medium templates
```

V5.3 Phase 4:

```text
live haptic audio -> Pro2 raw02 forwarding
rate limited
explicit send mode
emergency stop path
```

V5.3 Phase 5:

```text
game testing and tuning
compare Steam Input on/off
record latency and subjective feel
```

## Current Gate

```text
real_dualsense=false
audio_endpoint=false
translation_implementation=false
safe_next=plug_real_dualsense_usb_and_run_v5_3_capture
```
