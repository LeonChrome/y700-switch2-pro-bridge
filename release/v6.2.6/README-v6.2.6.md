# V6.2.6 新和联胜 VIIPER Latest-Held Gyro

V6.2.6 keeps the V6.2.5 calibrated motion mapping and fixes the gyro timing
model that made motion feel correct but not smooth.

## What changed

- Default gyro mode is now `hold_latest`.
- BLE still supplies real Pro2 IMU samples at the actual source cadence.
- Every virtual USB report now carries the latest gyro/accel sample until a new
  BLE sample arrives. This is a zero-order hold, not smoothing or fake input.
- The old `source_60hz` behavior cleared gyro/accel on repeated USB frames,
  which could create an alternating motion/zero pattern at 125Hz output.
- Existing saved `source_60hz（推荐）` settings are automatically migrated to the
  new `hold_latest（推荐）` default.
- `source_60hz_zero` remains available only as a diagnostic mode to reproduce
  the older choppy motion path.

## Verification

- `dotnet run --project tools/tests/v60_packet_mapper_test/V60PacketMapperTest.csproj -c Release`
- `dotnet build windows/v60_viiper_app/Y700Switch2V60Viiper.csproj -c Release`
- `dotnet run --project tools/tests/v60_fd2_replay/V60Fd2Replay.csproj -c Release -- --synthetic --quiet`

## Assets

- Local Chinese filename: `新和联胜VIIPER版本-aio-v6.2.6.exe`
- GitHub ASCII filename: `XinHeLianSheng-VIIPER-aio-v6.2.6.exe`
- SHA256: `92423FF3A6C0F7979DF96A1C5E3499491BC2046BDCD6309A2F835074050EE6D0`

GitHub release asset names are safer with ASCII. The two EXE files are
byte-for-byte identical.
