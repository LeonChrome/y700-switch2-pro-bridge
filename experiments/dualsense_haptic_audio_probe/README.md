# DualSense Haptic Audio Probe

V5.2 parallel research helper for DualSense audio haptics.

The important question is whether the PC exposes a DualSense / Wireless
Controller audio endpoint that can be captured through WASAPI loopback.

## Goal

```text
real DualSense audio device present
-> WASAPI loopback capture
-> channel count / sample rate / RMS / peak
-> detect haptic audio activity
```

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

This framework intentionally reports blocked when no DualSense audio endpoint is
present. A full RMS/peak capture implementation should be added only after the
real device or a verified virtual audio device exists.
