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

## Phase 1/2 HID Contract

ESP32-S3 experiment:

```text
firmware/esp32s3_dualsense_identity_experiment/main/
```

| DS5Dongle contract | Phase 1/2 implementation |
| --- | --- |
| VID `0x054c` | Implemented |
| PID `0x0ce6` | Implemented |
| DualSense product/manufacturer strings | Implemented |
| HID input report `0x01`, 63 data bytes | Implemented |
| HID output report `0x02`, 47 data bytes | Implemented and logged |
| 6 initial 8-bit axes | Implemented |
| Hat, 15 buttons, packed vendor bits | Implemented |
| Remaining 52-byte vendor input area | Implemented |
| Feature `0x05` | Declared, zero placeholder |
| Feature `0x08` | Declared, zero placeholder |
| Feature `0x09` | Declared, zero placeholder |
| Feature `0x20` | Declared, zero placeholder |
| Other feature reports | Not implemented |

The experiment report descriptor is intentionally smaller than the
DS5Dongle/real DualSense descriptor. It preserves the primary input and output
report shapes and only a small feature subset for enumeration.

## Endpoint Mapping

DS5Dongle full composite device:

| Endpoint | Role |
| --- | --- |
| `0x01` OUT isochronous | Four-channel audio/haptics |
| `0x82` IN isochronous | Two-channel audio input |
| `0x84` IN interrupt | HID input |
| `0x03` OUT interrupt | HID output |

V5.5 experiment fixed HID endpoints:

| Endpoint | Role |
| --- | --- |
| `0x81` IN interrupt | HID input |
| `0x01` OUT interrupt | HID output |
| `0x02` OUT isochronous | Audio render stream in audio profiles only |

HID report ID `0x02` and USB endpoint address `0x02` are different namespaces.
The HID report ID is carried on HID OUT endpoint `0x01`; the audio endpoint is
an isochronous OUT endpoint.

## Current V5.5 Profiles

| Profile | Descriptor shape | Serial string | Audio class | Audio channels |
| --- | --- | --- | --- | --- |
| `hid_only` | HID-only Phase 2.1 shape | `V55HIDONLY` | Disabled | 0 |
| `hid_audio_uac1_2ch` | UAC1 Audio + HID composite | `V55UAC1_2CH` | UAC1 custom driver | 2 |
| `hid_audio_uac2_2ch` | UAC2 Audio + HID composite | `V55UAC2_2CH` | TinyUSB UAC2 | 2 |
| `hid_audio_uac2_4ch` | UAC2 Audio + HID composite | `V55UAC2_4CH` | TinyUSB UAC2 | 4 |
| `hid_audio_uac2` | Legacy alias for `hid_audio_uac2_4ch` | `V55UAC2_4CH` | TinyUSB UAC2 | 4 |

`hid_audio_uac1_fallback` remains accepted as a warning alias for
`hid_audio_uac1_2ch`.

## Profile Interface Layout

```text
hid_only:
  bNumInterfaces=1
  interface 0 = HID gamepad

hid_audio_uac1_2ch:
  bNumInterfaces=3
  interface 0 = Audio Control, UAC1
  interface 1 = Audio Streaming OUT, 2ch, 48 kHz, signed 16-bit PCM
  interface 2 = HID gamepad

hid_audio_uac2_2ch:
  bNumInterfaces=3
  interface 0 = Audio Control, UAC2
  interface 1 = Audio Streaming OUT, 2ch, 48 kHz, signed 16-bit PCM
  interface 2 = HID gamepad

hid_audio_uac2_4ch:
  bNumInterfaces=3
  interface 0 = Audio Control, UAC2
  interface 1 = Audio Streaming OUT, 4ch, 48 kHz, signed 16-bit PCM
  interface 2 = HID gamepad
```

## Hardware Verification History

Phase 1 hardware verification passed on 2026-06-06:

```text
VID=054C
PID=0CE6
input report=0x01 + 63 bytes
observed rate=250 Hz
USB disconnect=false
```

The first old `V55PHASE3` UAC2 4ch hardware check failed:

```text
phase3_usb_found=true
phase3_status=Error
phase3_problem_code=10
hid_child_active=false
audio_endpoint_active=false
conclusion=composite enumeration failure
```

That failure is why Phase 3 now tests `hid_only`, then UAC1 2ch, then UAC2 2ch,
then UAC2 4ch.

## Audio Processing

UAC1 2ch currently verifies composite enumeration and logs OUT packet activity.

UAC2 2ch and UAC2 4ch initialize the dry-run haptic audio pipeline:

```text
uac2_2ch: channels 0/1 -> haptic left/right statistics
uac2_4ch: channels 2/3 -> haptic left/right statistics
```

`haptic_audio_to_raw02` emits dry-run `Left[16] + Right[16]` payloads only.
Live raw02 forwarding remains disabled in Phase 3.

## Deferred

Not implemented in Phase 3:

- speaker/headset playback,
- microphone/audio IN endpoint,
- complete feature report table,
- calibration and pairing data,
- live Pro2 raw02 output from haptic audio.
