# V5.2 VIIPER ns2pro Probe Results

Date: 2026-06-06

## Summary

```text
usbip-win2 installed: true
VIIPER server: started
Windows recognition: true
Steam recognition: not manually tested in this run
SDL recognition: true as low-level joystick, false as SDL gamepad
VIIPER ns2pro input: true
output_feedback: true
LeftRumble[16]/RightRumble[16] nonzero: true
nonzero trigger source: direct Windows HID output write through tools\Send-HidHapticProbe.ps1
repeatable validation: experiments\viiper_ns2pro_hid_rumble_probe
current blocker: Steam/SDL ordinary rumble sources still need mapping/recognition work
```

Phase 2 reached the important proof point: VIIPER's `ns2pro` output callback can
carry non-zero 16-byte left and right rumble blocks. The non-zero trigger source
is not Steam or SDL's ordinary rumble API yet; it is a direct HID output report
written to the virtual `VID_057E&PID_2069&MI_00` interface.

This does not modify V5.1, the ESP32-S3 firmware, the Manager GUI, or real Pro2
rumble forwarding.

## Environment

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_viiper_env.ps1
```

Observed:

```text
[VIIPER_ENV] usbip_win2=installed
[VIIPER_ENV] usbip_exe=installed
[VIIPER_ENV] usbip_service=not_found
[VIIPER_ENV] usbip_driver=USBip 3.X Emulated Host Controller:OK
[VIIPER_ENV] usbip_root_hub=USBip 3.X Emulated Host Controller:OK
[VIIPER_ENV] viiper=.\work\tools\viiper\viiper.exe
[VIIPER_ENV] admin=false
[VIIPER_ENV] steam=running
[VIIPER_ENV] next=powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1
```

## Monitor Mode

Added:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1 -MonitorOnly -Seconds 300
```

Behavior:

- starts VIIPER server
- creates and auto-attaches a virtual `ns2pro`
- keeps the device online for the requested duration
- keeps sending synthetic input frames
- continuously prints output feedback
- exits successfully even if no non-zero rumble arrives during the window

Short validation:

```text
[VIIPER_LOG] usbip-win2 driver found
[VIIPER_LOG] Successfully attached device via IOCTL busID=1 deviceID=1 usbPort=1
[NS2PRO] virtual device created bus=1 dev=1 vid=0x057e pid=0x2069
[NS2PRO_OUTPUT] flags=0x02 led=0x01 left_rumble_hex=00000000000000000000000000000000 right_rumble_hex=00000000000000000000000000000000
[NS2PRO] result output_feedback=true nonzero=false
```

## SDL Runtime

`experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1` now:

- locates an existing `SDL3.dll`
- falls back to downloading the latest official SDL3 Windows x64 runtime from
  `libsdl-org/SDL`
- handles GitHub API `403` by parsing the release expanded-assets HTML
- copies `SDL3.dll` to the test output directory

Observed runtime:

```text
[SDL_RUNTIME] github_api_403=libsdl-org/SDL/releases/latest
[SDL_RUNTIME] release_source=html tag=release-3.4.10 asset=SDL3-3.4.10-win32-x64.zip
[SDL_RUNTIME] canonical_dll=.\work\deps\sdl3\SDL3.dll
[SDL] version=3.4.10 raw=3004010
[SDL] revision=SDL-release-3.4.10-0-g8e37db5e7 (libsdl.org)
```

## SDL Rumble Test

Command used while VIIPER monitor was online:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1 -All -DurationMs 1200
```

Result:

```text
[SDL] joystick_count=1
[SDL] joystick_name_for_id=HID Interface
[SDL] joystick_path_for_id=\\?\HID#VID_057E&PID_2069&MI_00#...
[SDL] joystick_is_gamepad=false
[SDL] joystick_axes=4
[SDL] joystick_buttons=21
[SDL] rumble_supported=not_available
[SDL] trigger_rumble_supported=not_available
[SDL] joystick_rumble_result=false error=That operation is not supported
[SDL] joystick_trigger_rumble_result=false error=That operation is not supported
[SDL] send_effect_result=false api=joystick label=ns2pro_hd_report_02_16_16 error=That operation is not supported
[SDL] send_effect_result=false api=joystick label=ns2pro_hd_16_16 error=That operation is not supported
[SDL] send_effect_result=false api=joystick label=switch_pro_output_10 error=That operation is not supported
[SDL] gamepad_count=0
```

Conclusion:

```text
SDL recognition: true, low-level joystick only
SDL gamepad recognition: false
SDL_RumbleGamepad: not reached because gamepad_count=0
SDL_RumbleGamepadTriggers: not reached because gamepad_count=0
SDL_RumbleJoystick: false
SDL_RumbleJoystickTriggers: false
SDL_SendJoystickEffect: false
VIIPER nonzero from SDL: false
```

## Direct HID Output Trigger

`tools\Send-HidHapticProbe.ps1` was constrained with `-PathContains` so it can
target only the intended virtual HID interface.

Command used while VIIPER monitor was online:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticProbe.ps1 -Vid 057e -Pids 2069 -PathContains "vid_057e&pid_2069&mi_00" -PulseMs 800 -Pattern single
```

