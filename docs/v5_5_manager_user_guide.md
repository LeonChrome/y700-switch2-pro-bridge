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
4. 点击 `刷 DualSense 触觉`，或点击 `刷 Pro2 原生` 回到 Switch2/Pro2 原生桥接。
5. 重插 native USB / OTG 口。
6. 点击 `快速检查` 和 `音频列表`。
7. 点击 `扫描` 或 `连上次` 连接真实 Pro2。
8. 先保持 `Dry-run On` 和 `Live Off`。
9. 点击 `Tick 实震` / `Punch 实震` 做 raw02 live one-shot，或点击 `发送音频实震` 做一次 4ch audio -> raw02 -> BLE 测试。

### 开启真实震动

确认 BLE connected、Tick/Punch 或音频实震已有非零 BLE writes 后，再点击 `Live On`。Manager 会要求二次确认。

推荐先只测短脉冲：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -DeviceName "Wireless Controller" -Pattern both_tick -DurationMs 600 -Intensity 48
```

停止：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic test live stop" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic dryrun on" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic raw02 off" -ReadSeconds 3
```

### 2026-06-07 实测基线

```text
manager_profiles=hid_audio_uac1_4ch_ds5like,hid_only,pro2_bridge_v5_5
DualSense_identity=VID_054C/PID_0CE6
Pro2_BLE=connected
Tick_live_one_shot=sent=true, errors=0
Punch_live_one_shot=sent=true, errors=0
audio_pattern=both_punch
audio_packets=375
audio_active=19
raw02_live_packets=8
raw02_ble_writes=8
raw02_ble_errors=0
```

这说明测试音频源已经能驱动 Pro2 raw02。游戏侧是否有明显触觉，取决于游戏是否把 DualSense haptic audio 输出到 `Wireless Controller Audio`。

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

Run `.\release\v5.5\Y700Switch2V55Manager-aio-v5.5.0.exe`, flash either the DualSense haptic profile or the native Pro2 bridge profile, replug native USB, run the USB checks, connect the real Pro2 over BLE, then use the Tick/Punch one-shot or audio haptic test before enabling live forwarding for games.

The 2026-06-07 baseline verified non-zero BLE writes and zero BLE errors for both live raw02 one-shots and the `both_punch` audio pattern. Game haptics still depend on the game sending DualSense haptic audio to `Wireless Controller Audio`.
