# V6.2.7 新和联胜 VIIPER Gyro Direction Profiles

V6.2.7 keeps the V6.2.6 smooth latest-held gyro path as the default, and adds a visible `Gyro Dir` selector for field environments where left/right gyro is reversed.

## What Changed

- Added `Gyro Dir` in LINK LAB:
  - `标准方向（推荐）`: the same report-space direction used by V6.2.6.
  - `左右反向修正`: use only when in-game gyro left/right is reversed.
- The correction only flips the horizontal gyro/Yaw sign for PS5 and Pro2/Nintendo virtual devices.
- Accel mapping, vertical gyro axes, stick processing, Xbox/XInput, PS5 HD haptics, and BLE reconnect behavior are unchanged.
- Startup/runtime telemetry now includes `gyro_dir=reference` or `gyro_dir=invert_horizontal`.

## Files

- `新和联胜VIIPER版本-aio-v6.2.7.exe`
- `XinHeLianSheng-VIIPER-aio-v6.2.7.exe`
- `SHA256SUMS-v6.2.7.txt`

## Suggested Use

Use `标准方向（推荐）` first. If a user reports that gyro left/right is reversed in game, switch LINK LAB -> `Gyro Dir` to `左右反向修正`, then restart the selected virtual mode.
