# User Guide

This guide is for the V5.9.3 新和联胜 all-in-one Manager.

## Hardware Setup

Use two USB connections when available:

- CH343P / WCH control port: flashing, logs, BLE commands, Manager control.
- ESP32-S3 native USB / OTG port: the actual USB gamepad seen by Windows / Steam.

The control port can be unplugged after configuration. Daily use should continue through the native USB gamepad port and BLE auto reconnect.

## Flashing a Mode

1. Close older Manager windows and serial monitors.
2. Connect the CH343P control port.
3. Open `新和联胜版本-aio-v5.9.3.exe`.
4. Click refresh serial if the COM port is not selected.
5. Click the desired mode card.
6. Wait for flashing to finish.
7. Replug the native USB / OTG gamepad cable.
8. Click USB check.

If Windows reports that the COM port is busy or names an esptool PID that
cannot be terminated, unplug the CH343P control cable for 3-5 seconds, plug it
back in, refresh serial, and try again. Do not repeatedly click the mode card;
V5.9.3 will refuse to stack another flashing process.

## Recommended Mode

Use 新和联胜 / PS5 for games with native DualSense support or controller-audio HD haptics. It also accepts valid ordinary DualSense motor commands; firmware selects the active source from host intent instead of classifying games by name.

Use Pro2 / Nintendo for Steam Input and native Pro2-style behavior.

Use Xbox / XInput for games that behave better with XInput devices.

## Connecting the Pro2

### First connection after flashing

1. Turn off other Pro2 controllers nearby.
2. Make sure the new controller is not connected to a PC, phone, or console.
3. Wake the controller and keep it available for connection.
4. Click `首次连接`.
5. Wait until the Manager shows `手柄连接完成`.

The firmware stores the successful BLE address in NVS and enables automatic reconnect.

### Reconnect a saved controller

Wake the same controller. Firmware normally reconnects automatically after sleep or a temporary disconnect. Click `重连已配对` only when you want to request it immediately.

### Replace the controller

1. Turn off the old controller.
2. Wake the new controller.
3. Click `更换手柄` and confirm.
4. The Manager disconnects the old target, clears its saved address, scans, connects, and stores the new address.

If automatic selection cannot finish, open `高级连接工具`, scan, select the correct result, and click `连接所选目标`.

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
