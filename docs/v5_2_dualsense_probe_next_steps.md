# V5.2 DualSense Probe Next Steps

Date: 2026-06-06

## Current Local Result

Runnable probes now exist, but this machine does not currently expose a real
DualSense HID device or a DualSense audio endpoint.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

Observed:

```text
[DUALSENSE_ENV] hid_usb=false
[DUALSENSE_ENV] hid_bluetooth=false
[DUALSENSE_ENV] real_dualsense=false
[DUALSENSE_AUDIO] device=not_found
[DUALSENSE_BLOCKED] reason=no_real_dualsense
[DUALSENSE_BLOCKED] reason=no_dualsense_audio_endpoint
```

## What The Probes Can Do Now

`dualsense_hid_output_probe`:

- enumerates Sony HID devices with DualSense-like PIDs
- logs USB/Bluetooth-ish transport hints
- logs HID usage and report lengths
- refuses to send any output unless an explicit safe test is implemented later

`dualsense_haptic_audio_probe`:

- enumerates Windows sound/PnP endpoints that look like DualSense / Wireless
  Controller / Sony Interactive
- logs haptic-audio placeholder metrics
- blocks cleanly when no matching endpoint exists

## Why This Should Stay V5.3

DualSense haptics are an audio-haptics route plus HID/adaptive-trigger control.
It is not the same shape as the Switch 2 Pro `ns2pro` 16+16 HD rumble callback.

To make it useful, V5.3 needs one of:

- a real DualSense connected over USB or Bluetooth,
- a virtual DualSense HID device plus a virtual multichannel audio endpoint,
- a proxy/filter route that can capture output reports and haptic audio,
- or a controlled game/app that sends known DualSense haptic content.

Without that hardware or driver/audio stack, DualSense cannot advance the V5.2
Pro2 HD path beyond research notes.

## Recommended Next Steps

1. Keep the probes in the repo as executable research harnesses.
2. Do not mix DualSense into V5.2 Pro2 release work.
3. When real DualSense hardware is available, rerun both probes.
4. If an audio endpoint appears, implement WASAPI loopback RMS/peak capture.
5. If HID output is needed, add a safe lightbar/trigger-only output test before
   any adaptive trigger or haptic experiment.
