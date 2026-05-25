# Y700 Switch 2 Pro Bridge

中文：

这个项目把一台已经 root 的联想 Y700 平板用作 Switch 2 Pro 手柄的 BLE 转 USB 桥接器。真实的 Switch 2 Pro 手柄通过蓝牙连接到 Y700，Y700 读取手柄的私有 BLE GATT 输入通知，然后在 USB 侧模拟 Nintendo 风格的 HID 设备，让 Windows / Steam 按 Nintendo 控制器路径识别它。

English:

This project uses a rooted Lenovo Y700 tablet as a BLE-to-USB bridge for a real Switch 2 Pro Controller. The controller connects to the Y700 over BLE; the Y700 reads the controller's private BLE GATT notifications and exposes a Nintendo-style USB HID device to Windows / Steam.

## 当前状态 / Current Status

中文：

当前 v3 稳定版已经完成作者环境和另一台 Windows 电脑的基本验证。按键转发、Switch 2 Pro 额外按键，以及基础震动/HD rumble 路径都已经跑通。但它仍然是实验项目，不是成熟的一键安装包，也不是 Nintendo 官方驱动。

English:

The current v3 stable build has been validated in the author's environment and on another Windows PC. Button forwarding, Switch 2 Pro extra buttons, and the basic rumble / HD rumble path are working. It is still an experimental research project, not a polished one-click installer or an official Nintendo driver.

## 快速上手 / Quickstart

中文：

最短使用流程见：

English:

For the shortest setup path, see:

[QUICKSTART.md](QUICKSTART.md)

## 实验版 Release / Experimental Release

中文：

当前公开 release 是 `v0.1.0-experimental`。我不建议只发布单个 exe，因为当前 `Y700Switch2Launcher.exe` 可以推送 Y700 端文件，但没有把 Android/Y700 端 jar 和 setup 脚本嵌入 exe 内部。因此 release 采用两个文件：

English:

The current public release is `v0.1.0-experimental`. A single exe is not recommended yet because `Y700Switch2Launcher.exe` can push files to the Y700, but the Android/Y700 jars and setup script are not embedded inside the exe. Therefore the release ships as two files:

```text
Y700Switch2Launcher.exe
y700-switch2-y700-payload-v0.1.0-experimental.zip
```

中文：

使用时请把 payload zip 解压到和 `Y700Switch2Launcher.exe` 同一个文件夹，再运行 launcher。这个 release 已经明确标注为实验版，不保证在所有 Y700 固件、Windows、Steam 或 ADB 环境下可靠。

English:

Extract the payload zip next to `Y700Switch2Launcher.exe`, then run the launcher. This release is explicitly marked experimental and is not guaranteed to work across all Y700 firmware, Windows, Steam, or ADB environments.

Release notes:

[RELEASE_NOTES_v0.1.0-experimental.md](RELEASE_NOTES_v0.1.0-experimental.md)

## 需要准备 / Requirements

中文：

- 已 root 的 Lenovo Y700 平板。
- Y700 已开启无线调试，或至少能通过 ADB 连接。
- Switch 2 Pro Controller 可以通过蓝牙连接到 Y700。
- Windows PC 上有 `adb.exe`，可以放在 `PATH`、放在 launcher 同目录，或用 `--adb` 指定路径。
- 推荐使用无线 ADB，因为重配 USB gadget 时可能断开 USB ADB。

English:

- A rooted Lenovo Y700 tablet.
- Wireless debugging enabled on the Y700, or at least working ADB access.
- A Switch 2 Pro Controller that can connect to the Y700 over Bluetooth.
- `adb.exe` available on the Windows PC, either in `PATH`, next to the launcher, or passed with `--adb`.
- Wireless ADB is recommended because reconfiguring the USB gadget can disconnect USB ADB.

## 架构 / Architecture

中文：

当前工作链路：

English:

Current working route:

```text
real Switch 2 Pro Controller
-> BLE connection to Lenovo Y700
-> Y700 BLE bridge parses private GATT notifications
-> /data/local/tmp/switch2_state.txt
-> Y700 USB Gadget / FunctionFS responder
-> Nintendo-style USB HID device
-> Windows / Steam Nintendo controller path
```

中文：

核心思路不是让手柄直接连 Windows，而是让 Y700 承担中间桥的角色：一边和真实手柄走 BLE，一边对 Windows 暴露 USB HID。

English:

The core idea is not to connect the controller directly to Windows. Instead, the Y700 acts as the bridge: BLE on the real-controller side, USB HID on the Windows side.

## 已验证能力 / Verified Capabilities

中文：

