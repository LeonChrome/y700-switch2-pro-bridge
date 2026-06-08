# PRO2 手柄无线接收器控制板

当前主线是 **V5.8 三模普通震动版本**：用 ESP32-S3 开发板接收真实 Switch 2 Pro Controller 的 BLE 输入，再通过原生 USB 在 Windows / Steam 上切换成 Pro2 / Nintendo、Xbox / XInput 或 DualSense-like 三种手柄身份。

## 下载

推荐直接使用 GitHub Release 里的 All-in-one EXE：

[下载 V5.8.3 Manager](https://github.com/LeonChrome/y700-switch2-pro-bridge/releases/tag/v5.8.3)

EXE 内置固件、esptool、刷写流程、BLE 控制和常用测试工具。普通用户不需要手动安装 ESP-IDF，也不需要从仓库目录里挑固件文件。

## V5.8 功能

- 三模切换：Pro2 / Nintendo、Xbox / XInput、DualSense-like。
- 真实 Pro2 BLE 输入：按键、方向键、摇杆、扳机、C、GL、GR、Home、Capture。
- 摇杆满量程修正：解决主机侧只能推到约 80% 的问题。
- 摇杆中心吸附：静止附近统一压回协议中心，减少 tester 里轻微偏移。
- 普通震动：三种 USB 身份都走 normalized rumble，再回传到真实 Pro2。
- BLE 管理：扫描、列表、连接目标、断开、重连上次、自动重连开关。
- 状态检查：串口、USB 身份、BLE 状态、输入状态、震动状态。
- 离线可用：没插 ESP32 时界面不会卡死，串口刷新不会主动打开 COM 口。
- 安全切换：刷写或切换进行中会阻止重复点击，避免并发 esptool 冲突。

## 三种模式

| 模式 | USB 身份 | 适合场景 |
| --- | --- | --- |
| Pro2 / Nintendo | Switch Pro / Pro2 风格 HID | 日常使用、Steam、Pro2 普通震动 |
| Xbox / XInput | Xbox 360 / XInput 风格 | 对 XInput 兼容更好的游戏，例如部分射击游戏 |
| DualSense-like | DualSense 风格 HID + 控制器音频实验端点 | DualSense-like 兼容性测试、普通震动验证 |

V5.8 只承诺普通震动，不承诺原生 DualSense HD haptic 或 Nintendo HD Rumble 2 的完整复刻。DualSense-like 的音频链路保留为实验入口。

## 使用方式

1. 下载并打开 V5.8 Manager EXE。
2. 用 CH343P / WCH Type-C 口连接 ESP32-S3 控制板。
3. 在 Manager 中确认 COM 口。
4. 点击三模切换台上的目标手柄卡片，刷入对应固件。
5. 刷写完成后，重新插拔 ESP32-S3 原生 USB / OTG 口。
6. 在 Manager 中执行 USB 检查，确认 Windows 识别到了目标模式。
7. 连接真实 Pro2 的 BLE，再进入游戏或 tester 测试输入与普通震动。

## 硬件

当前实测硬件：

- ESP32-S3-N16R8
- 16MB flash
- 8MB PSRAM
- CH343P / WCH 串口用于刷写、日志和控制
- ESP32-S3 native USB / OTG 用于输出 USB 手柄身份

如果你的板子启动异常或反复 watchdog reset，优先确认 flash / PSRAM 配置、USB 线、供电和实际板型是否与 release 固件匹配。

## 开发命令

从仓库根目录执行：

```powershell
.\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
.\tools\package_v5_8_manager.ps1 -SkipFirmwareBuild
```

打包后的 EXE 位于：

```text
.\release\v5.8\
```

## English

This project turns an ESP32-S3 board into a wireless receiver for the real Switch 2 Pro Controller. V5.8 is the current mainline release and focuses on three ordinary-rumble USB modes: Pro2 / Nintendo, Xbox / XInput, and DualSense-like.

Download the latest All-in-one Manager from [GitHub Releases](https://github.com/LeonChrome/y700-switch2-pro-bridge/releases/tag/v5.8.3). It bundles the firmware, flasher, BLE controls, USB checks, and test tools.

V5.8 supports real Pro2 BLE input, full-stick scaling, neutral center snapping, mode switching, ordinary rumble forwarding, and offline-safe Manager behavior. It does not claim full native DualSense HD haptics or Nintendo HD Rumble 2 reproduction.

## License

Apache License 2.0. This is an independent experimental project and is not affiliated with Nintendo, Sony, Microsoft, Valve, Espressif, or related companies.
