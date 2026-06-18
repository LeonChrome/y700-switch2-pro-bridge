# V6.2.8 新和联胜 VIIPER 版本

Windows-only VIIPER route for direct Pro2 BLE input and three virtual modes:

- 新和联胜 / PS5
- Pro2 / Nintendo
- Xbox / XInput

## Changes

- Main lobby cards no longer scale during hover or selected-state changes, keeping the UI visually stable while modes or drivers are running.
- Push Hz, Gyro, Stick, and Backend dropdowns use a dark in-app template so option text stays visible across Windows themes.
- The old Gyro Dir dropdown is replaced by independent `X 反向`, `Y 反向`, and `Z 反向` switches.
- Old `左右反向` settings migrate to `Y 反向` automatically.
- Runtime logs now report `gyro_axis_inv=x0,y0,z0`.

## Notes

- This release does not change VIIPER / USBIP startup behavior. A VIIPER `exit=1` immediately after `Starting VIIPER USB-IP server addr=127.0.0.1:3241` is a pre-BLE startup failure, usually caused by USBIP driver readiness, port/process conflict, permissions, or a missing reboot after driver installation.
- `3241` is the USB-IP server port; `3242` is the VIIPER API port.
