# PRO2 无线接收器控制板 V6.2.18 VIIPER Windows 版源码

这是不需要 ESP32 开发板的 V6.2.18 Windows-only 路线源码分支。程序通过 Windows BLE 直接连接真实 Pro2，再用 VIIPER/usbip-win2 创建虚拟 USB 手柄。

## 版本定位

- **硬件要求**：Windows 电脑 + 可用 BLE 适配器 + Pro2 手柄。
- **不需要**：ESP32-S3 开发板、CH343P 串口、外接桥接板。
- **四种虚拟模式**：新和联胜 / PS5、PS5 Edge / 背键、Pro2 / Nintendo、Xbox / XInput。
- **PS5 输出层 IMU**：`Accel=X×2/Y×2/Z×-2`，`Gyro` 三轴取反，并保留 0.1x~4.0x 可调倍率。
- **DualSense Edge**：独立模式，不替换普通 PS5。VIIPER device type 为 `dualsenseedge`，USB 身份为 `054C:0DF2 / DualSense Edge Wireless Controller`，Pro2 背键映射到 Edge `L4/R4`。

## 目录

- `windows/v60_viiper_app`：V6.2.18 WPF 管理器和四模虚拟 USB 桥接主程序。
- `tools/viiper/haptic-v0.8.0`：随包 VIIPER haptic server 运行时及许可证。
- `tools/usbip-win2/v0.9.7.7`：随包 usbip-win2 安装器及许可证。
- `tools/tests/v60_*`：V6 packet、BLE replay、UI 和 haptic 端到端测试。
- `release/v6.2.18`：当前正式版本说明与校验文件。

## 构建

```powershell
dotnet build windows\v60_viiper_app\Y700Switch2V60Viiper.csproj -c Release
dotnet run --project tools\tests\v60_packet_mapper_test\V60PacketMapperTest.csproj -c Release
```

发布单文件 EXE：

```powershell
powershell -ExecutionPolicy Bypass -File tools\package_v6_2_18_dualsense_edge_release.ps1 -SkipDotnetInstall
```

正式 EXE 应上传 GitHub Releases，不建议直接提交到源码分支。

## 许可证

本项目主体保留根目录 `LICENSE`。VIIPER 与 usbip-win2 的许可证见 `THIRD_PARTY_NOTICES.md` 和各自目录下的 `LICENSE.txt`。
