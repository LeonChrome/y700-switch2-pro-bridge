# Switch 2 Pro Y700 Bridge v3 Notes

## Goal

v3 is a separate experimental path for HD rumble forwarding. It does not replace the existing working bridge/responder files.

The intended flow is:

```text
Steam USB HID OUT rumble report
-> Switch2FfsResponderV3 decodes Switch-style rumble frames
-> /data/local/tmp/switch2_ble_write_v3.txt receives hdstream/hdstop commands
-> Switch2BleBridgeV3 streams Pro Controller 2 BLE HD packets to cc483f51-...
-> Real Switch 2 Pro Controller vibrates
```

## New Files

```text
src/Switch2BleBridgeV3.java
src/Switch2FfsResponderV3.java
build_switch2_ble_bridge_v3.ps1
build_switch2_responder_v3.ps1
run_switch2_ble_bridge_v3.ps1
restart_switch2_responder_v3.ps1
set_switch2_haptic_mode_v3.ps1
switch2_ble_bridge_v3.jar
switch2_ffs_responder_v3.jar
```

## Isolation

v3 uses separate remote files:

```text
/data/local/tmp/switch2_ble_bridge_v3.log
/data/local/tmp/switch2_ble_input_raw_v3.log
/data/local/tmp/switch2_button_changes_v3.log
/data/local/tmp/switch2_ffs_responder_v3.log
/data/local/tmp/switch2_hid_output_v3.log
/data/local/tmp/switch2_ble_write_v3.txt
```

The state file remains shared:

```text
/data/local/tmp/switch2_state.txt
```

That is deliberate because the USB responder still reads the live controller state from the BLE bridge.

## Main Protocol Changes

### Input

v3 treats this characteristic as the primary input report:

```text
ab7de9be-89fe-49ad-828f-118f09df7fd2
```

It maps the 32-bit button field into the existing state file layout, including:

```text
C, GL, GR
```

Runtime update on 2026-05-25:

```text
7492866c-ec3e-4619-8258-32755ffcc0f8
```

is also parsed as a legacy Pro2 input stream. On the live Y700, `ab7...fd2`
can produce only initial snapshots, while `749...cc0f8` continues to notify at
runtime. `Switch2BleBridgeV3` now accepts both formats and writes both into the
shared state file, so button and stick forwarding does not stall after init.

For v3 guided button capture, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\capture_switch2_button_map.ps1 -V3 -AdbPath "C:\path\to\adb.exe" -DeviceSerial "<serial>"
```

### Preset

v3 corrects the preset command length to 4 bytes:

```text
0A 91 01 02 00 04 00 00 XX 00 00 00
```

The previous working experiments used length 8. They worked, but the extra bytes were probably ignored by the controller.

### HD Rumble

v3 writes HD rumble to:

```text
cc483f51-9258-427d-a939-630c31f72b05
```

The BLE bridge streams packets every about 20 ms while a recent `hdstream` command is active.

Packet shape:

```text
00 + leftMotorBlock + rightMotorBlock
```

Each motor block:

```text
packetId(0x50..0x5F) + vibration1(5 bytes) + vibration2(5 bytes) + vibration3(5 bytes)
```

v3 currently fills `vibration1` from Steam's rumble frame and keeps `vibration2/3` at zero amplitude.

## Modes

Default mode:

```text
hd
```

Safety modes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode_v3.ps1 -Mode log-only -AdbPath "C:\path\to\adb.exe" -DeviceSerial "<serial>"
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode_v3.ps1 -Mode preset-fallback -AdbPath "C:\path\to\adb.exe" -DeviceSerial "<serial>"
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode_v3.ps1 -Mode hd -AdbPath "C:\path\to\adb.exe" -DeviceSerial "<serial>"
```

`log-only` suppresses active HD writes and only logs what would have been sent.

`preset-fallback` uses the older preset-style rumble bridge from HID OUT instead of HD streaming.

## Suggested Test Sequence

1. Start the USB gadget/responder v3:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\restart_switch2_responder_v3.ps1 -AdbPath "C:\path\to\adb.exe" -DeviceSerial "<serial>"
```

2. Start BLE bridge v3:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_ble_bridge_v3.ps1 -AdbPath "C:\path\to\adb.exe" -DeviceSerial "<serial>" -Background
```

3. After the Pro Controller 2 connects, run a direct BLE HD self-test:

```powershell
adb -s <serial> shell su -c "echo play-hd > /data/local/tmp/switch2_ble_write_v3.txt"
```

4. If self-test vibrates, test Steam/BzzzController rumble.

5. Pull logs if needed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_ble_bridge_v3.ps1 -AdbPath "C:\path\to\adb.exe" -DeviceSerial "<serial>" -PullLogs
adb -s <serial> shell su -c "tail -n 160 /data/local/tmp/switch2_ffs_responder_v3.log"
```

## Rollback

Use the existing non-v3 scripts:

```text
run_switch2_ble_bridge.ps1
restart_switch2_responder.ps1
```

The old jars and source files are untouched.
