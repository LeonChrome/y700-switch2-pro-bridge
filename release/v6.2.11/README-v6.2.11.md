# V6.2.11 新和联胜 VIIPER 版本

Windows-only VIIPER route for direct Pro2 BLE input and three virtual modes:

- 新和联胜 / PS5
- Pro2 / Nintendo
- Xbox / XInput

## Changes

- Corrects PS5 mode gyro signs based on live V6.2.10 testing.
- Keeps the user-confirmed roll/Z direction unchanged.
- Flips the other two PS5 gyro axes by changing the DualSense target mapping
  from `-Y, Z, -X` to `Y, -Z, -X`.
- Keeps Pro2/Nintendo mode gyro mapping unchanged.
- Keeps manual X/Y/Z gyro inversion switches available as emergency/user
  calibration controls.

## Why This Exists

V6.2.10 correctly split PS5 and Pro2 gyro paths, but live testing showed the PS5
target still had two reversed directions while Pro2 mode was normal. This release
does not touch the Pro2 path; it only fixes the PS5 target signs.

## Verification

- `v60_packet_mapper_test: passed`
- `v60_fd2_replay --synthetic: parse_failures=0`
- `dotnet build` with isolated V6.2.11 output: 0 warnings, 0 errors
- Published EXE reports `6.2.11-ps5-gyro-sign-fix`
