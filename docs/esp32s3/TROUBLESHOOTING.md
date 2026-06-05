# ESP32-S3 Troubleshooting

Status: PENDING_HARDWARE_TEST.

## idf.py Not Found

Open an ESP-IDF PowerShell or pass `-IdfPath <path-to-esp-idf>` to the scripts.

## ESP-IDF Environment Variables Not Loaded

Run ESP-IDF `export.ps1`, then retry `idf.py --version`.

## COM Port Not Found

Connect the CH343P Type-C port. Run:

```powershell
.\tools\esp32s3\detect_ports.ps1
```

## CH343 Driver Problem

Install or update the CH343/CH34x driver, then reconnect the CH343P Type-C cable.

## Flash Failed

Check the selected COM port, cable, boot mode, ESP-IDF target, and permissions.

If `Invalid head of packet` or serial noise/corruption appears, retry at a lower baud rate:

```powershell
.\tools\esp32s3\flash.ps1 -Port COM12 -Baud 115200
```

If the stub still reports serial noise/corruption, use the no-stub recovery path:

```powershell
.\tools\esp32s3\flash.ps1 -Port COM12 -Baud 115200 -NoStub
```

## Monitor Shows No Logs

Confirm you are connected to CH343P Type-C, not the native USB HID port.

## Windows Does Not Recognize HID

Confirm the native ESP32-S3 USB & OTG Type-C cable is connected. Replug after changing mode.

## joy.cpl Does Not Show Device

Test Generic HID mode first. Do not debug Nintendo experimental mode until Generic mode enumerates.

## Steam Only Shows If_Hid

Record logs, VID/PID, product string, manufacturer string, and descriptor mode. Do not claim Nintendo path success.

## Mode Switch Does Not Change Device

USB descriptors are read during enumeration. Unplug and replug the native USB & OTG Type-C port.

## Native USB Replug Needed

Expected after switching between Generic HID and Nintendo experimental identity.

## Generic Works But Nintendo Does Not

Return to Generic mode and keep the failing Nintendo descriptor/logs for analysis.

## BLE Scan Cannot Find Controller

Put the controller into pairing/connect mode. Confirm BLE antenna/stack is working.

## BLE Connect Has No Notify

Record service discovery output and subscribed UUIDs. Compare against Y700 logs.

## Logs Make Manager Slow

Use filters, clear logs, or save to file. Later versions can add log backpressure.

## Steam Log Folder Not Found

Steam may be installed outside `Program Files (x86)`. Open the folder manually and update manager settings later.
