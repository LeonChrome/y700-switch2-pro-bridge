# V4.0.0 Release Notes / V4.0.0 发布说明

Date: 2026-05-29

## 中文

V4 是这个项目从 Y700 平板桥接走向 ESP32-S3 独立桥接器的第一个完整发布版。真实 Switch 2 Pro Controller 通过 BLE 连接 ESP32-S3，ESP32-S3 再通过原生 USB 向 Windows / Steam 暴露 Nintendo Switch Pro Controller 兼容设备。

### 这版包含什么

- ESP32-S3 固件二进制、bootloader 和 partition table。
- Windows Manager 单文件 exe。
- CH343P 烧录脚本、端口检测脚本、串口命令脚本。
- HID report-rate 测量工具。
- 原生 USB HID feature 控制探针。
- 中英双语说明和烧录流程。

### 发布文件

```text
esp32s3-pro2-bridge-v4.0.0-20260529.zip
esp32s3-pro2-bridge-firmware-v4.0.0-20260529.zip
Y700Switch2Manager-v4.0.0.exe
```

### 核心验证结果

- Steam 能走 Nintendo Switch Pro / Pro2 控制器路径识别。
- Pro2 BLE 自动重连、GATT discovery、CCCD subscribe、输入解析和 rumble 写入均已跑通。
- BLE connection interval 固定请求为 `6`，即 `7.5 ms`。
- 实测 Pro2 BLE 输入约 `133.3 Hz`。
- USB HID endpoint interval 为 `1 ms`。
- USB report loop 设置为 `1000 Hz` 时，固件侧约 `991-998 Hz`，Windows HID 侧 10 秒窗口实测约 `993.3 Hz`。
- 当 USB report rate 高于 BLE 输入频率时，USB 会重复最新 BLE 状态；真实输入新鲜度看 `ble_input_actual_mhz`。
- 右摇杆 neutral 偏移问题已修复：FD2 与 legacy BLE notify 流使用独立 auto-center calibration。
- Rumble / HD rumble 路径已产生物理反馈，默认 tune 为 `rumble tune 100 180 20 3`。

### 烧录重点

1. 用 USB 线连接开发板的 `CH343P Type-C` 口。
2. native USB & OTG 口用于 Windows / Steam HID 识别，可同时插着；如果刷完不重新识别，重插 native USB。
3. 在 release 解压目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

4. 如果 CH343P 不是 COM12，先运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

5. 如果高速烧录失败，尝试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200 -NoStub
```

### Manager 面板能看到什么

- 控制通道：CH343P serial、native USB HID feature、native USB bulk。
- USB 状态：HID mounted、bulk mounted、Steam init guard。
- BLE 状态：connected / connecting / scanning / idle、target、auto reconnect、live input。
- 速率状态：target report rate、actual USB report rate、BLE input Hz、BLE interval、BLE gap。
- 排障状态：HID OUT/GET last、bulk last、bulk pending、BLE update rc/status。
- Rumble 状态：active/idle、updates、writes、stops、errors、tuning。

### 已知限制

- 这是研究型项目，不是 Nintendo 官方驱动。
- 目前主要在一块 ESP32-S3-N16R8 板子和当前 Windows / Steam 环境验证。
- 1000 Hz USB report 是实验选项；它提高 PC 侧轮询频率，但不会让 BLE 输入超过真实约 133.3 Hz。
- BLE 环境、USB 线、开发板版本、Steam 版本都可能影响表现。

## English

V4 is the first complete release that moves the project from the Y700 tablet bridge to a standalone ESP32-S3 bridge. The real Switch 2 Pro Controller connects to the ESP32-S3 over BLE, and the ESP32-S3 exposes a Nintendo Switch Pro Controller-compatible USB device to Windows / Steam.

### What Is Included

- ESP32-S3 firmware binary, bootloader, and partition table.
- Windows Manager single-file exe.
- CH343P flash script, port detector, and serial command script.
- HID report-rate measurement tool.
- Native USB HID feature control probe.
- Bilingual documentation and flashing instructions.

### Release Assets

```text
esp32s3-pro2-bridge-v4.0.0-20260529.zip
esp32s3-pro2-bridge-firmware-v4.0.0-20260529.zip
Y700Switch2Manager-v4.0.0.exe
```

### Core Validation Results

- Steam recognizes the device through the Nintendo Switch Pro / Pro2 controller path.
- Pro2 BLE auto-reconnect, GATT discovery, CCCD subscription, input parsing, and rumble writes are working.
- BLE connection interval is pinned/requested as `6`, which is `7.5 ms`.
- Measured Pro2 BLE input is about `133.3 Hz`.
- USB HID endpoint interval is `1 ms`.
- With the USB report loop set to `1000 Hz`, firmware-side output is about `991-998 Hz`, and the 10-second host-side HID measurement is about `993.3 Hz`.
- When the USB report rate is higher than the BLE input cadence, USB repeats the latest BLE state; real input freshness is represented by `ble_input_actual_mhz`.
- The right-stick neutral drift issue is fixed by using separate auto-center calibration for FD2 and legacy BLE notify streams.
- Rumble / HD rumble produces physical feedback, with the default tune set to `rumble tune 100 180 20 3`.

### Flashing Highlights

1. Connect the board's `CH343P Type-C` port.
2. The native USB & OTG port is used for Windows / Steam HID enumeration and may stay connected; if enumeration does not refresh after flashing, replug native USB.
3. From the extracted release folder, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

4. If the CH343P port is not COM12, first run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

5. If high-speed flashing fails, try:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12 -Baud 115200 -NoStub
```

### What The Manager Panel Shows

- Control transport: CH343P serial, native USB HID feature, or native USB bulk.
- USB state: HID mounted, bulk mounted, and Steam init guard.
- BLE state: connected / connecting / scanning / idle, target, auto reconnect, and live input.
- Rate state: target report rate, actual USB report rate, BLE input Hz, BLE interval, and BLE gaps.
- Diagnostics: HID OUT/GET last values, bulk last values, bulk pending, and BLE update rc/status.
- Rumble state: active/idle, updates, writes, stops, errors, and tuning.

### Known Limits

- This is a research project, not an official Nintendo driver.
- Validation is currently centered on one ESP32-S3-N16R8 board and the current Windows / Steam environment.
- 1000 Hz USB reporting is experimental; it increases the PC-side poll cadence but does not make BLE input exceed the real approximately 133.3 Hz cadence.
- BLE environment, USB cable quality, board revision, and Steam version may affect behavior.
