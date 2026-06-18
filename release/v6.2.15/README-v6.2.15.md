# V6.2.15 新和联胜 VIIPER 版本

这是 V6.2.15 的小步校正版，只做一个调整：PS5 模式默认陀螺仪 / IMU 映射里，把 pitch/roll 对应关系对换。

## 主要更新

- PS5 模式默认 `PS5 IMU Map` 改为：
  `G=-X,+Z,-Y / A=-X,+Z,-Y`
- 这个默认值是在 V6.2.14 的 SDL/Nintendo 基线上互换 PS5 report-space 的 pitch/roll 输出轴。
- gyro 和 accel 成对互换，避免只改 gyro 导致静置融合不一致。
- V6.2.14 的默认映射仍保留在下拉框里，名称为：
  `V6.2.14 回退 SDL/Nintendo 基线`
- Pro2 / Nintendo 模式、Xbox / XInput 模式、BLE、VIIPER、USBIP、音频保护和自动重连逻辑都不改。

## 使用建议

- 如果你之前用默认 PS5 陀螺仪时感觉 pitch/roll 对应不对，直接用 V6.2.15 默认即可。
- 如果现场觉得 V6.2.14 更顺，可以在 `PS5 IMU Map` 里手动切回 `V6.2.14 回退 SDL/Nintendo 基线`。

## 校验

- `dotnet build windows/v60_viiper_app/Y700Switch2V60Viiper.csproj -c Release`
- `dotnet run --project tools/tests/v60_packet_mapper_test/V60PacketMapperTest.csproj -c Release`
- `dotnet run --project tools/tests/v60_fd2_replay/V60Fd2Replay.csproj -c Release -- --synthetic --quiet`
