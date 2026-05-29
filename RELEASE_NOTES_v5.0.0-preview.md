# V5.0.0 Preview Release Notes / V5.0.0 预览发布说明

Date: 2026-05-29

## 中文

V5.0.0 Preview 是 All-in-one Manager / Flasher 预览版。它面向不会手动烧录 ESP32-S3 的普通用户：一个 EXE 同时包含固件、烧录器和控制面板。

### 发布文件

```text
Y700Switch2Manager-aio-v5.0.0-preview.exe
```

### 这版包含什么

- 内置当前稳定 ESP32-S3 固件 payload：firmware `4.0.0`。
- 内置 bootloader、partition table 和 app bin。
- 内置独立 esptool `4.11.0`，不要求用户安装 ESP-IDF 或 Python。
- 支持一键刷入、修复重刷、仅清除、清除并重刷。
- 刷录前读取芯片信息，只允许 ESP32-S3 继续写入。
- CH343P / CH340 / WCH COM 口识别。
- CH343 / CH341 官方驱动下载入口和本地 driver 包目录入口。
- 启动后自动连接控制通道并同步状态。
- BLE 自动辅助、搜索手柄、读取候选列表、连接选中设备。
- Manager 面板继续显示 USB、BLE、Actual rate、BLE input Hz、BLE interval、rumble、HID OUT/GET、bulk 等状态。

### 已验证

- `仅清除` 路径已在当前 ESP32-S3 开发板上实测通过。
- `清除并重刷` 路径已在当前 ESP32-S3 开发板上实测通过。
- 刷录后 `bootloader`、`partition table`、`app` 写入 hash verified。
- 刷后 status：`version=4.0.0`、`usb=mounted`、`bulk=mounted`、`hid_guard=done`。
- 新增固件命令 `ble list` 可返回 BLE 扫描缓存 JSON，Manager 可展示候选列表。

### 使用重点

1. 插开发板的 `CH343P Type-C` 口。
2. 打开 `Y700Switch2Manager-aio-v5.0.0-preview.exe`。
3. EXE 会自动识别 COM 口并尝试连接。
4. 正常新板选择 `一键刷入`。
5. 想模拟空白新板可先点 `仅清除`，然后再点 `一键刷入`。
6. 如果状态异常，使用 `清除并重刷` 或 `修复重刷`。
7. BLE 手柄地址不固定时，使用 `搜索手柄`，再从候选列表里选择标记 `推荐` 的设备尝试连接。

## English

V5.0.0 Preview is the first All-in-one Manager / Flasher preview. It is designed for users who do not want to manually flash an ESP32-S3 board: one EXE contains the firmware payload, flasher, and control panel.

### Release Asset

```text
Y700Switch2Manager-aio-v5.0.0-preview.exe
```

### What Is Included

- Bundled stable ESP32-S3 firmware payload: firmware `4.0.0`.
- Bundled bootloader, partition table, and app bin.
- Bundled standalone esptool `4.11.0`; ESP-IDF and Python are not required on the user's PC.
- One-click flash, repair reflash, erase-only, and erase-and-flash.
- Chip identity check before flashing; only ESP32-S3 is allowed to continue.
- CH343P / CH340 / WCH COM-port detection.
- Official CH343 / CH341 driver links and a local driver-package folder hook.
- Automatic control-channel connection and status sync on startup.
- BLE assist, controller search, candidate list, and connect-selected flow.
- The Manager panel continues to show USB, BLE, Actual rate, BLE input Hz, BLE interval, rumble, HID OUT/GET, and bulk diagnostics.

### Verified

- `Erase only` was tested on the current ESP32-S3 development board.
- `Erase and flash` was tested on the current ESP32-S3 development board.
- Bootloader, partition table, and app writes completed with hash verification.
- Post-flash status: `version=4.0.0`, `usb=mounted`, `bulk=mounted`, `hid_guard=done`.
- New firmware command `ble list` returns BLE scan-cache JSON, and the Manager can show candidate devices.

### Quick Use

1. Connect the board's `CH343P Type-C` port.
2. Open `Y700Switch2Manager-aio-v5.0.0-preview.exe`.
3. The EXE auto-detects the COM port and tries to connect.
4. Use `一键刷入` for a normal new board.
5. To simulate a blank board, use `仅清除`, then `一键刷入`.
6. If the board state is unusual, use `清除并重刷` or `修复重刷`.
7. If the BLE controller address is unknown, use `搜索手柄`, then choose a candidate marked `推荐`.
