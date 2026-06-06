# V5.5 USB Audio Bandwidth Analysis

Date: 2026-06-06

## Audio Payload Cost

At 48 kHz, signed 16-bit PCM:

```text
2 channels:
  48000 samples/sec * 2 bytes/sample * 2 channels = 192000 bytes/sec
  nominal USB payload = 192 bytes/ms

4 channels:
  48000 samples/sec * 2 bytes/sample * 4 channels = 384000 bytes/sec
  nominal USB payload = 384 bytes/ms
```

The UAC2 TinyUSB endpoint size intentionally includes one extra full-speed
frame of slack:

```text
UAC2 2ch max_packet = 196 bytes
UAC2 4ch max_packet = 392 bytes
```

The UAC1 2ch fallback uses a fixed 192-byte isochronous OUT packet because it
is meant to be the simplest Windows composite enumeration check.

## Full-Speed USB Budget

The ESP32-S3 native USB device path is full-speed. Full-speed USB has limited
frame budget, and isochronous transfers reserve bandwidth each 1 ms frame. The
audio endpoint is not the only active work:

```text
HID input: about 250 Hz, endpoint 0x81 interrupt
HID output: endpoint 0x01 interrupt
BLE central: real Pro2 input around 133 Hz in the V5.x bridge path
Audio OUT: 2ch or 4ch isochronous endpoint 0x02
Firmware work: BLE parsing, DualSense report mapping, optional haptic audio statistics
```

Because of that, 2ch audio must be verified first. A 4ch descriptor may be
valid but still fail or become unstable if the host/driver/firmware scheduling
cannot tolerate the higher full-speed isochronous load.

## Verification Strategy

```text
1. hid_only:
   recover the HID baseline and confirm there is no input or output regression.

2. hid_audio_uac1_2ch:
   prove a simple UAC1 2ch composite descriptor can start on Windows.

3. hid_audio_uac2_2ch:
   prove TinyUSB UAC2 can start with the lower audio bandwidth.

4. hid_audio_uac2_4ch:
   test the DS5-like 4ch route only after the lower-risk paths work.
```

`hid_audio_uac2` remains a warning alias for `hid_audio_uac2_4ch`, but it should
not be the first profile flashed after a composite Code 10 failure.

## Expected Host Checks

The host tools print profile-aware hints:

```text
current_serial=V55HIDONLY / V55UAC1_2CH / V55UAC2_2CH / V55UAC2_4CH
current_profile=hid_only / hid_audio_uac1_2ch / hid_audio_uac2_2ch / hid_audio_uac2_4ch
suggested_next_action=...
```

If a profile starts the composite parent but loses the HID child, the issue is
descriptor/interface/class-driver level. If the HID child remains but audio is
missing, the next focus is the audio class descriptor or endpoint selection.

## Fallback Options For Future Work

If 4ch/48kHz remains unstable on full-speed USB, these options stay open:

- Keep a 2ch haptic endpoint and map host-side haptic sources into two channels.
- Split channels on the host side before sending to the ESP32-S3.
- Try 4ch at 24 kHz to reduce nominal audio payload from 384 bytes/ms to
  192 bytes/ms.
- Use a vendor/serial feature transport for haptic parameters instead of a
  full four-channel audio stream.

These are future fallback paths only. Current Phase 3 work is limited to
getting Windows to enumerate the HID + audio composite profiles reliably.
