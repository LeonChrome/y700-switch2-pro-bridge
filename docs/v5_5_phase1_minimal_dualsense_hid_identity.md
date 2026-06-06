# V5.5 Phase 1 Minimal DualSense HID Identity

Date: 2026-06-06

## 1. Goal

Phase 1 provides a standalone ESP32-S3 firmware that exposes a minimal
DualSense-like USB HID identity:

```text
VID=0x054c
PID=0x0ce6
manufacturer=Sony Interactive Entertainment
product=DualSense Wireless Controller
input=report 0x01 with 63 data bytes
output=report 0x02 with 47 data bytes
```

It sends a neutral input report every 4 ms and logs output reports received
from the PC.

This is an isolated experiment:

```text
firmware/esp32s3_dualsense_identity_experiment/
```

It does not change or replace `firmware/esp32s3_switch2_bridge/`. The V5.2
Pure Pro2 / VIIPER path remains the default repository firmware route.

## 2. Not In Phase 1

Phase 1 does not implement:

- USB Audio,
- haptic audio,
- Pro2 BLE input,
- Pro2 raw02 forwarding,
- real DualSense Bluetooth forwarding,
- complete DualSense feature/calibration reports,
- touchpad or motion input.

Feature reports `0x05`, `0x08`, `0x09`, and `0x20` are declared and return
fixed zero-filled placeholders. This may be enough for enumeration but is not
a complete DualSense contract.

## 3. Build

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

Verified build result:

```text
ESP-IDF=5.3.3
target=esp32s3
result=passed
binary=esp32s3_dualsense_identity_experiment.bin
binary_size=0x39a50
```

## 4. Flash

Connect the CH343P programming/control port and replace `COM12` with the port
on the current machine:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf -Monitor
```

After flashing, reconnect the ESP32-S3 native USB/OTG port. Expected log
markers:

```text
[DS5_IDENTITY] enabled=true
[DS5_USB] mounted=true
[DS5_REPORT] sent=true
[DS5_OUTPUT] report_id=... len=...
```

Flashing this experiment replaces the firmware currently on the board. Return
to the normal bridge by reflashing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

## 5. Windows Enumeration

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_identity.ps1
```

Expected after successful enumeration:

```text
[V5_5_DS5_IDENTITY] hid_found=true
[V5_5_DS5_IDENTITY] vid=054C
[V5_5_DS5_IDENTITY] pid=0CE6
[V5_5_DS5_IDENTITY] product=DualSense Wireless Controller
[V5_5_DS5_IDENTITY] likely_dualsense=true
```

Hardware validation completed on 2026-06-06:

```text
hid_found=true
vid=054C
pid=0CE6
product=HID-compliant game controller
likely_dualsense=true
report_id=0x01
len=63
rate=250Hz
usb_disconnect=false
phase1_status=passed
```

Windows used a generic localized HID product label, but the VID/PID,
63-byte input payload, 250 Hz cadence, and stable USB connection all matched
the Phase 1 contract.

Before flashing, no matching device is a valid blocked result with exit code
zero:

```text
hid_found=false
likely_dualsense=false
result=blocked_no_dualsense_identity
```

## 6. Manual Validation

Windows:

1. Open Device Manager and inspect Human Interface Devices.
2. Run `joy.cpl` and check for a controller entry.
3. Open its properties and confirm the four stick axes remain centered,
   triggers remain released, and buttons remain released.
4. Leave the device connected for at least five minutes and check that it does
   not repeatedly disconnect/re-enumerate.

Steam:

1. Start the experiment before opening Steam.
2. Open Steam Settings, Controller.
3. Check whether Steam displays a PlayStation/DualSense controller.
4. Open Controller Test.
5. Any host output should produce a `[DS5_OUTPUT]` firmware log even though
   Phase 1 does not process it.

The standalone firmware uses the same CH343P/native-USB two-port board layout
as the normal bridge: CH343P for flash/logs, native USB/OTG for the PC-facing
controller.

## 7. Success Criteria

Passed:

- firmware build succeeds,
- Windows enumerates VID `054c`, PID `0ce6`,
- HID remains connected,
- neutral report `0x01` is observed,
- HID tools or `joy.cpl` show stable neutral input,
- output report callbacks log host traffic,
- Steam recognizes it, when Steam accepts the minimal feature-report set.

Partial:

- Windows HID enumeration works but Steam does not classify it as DualSense.

Failed:

- Device Manager reports a descriptor error,
- the device repeatedly disconnects,
- no HID interface appears,
- input report submission continually fails.

## 8. Failure Triage

Descriptor problem:

- Check configuration total length and endpoint addresses.
- Confirm the report descriptor length matches the HID descriptor.
- Confirm one HID interface has both interrupt IN and OUT endpoints.

Report descriptor problem:

- Confirm report `0x01` has exactly 63 data bytes.
- Confirm report `0x02` has 47 data bytes.
- Confirm the neutral hat uses null value `8`.

VID/PID problem:

- Clear stale Windows device instances if the old descriptor is cached.
- Reconnect the native USB port after flashing.

Feature-report problem:

- Capture which report IDs Windows/Steam requests.
- Replace zero-filled placeholders with structured calibration/capability data
  in a later Phase 1 compatibility pass.

TinyUSB problem:

- Check `[DS5_USB] mounted=true`.
- Verify `CONFIG_TINYUSB_HID_COUNT=1`.
- Ensure only the experiment firmware owns the native USB port.

## 9. Next Phase

Phase 1 is closed as passed. Phase 2 maps real Pro2 BLE input into DualSense
input report `0x01`:

```text
Pro2 buttons/sticks/triggers/gyro/accel
-> DualSenseInputReportBuilder
-> PC-facing DualSense report 0x01
```

Audio and haptic translation remain deferred until Phase 4 and Phase 5.

See `docs/v5_5_phase2_pro2_to_dualsense_input_mapping.md`.
