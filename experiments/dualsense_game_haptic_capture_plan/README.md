# V5.3 DualSense Game Haptic Capture Plan

This directory documents the future real-game capture pass. The executable entry
point is the repository-root helper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_dualsense_capture.ps1 -DurationSeconds 90
```

The runner does not start a game automatically. It first runs
`tools\check_dualsense_env.ps1`; if a real DualSense is present, it starts the
HID output probe and the haptic audio probe, writes logs to
`logs\v5_3_dualsense\`, and asks the user to manually open a native
DualSense-capable PC game.

Current no-hardware behavior:

```text
[V5_3_CAPTURE] env=blocked
[V5_3_CAPTURE] blocked=no_real_dualsense
```

The blocked state exits with code 0 so night-run can continue.