- Switch 2 Pro 手柄通过 BLE 连接到 Y700。
- Y700 读取 BLE 输入并写入 Android 本地状态文件。
- Y700 通过 USB Gadget / FunctionFS 暴露 Nintendo 风格 USB HID。
- Windows / Steam 可走 Nintendo 控制器识别路径。
- 已验证 A/B/X/Y、方向键、肩键、摇杆、摇杆按下、`+`、`-`。
- 已验证 Switch 2 Pro 额外按键 `C`、`GL`、`GR`。
- 已验证基础震动/HD rumble 路径有物理反馈。
- Windows 侧 `Y700Switch2Launcher.exe` 可部署、启动、查看状态、测试震动、停止和拉取日志。

English:

- The Switch 2 Pro Controller connects to the Y700 over BLE.
- The Y700 reads BLE input and writes an Android-local state file.
- The Y700 exposes a Nintendo-style USB HID device through USB Gadget / FunctionFS.
- Windows / Steam can use the Nintendo controller recognition path.
- A/B/X/Y, D-pad, shoulders, sticks, stick clicks, `+`, and `-` have been verified.
- Switch 2 Pro extra buttons `C`, `GL`, and `GR` have been verified.
- The basic rumble / HD rumble path has produced physical feedback.
- The Windows-side `Y700Switch2Launcher.exe` can deploy, start, check status, test rumble, stop, and pull logs.

## Windows Launcher 用法 / Windows Launcher Usage

中文：

在 release 文件夹里打开 PowerShell：

English:

Open PowerShell in the release folder:

```powershell
.\Y700Switch2Launcher.exe start
```

中文：

推荐显式指定无线 ADB serial：

English:

Passing a wireless ADB serial is recommended:

```powershell
.\Y700Switch2Launcher.exe start --serial 192.168.x.x:port
```

中文：

如果 `adb.exe` 不在 `PATH`：

English:

If `adb.exe` is not in `PATH`:

```powershell
.\Y700Switch2Launcher.exe start --adb C:\path\to\adb.exe --serial 192.168.x.x:port
```

中文：

常用命令：

English:

Common commands:

```powershell
.\Y700Switch2Launcher.exe status --serial 192.168.x.x:port
.\Y700Switch2Launcher.exe haptic-test --serial 192.168.x.x:port
.\Y700Switch2Launcher.exe stop --serial 192.168.x.x:port
.\Y700Switch2Launcher.exe logs --serial 192.168.x.x:port
```

## 稳定包 / Stable Package

中文：

当前已验证稳定包位于：

English:

The current validated stable package is:

```text
release/v3-stable-20260525-input-rumble
```

中文：

这个目录冻结了当前已验证的 v3 artifacts，包括 Windows launcher、Y700 端 BLE bridge jar、FunctionFS responder jar、setup 脚本、辅助 PowerShell 脚本和 manifest。

English:

This folder freezes the validated v3 artifacts, including the Windows launcher, Y700-side BLE bridge jar, FunctionFS responder jar, setup script, helper PowerShell scripts, and manifest.

## 关键运行文件 / Key Runtime Files

中文：

Y700/Android 端主要文件：

English:

Main Y700/Android-side files:

```text
/data/local/tmp/switch2_ble_bridge_v3.jar
/data/local/tmp/switch2_ffs_responder_v3.jar
/data/local/tmp/setup_y700_switch2_proto_v3.sh
/data/local/tmp/switch2_state.txt
/data/local/tmp/switch2_ble_write_v3.txt
```

中文：

Windows/release 侧主要文件：

English:

Main Windows/release-side files:

```text
Y700Switch2Launcher.exe
switch2_ble_bridge_v3.jar
switch2_ffs_responder_v3.jar
setup_y700_switch2_proto_v3.sh
MANIFEST.md
```

## USB Gadget 信息 / USB Gadget Details

中文：

当前目标 USB gadget 状态：

English:

Current target USB gadget state:

```text
Gadget path: /config/usb_gadget/g1
UDC: a600000.dwc3
HID node: /dev/hidg0
FunctionFS path: /dev/usb-ffs/switch2
VID/PID: 057e:2069
Manufacturer: Nintendo Co., Ltd.
Product: Nintendo Switch Pro Controller
```

中文：

Steam / Windows 侧的目标是走 Nintendo/Switch 控制器识别路径，而不是普通 generic HID gamepad 路径。

English:

The goal on the Steam / Windows side is to enter the Nintendo/Switch controller path rather than the generic HID gamepad path.

## BLE 与输入映射 / BLE And Input Mapping

中文：

v3 bridge 同时处理两个输入通知来源：

English:

The v3 bridge handles two input notification sources:

```text
ab7de9be-89fe-49ad-828f-118f09df7fd2
7492866c-ec3e-4619-8258-32755ffcc0f8
```

中文：

