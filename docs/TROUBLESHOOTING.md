# Troubleshooting

## Flashing Says COM Port Is Busy

This happens before firmware writing starts. It means Windows did not allow esptool to open the CH343P COM port.

Try:

1. Close old Manager windows.
2. Close serial monitors, ESP-IDF monitor, PowerShell `send_command` / `monitor`, Arduino Serial Monitor, PuTTY, or similar tools.
3. Unplug the CH343P control cable for 3-5 seconds.
4. Plug it back in.
5. Refresh serial in the Manager and retry.

V5.9.3 refuses to start a second esptool while an older matching process still
exists. `chip_id` also has a 20-second watchdog, so a blocked CH343 driver is
reported instead of leaving the UI waiting indefinitely.

### Windows 26300 And WCH Driver 2.1.2025.7

This exact combination was reproduced blocking both esptool and espflash
inside the CH343 kernel driver. The process could not be terminated until the
CH343 control cable was unplugged.

V5.9.3 reads the active driver before flashing and refuses to launch esptool
for this known-risk combination. Open Device Manager, choose the CH343 port,
use `Update driver` / `Let me pick`, and select Microsoft's `USB Serial Device`
driver. Replug the CH343 control cable and refresh the Manager.

For development machines, the repository also includes:

```powershell
.\tools\diagnostics\switch_ch343_to_usbser.ps1
```

Run it from an elevated PowerShell. It backs up the WCH package before
switching. A physical replug may still be required if the old driver already
has a kernel-stuck process.

## Manager Freezes When Both USB Cables Are Connected

Use the final V5.9.3 EXE. The flashing flow runs off the UI thread and each
esptool command has its own timeout. The Manager deliberately does not probe
COM by calling `SerialPort.Open()` before flashing because a blocked CH343
driver open cannot be cancelled safely. If Windows leaves a process in an
uninterruptible serial open, unplug the CH343P control port, wait a few
seconds, and reconnect it.

## Board Reboots Before Firmware Logs

For ESP32-S3 N16R8 boards, unstable PSRAM settings can prevent `app_main()` from running.

The final V5.9 release uses:

```text
CONFIG_SPIRAM_USE_MEMMAP=y
# CONFIG_SPIRAM_USE_MALLOC is not set
# CONFIG_SPIRAM_MEMTEST is not set
```

If you rebuild from source, keep these defaults unless you are testing a different board profile.

## Windows Shows Code 28 on MI_01

In Pro2 / Nintendo mode, MI_01 is intended for WinUSB binding through Microsoft OS descriptors. V5.9 exposes BOS / MS OS 2.0 descriptor data and bumps the device version to reduce stale Windows cache issues.

If Windows still keeps an old cache, remove the old device from Device Manager, unplug/replug the native USB cable, then check again.

## Steam Does Not Recognize Pro2 Layout

Check:

- VID/PID should be `057E:2069`.
- Product string should be Nintendo Switch Pro style.
- HID input report should use report ID `0x05`.
- Replug the native USB / OTG cable after flashing.
- Restart Steam if it cached the previous mode.

V5.9.3 release profiles lock their compiled USB identity. Old NVS mode values
can no longer make a Pro2 flash enumerate as Xbox or make an Xbox flash
enumerate as Pro2.

## Controller Sleeps And Does Not Reconnect

Keep `ble_auto` enabled. V5.9 firmware schedules background reconnect attempts
after BLE disconnect or failed connection. The Manager and CH343P control port
are not required for every reconnect once the controller address has been saved.
