# V6.2.5 新和联胜 VIIPER Calibrated Motion

V6.2.5 keeps the V6.2 Windows-only VIIPER route and the V6.2.4 Raw Direct stick
path, then fixes the IMU layer.

## What changed

- Pro2 BLE motion input now learns a stationary flat rest window at the parser
  boundary. Gyro zero bias is subtracted and small accelerometer rest offsets
  are normalized before virtual-device mapping.
- DualSense/PS5 motion output now uses explicit report-space mapping:
  `gyro=(x,-y,z)` and `accel=(x,z,-y)`, so a flat Pro2 rest becomes
  DualSense `AccelZ=-8192`.
- NS2Pro motion output now uses explicit Switch2 report-space mapping:
  `gyro=(x,-y,z)` and `accel=(x,-y,z)`.
- Xbox remains motion-free.
- Tray right-click commands now run in the background. The main window is only
  restored by double-clicking the tray icon.

## Verification

- `dotnet run --project tools/tests/v60_packet_mapper_test/V60PacketMapperTest.csproj -c Release`
- `dotnet build windows/v60_viiper_app/Y700Switch2V60Viiper.csproj -c Release`
- `dotnet run --project tools/tests/v60_fd2_replay/V60Fd2Replay.csproj -c Release -- --synthetic --quiet`
- Direct GUI launch/close smoke: process stayed alive and exited cleanly with
  code 0.

The legacy `tools/tests/v60_ui_smoke.ps1` script currently has a PowerShell
parser issue before it reaches the app, so it was not used as the gating UI
test for this release.

## Assets

- Local Chinese filename: `新和联胜VIIPER版本-aio-v6.2.5.exe`
- GitHub ASCII filename: `XinHeLianSheng-VIIPER-aio-v6.2.5.exe`
- SHA256: `4A1C91DA0253A556C6C8242500403D406464EF2FB44E58DCA2A769B6803D39C8`

GitHub release asset names are safer with ASCII. The two EXE files are
byte-for-byte identical.
