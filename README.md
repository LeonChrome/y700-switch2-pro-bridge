# Y700 / ESP32-S3 Switch 2 Pro Bridge

## 项目简介 / Overview

这是一个面向 Switch 2 Pro Controller 的低成本、低延迟 BLE-to-USB 硬件桥接项目。当前主线是 ESP32-S3 接收器：真实 Switch 2 Pro Controller 通过 BLE 连接 ESP32-S3，开发板再通过原生 USB 向 Windows / Steam 暴露 Nintendo Switch Pro / Pro2 风格的 USB HID 控制器。

This is a low-cost, low-latency BLE-to-USB hardware bridge for the Switch 2 Pro Controller. The current mainline is the ESP32-S3 receiver: the real controller connects to the ESP32-S3 over BLE, and the board exposes a Nintendo Switch Pro / Pro2-style USB HID controller to Windows / Steam.

早期的 Lenovo Y700 Android USB Gadget 方案仍保留在仓库中，作为历史稳定路线和研究资料。新用户建议直接使用 ESP32-S3 路线。

The older Lenovo Y700 Android USB Gadget route remains in the repository as historical stable/research material. New users should start with the ESP32-S3 path.

## 当前正式版 / Current Release

| 路线 | 状态 | 推荐用户 | 说明 |
| --- | --- | --- | --- |
| V5.0.0 ESP32-S3 Pro2 Bridge | 正式版 / Stable | Windows / Steam 普通用户 | All-in-one Manager EXE、内置 V5 固件、BLE 输入、接近 raw 的陀螺仪、震动桥接、USB `0x05` full report |
| Y700 Android USB Gadget route | 历史方案 / Legacy | 研究或旧 Y700 用户 | 保留为参考资料；当前主线已经转向 ESP32-S3 |

| Track | Status | Recommended for | Notes |
| --- | --- | --- | --- |
| V5.0.0 ESP32-S3 Pro2 Bridge | Stable | Normal Windows / Steam users | All-in-one Manager EXE, bundled V5 firmware, BLE input, raw-like gyro, rumble bridge, USB `0x05` full report |
| Y700 Android USB Gadget route | Legacy | Research / previous Y700 users | Kept for reference; the mainline has moved to ESP32-S3 |

## V5 核心特性 / V5 Highlights

中文：

- Windows / Steam 走 Nintendo Switch Pro / Pro2 风格识别路径，USB input report ID 使用 `0x05`。
- Switch 2 Pro BLE 输入使用 FD2 full report；在已测试硬件和环境中，BLE input 约为 `133 Hz` 级别。
- 陀螺仪使用 Pro2 BLE FD2 motion 区 `bytes 48..59`，以接近 raw 的方式映射到 USB `0x05` motion 区。默认关闭平滑、缩放、死区和自动校准。
- USB report loop 默认 `250 Hz`，这是当前陀螺仪稳定性的推荐值；`1000 Hz` 仍保留为 Manager 和串口命令里的可选实验档。
- 已集成按键、方向键、摇杆、摇杆按下、扳机、`+`、`-`、`Home`、`Capture`、`C`、`GL`、`GR`、BLE 自动重连、Steam init guard，以及 Manager 状态/控制通道。
- 震动已经能产生可用的物理反馈，并不是单个固定 preset；它会跟随 Steam/SDL HID OUT rumble 更新并驱动 Pro2 BLE rumble stream。
- 语音、耳机音频、麦克风音频，以及完整 HD Rumble 2 音频复刻未实现。

English:

- Windows / Steam use the Nintendo Switch Pro / Pro2-style path with USB input report ID `0x05`.
- Switch 2 Pro BLE input uses the FD2 full report; measured BLE input is around the `133 Hz` class on tested hardware.
- Gyro uses the Pro2 BLE FD2 motion block at `bytes 48..59` and maps it into the USB `0x05` motion block with a raw-like path. Smoothing, scaling, deadband, and auto calibration are off by default.
- USB report loop defaults to `250 Hz`, the current gyro-stability recommendation. `1000 Hz` remains available in the Manager and serial command path as an optional experimental mode.
- Buttons, D-pad, sticks, stick clicks, triggers, `+`, `-`, `Home`, `Capture`, `C`, `GL`, `GR`, BLE auto-reconnect, Steam init guard, and Manager status/control paths are integrated.
- Rumble produces usable physical feedback and is not a single fixed preset; it tracks Steam/SDL HID OUT rumble updates and drives the Pro2 BLE rumble stream.
- Voice, headphone audio, microphone audio, and full HD Rumble 2 audio reproduction are not implemented.

