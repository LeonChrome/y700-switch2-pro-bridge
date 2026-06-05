# V5.2 VIIPER ns2pro Probe Results

Date: 2026-06-06

## Environment

```text
[VIIPER_ENV] windows=Microsoft Windows 11 Pro 10.0.26200 build=26200
[VIIPER_ENV] dotnet=8.0.421
[VIIPER_ENV] git=git version 2.50.1.windows.1
[VIIPER_ENV] go=go version go1.26.4 windows/amd64
[VIIPER_ENV] cmake=not_found
[VIIPER_ENV] usbip_win2=not_found
[VIIPER_ENV] usbip_exe=not_found
[VIIPER_ENV] viiper=work/tools/viiper/viiper.exe
[VIIPER_ENV] admin=false
[VIIPER_ENV] steam=running
```

VIIPER was built locally from `work/upstream-research/VIIPER` with the portable
Go toolchain under `work/deps/go`.

## usbip-win2

Status: not installed.

`tools/install_usbip_win2.ps1` was added and run in non-install mode. The current
environment received GitHub API `403` responses for both:

- `vadimgrn/usbip-win2`
- `OSSign/vadimgrn--usbip-win2`

No driver installation was attempted because the current shell is not elevated
and kernel driver install must not be done silently.

## VIIPER ns2pro Probe

Entry point:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1
```

### Auto-Attach Run

Result: blocked before device attach.

Important lines:

```text
[VIIPER] backend=server auto_attach=True
[VIIPER] bus=1
Failed to auto-attach device: exec: "usbip": executable file not found in %PATH%
```

Conclusion: without usbip-win2, VIIPER cannot attach the virtual USB device into
the Windows USB stack. Steam/SDL therefore cannot see the device.

### No-Auto-Attach Run

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1 -DurationSeconds 12 -NoAutoAttach
```

Result:

```text
[NS2PRO] virtual device created bus=1 dev=1 vid=0x057e pid=0x2069
[NS2PRO] virtual device connected
[NS2PRO_INPUT] buttons=... gyro=(...) accel=(...)
[NS2PRO_OUTPUT] left_nonzero=false right_nonzero=false
[NS2PRO] result output_feedback=false nonzero=false
```

This proves the VIIPER API, bus creation, ns2pro creation, stream connection,
and 24-byte synthetic input feed work. It does not satisfy Phase 2 success
because no host is attached and no rumble output can be produced.

## SDL Rumble Test

Added:

```text
experiments/sdl_ns2pro_rumble_test
```

Build: passed.

Run result:

```text
[SDL_NS2PRO] blocked: SDL3.dll not found. Pass --sdl3 <path> or set SDL3_DLL.
```

Even with SDL3 present, this test needs usbip-win2 first so Windows can see the
VIIPER ns2pro device.

## Phase 3 Real Pro2 Rumble

Added:

```text
experiments/viiper_ns2pro_to_real_pro2_rumble_probe
```

Dry-run result:

```text
[NS2PRO_OUTPUT] left_nonzero=true right_nonzero=true
[PRO2_HD_RUMBLE] mode=dry_run
[PRO2_HD_RUMBLE] payload_0x02=02...
[PRO2_HD_RUMBLE] sent=false
```

The dry-run mapper reconstructs:

```text
0x02 + LeftRumble[16] + RightRumble[16] + zero padding to 64 bytes
```

Real send was not attempted because:

- Phase 2 did not capture real non-zero `LeftRumble[16] / RightRumble[16]`.
- V5.1 firmware exposes `rumble hdtest`, `rumble hold`, `rumble tune`, and
  `rumble stop`, but no raw control command for arbitrary `0x02 + 16 + 16`
  injection.

## Answers

1. Can VIIPER ns2pro receive non-zero HD rumble output now?
   No. The stream works, but Windows cannot attach the device without usbip-win2.

2. Can the 16+16 bytes be forwarded to real Pro2 now?
   Not yet. The likely HID OUT packet can be dry-run reconstructed, but real
   forwarding needs captured non-zero output and a firmware raw-send command.

3. Is VIIPER still the best Pro2 HD route?
   Yes. It is the only investigated route that exposes Switch 2 Pro `0x02`
   HD rumble output as `LeftRumble[16] / RightRumble[16]`.

## Next Required Action

Install usbip-win2 in an elevated Windows session, then rerun:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1
```

After Windows sees the ns2pro device, run the SDL rumble test or Steam controller
test and check for non-zero `LeftRumble[16] / RightRumble[16]`.
