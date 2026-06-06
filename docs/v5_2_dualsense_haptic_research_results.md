# V5.2 DualSense Haptic Research Results

## Summary

DualSense haptics is a different route from Pro2 HD rumble. It is primarily an
audio-haptics path plus HID/adaptive-trigger control, not a simple DS4/ViGEm
ordinary rumble upgrade.

Current conclusion: keep DualSense as V5.3 research unless real hardware or a
verified virtual DualSense + virtual audio stack is available.

## DS5Dongle

Source: `work/upstream-research/DS5Dongle`.

- Presents a Pico2W/Pico W bridge as a host-visible DualSense-like device after
  the real DualSense is connected over Bluetooth.
- README claims enhanced / HD haptics support.
- Pico W variant has haptics only and no speaker.
- `src/audio.cpp` shows the core path: read USB audio, split channels, use
  channels 2/3 for haptics, resample `48000 -> 3000`, then pack the haptic bytes
  into Bluetooth reports.
- This is hardware-assisted and depends on a real DualSense.

Answer to "how PC thinks it is wired USB": the Pico-side firmware enumerates as
the USB device while the real controller is on Bluetooth behind it. The exact
descriptor path must be validated before copying anything into this project.

## dualsense-bt-haptics

Source: `work/upstream-research/dualsense-bt-haptics`.

- Creates a virtual DS5 through a forked ViGEmBus.
- Requires a virtual audio device named like `DualSense Wireless Controller`.
- Captures audio through WASAPI loopback.
- Uses SAxense-style Bluetooth haptic packet injection to the real controller.
- README explicitly warns the route is not generic for most AAA games.
- Requires driver work and is not suitable for direct V5.2 Pro2 integration.

## SAxense

Source: `work/upstream-research/SAxense`.

- Linux proof of concept for DualSense haptics over Bluetooth.
- Converts haptic audio to a `3000 Hz`, two-channel stream and writes it to
  DualSense hidraw.
- Recommends a separate haptics sink over loopback capture.
- Notes that loopback latency can be subtle or even seconds depending on setup.

## Gamepad-Core / Unreal-Dualsense

Sources:

- `work/upstream-research/Gamepad-Core`
- `work/upstream-research/Unreal-Dualsense`

These are useful references for native DualSense features, adaptive triggers,
lightbar, and audio haptics in engines, but they are not a direct Pro2 HD rumble
capture route for this bridge.

## Answers

1. DS5Dongle handles enhanced haptics by splitting USB audio haptic channels,
   resampling them, and forwarding Bluetooth reports to a real DualSense.
2. dualsense-bt-haptics converts a virtual audio endpoint plus loopback capture
   into Bluetooth haptic packets, but it needs real DualSense and driver pieces.
3. SAxense latency mainly comes from audio loopback/capture, not from the haptic
   packet writer itself.
4. Without real DualSense, the pure software route needs a signed virtual
   DualSense HID driver plus a virtual multichannel audio endpoint.
5. That driver/audio stack is a V5.3-scale project, not a safe V5.2 Pro2 release
   addition.
6. A direct Pro2 translation remains theoretical until we capture either
   DualSense haptic audio or VIIPER ns2pro non-zero 16+16 HD rumble output.

## Local Executable Probes

Added runnable V5.2 research harnesses:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

Current local result:

```text
[DUALSENSE_ENV] hid_usb=false
[DUALSENSE_ENV] hid_bluetooth=false
[DUALSENSE_ENV] real_dualsense=false
[DUALSENSE_AUDIO] device=not_found
[DUALSENSE_BLOCKED] reason=no_real_dualsense
[DUALSENSE_BLOCKED] reason=no_dualsense_audio_endpoint
```

This means the DualSense route is executable as a probe framework, but blocked
on this machine by missing real DualSense HID/audio endpoints. Keep it as V5.3
research and do not merge it into the V5.2 Pro2 HD path.

V5.3 follow-up docs:

```text
docs/v5_3_dualsense_haptic_roadmap.md
docs/v5_3_dualsense_test_checklist.md
```
