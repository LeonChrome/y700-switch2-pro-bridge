# V5.2 Steam / SDL ns2pro Rumble Gap

Date: 2026-06-06

## Known Baseline

Direct HID output report `0x02` to the VIIPER virtual `ns2pro` device produces
non-zero `LeftRumble[16] / RightRumble[16]`.

Validated command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_hid_rumble_probe\run_viiper_ns2pro_hid_rumble_probe.ps1
```

Validated result:

```text
[NS2PRO_HID_RUMBLE_PROBE] output_feedback=true
[NS2PRO_HID_RUMBLE_PROBE] nonzero=true
[NS2PRO_HID_RUMBLE_PROBE] result=passed
```

Therefore, the remaining gap is not VIIPER's output callback. It is host-side
recognition and API mapping.

This gap is not a V5.2 closeout blocker. V5.2 closes on the verified direct HID
`0x02` / VIIPER raw02 forwarding path:

```text
VIIPER ns2pro output callback=true
direct HID 0x02 nonzero 16+16=true
raw02 forwarding to real Pro2=true
physical vibration=true
```

## SDL Result

SDL 3.4.10 runtime is automatically prepared by:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1 -All -DurationMs 1200
```

Observed while the VIIPER monitor was online:

```text
[SDL] version=3.4.10
[SDL] joystick_count=1
[SDL] name=HID Interface
[SDL] path=\\?\HID#VID_057E&PID_2069&MI_00#...
[SDL] vendor=057E
[SDL] product=2069
[SDL] nintendo_path=true
[SDL] switch_path=true
[SDL] joystick_is_gamepad=false
[SDL] gamepad_count=0
[SDL] joystick_rumble_result=false error=That operation is not supported
[SDL] joystick_trigger_rumble_result=false error=That operation is not supported
[SDL] send_effect_result=false api=joystick error=That operation is not supported
```

Conclusion:

```text
SDL ordinary rumble is not equal to the ns2pro HD rumble 0x02 path.
SDL sees the virtual device as a generic low-level HID joystick, not as a
rumble-capable Nintendo/Switch gamepad.
```

## Required Answers

1. Steam Controller Test may send ordinary rumble rather than Nintendo HD rumble.
   It must be tested manually while `-MonitorOnly` is running.

2. SDL ordinary rumble can degrade to generic rumble or be unsupported. In the
   current run it is unsupported for this virtual HID joystick.

3. `SDL_SendGamepadEffect` raw effect was not reached because `gamepad_count=0`.
   `SDL_SendJoystickEffect` was reached, but returned unsupported.

4. SDL recognizes the path as `VID_057E&PID_2069`, but the exposed name is
   `HID Interface` and `joystick_is_gamepad=false`.

5. Tested hints include:

```text
SDL_JOYSTICK_HIDAPI=1
SDL_JOYSTICK_HIDAPI_NINTENDO_SWITCH=1
SDL_JOYSTICK_HIDAPI_SWITCH=1
SDL_JOYSTICK_HIDAPI_JOY_CONS=1
SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1
SDL_GAMECONTROLLERTYPE=SwitchPro
```

They did not promote the device into an SDL gamepad.

6. SDL3 latest release at the time of this run is `3.4.10`. A nightly SDL can be
   tested later, but the stable release already proves the current descriptor
   does not enter the expected Nintendo rumble path.

7. The VIIPER `ns2pro` VID/PID are Nintendo-like, but the descriptor/name exposed
   to SDL appear insufficient for SDL's Nintendo HIDAPI rumble backend.

8. The current 0x02 trigger is reliable through raw Windows HID write. It is not
   currently exposed through SDL ordinary rumble.

## Next Probe Ideas

These are future compatibility ideas, not required V5.2 work:

1. Compare VIIPER `ns2pro` descriptor against the descriptor SDL expects for
   Switch Pro / Switch 2 Pro HIDAPI.
2. Test SDL nightly once available in the same runtime loader.
3. Run Steam Controller Test while the monitor is online and inspect whether it
   emits non-zero `0x02` output.
4. If SDL remains generic, keep raw HID write as the controlled baseline for
   Pro2 HD payload forwarding research.

Final V5.2 conclusion:

```text
Steam/SDL ordinary rumble != ns2pro HD 0x02
native Steam game HD rumble support=game/input-stack dependent
V5.2 reliable source=direct HID 0x02 or VIIPER probe capture
all-games HD rumble claim=false
```
