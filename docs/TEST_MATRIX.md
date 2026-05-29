# Test Matrix

Date: 2026-05-29

Status labels:

- **Verified**: tested in the current project environment.
- **Planned**: intended direction, not stable yet.
- **Not tested**: no confirmed test result yet.

## Windows 10 / Windows 11

| Area | Test item | Current status | Notes |
| --- | --- | --- | --- |
| USB detection | Native USB enumerates as Nintendo / Switch Pro-style HID | Verified | Current ESP32-S3 V4 path uses the Windows / Steam Nintendo-style route. |
| Steam detection | Steam Input can use the controller path | Verified | Do not promise every Steam version displays "Switch 2 Pro". |
| Input mapping | Buttons, D-pad, sticks, stick clicks, `+`, `-`, Home, Capture, C, GL, GR | Verified | Current tested environment only. |
| Rumble | Basic rumble / haptic forwarding | Verified | Physical feedback observed; full HD rumble feature parity is not claimed. |
| BLE input rate | 133Hz-class BLE input | Verified | BLE interval request `6` / `7.5 ms`; environment-dependent. |
| USB report rate | 1000Hz-class USB HID report output | Verified | Windows host-side test observed about `993 Hz` over 10 seconds. |
| Manager status | USB/BLE/rate/rumble status panel | Verified | Windows Manager exists and shows key fields. |

## macOS

| Area | Test item | Current status | Notes |
| --- | --- | --- | --- |
| USB detection | Generic USB HID Gamepad enumeration | Planned / Not tested | Future target is standard USB HID, not native Pro2 identity. |
| Buttons / sticks / triggers | Basic gamepad controls | Planned / Not tested | Needs real macOS host testing. |
| Steam or browser gamepad tester | Host-observed input behavior | Planned / Not tested | Record macOS version and tool used. |
| Rumble | Generic HID rumble behavior | Not tested | Not a stable promise. |
| Rate measurement | Host-observed event/report rate | Planned / Not tested | Needs a macOS HID inspector or test tool. |

## Android

| Area | Test item | Current status | Notes |
| --- | --- | --- | --- |
| OTG detection | Android device powers and enumerates the board | Planned / Not tested | Requires known-good OTG/data cable. |
| Generic USB HID detection | Android sees a wired gamepad | Planned / Not tested | Target is Generic HID, not native Pro2 identity. |
| Cloud gaming / emulator | App-level mapping | Planned / Not tested | Record app, Android version, and device model. |
| Browser gamepad tester | Host-observed input | Planned / Not tested | Browser support may vary. |
| Power / cable behavior | Replug, hub, screen lock, Type-C direction | Planned / Not tested | Important for troubleshooting. |
| Rate measurement | Host-observed event/report rate | Planned / Not tested | Android APIs may coalesce events. |

## Dual Controller Mode

| Area | Test item | Current status | Notes |
| --- | --- | --- | --- |
| Two BLE connections | One board connects to two Pro2 controllers | Planned / Not tested | First dual-mode gate. |
| Input isolation | A/B controller inputs do not mix | Planned / Not tested | Requires per-slot parser and metrics. |
| USB composite HID | Host sees two independent gamepads | Planned / Not tested | Prefer two HID interfaces for first experiment. |
| A/B identity stability | Saved controller slots survive reconnect | Planned / Not tested | Needs NVS slot assignment design. |
| 66Hz dual test | Conservative dual input rate | Planned / Not tested | First performance target. |
| 100Hz dual test | Higher dual input rate | Planned / Not tested | Only after 66Hz is stable. |
| 133Hz dual challenge | Maximum dual input experiment | Planned / Not tested | Do not advertise as stable until measured. |

## Required Log Fields For Future Tests

| Field | Meaning |
| --- | --- |
| `firmware_version` | Firmware version under test |
| `board_type` | Board model/revision |
| `host_os` | Windows/macOS/Android version |
| `profile` | Windows / Steam, macOS Generic, Android Generic, or dual mode |
| `ble_input_actual_mhz` | BLE input notification rate in millihertz |
| `report_actual_mhz` | USB report output rate in millihertz |
| `report_failed` | USB report send failures |
| `ble_input_last_gap_us` | Last BLE input gap |
| `ble_input_max_gap_us` | Recent maximum BLE input gap |
| `host_observed_rate` | Rate measured by the host tool, if available |
| `known_issues` | Mapping, power, cable, reconnect, or host quirks |

