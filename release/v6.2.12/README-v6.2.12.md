# V6.2.12 新和联胜 VIIPER 版本

Windows-only VIIPER route for direct Pro2 BLE input and three virtual modes:

- 新和联胜 / PS5
- Pro2 / Nintendo
- Xbox / XInput

## Changes

- Fixes the PS5-mode stillness shake introduced after V6.2.11 gyro sign changes.
- Keeps the V6.2.11 PS5 gyro direction profile: `Y, -Z, -X`.
- Pairs PS5 accel signs with that gyro profile by changing accel from
  `X, Z, -Y` to `-X, -Z, -Y`.
- Keeps the PS5 accel Z/gravity axis unchanged, so the flat-rest baseline is not
  moved to a different axis.
- Keeps Pro2/Nintendo mode IMU mapping unchanged.

## Why This Exists

V6.2.11 fixed PS5 gyro direction signs but left the PS5 accel signs from the old
profile. Host-side IMU fusion can then see gyro and accel disagree while the
controller is still, which appears as XYZ shaking in calibration/test views.

V6.2.12 makes the PS5 gyro and accel signs consistent without touching the Pro2
path.

## Verification

- `v60_packet_mapper_test: passed`
- `v60_fd2_replay --synthetic: parse_failures=0`
- `dotnet build` with isolated V6.2.12 output: 0 warnings, 0 errors
- Published EXE reports `6.2.12-ps5-imu-pairing-fix`
