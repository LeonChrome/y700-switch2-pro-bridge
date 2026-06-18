# V6.2.13 新和联胜 VIIPER 版本

Windows-only VIIPER route for direct Pro2 BLE input and three virtual modes:

- 新和联胜 / PS5
- Pro2 / Nintendo
- Xbox / XInput

## Changes

- Adds a front-end **PS5 IMU Map** selector.
- Uses the SDL/Nintendo-style IMU mapping as the default baseline:
  - gyro `G=-Y,+Z,-X`
  - accel `A=-Y,+Z,-X`
- Keeps V6.2.12, V6.2.11, V6.2.10, old shared, and Pro2 same-direction mappings as selectable A/B profiles.
- Persists the selected PS5 IMU map in `v6_settings.json`.
- Logs the active map as `ps5_imu_map=g=...;a=...` at startup and during VIIPER telemetry.
- Keeps Pro2/Nintendo mode IMU mapping unchanged.

## Why This Exists

PS5 gyro/accel direction debugging should not require a new EXE for every axis
guess. V6.2.13 moves the PS5 IMU mapping into an explicit front-end selector so
testing can converge by choosing a profile instead of rebuilding.

## Verification

- `v60_packet_mapper_test: passed`
- `v60_fd2_replay --synthetic: parse_failures=0`
- `dotnet build` with isolated V6.2.13 output: 0 warnings, 0 errors
- Published EXE reports `6.2.13-selectable-ps5-imu-map`

## Notes

- The old X/Y/Z gyro reverse switches still exist, but they are now emergency
  fine-tuning controls. Prefer selecting a PS5 IMU Map profile first.
- The selected profile applies on the next virtual-device start.
