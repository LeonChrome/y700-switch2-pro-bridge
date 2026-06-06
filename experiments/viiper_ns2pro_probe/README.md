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

Long-running monitor mode:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1 -MonitorOnly -Seconds 300
```

Use monitor mode when you want the virtual `ns2pro` to stay online while Steam,
SDL, or another host-side tool sends output reports.

The script builds a portable VIIPER server from `work/upstream-research/VIIPER`
when `work/tools/viiper/viiper.exe` is missing.

Windows recognition requires a USBIP client driver such as `usbip-win2`. Without
that driver, the probe can still create a VIIPER bus, create an `ns2pro` device,
connect the feeder stream, and send synthetic input, but Steam/SDL cannot see the
virtual USB device and no host rumble output is expected.

## usbip-win2 Install

Run all commands from the repository root.

Check the current machine:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_viiper_env.ps1
```

Automatic install path:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate
```

If the GitHub API returns `403` or does not expose a downloadable asset, download
the latest Windows x64/amd64 installer asset manually from:

```text
https://github.com/vadimgrn/usbip-win2/releases
https://github.com/OSSign/vadimgrn--usbip-win2/releases
```

Save it under:

```text
.\work\deps\usbip-win2\<asset-file>
```

Then install with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate -InstallerPath .\work\deps\usbip-win2\<asset-file>
```

The installer script only installs/verifies usbip-win2. It does not uninstall
existing drivers and does not touch the V5.1 Manager, ESP32-S3 firmware, or BLE
bridge path.

The common release asset name is shaped like
`USBip-<version>-x64-Release.exe`; `.msi` and `.zip` packages are also supported
when a release provides them.

## Expected Logs

```text
[VIIPER] starting
[VIIPER] mode=server/lib/subprocess
[NS2PRO] virtual device connected
[NS2PRO_INPUT] buttons=... lx=... ly=... rx=... ry=... gyro=(...) accel=(...)
[NS2PRO_OUTPUT] flags=... led=... left_rumble_hex=... right_rumble_hex=...
[NS2PRO_OUTPUT] left_nonzero=true right_nonzero=true
```

## Steam / SDL / HID Rumble Validation

1. Start the VIIPER ns2pro monitor:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1 -MonitorOnly -Seconds 300
```

2. Open Steam Controller Test and try its rumble test. If Steam emits non-zero
   output, the monitor should show:

```text
[NS2PRO_OUTPUT] left_nonzero=true right_nonzero=true
```

3. If Steam does not emit non-zero rumble, run the SDL test:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1 -All -DurationMs 1200
```

4. If SDL ordinary rumble also only produces zero or unsupported output, use the
   direct HID output trigger:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticProbe.ps1 -Vid 057e -Pids 2069 -PathContains "vid_057e&pid_2069&mi_00" -PulseMs 800 -Pattern single
```

Current result: SDL 3.4.10 sees the virtual `VID_057E&PID_2069&MI_00` interface
as a low-level joystick, not a gamepad, and reports ordinary rumble/effect as
unsupported. The direct HID output trigger does produce non-zero
`LeftRumble[16] / RightRumble[16]`.

For a one-command repeatable validation, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_hid_rumble_probe\run_viiper_ns2pro_hid_rumble_probe.ps1
```
