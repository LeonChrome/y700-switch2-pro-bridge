# V5.5 Manager App Design

Date: 2026-06-06

## 中文

V5.5 Manager 是独立实验 Manager，不替换 V5.0/V5.2 正式 GUI。它的目标是把 V5.5 的烧录、USB 检查、BLE 连接、haptic audio 测试、raw02 live 安全开关和串口日志放到一个页面里，方便实机快速验证。

布局：

```text
top status: Mode / FW / USB / BLE / Haptic / Raw02
left: Flash, Mode, BLE
right: USB Checks, Haptic Audio / raw02, Log
```

主要按钮：

- Flash V5.5 DualSense-Pro2 Haptic：刷入 `hid_audio_uac1_4ch_ds5like`。
- Flash HID-only Recovery：刷入 `hid_only`，用于恢复 HID 枚举。
- Composite / Identity / Audio / Reports：运行现有 V5.5 检查脚本。
- Scan / List / Connect / Connect Last / Auto On / Auto Off / Disconnect：BLE 控制。
- Status / Apply / Defaults：读取或设置 haptic 参数。
- Dry-run On / Live Off / Live On / Stop：raw02 安全开关。
- Test Tick / Test Punch / Send Audio Pattern：短脉冲测试。
- Custom command：直接发送串口命令。

安全交互：

- `Live On` 会弹出确认框。
- live 开启会依次发送 `haptic raw02 on` 与 `haptic dryrun off`。
- `Live Off` 会恢复 `haptic dryrun on` 与 `haptic raw02 off`。
- `Stop` 会发送 `haptic test stop`。
- Manager 不静默安装驱动，不修改 V5.0/V5.2 默认固件。

构建策略：

- 有 .NET SDK 时使用 `dotnet publish`。
- 没有 .NET SDK 时使用 Windows 自带 .NET Framework `csc.exe` fallback 编译 WPF exe。
- WPF 引用从 reference assemblies 或 GAC 动态定位，不硬编码用户路径。

打包命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package_v5_5_manager.ps1
```

## English

The V5.5 Manager is a standalone experimental manager. It does not replace the V5.0/V5.2 stable GUI. It provides one page for flashing, USB checks, BLE connection, haptic-audio tests, raw02 safety toggles, and serial logs.

It supports a .NET SDK publish path and a Windows .NET Framework `csc.exe` fallback path. WPF references are resolved dynamically from reference assemblies or the GAC.
