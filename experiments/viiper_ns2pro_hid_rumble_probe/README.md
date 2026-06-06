# VIIPER ns2pro HID Rumble Probe

This V5.2 Phase 2 probe turns the manual non-zero rumble check into a repeatable
one-command validation.

It does not touch V5.1, the ESP32-S3 firmware, the Manager GUI, or real Pro2
forwarding.

## Run

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_hid_rumble_probe\run_viiper_ns2pro_hid_rumble_probe.ps1
```

The script:

1. Starts the VIIPER `ns2pro` monitor with `-ExitOnNonZero`.
2. Waits for the virtual `VID_057E&PID_2069&MI_00` HID interface to attach.
3. Sends a Switch-style `0x02` HID output rumble stream through
   `.\tools\Send-HidHapticProbe.ps1`.
4. Parses the VIIPER monitor log for non-zero `LeftRumble[16]` and
   `RightRumble[16]`.

Expected success:

```text
[NS2PRO_HID_RUMBLE_PROBE] output_feedback=true
[NS2PRO_HID_RUMBLE_PROBE] nonzero=true
[NS2PRO_HID_RUMBLE_PROBE] result=passed
```
