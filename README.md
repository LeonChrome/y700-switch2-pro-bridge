# Y700 / ESP32-S3 Switch 2 Pro Bridge

## 中文

这个项目最初使用 root 后的联想 Y700 平板，把真实 Switch 2 Pro Controller 的 BLE 输入转成 Windows / Steam 可识别的 Nintendo 风格 USB HID 设备。

V4 版本把这条链路移植到了 ESP32-S3 开发板上：真实 Pro2 手柄通过 BLE 连接 ESP32-S3，ESP32-S3 再通过原生 USB 模拟 Nintendo Switch Pro Controller。Y700 v3 路线仍保留在仓库里作为历史稳定版本，但 V4 的主线目标已经是独立 MCU 桥接器。

## English

This project started as a rooted Lenovo Y700 BLE-to-USB bridge for a real Switch 2 Pro Controller. The Y700 read private BLE GATT notifications and exposed a Nintendo-style USB HID device to Windows / Steam.

V4 moves that bridge onto an ESP32-S3 development board: the real Pro2 controller connects to the ESP32-S3 over BLE, and the ESP32-S3 exposes a Nintendo Switch Pro Controller-compatible USB device to the PC. The Y700 v3 path is still kept in the repository as the previous stable route, while V4 is now the primary MCU bridge path.

## V4 Status / V4 状态

## 中文

V4 已在当前测试环境验证：

- ESP32-S3 通过 BLE 直接连接真实 Switch 2 Pro Controller。
- Steam 可识别为 Nintendo Switch Pro / Pro2 控制器路径。
- A/B/X/Y、方向键、肩键、摇杆、摇杆按下、`+`、`-`、`Home`、`Capture`、`C`、`GL`、`GR` 输入已跑通。
- Pro2 rumble / HD rumble 转发已产生物理反馈。
- BLE connection interval 固定请求为 `6`，即 `7.5 ms`，真实 BLE 输入约 `133.3 Hz`。
- ESP32-S3 USB HID 端点 interval 为 `1 ms`，USB report loop 可接近 `1000 Hz`，当前 10 秒窗口 Windows 侧实测约 `993.3 Hz`；高于 BLE 输入频率的 USB report 会重复最新 BLE 状态。
- 右摇杆 neutral 偏移问题已修复：FD2 与 legacy BLE notify 流使用独立自动中心校准。
- Windows Manager exe 可显示 USB、BLE、report rate、rumble、HID OUT/GET、bulk control 等状态。

## English

V4 has been verified in the current test environment:

- ESP32-S3 connects directly to the real Switch 2 Pro Controller over BLE.
- Steam recognizes the device through the Nintendo Switch Pro / Pro2 controller path.
- A/B/X/Y, D-pad, shoulders, sticks, stick clicks, `+`, `-`, `Home`, `Capture`, `C`, `GL`, and `GR` input paths are working.
- Pro2 rumble / HD rumble forwarding produces physical feedback.
- BLE connection interval is requested as `6`, which is `7.5 ms`; real BLE input is about `133.3 Hz`.
- The ESP32-S3 USB HID endpoint uses a `1 ms` interval, and the USB report loop can approach `1000 Hz`, with a current 10-second Windows-side measurement of about `993.3 Hz`; USB reports above the BLE input cadence repeat the latest BLE state.
- The right-stick neutral drift issue is fixed by giving the FD2 and legacy BLE notify streams separate auto-center calibration.
- The Windows Manager exe shows USB, BLE, report-rate, rumble, HID OUT/GET, and bulk-control status.

## Release Files / 发布文件

## 中文

V4 GitHub Release 会提供：

- `esp32s3-pro2-bridge-v4.0.0-20260529.zip`：完整发布包，包含 ESP32-S3 烧录固件、烧录脚本、Windows Manager exe、诊断工具和说明。
- `Y700Switch2Manager-v4.0.0.exe`：单独的 Windows Manager 控制台。
- `esp32s3-pro2-bridge-firmware-v4.0.0-20260529.zip`：仅固件和烧录脚本，适合只想刷板子的情况。

## English

The V4 GitHub Release provides:

- `esp32s3-pro2-bridge-v4.0.0-20260529.zip`: full package with ESP32-S3 firmware binaries, flash scripts, Windows Manager exe, diagnostic tools, and documentation.
- `Y700Switch2Manager-v4.0.0.exe`: standalone Windows Manager console.
- `esp32s3-pro2-bridge-firmware-v4.0.0-20260529.zip`: firmware-only flasher package for users who only need to flash the board.

## Hardware / 硬件

## 中文

V4 使用的开发板假设：

