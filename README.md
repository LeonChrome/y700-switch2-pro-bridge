# 新和联胜双版本 / XinHeLianSheng Pro2 Bridge

**新和联胜双版本** 是一个面向真实 Switch 2 Pro / Pro2 手柄的 Windows 无线三模桥接项目。它提供两条路线：不需要开发板的 **VIIPER Windows 版**，以及需要 ESP32-S3 开发板的 **ESP 固件版**。

**XinHeLianSheng Pro2 Bridge** is a Windows wireless tri-mode bridge for the real Switch 2 Pro / Pro2 controller. It ships in two final routes: a **VIIPER Windows edition** that does not require an ESP32 board, and an **ESP32-S3 firmware edition** for users who prefer a hardware bridge.

Latest final release / 最新完结版:

[**完结撒花双版本：V6.2.25 + ESP V5.9.13**](https://github.com/LeonChrome/XinHeLianSheng-Pro2-Bridge/releases/tag/v6.2.25-v5.9.13-finale)

## Download / 下载

| Route / 路线 | File / 文件 | Best For / 适合场景 |
| --- | --- | --- |
| VIIPER Windows Edition / 无开发板版 | `XinHeLianSheng-VIIPER-aio-v6.2.25.exe` | Users who want wireless Pro2 bridging directly on Windows without ESP32 hardware. |
| ESP32-S3 Edition / 开发板版 | `Y700Switch2V55Manager-aio-v5.9.13.exe` | Users who want a dedicated ESP32-S3 receiver, firmware flashing, and higher hardware-side control. |
| USBIP Runtime / USBIP 运行时 | `USBip-0.9.7.7-x64.exe` | Required by the VIIPER route when USBIP is not installed on the PC. |

中文说明：

- 不接开发板，直接用电脑蓝牙连接真实 Pro2 手柄：下载 **VIIPER Windows Edition**。
- 使用 ESP32-S3 开发板作为无线接收器：下载 **ESP32-S3 Edition**。
- 首次使用 VIIPER 路线且程序提示缺少 USBIP：安装 `USBip-0.9.7.7-x64.exe` 后重启电脑。

English notes:

- Use **VIIPER Windows Edition** if you want a no-board setup that connects the real Pro2 controller from Windows.
- Use **ESP32-S3 Edition** if you want a dedicated hardware receiver and firmware flashing workflow.
- Install `USBip-0.9.7.7-x64.exe` and reboot if the VIIPER edition reports that USBIP is missing.

## What It Does / 项目作用

The project converts the real Pro2 controller into multiple host-visible controller identities:

本项目把真实 Pro2 手柄转换为多个可被 Windows、Steam 和游戏识别的虚拟手柄身份：

| Mode / 模式 | Host Identity / 主机识别 | Purpose / 用途 |
| --- | --- | --- |
| 新和联胜 / PS5 | DualSense-compatible HID/audio | PS5-style compatibility, gyro, ordinary rumble, and HD haptic audio routing. |
| PS5 Edge | DualSense Edge-style identity | PS5 identity with Edge-style back-button support. |
| Pro2 / Nintendo | Nintendo Switch Pro style HID | Steam Input / Nintendo-style layout and native Pro2 feel. |
| Xbox / XInput | Xbox 360 / XInput style | Broad Windows game compatibility and configurable back-button mapping. |

## Two Final Routes / 双版本路线

### V6.2.25 VIIPER Windows Edition / V6.2.25 无开发板版

This route creates virtual USB controllers on Windows through VIIPER and USBIP, then connects to real Pro2 controllers through Windows BLE. It is Windows-only and does not require ESP32 hardware.

这条路线通过 VIIPER 和 USBIP 在 Windows 端创建虚拟 USB 手柄，再由 Windows BLE 连接真实 Pro2 手柄。它是 Windows 平台限定版，不需要 ESP32 开发板。

Highlights / 重点能力：

- Four independent Pro2 slots across PS5, PS5 Edge, Pro2/Nintendo, and Xbox/XInput modes.
- Stable slot-specific VIIPER serial numbers to reduce Steam device-name cache confusion.
- PS5-family IMU mapping tuned through the professional test flow.
- PS5 / PS5 Edge support for gyro and haptic routing within the current VIIPER feedback contract.
- Xbox mode back-button mapping with limited single-shot, turbo, and fixed-button options.
- Startup, reconnect, diagnostics, and log throttling intended for daily use.

### ESP V5.9.13 Edition / ESP V5.9.13 开发板版

This route flashes firmware to an ESP32-S3 board. The board connects to the real Pro2 controller over BLE and exposes the selected USB controller identity to the PC.

这条路线把固件刷入 ESP32-S3 开发板。开发板通过 BLE 连接真实 Pro2 手柄，并向电脑暴露所选 USB 手柄身份。

Highlights / 重点能力：

- Four firmware-facing modes: 新和联胜 / PS5, PS5 Edge, Pro2 / Nintendo, and Xbox / XInput.
- PS5 HD haptic audio routing plus ordinary rumble scheduling.
- Pro2 / Nintendo mode for Steam-native style usage.
- Xbox / XInput mode for broad game compatibility.
- BLE fast/turbo connection parameter request is delayed until live input is stable, reducing reconnect loops after pairing.
- Manager includes flashing, USB checks, BLE connection tools, diagnostics, and log console.

## Hardware / 硬件

The ESP route targets:

ESP 路线主要面向：

- ESP32-S3 N16R8
- 16 MB flash
- 8 MB Octal PSRAM
- CH343P / WCH USB serial control port
- ESP32-S3 native USB / OTG gamepad port

The VIIPER route only requires a Windows PC with a working Bluetooth adapter and USBIP runtime.

VIIPER 路线只需要 Windows 电脑、可用的蓝牙适配器，以及 USBIP 运行时。

## Basic Use / 基本使用

### VIIPER Windows Edition / 无开发板版

1. Install USBIP if prompted by the app.
2. Open `XinHeLianSheng-VIIPER-aio-v6.2.25.exe`.
3. Choose PS5, PS5 Edge, Pro2 / Nintendo, or Xbox / XInput.
4. Click enter/start game mode.
5. Wake the real Pro2 controller and wait for automatic BLE reconnect.
6. Use the tray icon for quick mode switching and background operation.

中文流程：

1. 如果程序提示缺少 USBIP，先安装 USBIP 并重启。
2. 打开 `XinHeLianSheng-VIIPER-aio-v6.2.25.exe`。
3. 选择 PS5、PS5 Edge、Pro2 / Nintendo 或 Xbox / XInput。
4. 点击进入游戏模式。
5. 唤醒真实 Pro2 手柄，等待自动 BLE 重连。
6. 后续可以通过右下角托盘图标快速切换模式或后台运行。

### ESP32-S3 Edition / 开发板版

1. Connect the ESP32-S3 CH343P control USB port.
2. Open `Y700Switch2V55Manager-aio-v5.9.13.exe`.
3. Select the COM port.
4. Click the target firmware mode.
5. Wait for flashing to finish.
6. Replug the ESP32-S3 native USB / OTG gamepad port.
7. Use the BLE connection area to pair or reconnect the real Pro2 controller.

中文流程：

1. 接入 ESP32-S3 的 CH343P 控制串口。
2. 打开 `Y700Switch2V55Manager-aio-v5.9.13.exe`。
3. 选择 COM 口。
4. 点击目标固件模式。
5. 等待刷写完成。
6. 重新插入 ESP32-S3 原生 USB / OTG 手柄口。
7. 在 BLE 连接区域配对或重连真实 Pro2 手柄。

## Source Branches / 源码分支

The two final routes are intentionally kept as separate source branches:

双版本源码分支分开保存，便于维护和回溯：

- VIIPER Windows edition: `codex/v6.2.25-finale-dual-release`
- ESP32-S3 edition: `codex/v5.9.13-finale-dual-release`

## Final Source Layout / 最终源码位置

The default branch is used as the bilingual landing page. The final source code is kept in the release branches listed above. Switch to the matching branch before building or studying a specific route.

默认分支主要作为双语项目首页使用。最终源码保存在上面列出的两个发布分支中；构建或研究某条路线前，请先切换到对应分支。

VIIPER branch / VIIPER 分支：

```text
windows/
  v60_viiper_app/                          VIIPER Windows edition

tools/
  package_v6_2_25_release.ps1              VIIPER AIO package script
  tests/v60_packet_mapper_test/            Packet mapper tests

release/
  v6.2.25/                                 VIIPER release artifacts
```

ESP branch / ESP 分支：

```text
firmware/
  esp32s3_switch2_bridge/                  ESP Pro2/Nintendo and Xbox/XInput bridge firmware
  esp32s3_dualsense_identity_experiment/   ESP PS5 / PS5 Edge firmware

windows/
  v55_manager_app/                         ESP firmware manager
  dual_ns2pro_host/                        Dual Pro2 host experiment/tooling

tools/
  esp32s3/                                 ESP-IDF build helpers
  package_v5_9_manager.ps1                 ESP AIO package script

docs/
  USER_GUIDE.md
  TROUBLESHOOTING.md
  TECHNICAL_NOTES.md

release/
  v5.9/                                    ESP release artifacts
  v6.2.25/                                 VIIPER release artifacts
```

## Build / 构建

Normal users should use the release EXE files. Building from source is only needed for development.

普通用户建议直接使用 Release 中的 AIO EXE。只有开发和二次修改才需要从源码构建。

V6 VIIPER app:

```powershell
dotnet build windows\v60_viiper_app\Y700Switch2V60Viiper.csproj -c Debug
dotnet run --project tools\tests\v60_packet_mapper_test\V60PacketMapperTest.csproj -c Debug
```

ESP Manager:

```powershell
dotnet build windows\v55_manager_app\Y700Switch2V55Manager.csproj -c Debug
```

ESP firmware requires an ESP-IDF environment matching the project scripts.

ESP 固件构建需要匹配项目脚本的 ESP-IDF 环境。

## Verification / 验证

Final release checks performed:

最终版已完成：

- V6 packet mapper test passed.
- V6 Debug build passed.
- V6.2.25 AIO package completed.
- ESP Manager Debug build passed.
- ESP firmware package verify passed.
- Release SHA256 files are published with the binaries.

## Notes / 说明

- This is an independent experimental project and is not affiliated with Nintendo, Sony, Microsoft, Valve, Espressif, WCH, or the VIIPER project.
- Brand names, controller names, and USB identities are used only to describe compatibility behavior.
- 本项目是独立实验项目，与 Nintendo、Sony、Microsoft、Valve、Espressif、WCH 或 VIIPER 项目没有从属关系。
- 文中品牌名、手柄名和 USB 身份仅用于描述兼容性行为。

## License / 许可证

This project keeps the original open-source license in [LICENSE](LICENSE). Third-party acknowledgements are listed in [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md).

本项目保留原开源许可证，见 [LICENSE](LICENSE)。第三方组件说明见 [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md)。
