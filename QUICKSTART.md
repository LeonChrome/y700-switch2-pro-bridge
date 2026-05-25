# Quickstart

中文快速上手：

这是在另一台 Windows 电脑上使用 v3 稳定包的最短流程。

## 需要准备

- 已 root 的联想 Y700 平板。
- Y700 已开启无线调试，或至少能通过 ADB 连接。
- Switch 2 Pro 手柄可以通过蓝牙连接到 Y700。
- Windows 电脑上有 `adb.exe`，可以放在 `PATH`、放在 `Y700Switch2Launcher.exe` 同目录，或用 `--adb` 指定路径。
- 稳定版 release 文件夹：

```text
release/v3-stable-20260525-input-rumble
```

## 必要文件

release 文件夹里至少需要：

```text
Y700Switch2Launcher.exe
switch2_ble_bridge_v3.jar
switch2_ffs_responder_v3.jar
setup_y700_switch2_proto_v3.sh
```

## 启动

在稳定版 release 文件夹里打开 PowerShell：

```powershell
.\Y700Switch2Launcher.exe start
```

更推荐使用无线 ADB serial，因为重配 USB gadget 时可能会断开 USB ADB：

```powershell
.\Y700Switch2Launcher.exe start --serial 192.168.x.x:port
```

如果 `adb.exe` 不在 `PATH` 里：

```powershell
.\Y700Switch2Launcher.exe start --adb C:\path\to\adb.exe --serial 192.168.x.x:port
```

## 查看状态

```powershell
.\Y700Switch2Launcher.exe status --serial 192.168.x.x:port
```

## 测试震动

```powershell
.\Y700Switch2Launcher.exe haptic-test --serial 192.168.x.x:port
```

## 停止

```powershell
.\Y700Switch2Launcher.exe stop --serial 192.168.x.x:port
```

## 拉取日志

```powershell
.\Y700Switch2Launcher.exe logs --serial 192.168.x.x:port
```

---

English:

This is the shortest path for using the stable v3 package on another Windows PC.

## Requirements

- Rooted Lenovo Y700 tablet with wireless debugging or USB ADB access.
- Switch 2 Pro Controller paired or ready to connect to the Y700 over Bluetooth.
- Windows PC with `adb.exe` available in `PATH`, beside `Y700Switch2Launcher.exe`, or passed with `--adb`.
- The stable artifact folder:

```text
release/v3-stable-20260525-input-rumble
```

## Files Needed

The stable folder should include at least:

```text
Y700Switch2Launcher.exe
switch2_ble_bridge_v3.jar
switch2_ffs_responder_v3.jar
setup_y700_switch2_proto_v3.sh
```

## Start

Open PowerShell in the stable release folder.

```powershell
.\Y700Switch2Launcher.exe start
```

Wireless ADB is strongly recommended, because reconfiguring the USB gadget can disconnect USB ADB:

```powershell
.\Y700Switch2Launcher.exe start --serial 192.168.x.x:port
```

If `adb.exe` is not in `PATH`:

```powershell
.\Y700Switch2Launcher.exe start --adb C:\path\to\adb.exe --serial 192.168.x.x:port
```

## Check Status

```powershell
.\Y700Switch2Launcher.exe status --serial 192.168.x.x:port
```

## Rumble Smoke Test

```powershell
.\Y700Switch2Launcher.exe haptic-test --serial 192.168.x.x:port
```

## Stop

```powershell
.\Y700Switch2Launcher.exe stop --serial 192.168.x.x:port
```

## Pull Logs

```powershell
.\Y700Switch2Launcher.exe logs --serial 192.168.x.x:port
```
