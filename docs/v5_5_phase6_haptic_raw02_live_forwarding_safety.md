# V5.5 Phase 6 Haptic raw02 Live Forwarding Safety

Date: 2026-06-06

## 中文

Phase 6 是实验性 live forwarding：把 haptic audio 转译出的 Pro2 `raw02` payload 通过 BLE 发给真实 Switch 2 Pro Controller。它默认关闭，必须显式打开，并且必须关闭 dry-run 才会真实发送。

必须同时满足：

```text
haptic raw02 on
haptic dryrun off
BLE connected
payload passes raw02 validation
rate limit allows send
not silent
```

安全机制：

- 默认 `live_forwarding=false`。
- 默认 `dry_run=true`。
- BLE 未连接时丢弃并计入 `raw02_dropped_no_ble`。
- 发送失败会记录 `raw02_ble_errors`，并自动关闭 live forwarding。
- 静音、播放停止或 AudioStreaming alt 0 会发 stop/silence payload。
- 没有循环满强度测试；测试 pattern 都是短脉冲或低强度 texture。
- `max_intensity` 和 `min_interval_ms` 可在串口或 Manager 里限制。

真实发送前建议顺序：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic defaults" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 20
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic status" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic raw02 on" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic dryrun off" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic test live tick" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic test live punch" -ReadSeconds 3
```

停止命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic test live stop" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic dryrun on" -ReadSeconds 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic raw02 off" -ReadSeconds 3
```

当前实机状态：

```text
ordinary DualSense HID output -> Pro2 BLE vibration=true
V5.2 raw02 real Pro2 physical vibration=true
V5.5 haptic audio -> raw02 live path implemented=true
V5.5 haptic test live tick=true
V5.5 haptic test live punch=true
V5.5 haptic audio live BLE writes=true
V5.5 haptic audio live physical vibration=transport_verified_user_feel_required
blocked_by_real_pro2=false
blocked_by_user_replug_or_game_test=false_for_test_pattern_true_for_real_game
```

2026-06-07 实机复测已经完成测试 pattern 链路：`haptic test live tick`
与 `haptic test live punch` 均返回 `sent=true`，日志显示
`RUMBLE_RAW02 sent=true active=true`、`DS5_RUMBLE source=raw02 errors=0`。
向 `Wireless Controller Audio` 发送 `both_punch` 后，状态为
`audio_packets=375`、`audio_active=19`、`raw02_live_packets=8`、
`raw02_ble_writes=8`、`raw02_ble_errors=0`。真实游戏仍需确认该游戏是否
实际向 DualSense-like audio endpoint 输出 haptic audio。

## English

Phase 6 is experimental live forwarding. It sends translated Pro2 `raw02` payloads to the real Switch 2 Pro over BLE. It is off by default and requires both `haptic raw02 on` and `haptic dryrun off`.

Live forwarding also requires BLE connected, validated payloads, rate-limit approval, and non-silent haptic input. BLE errors automatically disable live forwarding. Silence, playback stop, and AudioStreaming alt 0 emit stop/silence payloads.

The first live transport test produced successful BLE writes but no perceptible
physical vibration. The cause was an incorrect amplitude normalization divisor:
a 16-bit Switch rumble amplitude such as `28992` was mapped to only `10/1023`.
It is now mapped against `65535`, yielding about `453/1023`, and active raw02
frames are held for 120 ms and resent every 20 ms. A successful GATT write is
still reported as transport success only; physical vibration requires human
confirmation.

The generated calibration frames now use a real Switch rumble-frame encoder.
Low, medium, and high tests keep the same frequency pair (`LF=274`, `HF=391`)
and vary only both amplitudes (`170`, `341`, `512` out of `1023`). The previous
ad-hoc byte shaping changed frequency while leaving one amplitude nearly fixed,
so it was not a valid intensity comparison.

The 2026-06-07 retest verified the live path on hardware: live Tick and Punch
one-shots both produced active raw02 BLE writes with zero errors, and a
`both_punch` audio pattern sent to `Wireless Controller Audio` produced
`audio_packets=375`, `audio_active=19`, `raw02_live_packets=8`,
`raw02_ble_writes=8`, and `raw02_ble_errors=0`. Real games still depend on
their native DualSense haptic-audio output behavior.
