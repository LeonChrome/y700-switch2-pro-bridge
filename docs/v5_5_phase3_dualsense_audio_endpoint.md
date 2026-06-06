# V5.5 Phase 3 DualSense Audio Endpoint

Date: 2026-06-06

Phase 3 adds isolated USB Audio fallback profiles to the standalone
`dualsense_identity_experiment` firmware. The goal is to recover a Windows
HID + audio composite enumeration path before any haptic feature, raw02 live
forwarding, or V5.1 GUI work continues.

This phase does not change the V5.2 Pure Pro2 / VIIPER route, does not change
the V5.0/V5.1 GUI, and does not enable live Pro2 raw02 forwarding by default.

## Profiles

```text
hid_only:
  HID-only recovery profile
  serial=V55HIDONLY
  audio=false

hid_audio_uac1_2ch:
  HID + UAC1 render fallback
  serial=V55UAC1_2CH
  audio=2ch, 48 kHz, signed 16-bit PCM OUT

hid_audio_uac2_2ch:
  HID + UAC2 render experiment
  serial=V55UAC2_2CH
  audio=2ch, 48 kHz, signed 16-bit PCM OUT

hid_audio_uac2_4ch:
  HID + UAC2 render experiment
  serial=V55UAC2_4CH
  audio=4ch, 48 kHz, signed 16-bit PCM OUT

hid_audio_uac2:
  legacy alias for hid_audio_uac2_4ch
  warning=true
```

The `hid_audio_uac1_fallback` name is also accepted as a warning alias for
`hid_audio_uac1_2ch`.

## Hardware Result That Triggered Fallbacks

The first real `V55PHASE3` / old `hid_audio_uac2` hardware run did not
enumerate successfully:

```text
phase3_usb_found=true
phase3_status=Error
phase3_problem_code=10
phase3_config_error=CM_PROB_FAILED_START
phase3_hid_child_found=false
phase3_audio_child_found=false
identity_result=composite_error
audio_endpoint_found=false
```

Because of that result, Phase 3 now verifies in this order:

```text
1. hid_only
2. hid_audio_uac1_2ch
3. hid_audio_uac2_2ch
4. hid_audio_uac2_4ch
```

UAC1 2ch is the first real audio fallback. It is meant to prove that Windows
can enumerate the composite parent, keep the HID child alive, and expose a
basic render endpoint. It is not intended to reproduce full DualSense haptic
audio.

## Endpoint Shape

```text
USB identity: VID 054C / PID 0CE6
HID IN endpoint: 0x81 interrupt, 64 bytes, 1 ms poll
HID OUT endpoint: 0x01 interrupt, 64 bytes, 1 ms poll
Audio OUT endpoint: 0x02 isochronous adaptive
Audio sample rate: 48000 Hz
Audio sample width: 16-bit
```

For audio profiles, Audio Control is interface `0`, Audio Streaming OUT is
interface `1`, and HID moves to interface `2`. HID report IDs and HID endpoint
addresses remain unchanged from Phase 2/2.1.

## Audio Processing

UAC1 2ch only keeps the endpoint alive and logs OUT packet activity.

UAC2 profiles initialize the haptic-audio dry-run pipeline. For 2ch UAC2,
channels 0/1 are treated as left/right haptic source. For 4ch UAC2, channels
2/3 are treated as left/right haptic source and channels 0/1 are ignored for
now.

`haptic_audio_to_raw02` remains dry-run only. It converts haptic audio features
into candidate Pro2 `Left[16] + Right[16]` payload logs and never sends them
to the real controller in this phase.

## Validation Commands

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_only -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac1_2ch -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac2_2ch -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac2_4ch -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_identity.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_audio.ps1
```

The check scripts print `current_serial`, `current_profile`, and
`suggested_next_action` so the next flash can follow the fallback sequence
without guessing from stale Windows device nodes.

## Success Criteria

```text
Windows HID identity remains 054C:0CE6
input report remains 0x01 + 63 bytes around 250 Hz
real Pro2 input still drives DualSense input
ordinary HID output 0x02 rumble compatibility still works
Windows shows an audio render endpoint
firmware logs audio OUT activity or at least audio interface alt setting
USB does not disconnect
```

## Failure Signals

```text
HID disappears: composite descriptor or interface ordering is wrong
audio endpoint missing: audio descriptor, class config, or string/interface shape needs adjustment
unknown USB device: descriptor length or endpoint attributes are wrong
input rate drops badly: USB/BLE/audio scheduling needs throttling
ordinary rumble stops working: HID OUT path was disturbed and must be restored
```

## Current Limits

- No speaker playback.
- No microphone/audio IN endpoint.
- No complete DualSense haptic audio reproduction.
- No live Pro2 raw02 forwarding.
- raw02 dry-run payloads are safe approximation templates, not final HD Rumble
  2 reproduction.
