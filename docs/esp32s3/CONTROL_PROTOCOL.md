# ESP32-S3 Serial Control Protocol

Status: PENDING_HARDWARE_TEST.

First transport: CH343P USB serial.

Future transports may include USB CDC or HID feature reports.

## Rules

- One command per line.
- One JSON reply per line.
- Logs are prefixed with `[LOG]`.
- JSON replies start with `{`.
- Unknown or unimplemented commands return `ok:false`.

## Commands

```text
status
mode generic
mode nintendo
start
stop
reboot
loglevel debug
loglevel info
ble scan
ble connect
ble disconnect
hid test_a
hid neutral
version
```

## Example Replies

```json
{"ok":true,"cmd":"status","mode":"generic","usb":"mounted","ble":"idle","hid":"running","version":"0.1.0"}
{"ok":true,"cmd":"mode","mode":"nintendo","experimental":true,"note":"replug native USB may be required"}
{"ok":false,"cmd":"ble connect","error":"not implemented yet PENDING_HARDWARE_TEST"}
```

## Notes

`mode nintendo` changes firmware state, but USB descriptors are normally read during enumeration. A native USB replug may be required after switching identity mode.

PENDING_HARDWARE_TEST: verify that CH343P RX input reaches the firmware command parser.
