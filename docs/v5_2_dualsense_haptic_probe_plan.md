# V5.2 DualSense Haptic Probe Plan

Status: parallel research track. This is not a V5.2 Pro2 mainline integration
until real DualSense data or a verified virtual DualSense + virtual audio route
is available.

## Questions

1. Is a real DualSense present over USB or Bluetooth?
2. Is a DualSense / Wireless Controller audio endpoint present?
3. Can WASAPI loopback capture the haptic audio stream on Windows?
4. Can output HID reports be captured without a driver/proxy?
5. Can haptic audio be translated to Pro2 HD rumble packets?

## Local Probe Entry Points

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_dualsense_env.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

## Current Expected Result

On the current machine, no real DualSense and no DualSense audio endpoint were
detected. HID output capture and haptic audio capture are therefore blocked.

## Integration Boundary

Do not add DualSense haptics to the formal V5.1/V5.2 Manager GUI until one of
these is verified:

- real DualSense output/audio capture,
- DS5Dongle-style hardware bridge,
- or signed-driver virtual DualSense + virtual audio route.
