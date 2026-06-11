# User Guide

This guide is for the final V5.9 all-in-one Manager.

## Hardware Setup

Use two USB connections when available:

- CH343P / WCH control port: flashing, logs, BLE commands, Manager control.
- ESP32-S3 native USB / OTG port: the actual USB gamepad seen by Windows / Steam.

The control port can be unplugged after configuration. Daily use should continue through the native USB gamepad port and BLE auto reconnect.

## Flashing a Mode

1. Close older Manager windows and serial monitors.
2. Connect the CH343P control port.
3. Open `PRO2手柄无线接收器控制板-aio-v5.9.0.exe`.
4. Click refresh serial if the COM port is not selected.
5. Click the desired mode card.
6. Wait for flashing to finish.
7. Replug the native USB / OTG gamepad cable.
8. Click USB check.

If Windows reports that the COM port is busy, unplug the CH343P control cable for a few seconds, plug it back in, refresh serial, and try again.

## Recommended Mode

Use Pro2 / Nintendo mode for normal Steam usage. It exposes a Nintendo Switch Pro style HID identity and keeps raw Pro2 rumble report `0x02` as the authoritative path.

Use Xbox / XInput for games that behave better with XInput devices.

Use DualSense-like only when testing DualSense-style compatibility or the experimental audio/haptic path.

## BLE Use

The Manager can scan, list, connect, disconnect, and reconnect the controller. With `ble_auto` enabled, firmware retries in the background after sleep or disconnect.

Control port commands are mainly for setup and debugging. Normal play should not require opening the Manager every time.

## Steam Check

For Pro2 / Nintendo mode:

1. Open Steam Controller Settings.
2. Confirm the device appears as a Switch Pro / Nintendo-style controller.
3. Test buttons, sticks, triggers, gyro if applicable, and rumble.
4. If the device does not appear after flashing, unplug and replug the native USB / OTG cable.

For Xbox / XInput mode:

1. Confirm Windows shows `VID_045E&PID_028E`.
2. Use Windows Game Controllers, Steam, or a game to test sticks and rumble.

## Files

The final release EXE is in `release/v5.9/`.

Source firmware and Manager code remain in `firmware/` and `windows/v55_manager_app/`.