`ab7...fd2` 按较新的 32-bit button field 解析，`749...cc0f8` 按旧的 byte2/byte3/byte4 输入流解析。两者最终都写入 `/data/local/tmp/switch2_state.txt`，供 USB responder 读取。

English:

`ab7...fd2` is parsed as the newer 32-bit button field. `749...cc0f8` is parsed as the legacy byte2/byte3/byte4 input stream. Both paths write to `/data/local/tmp/switch2_state.txt`, which is consumed by the USB responder.

## 震动 / Rumble

中文：

当前 v3 已验证基础震动/HD rumble 路径有物理反馈。它不是最终的高质量 HD rumble 翻译器，但已经证明 Windows/Steam 侧 rumble 事件可以经由 Y700 转发到真实 Switch 2 Pro 手柄。

English:

The current v3 build has verified physical feedback through the basic rumble / HD rumble path. It is not a final high-quality HD rumble translator, but it proves that Windows/Steam rumble events can be forwarded through the Y700 to the real Switch 2 Pro Controller.

中文：

快速震动测试：

English:

Quick rumble smoke test:

```powershell
.\Y700Switch2Launcher.exe haptic-test --serial 192.168.x.x:port
```

## 仓库结构 / Repository Layout

中文：

主要目录和文件：

English:

Main folders and files:

```text
src/
  Switch2BleBridgeV3.java
  Switch2FfsResponderV3.java

tools/
  Y700Switch2Launcher.cs
  helper scripts and research tools

release/v3-stable-20260525-input-rumble/
  frozen stable v3 package

Y700Switch2Launcher.exe
  Windows launcher build output

QUICKSTART.md
  short setup guide

ACKNOWLEDGEMENTS.md
  project acknowledgements

RELEASE_NOTES_v0.1.0-experimental.md
  experimental release notes
```

## 研究记录 / Research Notes

中文：

更详细的研究过程、稳定节点、rumble 记录和 payload 实验保存在这些文件里：

English:

Detailed research notes, stable checkpoints, rumble notes, and payload experiments are kept in:

```text
STABLE_CHECKPOINT_20260525_V3_INPUT_RUMBLE.md
switch2_v3_hd_bridge_notes.md
switch2_steam_rumble_notes.md
switch2_hd_rumble_research.md
switch2_rumble_presets.md
switch2_payload_human_observations.md
release/v3-stable-20260525-input-rumble/MANIFEST.md
```

## 致谢 / Acknowledgements

中文：

本项目受到 `switch2-controller-windows10-dual-layouts` 以及相关 Switch 2 手柄 Windows 兼容性研究的启发。这里的实现路线不同：不是让手柄直接通过 BLE 连到 Windows，而是使用 root 后的 Y700 作为中间桥，解析 Switch 2 Pro 手柄的私有 BLE GATT 数据，再通过 USB Gadget / FunctionFS 向 Windows 和 Steam 暴露 Nintendo 风格的 HID 设备。

English:

This project was inspired in part by `switch2-controller-windows10-dual-layouts` and community research around Switch 2 controller layouts on Windows. This implementation uses a different route: instead of connecting the controller directly to Windows over BLE, a rooted Y700 acts as the bridge, parses the Switch 2 Pro Controller's private BLE GATT data, and exposes a Nintendo-style HID device to Windows and Steam through USB Gadget / FunctionFS.

完整致谢 / Full note:

[ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md)

## 限制与风险 / Limitations And Risks

中文：

- 需要 root 后的 Y700。
- 重配 USB gadget 有可能断开 USB ADB，所以推荐无线 ADB。
- 不同 Y700 固件、Windows、Steam、ADB 环境可能有差异。
- 当前 release 是实验版，不保证一键成功。
- 当前 Windows exe 没有内嵌 Android/Y700 payload。
- 本项目与 Nintendo 无关，也不是官方驱动。

English:

- A rooted Y700 is required.
- Reconfiguring the USB gadget may disconnect USB ADB, so wireless ADB is recommended.
- Different Y700 firmware, Windows, Steam, and ADB environments may behave differently.
- The current release is experimental and is not guaranteed to work as a one-click package.
- The current Windows exe does not embed the Android/Y700 payload.
- This project is not affiliated with Nintendo and is not an official driver.

## 后续方向 / Future Direction

中文：

这个阶段先告一段落。后续如果要继续演进，一个值得考虑的方向是用树莓派或类似开发板直接承担 Y700 当前的桥接角色：BLE 连接真实 Switch 2 Pro 手柄，同时在 USB 侧模拟 Nintendo 风格设备。这个方向需要等开发板到手后再验证。

English:

This stage is considered complete for now. A possible future direction is replacing the Y700 bridge role with a Raspberry Pi or a similar development board: BLE to the real Switch 2 Pro Controller on one side, Nintendo-style USB device emulation on the other. That direction should be revisited after the development board is available for testing.
