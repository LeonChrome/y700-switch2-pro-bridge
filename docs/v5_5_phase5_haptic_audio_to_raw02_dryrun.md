# V5.5 Phase 5 Haptic Audio To raw02 Dry Run

Date: 2026-06-06

## 中文

Phase 5 把 Phase 4 的左右 haptic audio 特征转成 Pro2 `raw02` 候选 payload。默认行为必须是 dry-run：固件只打印和计数，不向真实 Pro2 发送 BLE 震动。

默认安全配置：

```text
live_forwarding=false
dry_run=true
ble_required=true
max_intensity=96
gain=1.0
transient_gain=0.65
min_interval_ms=50
silence_timeout_ms=100
activity_threshold=512
mode=auto
```

转译策略：

- 使用左右 envelope 作为持续强度基础。
- 使用 transient 增强短促冲击。
- 保留左右平衡，避免所有声音都合并成单侧。
- 限制最大强度，避免长时间满强度。
- 按 `min_interval_ms` 限频。
- 静音或音频停止时生成 stop/silence payload。

串口命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic status" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic dryrun on" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic raw02 off" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic test tick" -ReadSeconds 3
```

host 音频测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -ListDevices
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -DeviceName "Wireless Controller" -Pattern both_tick -DurationMs 600 -Intensity 48
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -DeviceName "Wireless Controller" -Pattern texture -DurationMs 1200 -Intensity 36
```

dry-run 成功标准：

```text
audio_streaming=true
audio_packets increases
active_packet_count increases
raw02_dry_packets increases
raw02_live_packets stays unchanged
raw02_left/raw02_right become non-zero for active patterns
raw02_ble_writes stays unchanged
```

当前验证：

```text
host_sender_compile=true
manager_fallback_compile=true
firmware_build_hid_audio_uac1_4ch_ds5like=true
firmware_build_hid_only=true
dry_run_default=true
live_default=false
```

## English

Phase 5 translates left/right haptic-audio features into candidate Pro2 `raw02` payloads. The default is dry-run: the firmware logs and counts packets but does not send live BLE rumble to the real Pro2.

The mapper uses envelope for sustained intensity, transient for impacts, keeps stereo balance, clamps amplitude, rate-limits output, and emits a stop/silence payload when playback stops or becomes silent.

Dry-run success means audio packet counters and `raw02_dry_packets` increase, non-zero left/right payload previews appear for active patterns, and live BLE counters remain unchanged.