HID write result:

```text
path=\\?\hid#vid_057e&pid_2069&mi_00#...
vid=057e pid=2069 ver=0200 inLen=64 outLen=64 featLen=0
single strong seq 0 WriteFile=True written=64 err=0 data=025087152751710000000000000000000050871527517100
...
single stop seq 0 WriteFile=True written=64 err=0 data=025287012011000000000000000000000052870120110000
```

VIIPER callback result:

```text
[NS2PRO_OUTPUT] flags=0x01 led=0x00 left_rumble_hex=52871527517100000000000000000000 right_rumble_hex=52871527517100000000000000000000
[NS2PRO_OUTPUT] left_nonzero=true right_nonzero=true
...
[NS2PRO_OUTPUT] flags=0x01 led=0x00 left_rumble_hex=52870120110000000000000000000000 right_rumble_hex=52870120110000000000000000000000
[NS2PRO] result output_feedback=true nonzero=true
```

Conclusion:

```text
direct HID output write: true
VIIPER output_feedback: true
LeftRumble[16] nonzero: true
RightRumble[16] nonzero: true
```

## Automated HID Rumble Probe

Added:

```text
experiments\viiper_ns2pro_hid_rumble_probe
```

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_hid_rumble_probe\run_viiper_ns2pro_hid_rumble_probe.ps1
```

This command starts the VIIPER monitor with `-ExitOnNonZero`, waits for attach,
sends the HID output trigger, and parses the monitor log.

Validated result:

```text
[NS2PRO_HID_RUMBLE_PROBE] monitor_attached=true
[NS2PRO_HID_RUMBLE_PROBE] hid_matched=[HID_HAPTIC] matched_devices=1
[NS2PRO_HID_RUMBLE_PROBE] output_feedback=true
[NS2PRO_HID_RUMBLE_PROBE] nonzero=true
[NS2PRO_HID_RUMBLE_PROBE] summary=[NS2PRO_OUTPUT] feedback_count=2 nonzero_count=1
[NS2PRO_HID_RUMBLE_PROBE] first_nonzero=[NS2PRO_OUTPUT_FIRST_NONZERO] flags=0x01 led=0x00 left_rumble_hex=50871527517100000000000000000000 right_rumble_hex=50871527517100000000000000000000
[NS2PRO_HID_RUMBLE_PROBE] result=passed
```

Monitor summary fields now include:

```text
[NS2PRO_OUTPUT] feedback_count=...
[NS2PRO_OUTPUT] first_nonzero_flags=...
[NS2PRO_OUTPUT] first_nonzero_left=...
[NS2PRO_OUTPUT] first_nonzero_right=...
```

## Steam Validation Path

Manual Steam test still needs the user to open Steam Controller Test while the
monitor is running:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1 -MonitorOnly -Seconds 300
```

Then:

1. Open Steam Controller Test.
2. Confirm Steam sees the virtual Switch 2 Pro / `ns2pro`.
3. Trigger Steam's rumble test.
4. Watch the monitor log for `left_nonzero=true right_nonzero=true`.
5. If Steam only produces zero output, use the direct HID output trigger above.

## Current Interpretation

1. VIIPER's `ns2pro` output callback is wired correctly.
2. The 16+16 rumble fields are real and can become non-zero.
3. SDL 3.4.10 sees this device as a generic HID joystick, not an SDL gamepad.
4. SDL's ordinary rumble/effect APIs do not map to this virtual ns2pro HID
   output path in the current descriptor/recognition state.
5. A direct 64-byte HID output report with report ID `0x02` and Switch-style HD
   rumble bytes does map into VIIPER's 16+16 callback.

## Next Phase 2 Work

The next useful research target is no longer "can VIIPER carry non-zero rumble";
that is proven and automated. The next target is to make a real host-side source
generate the same `0x02` output path:

1. Steam Controller Test while monitor is running.
2. SDL mapping/HIDAPI recognition so `VID_057E&PID_2069` becomes a supported
   Switch 2 Pro / ns2pro gamepad instead of generic HID joystick.
3. If SDL remains unsupported, direct HID output can stay as the controlled
   baseline for decoding and later forwarding research.
