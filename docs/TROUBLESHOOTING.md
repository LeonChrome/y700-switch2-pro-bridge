# Troubleshooting

## Flashing Says COM Port Is Busy

This happens before firmware writing starts. It means Windows did not allow esptool to open the CH343P COM port.

Try:

1. Close old Manager windows.
2. Close serial monitors, ESP-IDF monitor, PowerShell `send_command` / `monitor`, Arduino Serial Monitor, PuTTY, or similar tools.
3. Unplug the CH343P control cable for 3-5 seconds.
4. Plug it back in.
5. Refresh serial in the Manager and retry.

V5.9 avoids repeated esptool retries when this happens, so it should fail quickly instead of freezing the UI.

## Manager Freezes When Both USB Cables Are Connected

Use the final V5.9 EXE. The flashing flow was moved off the UI thread and CH343 preflight has a timeout. If the UI still stalls, unplug the CH343P control port, wait a few seconds, and reconnect.

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

## Controller Sleeps And Does Not Reconnect

Keep `ble_auto` enabled. V5.9 firmware schedules background reconnect attempts after BLE disconnect or failed connection. The Manager is not required for every reconnect.
