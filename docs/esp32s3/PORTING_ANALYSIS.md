# ESP32-S3 Porting Analysis

Status: PENDING_HARDWARE_TEST.

This analysis is based on the existing Y700 stable route and source code. No ESP32-S3 hardware result is claimed.

## Extracted From Y700 Stable Route

- VID/PID: `057e:2069`
- Manufacturer string: `Nintendo Co., Ltd.`
- Product string: `Nintendo Switch Pro Controller`
- USB gadget path on Y700: `/config/usb_gadget/g1`
- UDC on Y700: `a600000.dwc3`
- HID node on Y700: `/dev/hidg0`
- FunctionFS path on Y700: `/dev/usb-ffs/switch2`
- HID report length: `64`
- HID descriptor: vendor/raw HID
- Input report: report ID `0x09` + 63 payload bytes
- Output report: report ID `0x02` + 63 payload bytes
- Switch 2 Pro full input report starts with `0x09`
- HID OUT / rumble reports are 64 bytes and commonly start with `0x02`

Y700 v3 HID descriptor bytes:

```text
06 00 ff 09 01 a1 01 15 00 26 ff 00 75 08 85 09
95 3f 09 01 81 02 85 02 95 3f 09 01 91 02 c0
```

## Button Mapping Of Interest

The Y700 bridge keeps BLE notify button groups and maps them into the wired Switch 2 state packet.

Legacy BLE notify bytes:

```text
byte2: B A Y X R ZR Plus RStick
byte3: DDown DRight DLeft DUp L ZL Minus LStick
byte4: Home Capture GR GL C
```

USB state mapping in `Switch2FfsResponderV3`:

```text
USB data[5] = Y X B A R ZR
USB data[6] = Minus Plus RStick LStick Home Capture C
USB data[7] = DDown DUp DRight DLeft L ZL
USB data[8] = GR GL
```

Newer `ab7...fd2` 32-bit field maps:

```text
0x00000001 Y
0x00000002 X
0x00000004 B
0x00000008 A
0x00000040 R
0x00000080 ZR
0x00000100 Minus
0x00000200 Plus
0x00000400 RStick
0x00000800 LStick
0x00001000 Home
0x00002000 Capture
0x00004000 C
0x00010000 Down
0x00020000 Up
0x00040000 Right
0x00080000 Left
0x00400000 L
0x00800000 ZL
0x01000000 GR
0x02000000 GL
```

## Steam And joy.cpl Difference

`joy.cpl` can show that Windows sees a generic HID input device. That alone does not prove Steam is using the Nintendo controller path. The Y700 route intentionally uses Nintendo-style identity and a Switch-like state packet so Steam can attempt its Nintendo/Switch handling path.

For ESP32-S3, generic mode should be used first to validate TinyUSB and Windows HID enumeration. Nintendo experimental mode must be tested separately and treated as PENDING_HARDWARE_TEST.

## Windows Launcher Current Responsibilities

The existing `Y700Switch2Launcher.exe`:

- locates `adb.exe`
- selects a Y700 ADB device
- warns if only USB ADB is selected
- checks root through `su -c id`
- pushes `switch2_ble_bridge_v3.jar`
- pushes `switch2_ffs_responder_v3.jar`
- pushes `setup_y700_switch2_proto_v3.sh`
- starts Y700 USB gadget/responder
- starts Y700 BLE bridge
- reads status
- sends `play-hd`
- stops Y700 processes
- pulls runtime logs

## Android/Y700 Bridge Current Responsibilities

The Y700 side:

- scans/connects to the real Switch 2 Pro Controller over BLE
- subscribes to private GATT notifications
- parses `ab7...fd2` and `749...cc0f8`
- writes `/data/local/tmp/switch2_state.txt`
- configures Linux USB Gadget
- exposes HID and FunctionFS endpoints
- maps state file into 64-byte Switch-style reports
- receives HID OUT / rumble reports
- writes BLE rumble commands back to the controller

## Logic That Can Migrate To ESP32-S3

- BLE scan/connect/notify subscription concept
- `ab7...fd2` and `749...cc0f8` parsing
- button and stick state model
- generic gamepad HID reports
- Nintendo experimental 64-byte report layout
- HID OUT handling entry point
- rumble reverse-path planning

## Android/Y700-Specific Logic

- ADB deployment
- Android root checks
- Linux USB Gadget configfs
- FunctionFS responder
- `/dev/hidg0`
- `/data/local/tmp/switch2_state.txt`
- Android Bluetooth GATT APIs
- Y700 UDC `a600000.dwc3`

## Logic For Windows Manager

- COM port discovery
- serial connection and JSON parsing
- mode commands
- start/stop/reboot commands
- build/flash/monitor script launching
- log viewing and filtering
- recognition checklist guidance
- settings persistence

## Logic For ESP32-S3 Firmware

- TinyUSB HID Device
- descriptor selection
- generic/nintendo experimental mode state
- serial command parser
- BLE Central scan/connect/notify skeleton
- state mapping
- report sending
- HID OUT callback and future rumble bridge

## Can Be Completed Before Hardware Arrives

- firmware project skeleton
- initial TinyUSB descriptors
- control protocol shape
- Windows Manager UI and serial client
- PowerShell scripts
- documentation and test plan

## Must Wait For Hardware

- ESP-IDF build against installed local SDK
- flashing
- CH343P serial detection
- USB enumeration
- joy.cpl behavior
- Steam behavior
- BLE scan/connect/notify
- rumble reverse path

## First Minimum Viable Goal

PENDING_HARDWARE_TEST:

1. Flash firmware through CH343P.
2. See boot logs in monitor.
3. Plug native USB & OTG Type-C into Windows.
4. Generic HID mode enumerates.
5. `joy.cpl` shows A button toggling every two seconds.
6. Windows Manager connects to CH343P and `status` returns one JSON line.
