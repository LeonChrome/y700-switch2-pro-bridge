# Next Generation Plan

Date: 2026-05-29

This document describes planned next-generation directions. Items marked Planned or Not tested are not current stable capabilities.

## 1. Direction

The current stable direction is the ESP32-S3 BLE-to-USB bridge for Windows / Steam. The next generation should keep that working path and add clearly separated USB output profiles.

### Windows / Steam Mode

Status: Verified on current ESP32-S3 path.

Goal:

- Keep Windows 10/11 and Steam Input as the strongest first-class target.
- Use a Steam-optimized Nintendo / Switch Pro-style USB HID path.
- Preserve the project identity: real Pro2 BLE input, hardware USB HID output, and Nintendo-style recognition path where Steam handles it well.

Boundaries:

- Do not promise every Steam version or every game will display the device as "Switch 2 Pro".
- Do not claim full Nintendo private feature parity.

### macOS Generic USB HID Gamepad Mode

Status: Planned / Not tested as a stable mode.

Goal:

- Expose a standard USB HID Gamepad to macOS.
- Prioritize driverless buttons, sticks, triggers/shoulders, and D-pad.
- Avoid requiring a macOS helper app for normal input.

### Android OTG Generic USB HID Gamepad Mode

Status: Planned / Not tested as a stable mode.

Goal:

- Expose a standard USB HID Gamepad to Android over USB OTG.
- Target Android tablets/phones, cloud gaming, emulators, and browser gamepad testers.
- Document OTG cable, Type-C direction, power, and compatibility notes.

### Dual Controller Mode

Status: Planned / Not tested.

Goal:

- One development board acts as BLE Central for two Switch 2 Pro Controllers.
- USB side exposes two independent HID Gamepad interfaces.
- Controller A/B identity should be stable where possible.

### Profile Switching

Status: Planned.

Goal:

- Support Windows / Steam, macOS Generic, and Android Generic profiles.
- Start with compile-time or serial-command switching.
- Add button long-press switching later.
- Keep the Windows Manager optional, not mandatory.

## 2. Why macOS / Android Do Not Target Native Pro2 Bluetooth Identity

macOS and Android may not provide native system-level Bluetooth support or authentication behavior for Switch 2 Pro Controller identity.

This project avoids that uncertainty by using the development board as a USB HID hardware device:

```text
Real Pro2 Controller
-> BLE to board
-> board exposes USB HID Gamepad
-> macOS / Android sees a wired USB gamepad
```

For macOS and Android, the target is standard Generic USB HID Gamepad behavior, not native Pro2 Bluetooth identity.

This means:

- No claim that macOS or Android will identify it as a real Switch 2 Pro Controller.
- No claim that the controller pairs directly to macOS or Android through this project.
- The board is the receiver and USB HID adapter.

## 3. Dual Controller Planning

Target architecture:

```text
Pro2 Controller A -> BLE -> board
Pro2 Controller B -> BLE -> board
board -> USB composite HID -> host sees Gamepad A + Gamepad B
```

Implementation stages:

1. Verify two BLE connections at the same time.
2. Verify input isolation between Controller A and Controller B.
3. Verify USB composite HID with two independent Gamepad interfaces.
4. Test conservative dual-controller rate around 66Hz.
5. Test 100Hz if 66Hz is stable.
6. Challenge 133Hz only after earlier stages are stable.

Do not promise stable dual-controller 133Hz before real hardware measurements.

## 4. Risk Boundaries

The project does not promise:

- All platforms display the device as Pro2.
- macOS / Android native Bluetooth recognition as Switch 2 Pro Controller.
- Stable dual-controller 133Hz.
- Complete HD rumble, gyro, NFC, wake-up, or battery reports.
- True Xbox / XInput identity.
- Identical behavior across all boards, cables, hosts, and Steam versions.

All performance numbers should be backed by firmware logs and host-side test tools.

## 5. Suggested Milestones

| Milestone | Scope | Status |
| --- | --- | --- |
| v4 stable maintenance | Keep ESP32-S3 Windows / Steam path stable | Current stable |
| V5 AIO manager hardening | Improve all-in-one flashing and status UX | Preview |
| macOS Generic HID | Standard USB HID Gamepad profile and tests | Planned / Not tested |
| Android Generic HID | OTG-friendly HID mapping and tests | Planned / Not tested |
| Profile switching | Saved mode selection and clear re-enumeration behavior | Planned |
| Dual Controller experiment | Two BLE controllers and two USB HID interfaces | Planned / Not tested |

