# Steam Recognition Test Plan

Status: PENDING_HARDWARE_TEST.

No ESP32-S3 USB enumeration or Steam result is claimed yet.

## Device Manager

1. Plug CH343P Type-C only for flashing/logging.
2. Plug native ESP32-S3 USB & OTG Type-C for HID tests.
3. Open Device Manager.
4. Record device class.
5. Record VID/PID.
6. Record product string.
7. Record manufacturer string.

## joy.cpl

1. Press `Win+R`.
2. Run `joy.cpl`.
3. In Generic HID mode, check whether a game controller appears.
4. Open Properties.
5. Watch whether the A button toggles every two seconds.

## Steam Controller Settings

1. Start Steam.
2. Open Controller Settings.
3. Record whether the device appears.
4. Record whether it appears as Generic, Nintendo, Switch, Switch Pro, or `If_Hid`.
5. Do not claim success unless real behavior is observed.

## Steam Logs

Search Steam logs for:

```text
If_Hid
Nintendo
Switch
Switch Pro
Pro Controller
057e
2069
HID
```

If `If_Hid` appears, record:

- full log lines
- VID/PID
- interface number
- product string
- manufacturer string
- report descriptor mode
- generic vs Nintendo experimental mode

## Rollback

If Generic HID mode works but Nintendo experimental mode fails:

1. Switch back with `mode generic`.
2. Unplug/replug native USB & OTG Type-C.
3. Keep the failing descriptor and logs for analysis.
4. Do not keep randomly changing VID/PID without recording each attempt.
