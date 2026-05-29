# Quickstart / 快速开始

## 中文

这是 V4 ESP32-S3 版本的最短使用流程。

## 需要准备

- ESP32-S3-N16R8 开发板，带 CH343P Type-C 和 native USB & OTG Type-C。
- Windows PC。
- 真实 Switch 2 Pro Controller。
- V4 release 包：`esp32s3-pro2-bridge-v4.0.0-20260529.zip`。

## 1. 解压 release

解压后应能看到：

```text
manager\Y700Switch2Manager.exe
tools\esp32s3\Flash-Pro2Bridge.bat
tools\esp32s3\flash_release.ps1
firmware\esp32s3_switch2_bridge\build\esp32s3_switch2_bridge.bin
```

## 2. 烧录

连接开发板的 `CH343P Type-C` 口，然后在 release 目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

如果端口不是 COM12：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

把命令里的 `COM12` 替换成检测到的 CH343P 端口。

## 3. 连接 native USB

刷完后，连接或重插 native USB & OTG Type-C。Windows 应枚举：

```text
VID_057E PID_2069
Nintendo Switch Pro Controller
Nintendo Switch 2 bulk
```

## 4. 连接 Pro2

固件默认开启 BLE 自动重连。如果之前已保存 Pro2 地址，开机后会自动连接。也可以用 manager 点击“重连”，或运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 30
```

## 5. 打开 Manager

运行：

```text
manager\Y700Switch2Manager.exe
```

面板应能看到：

```text
usb=mounted
bulk=mounted
ble=connected
live=active
BLE input Hz ~= 133.3 Hz
BLE interval ~= 7.50 ms
Actual rate ~= 1000 Hz, if rate is set to 1000
```

## 6. 测速

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-SwitchHidRate.ps1 -Seconds 5
```
## English

This is the shortest path for the V4 ESP32-S3 build.

## Requirements

- ESP32-S3-N16R8 board with CH343P Type-C and native USB & OTG Type-C.
- Windows PC.
- Real Switch 2 Pro Controller.
- V4 release package: `esp32s3-pro2-bridge-v4.0.0-20260529.zip`.

## 1. Extract The Release

After extraction, you should see:

```text
manager\Y700Switch2Manager.exe
tools\esp32s3\Flash-Pro2Bridge.bat
tools\esp32s3\flash_release.ps1
firmware\esp32s3_switch2_bridge\build\esp32s3_switch2_bridge.bin
```

## 2. Flash

Connect the board's `CH343P Type-C` port, then run this from the release folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

If the port is not COM12:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

Replace `COM12` with the detected CH343P port.

## 3. Connect Native USB

After flashing, connect or replug the native USB & OTG Type-C port. Windows should enumerate:

```text
VID_057E PID_2069
Nintendo Switch Pro Controller
Nintendo Switch 2 bulk
```

## 4. Connect The Pro2 Controller

BLE auto-reconnect is enabled by default. If a Pro2 address was saved before, the firmware connects automatically after boot. You can also click "Reconnect" in the manager or run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 30
```

## 5. Open The Manager

Run:

```text
manager\Y700Switch2Manager.exe
```

The panel should show:

```text
usb=mounted
bulk=mounted
ble=connected
live=active
BLE input Hz ~= 133.3 Hz
BLE interval ~= 7.50 ms
Actual rate ~= 1000 Hz, if rate is set to 1000
```

## 6. Measure Report Rate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-SwitchHidRate.ps1 -Seconds 5
```