- ESP32-S3-N16R8
- 16MB flash
- 8MB PSRAM
- CH343P Type-C：烧录、日志、串口控制
- ESP32-S3 native USB & OTG Type-C：对 Windows / Steam 暴露 USB HID

## English

V4 assumes this board shape:

- ESP32-S3-N16R8
- 16MB flash
- 8MB PSRAM
- CH343P Type-C for flashing, logs, and serial control
- ESP32-S3 native USB & OTG Type-C for the USB HID device exposed to Windows / Steam

## Flashing / 烧录

## 中文

最简单的烧录方式：

1. 下载并解压 `esp32s3-pro2-bridge-v4.0.0-20260529.zip`。
2. 用 USB 线连接开发板的 `CH343P Type-C` 口到 Windows。
3. native USB & OTG 口可以同时插着，用于刷完后让 Windows / Steam 识别 HID；如果识别不刷新，刷完后重新插拔 native USB 口。
4. 在解压目录打开 PowerShell，运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

如果 CH343P 不是 `COM12`，先运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

然后把命令里的 `COM12` 改成实际端口。也可以双击：

```text
tools\esp32s3\Flash-Pro2Bridge.bat
```

如果烧录出现串口噪声或 stub 传输失败，可尝试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200 -NoStub
```

## English

The simplest flashing path:

1. Download and extract `esp32s3-pro2-bridge-v4.0.0-20260529.zip`.
2. Connect the board's `CH343P Type-C` port to Windows.
3. The native USB & OTG port may stay connected so Windows / Steam can enumerate the HID device after flashing; if enumeration does not refresh, replug the native USB port.
4. Open PowerShell in the extracted folder and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

If the CH343P port is not `COM12`, first run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

Then replace `COM12` with the actual port. You can also double-click:

```text
tools\esp32s3\Flash-Pro2Bridge.bat
```

If flashing reports serial noise or stub transfer failures, try:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200 -NoStub
```

## Windows Manager / Windows 管理器

## 中文

`Y700Switch2Manager.exe` 会优先使用 CH343P 串口控制；没有串口时会尝试 native USB HID feature report 和 WinUSB bulk fallback。面板可查看：

- 控制连接方式
- USB HID / bulk mounted 状态
- Steam init guard 状态
- BLE connected / scanning / idle
- BLE target、自动重连、live input 状态
- BLE input Hz、BLE interval、BLE gap
- USB report target / actual rate
- HID OUT / GET 和 bulk control 计数
- rumble 状态、写入次数、错误数和调参值

## English

`Y700Switch2Manager.exe` prefers CH343P serial control, then falls back to native USB HID feature reports and WinUSB bulk control. The panel shows:

- Control transport
- USB HID / bulk mounted state
- Steam init guard state
- BLE connected / scanning / idle
- BLE target, auto-reconnect, and live input state
- BLE input Hz, BLE interval, and BLE gaps
- USB report target / actual rate
- HID OUT / GET and bulk-control counters
- Rumble state, write count, error count, and tuning values

## Useful Commands / 常用命令

```powershell
# Query firmware status / 查询固件状态
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command status -ReadSeconds 5

# Force reconnect to the saved Pro2 target / 重连已保存的 Pro2
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 30

# Set USB report loop to 1000 Hz / 设置 USB report loop 为 1000 Hz
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 1000" -ReadSeconds 3

# Measure host-observed HID report rate / 测量 Windows 侧 HID report rate
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-SwitchHidRate.ps1 -Seconds 5

# Rumble smoke test / 震动冒烟测试
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rumble hold 3000" -ReadSeconds 5
```

## Repository Layout / 仓库结构

```text
firmware/esp32s3_switch2_bridge/   ESP-IDF firmware for the ESP32-S3 V4 bridge
windows/manager_app/               .NET 8 WPF Manager
tools/esp32s3/                     Build, flash, monitor, and serial command scripts
docs/esp32s3/                      ESP32-S3 protocol and troubleshooting docs
release/                           Local packaged artifacts; large release assets are uploaded to GitHub Releases
src/                               Historical Y700 Android bridge/responder sources
```

## Safety Notes / 注意事项

## 中文

这仍然是研究型项目，不是 Nintendo 官方驱动。不同 ESP32-S3 开发板、Windows 版本、Steam 版本、USB 线材和蓝牙环境可能会影响结果。刷机前请确认使用的是 CH343P 烧录口，不要把 native USB HID 口误当作烧录口。

## English

This is still a research project, not an official Nintendo driver. Results may vary with different ESP32-S3 boards, Windows builds, Steam versions, USB cables, and BLE environments. Before flashing, make sure you are using the CH343P flashing port, not the native USB HID port.
