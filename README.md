# PRO2 无线接收器控制板 V5.9 ESP32-S3 源码

这是需要 ESP32-S3 开发板的 V5.9 系列源码分支。它保留三模固件、Windows 刷机/连接管理器和诊断工具，不包含 V6.2.16 的纯 Windows VIIPER 程序。

## 版本定位

- **硬件路线**：ESP32-S3 N16R8 + CH343P 控制串口 + ESP32-S3 native USB/OTG。
- **三种 USB 身份**：新和联胜 / PS5、Pro2 / Nintendo、Xbox / XInput。
- **核心目标**：PS5 HD 震动与普通震动调度、Pro2 BLE 稳定连接、刷机/首次配对/重连流程。

## 目录

- `firmware/esp32s3_dualsense_identity_experiment`：新和联胜 / PS5 身份、UAC1 四声道 HD 震动、普通震动仲裁。
- `firmware/esp32s3_switch2_bridge`：Pro2 / Nintendo、Xbox / XInput 和 ESP32 BLE 桥接固件。
- `windows/v55_manager_app`：V5.9 Windows 管理器，负责刷机、USB 身份检查、BLE 指令和诊断。
- `tools/esp32s3`：ESP-IDF 构建、刷写、擦除和串口工具。
- `tools/tests`：V5.9 固件/映射/刷机相关测试。
- `docs`：中文使用说明、故障排查和开发记录。

## 构建

```powershell
# Windows Manager
dotnet build windows\v55_manager_app\Y700Switch2V55Manager.csproj -c Release

# ESP32 固件需要 ESP-IDF 环境，按 docs/DEVELOPMENT_ENVIRONMENT.md 配置后运行：
powershell -ExecutionPolicy Bypass -File tools\esp32s3\build.ps1
```

## 发布原则

源码分支只保存必要源码、脚本、许可证和少量内置资源。正式 EXE 和历史大包请放 GitHub Releases，避免仓库继续堆旧库存。

## 许可证

本项目主体保留根目录 `LICENSE`。第三方工具和运行时的许可证见 `THIRD_PARTY_NOTICES.md`。