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

hid_audio_uac1_4ch_ds5like:
  HID + DS5Dongle-like UAC1 render
  serial=V55UAC1_4CH
  audio=4ch, 48 kHz, signed 16-bit PCM OUT
  channel_config=0x0033
  max_packet=384 bytes
  dwc2_mode=slave

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

## Hardware Result

The staged descriptor ladder now passes through full UAC1 2ch:

```text
hid_only=true
dummy_class_00=true
dummy_class_ef=true
audio_control_claimed=true
audio_streaming_alt0_claimed=true
uac1_2ch_parent=true
uac1_2ch_hid=true
uac1_2ch_media=true
uac1_2ch_audio_endpoint=true
```

The earlier composite Code 10 was caused by the custom TinyUSB application
driver callback not being pulled from `libmain.a`. `WHOLE_ARCHIVE` fixes the
linkage; the final ELF now contains a strong `usbd_app_driver_get_cb`.
UAC1 2ch proves that Windows can enumerate the parent, keep HID alive, and
expose a render endpoint. UAC1 4ch is the next DS5Dongle-like hardware stage.

Dynamic playback initially reached alt 1 but failed `usbd_edpt_open(0x02)`.
The ESP32-S3 DWC2 FIFO calculation showed that the 392-byte reference packet,
64-byte HID IN endpoint, and DMA metadata do not fit together. The 4ch profile
therefore uses slave mode and the exact 384 bytes required by 48 frames per
millisecond. Other profiles retain their existing TinyUSB mode.

The custom driver also follows TinyUSB's DWC2 ISO lifecycle: FIFO allocation
during configuration, ISO activation on alt 1, and no transfer re-arm after
alt 0. This allows repeated start/stop cycles without a stale transfer
continuing after playback ends.

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

UAC1 2ch and UAC1 4ch keep the endpoint alive and log OUT packet activity.
The 4ch profile uses channels 0/1/2/3 as the incoming stream; semantic haptic
channel processing remains out of scope until enumeration and transfer are
verified.

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
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac1_4ch_ds5like -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac2_2ch -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac2_4ch -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_identity.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_audio.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test_v5_5_dualsense_audio_stream.ps1 -Port COM12 -Seconds 3
```

The check scripts print `current_serial`, `current_profile`, and
`suggested_next_action` so the next flash can follow the fallback sequence
without guessing from stale Windows device nodes.

The stream test temporarily selects the controller endpoint, plays a
low-amplitude stereo source through Windows shared mode, captures UART, and
restores the previous default endpoint. Because the USB endpoint only exposes
4ch/48 kHz/16-bit, successful 384-byte packets verify the final four-channel
USB transport even though the portable test source itself is stereo.

## Completed 4ch Transport Result

```text
profile=hid_audio_uac1_4ch_ds5like
windows_composite=OK
windows_hid=OK
windows_media=OK
windows_audio_endpoint=OK
set_interface_1=true
streaming=true
ep=0x02
max_packet=384
dma=false
out_packet_len=384
out_packet_count_3s=3000
second_start_count_reset=true
stopped_at_alt_0=true
hid_concurrent_rate_hz=248.8
hid_concurrent_timeouts=0
result=passed
```

Phase 3 USB enumeration and four-channel transport are complete. Channel
semantics, haptic feature extraction, and live Pro2 translation remain Phase
4 work rather than descriptor or endpoint blockers.

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