## 发布下载 / Release Downloads

普通用户建议从 GitHub Releases 下载正式资产，不建议从仓库目录里手动挑二进制文件。

Normal users should download release assets from GitHub Releases rather than picking binaries manually from the repository tree.

```text
Y700Switch2Manager-aio-v5.0.0.exe
esp32s3-pro2-bridge-v5.0.0-20260601.zip
SHA256SUMS-v5.0.0.txt
```

最简单的方式是使用 All-in-one Manager EXE。它内置 V5 固件 payload、烧录器、驱动提示、BLE 扫描列表、状态面板、report-rate 控制、陀螺仪推荐的 `250 Hz` 默认值，以及可选的 `1000 Hz` 实验档。

The all-in-one Manager EXE is the simplest path. It includes the V5 firmware payload, flasher, driver hints, BLE scan list, status panel, report-rate controls, gyro-friendly `250 Hz` default, and optional `1000 Hz` command.

## 硬件 / Hardware

当前已测试的开发板形态：

- ESP32-S3-N16R8。
- 16MB flash。
- 8MB PSRAM。
- CH343P Type-C 用于烧录、日志和串口控制。
- ESP32-S3 native USB & OTG Type-C 用于向 Windows / Steam 输出 USB HID。

Current tested board shape:

- ESP32-S3-N16R8.
- 16MB flash.
- 8MB PSRAM.
- CH343P Type-C for flashing, logs, and serial control.
- ESP32-S3 native USB & OTG Type-C for USB HID output to Windows / Steam.

欢迎移植到其他开发板，但请同时记录硬件型号、SDK、USB 路径、BLE 路径和实测表现。

Other board ports are welcome, but they should document the board model, SDK version, USB path, BLE path, and measured behavior.

## 快速开始 / Quick Start

### 方式 A：All-in-one Manager

1. 从 GitHub Releases 下载 `Y700Switch2Manager-aio-v5.0.0.exe`。
2. 连接开发板的 `CH343P Type-C` 口。
3. 打开 EXE，选择 COM 口，然后烧录内置的 V5 固件。
4. 如果 Windows / Steam 没有刷新 HID 枚举，连接或重插 native USB & OTG 口。
5. 在 Manager 中连接真实 Switch 2 Pro Controller，或使用已保存目标的自动重连路径。

### Option A: All-in-one Manager

1. Download `Y700Switch2Manager-aio-v5.0.0.exe` from GitHub Releases.
2. Connect the board's `CH343P Type-C` port.
3. Open the EXE, choose the COM port, then flash the bundled V5 firmware.
4. Connect or replug the native USB & OTG port if Windows / Steam does not refresh HID enumeration.
5. Connect the real Switch 2 Pro Controller over BLE from the Manager, or use the saved-target auto reconnect path.

### 方式 B：Zip 包

1. 下载并解压 `esp32s3-pro2-bridge-v5.0.0-20260601.zip`。
2. 连接开发板的 `CH343P Type-C` 口。
3. 在解压目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

如果 CH343P 不是 COM12，先检测端口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

### Option B: Zip Package

1. Download and extract `esp32s3-pro2-bridge-v5.0.0-20260601.zip`.
2. Connect the board's `CH343P Type-C` port.
3. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

If the CH343P port is not COM12, detect it first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

## 常用命令 / Useful Commands

