# v0.1.0-experimental Release Notes

中文：

这是第一个公开实验版 release。它已经在作者环境和另一台 Windows 电脑上完成过基本验证，但仍然不是稳定的一键安装包。

## 推荐 release 资产

这个版本建议发布两个文件：

```text
Y700Switch2Launcher.exe
y700-switch2-y700-payload-v0.1.0-experimental.zip
```

不建议只发布单个 exe。当前 `Y700Switch2Launcher.exe` 可以把 Y700 端文件推送到平板，但它没有把 Android/Y700 端 jar 和 setup 脚本嵌入 exe 内部。也就是说，单独一个 exe 并不能完整启动桥接流程。

## 两个文件分别是什么

`Y700Switch2Launcher.exe` 是 Windows 侧启动器，用于：

- 查找或使用指定的 `adb.exe`
- 选择 Y700 ADB 设备
- 推送 Y700 端 jar 和 setup 脚本
- 启动 USB gadget/responder
- 启动 BLE bridge
- 查看状态、测试震动、停止进程、拉取日志

`y700-switch2-y700-payload-v0.1.0-experimental.zip` 是 Y700/Android 端 payload，里面包含：

- `switch2_ble_bridge_v3.jar`
- `switch2_ffs_responder_v3.jar`
- `setup_y700_switch2_proto_v3.sh`
- `setup_y700_switch2_proto_v3_detached.sh`
- v3 辅助脚本和 manifest

使用时可以把 payload zip 解压到和 `Y700Switch2Launcher.exe` 同一个文件夹，然后运行 launcher。

## 已验证

- Switch 2 Pro Controller 通过 BLE 连接到 Y700。
- Y700 通过 USB Gadget / FunctionFS 暴露 Nintendo 风格 USB HID。
- Windows / Steam 可识别并接收输入。
- A/B/X/Y、方向键、肩键、摇杆、`C`、`GL`、`GR` 已验证。
- 基础震动/HD rumble 路径已验证有物理反馈。
- release 文件夹已在另一台 Windows 电脑上完成过测试。

## 仍然不保证可靠

- 需要 root 后的 Lenovo Y700。
- 强烈建议使用无线 ADB；重配 USB gadget 时可能断开 USB ADB。
- 不同系统版本、Steam 版本、ADB 环境、Y700 固件可能表现不同。
- 这不是 Nintendo 官方驱动，也不是成熟产品安装器。
- 当前 exe 没有嵌入 payload，因此 release 需要两个文件配合使用。

---

English:

This is the first public experimental release. It has been validated in the author's environment and on another Windows PC, but it is not a polished one-click installer.

## Recommended Release Assets

This release should ship as two files:

```text
Y700Switch2Launcher.exe
y700-switch2-y700-payload-v0.1.0-experimental.zip
```

A single exe is not recommended yet. The Windows launcher can push files to the Y700, but the Android/Y700 jars and setup script are not embedded inside the exe. The exe alone is therefore not enough to start the full bridge.

## What The Files Are

`Y700Switch2Launcher.exe` is the Windows-side launcher. It finds or uses `adb.exe`, selects the Y700 ADB device, pushes the Y700 payload, starts the USB gadget/responder, starts the BLE bridge, checks status, sends a rumble smoke test, stops runtime processes, and pulls logs.

`y700-switch2-y700-payload-v0.1.0-experimental.zip` is the Y700/Android-side payload. Extract it next to `Y700Switch2Launcher.exe` before running the launcher.

## Reliability Warning

This requires a rooted Lenovo Y700. Wireless ADB is strongly recommended. Different firmware, Steam, Windows, and ADB environments may behave differently. This is a research build, not an official driver or mature installer.
