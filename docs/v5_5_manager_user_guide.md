# V5.5 Manager User Guide

Date: 2026-06-06

## 中文

V5.5 是实验版本，目标是验证：

```text
PC / Steam / game
-> ESP32-S3 DualSense-like HID + UAC1 4ch audio
-> haptic audio channel 2/3
-> Pro2 raw02 payload
-> BLE real Switch 2 Pro Controller
```

普通用户仍建议使用 V5.0.0 stable Manager。V5.5 适合研究 haptic audio 和 raw02 live forwarding。

### 打包

从仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package_v5_5_manager.ps1
```

生成：

```text
.\release\v5.5\Y700Switch2V55Manager-aio-v5.5.0.exe
.\release\v5.5\SHA256SUMS-v5.5.0.txt
```

### 首次使用

1. 连接 CH343P Type-C。
2. 打开 `.\release\v5.5\Y700Switch2V55Manager-aio-v5.5.0.exe`。
3. 选择 COM 口。
4. 点击 `一键刷 V5.5`。
5. 重插 native USB / OTG 口。
6. 点击 `快速检查` 和 `音频列表`。
7. 点击 `扫描` 或 `连上次` 连接真实 Pro2。
8. 先保持 `Dry-run On` 和 `Live Off`。
9. 点击 `音频列表` 和 `发送音频 Pattern`，观察日志里的 audio/raw02 counters。

### 开启真实震动

确认 BLE connected、dry-run 已有非零 raw02 preview 后，再点击 `Live On`。Manager 会要求二次确认。

推荐先只测短脉冲：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -DeviceName "Wireless Controller" -Pattern both_tick -DurationMs 600 -Intensity 48
```

停止：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic test stop" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic dryrun on" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic raw02 off" -ReadSeconds 3
```

### 故障判断

```text
Audio endpoint only shows 2ch:
  reflash V5.5 4ch profile and replug native USB.

HID disappeared:
  flash HID-only Recovery, replug native USB, then rerun checks.

raw02 preview changes but no physical vibration:
  confirm BLE connected, Live On, Dry-run Off, and raw02_ble_errors remains 0.

game has no haptic audio:
  use the host haptic audio sender first; game support depends on its native DualSense path.
```

## English

V5.5 is experimental. It validates the chain from PC/Steam/game to ESP32-S3 DualSense-like HID + UAC1 4ch audio, haptic channels 2/3, Pro2 raw02 payloads, and BLE output to the real Switch 2 Pro.

Normal users should stay on the V5.0.0 stable Manager. V5.5 is for haptic-audio and raw02 live-forwarding research.

Package from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package_v5_5_manager.ps1
```

Run `.\release\v5.5\Y700Switch2V55Manager-aio-v5.5.0.exe`, flash the V5.5 haptic profile, replug native USB, run the USB checks, connect the real Pro2 over BLE, keep dry-run on first, then test audio patterns before enabling live forwarding.
