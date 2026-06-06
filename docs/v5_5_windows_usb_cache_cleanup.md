# V5.5 Windows USB Cache Cleanup

Date: 2026-06-06

Windows can keep old `VID_054C&PID_0CE6` device nodes after flashing different
experimental profiles. Cleaning them can reduce confusing diagnostics, but it
is only an aid. A live `V55PHASE3` device with `Status=Error` still means the
descriptor or class configuration needs to be fixed.

## Device Manager

1. Open Device Manager.
2. Choose View -> Show hidden devices.
3. Search under USB devices, HID devices, game controllers, and audio devices.
4. Uninstall old `VID_054C&PID_0CE6` entries such as `V55PHASE1`,
   `V55PHASE2`, and failed `V55PHASE3`.
5. Replug the ESP32-S3 after flashing the desired profile.

## PowerShell Aid

List matching device nodes:

```powershell
Get-PnpDevice | Where-Object {
    $_.InstanceId -match "VID_054C&PID_0CE6"
} | Format-Table Status,Class,FriendlyName,InstanceId -AutoSize
```

The faster Phase 3 composite checker scans present devices by default. Use the
slower stale-cache mode only when needed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1 -IncludeStale
```

`pnputil` can remove devices, but use it carefully and only for the matching
experimental VID/PID:

```powershell
pnputil /enum-devices /instanceid "USB\VID_054C&PID_0CE6*"
```

Do not remove unrelated Sony, Nintendo, Steam, Bluetooth, or USB devices.

## Preferred Debug Flow

1. Flash `hid_only`.
2. Confirm HID input and report rate.
3. Clean stale cache entries if the device list is confusing.
4. Flash `hid_audio_uac1_2ch`.
5. If UAC1 2ch is healthy, flash `hid_audio_uac2_2ch`.
6. If UAC2 2ch is healthy, flash `hid_audio_uac2_4ch`.
7. Run `.\tools\check_v5_5_usb_composite.ps1` and
   `.\tools\check_v5_5_dualsense_audio.ps1`.

Cache cleanup cannot fix a malformed descriptor; it only makes the Windows
device list easier to read.
