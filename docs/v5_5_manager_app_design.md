# V5.5 Manager App Design

Date: 2026-06-06

## 中文

V5.5 Manager 是独立实验 Manager，不替换 V5.0/V5.2 正式 GUI。它的目标是把 V5.5 的烧录、USB 检查、BLE 连接、haptic audio 测试、raw02 live 安全开关和串口日志放到一个页面里，方便实机快速验证。

布局：

```text
top status: product identity and current action
left: Port/flash, USB/audio checks, BLE, haptic/raw02
right: firmware summary, safety summary, audio pattern sender, log console
```

主要按钮：

- `一键刷 V5.5`：用内嵌 esptool 和内嵌固件刷入 `hid_audio_uac1_4ch_ds5like`，不依赖 ESP-IDF。
- `HID 恢复`：刷入 `hid_only`，用于恢复 HID 枚举。
- `快速检查` / `音频列表`：检查 Windows USB device 和音频 endpoint。
- `扫描` / `列表` / `连上次` / `自动开` / `自动关` / `断开`：BLE 控制。
- `状态` / `Dry-run 开` / `Live 关` / `Live 开` / `Stop`：raw02 安全开关。
- `Tick` / `Punch` / `发送音频 Pattern`：短脉冲和 channel 2/3 音频测试。
- 自定义命令框：直接发送串口命令。

安全交互：

- `Live On` 会弹出确认框。
- live 开启会依次发送 `haptic raw02 on` 与 `haptic dryrun off`。
- `Live Off` 会恢复 `haptic dryrun on` 与 `haptic raw02 off`。
- `Stop` 会发送 `haptic test stop`。
- Manager 不静默安装驱动，不修改 V5.0/V5.2 默认固件。

构建策略：

- 有 .NET SDK 时使用 `dotnet publish`。
- 没有 .NET SDK 时，打包脚本自动下载本地 .NET 8 SDK 到 `.\work\dotnet`。
- 输出是 self-contained single-file EXE，内嵌 V5.5 固件、HID-only recovery、esptool 和 haptic audio sender。

打包命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package_v5_5_manager.ps1
```

## English

The V5.5 Manager is a standalone experimental manager. It does not replace the V5.0/V5.2 stable GUI. It provides one page for flashing, USB checks, BLE connection, haptic-audio tests, raw02 safety toggles, and serial logs.

It publishes a self-contained single-file EXE with embedded V5.5 firmware, HID-only recovery firmware, esptool, and the haptic audio sender. If no .NET SDK is present, the packaging script downloads a local .NET 8 SDK under `.\work\dotnet`.
