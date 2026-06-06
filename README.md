# Y700 / ESP32-S3 Switch 2 Pro Bridge

## 项目简介 / Overview

这是一个面向 Switch 2 Pro Controller 的低成本、低延迟 BLE-to-USB 硬件桥接项目。当前主线是 ESP32-S3 接收器：真实 Switch 2 Pro Controller 通过 BLE 连接 ESP32-S3，开发板再通过原生 USB 向 Windows / Steam 暴露 Nintendo Switch Pro / Pro2 风格的 USB HID 控制器。

This is a low-cost, low-latency BLE-to-USB hardware bridge for the Switch 2 Pro Controller. The current mainline is the ESP32-S3 receiver: the real controller connects to the ESP32-S3 over BLE, and the board exposes a Nintendo Switch Pro / Pro2-style USB HID controller to Windows / Steam.

早期的 Lenovo Y700 Android USB Gadget 方案仍保留在仓库中，作为历史稳定路线和研究资料。新用户建议直接使用 ESP32-S3 路线。

The older Lenovo Y700 Android USB Gadget route remains in the repository as historical stable/research material. New users should start with the ESP32-S3 path.

## 当前正式版 / Current Release

当前推荐直接使用 GitHub Release 里的新版 All-in-one Manager EXE：

[下载 `Y700Switch2Manager-aio-v5.0.0.exe`](https://github.com/LeonChrome/y700-switch2-pro-bridge/releases/download/v5.0.0/Y700Switch2Manager-aio-v5.0.0.exe)

该 EXE 已在 2026-06-04 刷新，内置 V5 固件和 Manager 最新 BLE 上次地址自动重连开关。新用户搭建时不需要再使用旧 Y700 转发方案，也不需要从仓库目录里手动挑固件或工具文件。

The recommended setup path is the latest all-in-one Manager EXE from GitHub Releases:

[Download `Y700Switch2Manager-aio-v5.0.0.exe`](https://github.com/LeonChrome/y700-switch2-pro-bridge/releases/download/v5.0.0/Y700Switch2Manager-aio-v5.0.0.exe)

This EXE was refreshed on 2026-06-04. It bundles the V5 firmware and the latest Manager controls, including the saved BLE target auto-reconnect on/off buttons. New users should not start from the old Y700 forwarding route or manually pick firmware/tool binaries from the repository tree.

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

- 一体化搭建：下载新版 Release EXE 后，可以直接完成驱动提示、端口选择、固件烧录、状态查看和常用控制。
- BLE 连接：支持扫描真实 Switch 2 Pro Controller，连接选中目标，保存目标，并通过按钮开启或关闭“上次地址自动重连”。
- Windows / Steam 走 Nintendo Switch Pro / Pro2 风格识别路径，USB input report ID 使用 `0x05`。
- Switch 2 Pro BLE 输入使用 FD2 full report；在已测试硬件和环境中，BLE input 约为 `133 Hz` 级别。
- 陀螺仪使用 Pro2 BLE FD2 motion 区 `bytes 48..59`，以接近 raw 的方式映射到 USB `0x05` motion 区。默认关闭平滑、缩放、死区和自动校准。
- USB report loop 默认 `250 Hz`，这是当前陀螺仪稳定性的推荐值；`1000 Hz` 仍保留为 Manager 和串口命令里的可选实验档。
- 已集成按键、方向键、摇杆、摇杆按下、扳机、`+`、`-`、`Home`、`Capture`、`C`、`GL`、`GR`、BLE 自动重连、Steam init guard，以及 Manager 状态/控制通道。
- 震动已经能产生可用的物理反馈，并不是单个固定 preset；它会跟随 Steam/SDL HID OUT rumble 更新并驱动 Pro2 BLE rumble stream。
- 语音、耳机音频、麦克风音频，以及完整 HD Rumble 2 音频复刻未实现。

English:

- All-in-one setup: the latest Release EXE handles driver hints, port selection, firmware flashing, status viewing, and common controls.
- BLE connection: the Manager can scan for a real Switch 2 Pro Controller, connect a selected target, save the target, and enable or disable saved-target auto reconnect.
- Windows / Steam use the Nintendo Switch Pro / Pro2-style path with USB input report ID `0x05`.
- Switch 2 Pro BLE input uses the FD2 full report; measured BLE input is around the `133 Hz` class on tested hardware.
- Gyro uses the Pro2 BLE FD2 motion block at `bytes 48..59` and maps it into the USB `0x05` motion block with a raw-like path. Smoothing, scaling, deadband, and auto calibration are off by default.
- USB report loop defaults to `250 Hz`, the current gyro-stability recommendation. `1000 Hz` remains available in the Manager and serial command path as an optional experimental mode.
- Buttons, D-pad, sticks, stick clicks, triggers, `+`, `-`, `Home`, `Capture`, `C`, `GL`, `GR`, BLE auto-reconnect, Steam init guard, and Manager status/control paths are integrated.
- Rumble produces usable physical feedback and is not a single fixed preset; it tracks Steam/SDL HID OUT rumble updates and drives the Pro2 BLE rumble stream.
- Voice, headphone audio, microphone audio, and full HD Rumble 2 audio reproduction are not implemented.

## 发布下载 / Release Downloads

普通用户建议从 GitHub Releases 下载正式资产，首选新版 All-in-one Manager EXE，不建议从仓库目录里手动挑二进制文件。

Normal users should download release assets from GitHub Releases, preferably the latest all-in-one Manager EXE, rather than picking binaries manually from the repository tree.

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

## V5.2 VIIPER ns2pro 实验模式 / V5.2 VIIPER ns2pro Experimental Mode

中文：

V5.2 是实验路线，不影响 V5.1/V5.0 ESP32-S3 正式桥接功能。它使用 VIIPER 创建虚拟 Switch 2 Pro / `ns2pro` USB 设备，捕获 `LeftRumble[16]` / `RightRumble[16]`，再通过 firmware/control 的 `rumble raw02 <hex>` 转发到真实 Pro2。当前已验证按钮、陀螺仪和 raw02 震动链路，真实 Pro2 已产生物理震动。

注意：Steam/SDL 的普通 rumble API 不等于 ns2pro HD `0x02` 输出。V5.2 已验证的可靠来源是 direct HID `0x02` 或 VIIPER probe 捕获；原生游戏 HD rumble 仍取决于游戏、Steam Input 和输入栈，不承诺所有 Steam 游戏都支持 HD rumble。

从仓库根目录先检查环境：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_viiper_env.ps1
```

自动安装 usbip-win2：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate
```

如果 GitHub API 返回 `403`，手动下载最新 Windows x64/amd64 installer asset。当前已验证的资产名形如 `USBip-0.9.7.7-x64.exe`。下载后放到：

```text
.\work\deps\usbip-win2\<asset-file>
```

然后从管理员 PowerShell 执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate -InstallerPath .\work\deps\usbip-win2\<asset-file>
```

安装成功后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1
```

English:

V5.2 is an experimental path and does not modify the V5.1/V5.0 ESP32-S3 stable bridge. It uses VIIPER to create a virtual Switch 2 Pro / `ns2pro` USB device, captures `LeftRumble[16]` / `RightRumble[16]`, then forwards the payload to the real Pro2 through the firmware/control `rumble raw02 <hex>` command. Buttons, gyro, and the raw02 rumble chain have been verified, including physical vibration on the real Pro2.

Note: Steam/SDL ordinary rumble is not the same as ns2pro HD `0x02` output. The reliable V5.2 source is direct HID `0x02` or VIIPER probe capture; native game HD rumble still depends on the game, Steam Input, and the input stack. V5.2 does not claim all Steam games support HD rumble.

Check the environment from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_viiper_env.ps1
```

Automatic usbip-win2 install:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate
```

Manual install after downloading a release installer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate -InstallerPath .\work\deps\usbip-win2\<asset-file>
```

Detailed results are tracked in [docs/v5_2_viiper_probe_results.md](docs/v5_2_viiper_probe_results.md).

Current Phase 2 finding: usbip-win2 attach works and VIIPER receives output
feedback. SDL 3.4.10 currently sees the virtual `VID_057E&PID_2069&MI_00`
interface as a low-level joystick, not an SDL gamepad, and SDL rumble/effect
APIs return unsupported. Direct Windows HID output writes through
`.\tools\Send-HidHapticProbe.ps1` do trigger non-zero
`LeftRumble[16] / RightRumble[16]`.

Repeatable Phase 2 validation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_hid_rumble_probe\run_viiper_ns2pro_hid_rumble_probe.ps1
```

Expected result:

```text
[NS2PRO_HID_RUMBLE_PROBE] output_feedback=true
[NS2PRO_HID_RUMBLE_PROBE] nonzero=true
[NS2PRO_HID_RUMBLE_PROBE] result=passed
```

## V5.2 真实 Pro2 raw02 验证 / V5.2 Real Pro2 raw02 Validation

中文：

V5.2 raw02 路线已经刷入 ESP32-S3 并完成真实 Pro2 验证：BLE connected、low/medium/captured VIIPER payload 均返回 `sent=true`，最终状态里 `rumble_writes=49`、`rumble_errors=0`，没有 BLE 异常断连。用户已确认按键、陀螺仪和物理震动均有效。

从仓库根目录执行 build/flash：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

如果 CH343P 不是 COM12，先检测端口并替换：

```powershell
[System.IO.Ports.SerialPort]::GetPortNames()
Get-PnpDevice -Class Ports
```

真实发送按低强度到中强度再到 captured 的顺序：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset low -Send -Port COM12
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset medium -Send -Port COM12
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -SendToRealPro2 -Port COM12 -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

English:

The V5.2 raw02 path has been flashed to ESP32-S3 and validated on a real Pro2: BLE connected, low/medium/captured VIIPER payloads returned `sent=true`, and final status showed `rumble_writes=49` with `rumble_errors=0` and no abnormal BLE disconnect. The user confirmed buttons, gyro, and physical vibration on the real controller.

Detailed notes are tracked in [docs/v5_2_real_pro2_hd_rumble_probe_results.md](docs/v5_2_real_pro2_hd_rumble_probe_results.md) and [docs/v5_2_ns2pro_viiper_integration_plan.md](docs/v5_2_ns2pro_viiper_integration_plan.md).

## V5.3 DualSense 触觉研究 / V5.3 DualSense Haptic Research

中文：

V5.3 正在推进 DualSense / PS5 haptic source research。它不是已支持功能，也不会进入 V5.2 GUI。当前目标是一插入真实 DualSense USB 后，可以自动检测 HID、audio endpoint、WASAPI loopback，并运行 HID output probe、haptic audio probe 和未来游戏捕获流程。

当前没有真实 DualSense 时，所有 V5.3 probe 都应输出 blocked 并以 exit code 0 结束，方便 night-run：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_dualsense_env.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_dualsense_capture.ps1 -DurationSeconds 5
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_night_probe.ps1
```

English:

V5.3 is in progress as DualSense / PS5 haptic source research. It is not a supported feature yet and is not part of the V5.2 GUI. The goal is to make a real USB DualSense immediately diagnosable: HID detection, audio endpoint detection, WASAPI loopback status, HID output probe, haptic audio probe, and future game capture flow.

Without a real DualSense, V5.3 probes should report blocked and exit with code 0 so night-run can continue.

## V5.4/V5.5 双身份路线 / V5.4/V5.5 Dual-Identity Routes

中文：

V5.2 Pure Pro2 / VIIPER 路线已经封存保留，不再作为 V5.5 的改造对象。它继续提供 `pro2_ns2_viiper` 身份，保留已验证的按键、陀螺仪、VIIPER `LeftRumble[16] / RightRumble[16]` 捕获、raw02 转发和真实 Pro2 物理震动。

V5.5 新增独立的 `dualsense_esp32s3_experimental` 身份：PC 将 ESP32-S3 识别为有线 DualSense，游戏可以向它发送 DualSense HID output 和四声道 haptic audio；ESP32-S3 只提取有价值的普通震动、触发器事件和左右 haptic 声道特征，再转换为 Pro2 raw02，通过 BLE 发送给真实 Switch 2 Pro Controller。目标产品不依赖真实 DualSense。

```text
output_identity=pro2_ns2_viiper
output_identity=dualsense_esp32s3_experimental
```

当前版本边界：

- V5.2：Pure Pro2 / VIIPER ns2pro 路线收口并长期保留。
- V5.3：DualSense haptic audio 到 Pro2 raw02 转译原型。
- V5.4：双身份策略、游戏行为矩阵和安全策略。
- V5.5：基于 DS5Dongle 研究的 ESP32-S3 DualSense identity 实验。

English:

The V5.2 Pure Pro2 / VIIPER route is frozen and preserved. It retains the verified buttons, gyro, VIIPER `LeftRumble[16] / RightRumble[16]` capture, raw02 forwarding, and physical Pro2 vibration path.

V5.5 adds a separate `dualsense_esp32s3_experimental` identity. The PC sees the ESP32-S3 as a wired DualSense; the board receives DualSense HID output and four-channel haptic audio, extracts useful rumble/trigger/haptic features, translates them to Pro2 raw02, and writes them to the real Switch 2 Pro over BLE. The target product does not require a real DualSense.

Planning and repeatable probes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\fetch_v5_5_ds5dongle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\analyze_v5_5_ds5dongle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_dualsense_to_pro2_pipeline.ps1 -Synthetic -Event impact -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_4_hybrid_haptic_probe.ps1
```

### V5.5 Phase 1/2/3: DualSense Identity, Pro2 Input, and Audio Stub

中文：

Phase 1 实机验证已通过：Windows 识别 VID `054c` / PID `0ce6`，持续接收 `0x01 + 63 bytes`、约 `250 Hz` 的输入报告，且未发生 USB 断连。Phase 2 在同一个独立实验固件中复用现有 Pro2 BLE FD2 解析器，将真实 Pro2 的按键、摇杆、扳机和 motion 映射到 DualSense 输入报告。Phase 2.1 根据实测反转了两根摇杆 Y 轴，并把 DualSense 普通 light/heavy motor 输出安全近似为 Pro2 BLE vibration；受控测试已得到非零 BLE 写入和零错误。Phase 3 新增最小 USB Audio render endpoint stub：目标是让 Windows 枚举 DualSense-like 4ch/48kHz 音频输出，并在固件中只统计 haptic channels 2/3，生成 Pro2 raw02 dry-run payload。现有 V5.2/V5.0 默认桥接固件和 GUI 不变；haptic raw02 实时转发仍默认关闭。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf -Monitor
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_identity.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_input.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_reports.ps1 -Seconds 6
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_dualsense_rumble_test.ps1 -RightLight 48 -LeftHeavy 80 -PulseMs 250 -Send
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_audio.ps1
```

English:

Phase 1 passed hardware validation with VID `054c`, PID `0ce6`, stable `0x01 + 63-byte` input at about 250 Hz, and no USB disconnect. Phase 2 reuses the existing Pro2 BLE FD2 parser inside the standalone experiment and maps real Pro2 buttons, sticks, triggers, and motion into the DualSense input report. Phase 2.1 reverses both stick Y axes from hardware feedback and safely approximates ordinary DualSense light/heavy motor output through Pro2 BLE vibration; a controlled test produced non-zero BLE writes with zero errors. Phase 3 adds a minimal USB Audio render endpoint stub so Windows can enumerate a DualSense-like 4ch/48 kHz output path; firmware currently extracts haptic channel 2/3 statistics and emits Pro2 raw02 dry-run payloads only. The V5.2/V5.0 default firmware and GUI remain unchanged; live haptic raw02 forwarding remains off.

See the [Phase 1 guide](docs/v5_5_phase1_minimal_dualsense_hid_identity.md), [Phase 2 mapping guide](docs/v5_5_phase2_pro2_to_dualsense_input_mapping.md), and [Phase 3 audio endpoint guide](docs/v5_5_phase3_dualsense_audio_endpoint.md).

## 文档 / Documentation

- [快速开始 / Quickstart](QUICKSTART.md)
- [更新日志 / Changelog](CHANGELOG.md)
- [V5.0.0 发布说明 / Release Notes](RELEASE_NOTES_v5.0.0.md)
- [V5.2 GitHub Release 草稿 / GitHub Release Draft](release_notes/V5.2.md)
- [V5.2 实验说明 / Experimental Notes](RELEASE_NOTES_v5.2.0-experimental.md)
- [V5.2 VIIPER / raw02 实测结果](docs/v5_2_real_pro2_hd_rumble_probe_results.md)
- [V5.3 DualSense 触觉路线图](docs/v5_3_dualsense_haptic_roadmap.md)
- [V5.3 DualSense 实机测试清单](docs/v5_3_dualsense_test_checklist.md)
- [V5.3 DualSense 上游研究](docs/v5_3_dualsense_upstream_research.md)
- [V5.3 DualSense 到 Pro2 转译计划](docs/v5_3_dualsense_to_pro2_translation_plan.md)
- [V5.4 Hybrid haptic engine 架构](docs/v5_4_hybrid_haptic_engine_architecture.md)
- [V5.5 DS5Dongle bridge 研究](docs/v5_5_ds5dongle_derived_bridge_study.md)
- [V5.5 ESP32-S3 移植计划](docs/v5_5_ds5dongle_esp32s3_port_plan.md)
- [V5.5 DualSense identity 可行性](docs/v5_5_esp32s3_dualsense_identity_feasibility.md)
- [V5.5 DualSense identity 实验计划](docs/v5_5_esp32s3_dualsense_identity_experiment_plan.md)
- [V5.5 Phase 1 最小 DualSense HID identity](docs/v5_5_phase1_minimal_dualsense_hid_identity.md)
- [V5.5 Phase 2 Pro2 到 DualSense 输入映射](docs/v5_5_phase2_pro2_to_dualsense_input_mapping.md)
- [V5.5 Phase 3 DualSense audio endpoint](docs/v5_5_phase3_dualsense_audio_endpoint.md)
- [V5.5 DS5 descriptor 对照](docs/generated/v5_5_ds5_descriptor_mapping.md)
- [Token 安全卫生](docs/security_token_hygiene.md)
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
