# V5.5 Phase 4 Haptic Audio Channel Analysis

Date: 2026-06-06

## 中文

Phase 4 的目标是把 Windows 发送到 ESP32-S3 的四声道 USB Audio OUT 变成可观测的触觉特征，而不是先追求“完全复刻 DualSense”。当前固件使用 `hid_audio_uac1_4ch_ds5like` profile：Windows 看到 DualSense-like HID 和一个 4ch/48 kHz/16-bit render endpoint，ESP32-S3 在 AudioStreaming alt 1 下接收每毫秒 384 bytes 的等时 OUT 包。

当前通道约定：

```text
channel 0: 普通左声道，占位，不参与 Pro2 触觉
channel 1: 普通右声道，占位，不参与 Pro2 触觉
channel 2: 左侧 haptic source
channel 3: 右侧 haptic source
```

`dualsense_haptic_audio` 会对 channel 2/3 计算：

```text
rms_l / rms_r
peak_l / peak_r
mean_abs_l / mean_abs_r
envelope_l / envelope_r
transient_l / transient_r
active_packet_count
silence_packet_count
source_channels
streaming
```

这些值通过串口 `status` / `haptic status` 返回，也会以 `[DS5_HAPTIC_AUDIO]` 日志限频输出。通道 0/1 暂时只用于保持 DualSense-like 音频设备形状；V5.5 不实现扬声器播放、麦克风输入或语音功能。

验证命令从仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -ListDevices
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -DeviceName "Wireless Controller" -Pattern ch2_tick -DurationMs 600 -Intensity 48
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_v5_5_haptic_audio_test.ps1 -DeviceName "Wireless Controller" -Pattern ch3_tick -DurationMs 600 -Intensity 48
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "haptic status" -ReadSeconds 3
```

预期现象：

- `ch2_tick` 主要提高 `rms_l`、`peak_l`、`env_l`、`transient_l`。
- `ch3_tick` 主要提高 `rms_r`、`peak_r`、`env_r`、`transient_r`。
- `both_tick` 两侧同时变化。
- 静音或停止播放后 `audio_streaming=false` 或 `silence_packet_count` 增长，并触发 raw02 stop/silence 逻辑。

已验证：

```text
uac1_4ch_transport=true
out_packet_len=384
out_packet_count_3s=3000
hid_concurrent_rate_hz=248.8
hid_concurrent_timeouts=0
firmware_build_hid_audio_uac1_4ch_ds5like=true
```

当前限制：

- 主机端工具能发送 4ch 测试流，但如果 Windows 当前只暴露 2ch endpoint，说明还没有刷入或重插 V5.5 4ch profile。
- Game/Steam 是否真正向 channel 2/3 输出 haptic audio，需要实机游戏验证。
- 本阶段只提取信号特征，不保证等价于 DualSense 原生触觉。

## English

Phase 4 turns the four-channel USB Audio OUT stream into observable haptic features. It does not attempt full DualSense reproduction first. The active profile is `hid_audio_uac1_4ch_ds5like`: Windows sees a DualSense-like HID device and a 4ch/48 kHz/16-bit render endpoint, while the ESP32-S3 receives 384-byte isochronous OUT packets at AudioStreaming alt 1.

Channel convention:

```text
channel 0: ordinary left placeholder, ignored for Pro2 haptics
channel 1: ordinary right placeholder, ignored for Pro2 haptics
channel 2: left haptic source
channel 3: right haptic source
```

`dualsense_haptic_audio` computes RMS, peak, mean absolute value, envelope, transient, active/silence packet counts, channel count, and streaming state. These values are visible through `status`, `haptic status`, and rate-limited `[DS5_HAPTIC_AUDIO]` logs.

V5.5 does not implement speaker playback, microphone input, voice audio, or a full native DualSense haptic renderer.
