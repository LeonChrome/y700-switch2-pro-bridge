# SDL ns2pro Rumble Test

V5.2 Phase 2 helper for triggering host-side rumble after a VIIPER `ns2pro`
device is visible to Windows.

This does not modify the V5.1 firmware, Manager GUI, or output mode.

## Goal

```text
Windows sees VIIPER ns2pro
-> SDL3 opens it as a gamepad
-> SDL3 sends rumble
-> VIIPER stream receives non-zero LeftRumble[16] / RightRumble[16]
```

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1
```

If `SDL3.dll` is not on the machine, pass it explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1 -Sdl3Path C:\path\to\SDL3.dll
```

## Expected Logs

```text
[SDL_NS2PRO] sdl3=...
[SDL_NS2PRO] gamepads=...
[SDL_NS2PRO] device=... name=...
[SDL_NS2PRO] rumble_result=...
```

Without usbip-win2, Windows cannot see the VIIPER virtual USB device, so this
test is expected to report no matching controller.
