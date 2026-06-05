# DualSense HID Output Probe

V5.2 parallel research helper for the PS5 / DualSense route.

This probe does not install drivers and does not create a virtual DualSense. It
only checks whether a real DualSense-like HID device is present and records why
HID output capture is blocked on this machine.

## Goal

```text
real DualSense present
-> supported game or Steam sends output reports
-> capture output report hex
-> classify ordinary rumble, triggers, LEDs, and possible haptic controls
```

## Current Limitation

Normal user-space HID access cannot sniff output reports already sent by Steam to
another HID handle. A real capture needs one of these:

- a real DualSense opened through a library that receives output callbacks,
- a proxy device path,
- a filter driver,
- or a controlled virtual DualSense implementation that exposes output reports.

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
```
