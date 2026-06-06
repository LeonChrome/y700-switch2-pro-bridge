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

The runner automatically locates or downloads `SDL3.dll`, then copies it into
the Release output directory. If you want to pass a DLL explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1 -Sdl3Path <path-to-SDL3.dll>
```

## Expected Logs

```text
[SDL_RUNTIME] release_source=...
[SDL] version=...
[SDL] joystick_count=...
[SDL] gamepad_count=...
[SDL] name=...
[SDL] path=...
[SDL] rumble_supported=...
[SDL] trigger_rumble_supported=...
[SDL] joystick_rumble_result=...
[SDL] joystick_trigger_rumble_result=...
[SDL] send_effect_result=...
```

Without usbip-win2, Windows cannot see the VIIPER virtual USB device, so this
test is expected to report no matching controller.

The raw effect path uses SDL3 `SDL_SendGamepadEffect` when available. By default
it tries a 0x02 + 16-byte left + 16-byte right ns2pro-shaped packet, a bare
16+16 packet, and a short Switch Pro 0x10-style packet. To send one specific
payload:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1 -EffectHex "02 20 21 ..."
```

Current V5.2 result with SDL 3.4.10: the VIIPER `ns2pro` appears as a low-level
SDL joystick (`VID_057E&PID_2069&MI_00`) but not as an SDL gamepad.
`SDL_RumbleJoystick`, trigger rumble, and `SDL_SendJoystickEffect` return
unsupported. The separate direct HID output probe can trigger non-zero VIIPER
`LeftRumble[16] / RightRumble[16]`.
