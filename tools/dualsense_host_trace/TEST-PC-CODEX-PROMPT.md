# DualSense disconnect capture task

Run this task on the Windows PC where the disconnect occurs.

1. Connect both ESP32-S3 USB cables and close the PRO2 Manager.
2. Open PowerShell in the supplied `DualSenseHostTrace` folder.
3. For a normal game disconnect capture, run:

   `powershell -ExecutionPolicy Bypass -File .\Start-DualSenseHostTrace.ps1 -DurationSeconds 1800`

4. Start Gamepad Tester first, confirm live input, then reproduce the failure in the game.
5. After input is lost, leave the trace running for at least 30 seconds, then press `Enter` in the trace window.
6. Separately, with Steam and games closed, verify the USB output path:

   `powershell -ExecutionPolicy Bypass -File .\Start-DualSenseHostTrace.ps1 -DurationSeconds 30 -RumbleTest`

7. Return both generated ZIP files.

Analyze `host_trace.log` around the first failure:

- `pnp_snapshot` loses all `VID_054C&PID_0CE6`: physical USB reset/removal.
- PnP remains present but `hid_read_timeout` repeats: HID input endpoint or host HID stack stopped.
- `hid_exception` followed by a new `hid_open generation`: Windows closed/recreated the HID device.
- Serial JSON has fresh `ble_notify_age_ms` but stale `hid_report_age_ms`: USB HID path failed while BLE remained healthy.
- Serial JSON has stale BLE notification age first: BLE/input source failed.
- `hid_rumble_write` appears but firmware `hid_output_count` does not increase: host OUT transfer did not reach firmware.
- Firmware receives nonzero motors but `hid_rumble_active_updates` or `hid_rumble_ble_writes` does not increase: firmware rumble conversion bug.
- BLE write counters increase with no physical vibration: Pro2 raw rumble packet/handset behavior bug.

Do not change firmware during this capture. Preserve exact timestamps and return the ZIPs first.
