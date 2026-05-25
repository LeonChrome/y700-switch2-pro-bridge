# Stable checkpoint: Y700 Switch 2 Pro v3 input + rumble

Date: 2026-05-25

This is the current known-good checkpoint before discussing a Windows exe route.

## User-confirmed result

The live Y700 bridge is confirmed working:

```text
Controller buttons: accurate
Rumble/vibration: feedback present
```

## Current runtime

ADB serial used during stabilization, redacted for public release:

```text
<wireless-adb-ip>:<port>
<wireless-adb-mdns-service>
```

USB gadget state:

```text
/sys/class/udc/a600000.dwc3/state = configured
```

Running Android processes:

```text
Switch2BleBridgeV3 --address 38:C6:CE:27:FC:2D
Switch2FfsResponderV3 /dev/usb-ffs/switch2 /dev/hidg0
```

Deployed jar hashes:

```text
d5146215966668dcb74c0ece5111a78a  switch2_ble_bridge_v3.jar
9403c3f992ad6d237fb302b6755d6c27  switch2_ffs_responder_v3.jar
```

## Frozen files

The stable artifact bundle is:

```text
release/v3-stable-20260525-input-rumble
```

Runtime logs were pulled to:

```text
logs/stable_v3_20260525_195340
```

## Key fix in this checkpoint

`Switch2BleBridgeV3` now parses both input notification formats:

```text
ab7de9be-89fe-49ad-828f-118f09df7fd2
7492866c-ec3e-4619-8258-32755ffcc0f8
```

`ab7...fd2` is parsed as the newer 32-bit button field. `749...cc0f8` is parsed as the legacy byte2/byte3/byte4 input stream. Both write to:

```text
/data/local/tmp/switch2_state.txt
```

This prevents v3 from losing live button updates when `ab7...fd2` only emits initial snapshots.

## Evidence

Button forwarding log includes:

```text
A B X Y
DUp DDown DLeft DRight
L ZL R ZR
Minus Plus
Home Capture
C GL GR
```

Rumble forwarding log includes:

```text
HID OUT 64 bytes
HD HID rumble event start
HD rumble bridge hid-out-hd wrote hdstream
BLE write uuid=cc483f51-9258-427d-a939-630c31f72b05
```

## Preserve this state

Do not replace the v3 jars or restart the USB gadget unless intentionally testing a new build. If wireless ADB drops, rediscover the new port first instead of rebooting Y700.
