# V6.2.10 新和联胜 VIIPER 版本

Windows-only VIIPER route for direct Pro2 BLE input and three virtual modes:

- 新和联胜 / PS5
- Pro2 / Nintendo
- Xbox / XInput

## Changes

- Fixes PS5 mode gyro coordinate mapping independently from Pro2 mode.
- Converts Pro2/Nintendo raw gyro into DualSense-style raw gyro as `-Y, Z, -X`.
- Keeps Pro2/Nintendo mode gyro mapping unchanged, because user reports and packet tests show Pro2 mode was already aligned.
- Keeps manual X/Y/Z gyro inversion switches available as emergency/user calibration controls.
- Retains V6.2.9 VIIPER startup diagnostics, port preflight, and automatic port fallback.

## Why This Exists

Mature controller stacks do not treat Nintendo Pro Controller IMU axes and
DualSense IMU axes as the same target coordinate system. The earlier V6.2.6 to
V6.2.9 line used the same default gyro mapping for PS5 and Pro2 virtual outputs,
which can explain reports where Pro2 mode feels correct but PS5 mode has two
axes reversed or swapped.

This release moves that correction into the PS5 target mapping instead of asking
users to globally flip axes.

## Verification

- `v60_packet_mapper_test: passed`
- `v60_fd2_replay --synthetic: parse_failures=0`
- `dotnet build` with isolated V6.2.10 output: 0 warnings, 0 errors
- Published EXE reports `6.2.10-ps5-gyro-coordinate-fix`

## References

- SDL DualSense HIDAPI keeps DualSense gyro axes direct at sensor send level.
- SDL Switch HIDAPI converts Nintendo raw gyro to standardized axes as
  `-Y, Z, -X`.
- Linux `hid-playstation` maps DualSense gyro report axes directly to motion
  ABS axes.
- Linux `hid-nintendo` documents Nintendo controller IMU orientation separately.
