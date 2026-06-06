# V5.4 Hybrid Haptic Engine Architecture

Date: 2026-06-06

## 1. Version Boundary

V5.4 defines policy and architecture. It does not replace the V5.2 path and
does not change the V5.1 GUI.

| Version | Role | Status |
| --- | --- | --- |
| V5.2 | Pure Pro2 / VIIPER ns2pro identity and verified raw02 forwarding | Frozen, preserved |
| V5.3 | DualSense HID/audio feature extraction to Pro2 raw02 translator | Prototype |
| V5.4 | Identity selection, game behavior matrix, haptic policy | Architecture |
| V5.5 | ESP32-S3 PC-facing wired DualSense identity | Experimental implementation plan |

The two PC-facing identities are separate:

```text
output_identity = pro2_ns2_viiper
output_identity = dualsense_esp32s3_experimental
```

`pro2_ns2_viiper` remains the long-term Pure Pro2 route. V5.5 adds a second
route; it does not rename, delete, or silently replace V5.2.

## 2. Routes

Pure Pro2:

```text
PC / VIIPER ns2pro
-> Switch 2 Pro output 0x02
-> LeftRumble[16] + RightRumble[16]
-> ESP32-S3 raw02
-> real Pro2 BLE
```

DualSense identity:

```text
PC game
-> ESP32-S3 wired DualSense HID + audio identity
-> DualSense HID output and/or haptic audio
-> feature extraction and event policy
-> Pro2 raw02
-> real Pro2 BLE
```

## 3. Policy

Identity is selected before USB enumeration and requires a USB reconnect or
reboot. Runtime haptic logic must never blend two host identities.

| Host/game behavior | Preferred identity | Translation |
| --- | --- | --- |
| Native Switch 2 Pro HD output | `pro2_ns2_viiper` | Direct 16+16 raw02 |
| Generic Steam/SDL rumble only | Existing stable Pro2 path | Existing rumble bridge |
| Native DualSense ordinary rumble | `dualsense_esp32s3_experimental` | Bounded raw02 template |
| Native DualSense haptic audio | `dualsense_esp32s3_experimental` | Window features to raw02 |
| Adaptive trigger event only | `dualsense_esp32s3_experimental` | Optional short event cue |
| Unknown/invalid output | Either | Ignore and log |

Safety policy:

- Default all V5.3/V5.5 translation probes to dry-run.
- Rate-limit live raw02 output.
- Stop on BLE disconnect, stale audio, USB suspend, or backend error.
- Clamp RMS, peak, pulse duration, and packet rate.
- Do not create a sustained high-intensity test preset.

## 4. Architecture

```text
PcFacingDualSenseDevice
  input_report_builder
  output_report_receiver
  audio_haptic_receiver

ControllerBackend
  Ds5BtBackend
  Pro2BleInputBackend
  Pro2Raw02Backend
```

Responsibilities:

- `PcFacingDualSenseDevice` owns descriptors, control transfers, HID reports,
  audio interfaces, USB timing, and host-visible state.
- `Pro2BleInputBackend` owns Pro2 discovery, reconnect, input parsing, motion,
  battery, and freshness.
- `Pro2Raw02Backend` owns safe raw02 assembly, BLE writes, rate limits, stop,
  and counters.
- `Ds5BtBackend` is an upstream-reference validation backend only. The target
  V5.5 product does not require a real DualSense.

The translator sits between the PC-facing device and controller backend:

```text
DualSenseOutputEvent or HapticAudioWindow
-> HapticFeatureExtractor
-> HapticPolicy
-> Raw02Frame
-> Pro2Raw02Backend
```

## 5. Translation Inputs

HID report `0x02`:

- ordinary rumble intensity,
- event timing,
- adaptive trigger state,
- mute/light/player state for diagnostics.

Audio OUT channels:

- channels 0/1: speaker or headset audio, optional and ignorable for MVP,
- channels 2/3: left/right haptic source,
- 48 kHz signed PCM in the DS5Dongle reference.

Initial feature window:

```text
window_ms=10..25
rms_left/right
peak_left/right
transient_score
low_frequency_energy
stereo_balance
```

## 6. Probe

Without a real DualSense, policy and raw02 generation remain testable:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_dualsense_to_pro2_pipeline.ps1 -Synthetic -Event impact -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_4_hybrid_haptic_probe.ps1
```

Expected no-hardware state:

```text
pure_pro2_path_preserved=true
synthetic_policy_probe=passed
hardware_probe=blocked
result=passed_as_blocked
```