```powershell
# 查询固件状态 / Query firmware status
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "status" -ReadSeconds 5

# 强制重连保存的 Pro2 目标 / Force reconnect to the saved Pro2 target
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 30

# 陀螺仪推荐默认值 / Gyro-friendly default
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 250" -ReadSeconds 3

# 可选 1000 Hz 实验档 / Optional experimental USB output cadence
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 1000" -ReadSeconds 3

# 测量 Windows 主机侧 HID report rate / Measure host-observed HID report rate on Windows
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-SwitchHidRate.ps1 -Seconds 5

# 震动冒烟测试 / Rumble smoke test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rumble hold 3000" -ReadSeconds 5
```

## 性能说明 / Performance Notes

中文：

- 真实输入新鲜度看 `BLE input Hz` / `ble_input_actual_mhz`。
- USB HID 输出可以快于 BLE 输入。当 USB report rate 高于 BLE 输入频率时，USB 侧会重复最新 BLE 手柄状态。
- `1000 Hz` USB 输出不代表真实手柄每秒产生 1000 个新的 BLE 输入样本。
- 操作系统和游戏 API 可能合并或限流事件，所以应用内显示频率可能更低。
- 陀螺仪手感会受 Steam Input 设置、游戏鼠标处理、USB 线材、BLE 环境和手柄固件影响。

English:

- Real input freshness is represented by `BLE input Hz` / `ble_input_actual_mhz`.
- USB HID output can run faster than BLE input. When USB report rate is higher than BLE input rate, USB repeats the latest BLE controller state.
- `1000 Hz` USB output does not mean the physical controller generates 1000 new BLE samples per second.
- Host applications may show lower rates because OS/game APIs can coalesce or throttle events.
- Gyro feel depends on Steam Input settings, game mouse handling, USB cable quality, BLE environment, and controller firmware.

## 文档 / Documentation

- [快速开始 / Quickstart](QUICKSTART.md)
- [V5.0.0 发布说明 / Release Notes](RELEASE_NOTES_v5.0.0.md)
- [V5.0.0 预览说明 / Preview Notes](RELEASE_NOTES_v5.0.0-preview.md)
- [V4.0.0 发布说明 / Release Notes](RELEASE_NOTES_v4.0.0.md)
- [ESP32-S3 文档 / ESP32-S3 documentation](docs/esp32s3/README_ESP32S3.md)
- [控制协议 / Control protocol](docs/esp32s3/CONTROL_PROTOCOL.md)
- [测试矩阵 / Test matrix](docs/TEST_MATRIX.md)
- [发布打包计划 / Release packaging plan](docs/RELEASE_PACKAGING_PLAN.md)
- [贡献指南 / Contributing](CONTRIBUTING.md)

## 仓库结构 / Repository Layout

```text
firmware/esp32s3_switch2_bridge/   ESP-IDF firmware for the ESP32-S3 bridge
windows/manager_app/               .NET 8 WPF Manager and all-in-one flasher source
tools/esp32s3/                     Build, flash, monitor, and serial command scripts
tools/                             HID, Steam, haptic, and rate-test helper tools
docs/esp32s3/                      ESP32-S3 protocol, design, and troubleshooting docs
release/                           Local packaged artifacts; public downloads should use GitHub Releases
src/                               Historical Y700 Android bridge/responder sources
```

## 免责声明 / Disclaimer

本项目不是 Nintendo、Valve、Microsoft、Apple、Google、Espressif 或其他软硬件厂商的官方项目，也未获得上述公司的认可或赞助。Nintendo Switch、Switch Pro Controller、Steam、Windows、macOS、Android、ESP32 等名称归各自权利人所有。

This project is not affiliated with, endorsed by, or sponsored by Nintendo, Valve, Microsoft, Apple, Google, Espressif, or any other hardware/software vendor. Nintendo Switch, Switch Pro Controller, Steam, Windows, macOS, Android, ESP32, and related names are trademarks of their respective owners.

这是一个实验性研究项目。不同 ESP32-S3 开发板、Windows 版本、Steam 版本、USB 线材、BLE 环境、游戏和手柄固件都可能影响结果。

This is an experimental research project. Results may vary with different ESP32-S3 boards, Windows builds, Steam versions, USB cables, BLE environments, games, and controller firmware.

## 许可证 / License

本项目采用 Apache License 2.0 开源协议，详见 [LICENSE](LICENSE)。

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.
