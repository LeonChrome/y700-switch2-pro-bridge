# V5.5 DS5 Descriptor Mapping

Date: 2026-06-06

## Reference

DS5Dongle checkout:

```text
research/upstream/DS5Dongle
commit=8760ee3f4fa9335e3c5e1a0d0aead92b55f23abb
license=MIT
```

Relevant upstream files:

| File | Role |
| --- | --- |
| `src/usb_descriptors.cpp` | Device/configuration/HID report descriptors |
| `src/tusb_config.h` | HID and Audio Class sizing |
| `src/main.cpp` | HID input, GET_REPORT, SET_REPORT |
| `src/usb.cpp` | Audio Class control requests |
| `src/audio.cpp` | Four-channel audio and haptic channel processing |

No DS5Dongle source checkout is committed to this repository.

## Phase 1 Mapping

ESP32-S3 experiment:

```text
firmware/esp32s3_dualsense_identity_experiment/main/
```

| DS5Dongle contract | Phase 1 implementation |
| --- | --- |
| VID `0x054c` | Implemented |
| PID `0x0ce6` | Implemented |
| DualSense product/manufacturer strings | Implemented |
| HID input report `0x01`, 63 data bytes | Implemented |
| HID output report `0x02`, 47 data bytes | Implemented and logged |
| 6 initial 8-bit axes | Implemented |
| Hat, 15 buttons, packed vendor bits | Implemented |
| Remaining 52-byte vendor input area | Implemented, neutral zeros |
| Feature `0x05` | Declared, zero placeholder |
| Feature `0x08` | Declared, zero placeholder |
| Feature `0x09` | Declared, zero placeholder |
| Feature `0x20` | Declared, zero placeholder |
| Other feature reports | Not implemented |

The experiment report descriptor is intentionally smaller than the
DS5Dongle/real DualSense 321-byte descriptor. It preserves the primary input
and output report shapes and only a small feature subset for enumeration.

## Endpoint Mapping

DS5Dongle full composite device:

| Endpoint | Role |
| --- | --- |
| `0x01` OUT isochronous | Four-channel audio/haptics |
| `0x82` IN isochronous | Two-channel audio input |
| `0x84` IN interrupt | HID input |
| `0x03` OUT interrupt | HID output |

Phase 1/2 HID-only experiment:

| Endpoint | Role |
| --- | --- |
| `0x81` IN interrupt | HID input |
| `0x01` OUT interrupt | HID output |

Phase 3 audio stub experiment:

| Endpoint | Role |
| --- | --- |
| `0x02` OUT isochronous | Four-channel audio/haptics render stream |
| `0x81` IN interrupt | HID input |
| `0x01` OUT interrupt | HID output |

Phase 3 adds Audio Control interface `0`, Audio Streaming OUT interface `1`,
and moves the HID interface to `2`. HID report IDs and HID endpoint addresses
remain unchanged to preserve the Phase 2 input and ordinary-rumble paths.

## Neutral Input

Phase 1 sends report ID `0x01` every 4 ms:

```text
left/right sticks=center 0x80
L2/R2=0x00
hat=null 0x08
buttons=released
motion/touch/vendor data=0x00
```

This validates enumeration and periodic input only. It is not calibrated
DualSense motion data.

## Hardware Verification

Phase 1 hardware verification passed on 2026-06-06:

```text
VID=054C
PID=0CE6
input report=0x01 + 63 bytes
observed rate=250 Hz
USB disconnect=false
```

Windows displayed the generic `HID-compliant game controller` label, while
the underlying VID/PID and report contract matched the experiment.

## Phase 2 Input Mapping

Phase 2 keeps the same descriptor and endpoints. It reuses the existing Pro2
BLE FD2 parser, maps buttons and 12-bit sticks into report `0x01`, and copies
the newest parsed accelerometer/gyroscope sample into the DualSense motion
fields. Neutral reports continue to carry increasing sequence and timestamp
values when the Pro2 is disconnected or its input is stale.

Phase 2.1 keeps the descriptor unchanged and consumes the existing output
report `0x02`. Ordinary light/heavy motor intensity is converted into bounded
Pro2 BLE vibration. DualSense haptic audio and raw02 pass-through remain
deferred.

## Phase 3 Audio Stub

Phase 3 changes the configuration descriptor from HID-only to a minimal
composite Audio + HID shape:

```text
interface 0 = Audio Control, UAC2
interface 1 = Audio Streaming OUT, 4ch, 48 kHz, signed 16-bit PCM
interface 2 = HID gamepad
```

The firmware reads host OUT audio data with TinyUSB and extracts channels 2/3
as haptic left/right statistics:

```text
rms_l/rms_r
peak_l/peak_r
activity
transient
frame_count
overrun_count
```

`haptic_audio_to_raw02` currently emits dry-run `Left[16] + Right[16]`
payloads only. Live raw02 forwarding remains disabled.

## Deferred

Not implemented in Phase 3:

- speaker/headset processing,
- complete feature report table,
- calibration and pairing data,
- microphone/touchpad behavior,
- live Pro2 raw02 output from haptic audio.

## Future Mapping

Future phases may add:

```text
Audio Streaming IN compatibility shape, if required
HID IN endpoint 0x84
HID OUT endpoint 0x03
Audio OUT endpoint 0x01
Audio IN endpoint 0x82
safe haptic_raw02_forwarding=on switch
```

Channels:

```text
0/1=speaker/headset compatibility channels
2/3=left/right haptic source
```

Speaker audio may be discarded in the first audio experiment. Channels 2/3
will feed the Phase 5 feature extractor and raw02 dry-run translator.
