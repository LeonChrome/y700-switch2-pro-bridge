# V6.2.16 新和联胜 VIIPER 版本

这是 V6.2.16 的固化版。基于 V6.2.15 的现场测试结果，PS5 模式陀螺仪对应方式已经确认正确，因此本版把相关可选参数收掉，避免用户误选旧映射或反向轴。

## 主要更新

- PS5 模式陀螺仪 / 加速度计映射固定为：
  `G=-X,+Z,-Y / A=-X,+Z,-Y`
- 移除前端里的 `Gyro mode`、`PS5 IMU Map`、`XYZ 反向` 等陀螺仪可选项。
- 启动时会忽略旧配置文件里保存过的旧 IMU Map、旧 Gyro mode、XYZ 反向设置，统一回到固定正确配置。
- Pro2 / Nintendo 模式、Xbox / XInput 模式、BLE、VIIPER、USBIP、音频保护、自动重连逻辑都不改。
- 日志仍会输出固定后的 `ps5_imu_map` 和 `gyro_axis_inv=x0,y0,z0`，方便确认现场运行状态。

## 使用建议

- PS5 模式直接使用默认配置即可，不需要再手动调 IMU Map 或轴反向。
- 如果用户从旧版本升级后曾经改过陀螺仪参数，本版会自动清回正确默认值。
- 仍建议保持 `PS5 音频保护` 开启，避免 DualSense 虚拟音频被 Windows 设成默认播放或麦克风。

## 校验

- `dotnet build windows/v60_viiper_app/Y700Switch2V60Viiper.csproj -c Release`
- `dotnet run --project tools/tests/v60_packet_mapper_test/V60PacketMapperTest.csproj -c Release`
- `dotnet run --project tools/tests/v60_fd2_replay/V60Fd2Replay.csproj -c Release -- --synthetic --quiet`
