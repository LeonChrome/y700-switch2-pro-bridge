# VIIPER ns2pro Probe

This is the V5.2 Phase 2 probe for the Pro2 HD route.

It does not touch the V5.1 Manager or firmware path.

## Goal

```text
VIIPER ns2pro virtual device
-> Windows / Steam / SDL recognition
-> synthetic input
-> output feedback capture
-> non-zero LeftRumble[16] / RightRumble[16]
```

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1
```

The script builds a portable VIIPER server from `work/upstream-research/VIIPER`
when `work/tools/viiper/viiper.exe` is missing.

Windows recognition requires a USBIP client driver such as `usbip-win2`. Without
that driver, the probe can still create a VIIPER bus, create an `ns2pro` device,
connect the feeder stream, and send synthetic input, but Steam/SDL cannot see the
virtual USB device and no host rumble output is expected.

## Expected Logs

```text
[VIIPER] starting
[VIIPER] mode=server/lib/subprocess
[NS2PRO] virtual device connected
[NS2PRO_INPUT] buttons=... lx=... ly=... rx=... ry=... gyro=(...) accel=(...)
[NS2PRO_OUTPUT] flags=... led=... left_rumble_hex=... right_rumble_hex=...
[NS2PRO_OUTPUT] left_nonzero=true right_nonzero=true
```
