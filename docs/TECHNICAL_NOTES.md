# 技术说明

## Firmware Profiles

V5.9.6 Manager 内置四个 profile，包括一个恢复 profile：

| Profile ID | Purpose |
| --- | --- |
| `pro2_bridge_v5_5` | Pro2 / Nintendo USB HID bridge |
| `xinput_bridge_v5_8` | Xbox / XInput bridge，带固件侧 Pro2 GL/GR 映射 |
| `hid_audio_uac1_4ch_ds5like` | 新和联胜 / PS5 Edge identity + UAC1 4ch HD haptics |
| `hid_only` | PS5 Edge HID-only recovery profile |

部分 profile ID 保留历史命名，用于兼容旧设置和刷机脚本。Xbox Elite 2 / GIP 不进入本发布包：Windows/GIP 链路收益不稳定，V5.9.6 的 Xbox 背键路线改为固件侧映射普通 XInput 按键。

## Pro2 / Nintendo USB Identity

- VID/PID: `057E:2069`
- Manufacturer string: `Nintendo Co., Ltd.`
- Product string: `Nintendo Switch Pro Controller`
- HID input report ID: `0x05`
- Main report size: 64 bytes
- Vendor interface: MI_01
- BOS / Microsoft OS 2.0 descriptor exposed for WinUSB binding

## Rumble Paths

Pro2 / Nintendo mode keeps raw HID report `0x02` as the authoritative rumble input. Preset-style bulk fallback is ignored and counted as `rumble_preset_ignored`.

Xbox / XInput and DualSense ordinary motor paths preserve left/right strength. Because those host APIs do not carry native Pro2 frequency fields, firmware generates dynamic frequency shaping before sending Pro2 BLE rumble.

## PS5 Edge Identity

新和联胜 profile 从 V5.9.6 开始枚举为 DualSense Edge 方向：

- VID/PID: `054C:0DF2`
- Product string: `DualSense Edge Wireless Controller`
- Pro2 `GL` 映射到 Edge `L4`
- Pro2 `GR` 映射到 Edge `R4`
- HD 音频转 raw `0x02` 和普通 DualSense 震动调度逻辑保持不变

旧 `054C:0CE6` 仍在 Manager 里作为兼容检测保留，但新固件和新 release 的目标校验以 `0DF2` 为准。

The 新和联胜 profile uses one BLE writer with two independent source states
and a host-intent mode:

- `valid_flag0` bit 0 or `valid_flag2` bit 2 validates compatibility motors.
- `valid_flag0` bit 1 selects compatibility haptics even with zero motor values.
- Compatibility mode routes only ordinary motors and blocks haptic-audio
  candidates.
- Without compatibility selection, haptic audio routes through raw `0x02` and
  stale ordinary motor bytes are ignored.
- A game may switch modes repeatedly during one session. The firmware does not
  classify games or permanently select HD output.
- LED, player-state, and trigger-only fields do not independently select a
  vibration source.
- Bounded stop packets are emitted when the currently selected source ends.

This is source arbitration, not byte-level mixing. Alternating ordinary and HD
packets at USB report cadence would add BLE load and produce discontinuities.

## Xbox Pro2 Paddle Mapping

XInput `045E:028E` 没有独立背键字段，因此 V5.9.6 不再把稳定主线切到 Elite/GIP。固件在生成 XInput report 前复制一份 `internal_gamepad_state_t`，只对这份临时 state 叠加 GL/GR 映射，不修改真实 Pro2 输入，也不影响 Pro2 / Nintendo 或 PS5 Edge 模式。

串口协议：

```text
xbox paddle status
xbox paddle reset
xbox paddle left off
xbox paddle left hold B+A
xbox paddle right tap ZR 70
xbox paddle right turbo A 45 45
```

配置保存到 ESP32 NVS，默认关闭。支持动作：

- `hold`: 背键按住时输出目标键。
- `tap`: 背键按下沿触发一个短脉冲。
- `turbo`: 背键按住时按 on/off 周期持续连发。

## DualSense USB Recovery

Host bus reset is allowed to complete before firmware considers a forced USB
disconnect/connect. The audio profile suppresses forced recovery while UAC
streaming is active and applies a 15-second post-transition grace window.
`status diag` exposes the inhibit reason, UAC transfer failures, rearm failures,
microphone alternate-setting attempts, and the selected rumble source.

## BLE Reconnect

BLE auto reconnect is state-machine guarded:

- one reconnect task at a time
- delayed retry after disconnect or failed connect
- no retry when already connected
- manual disconnect suppresses the next automatic reconnect
- normal control-port commands remain available
- `ble forget` disconnects the current controller and clears the saved target
- first-pair and replacement flows re-enable automatic reconnect before scanning

## PSRAM

The final ESP32-S3 N16R8 safe defaults avoid boot loops on AP 8 MB Octal PSRAM boards:

```text
CONFIG_SPIRAM_USE_MEMMAP=y
# CONFIG_SPIRAM_USE_MALLOC is not set
# CONFIG_SPIRAM_MEMTEST is not set
```

## Windows Manager

The final Manager is a .NET 8 WPF single-file app. It extracts embedded firmware and tools into:

```text
%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\embedded\v5.9.6-aio
```

The flasher closes the Manager serial session, cleans known project serial
consumers and stale matching esptool processes, then launches esptool without
a Manager-side `SerialPort.Open()` preflight. It uses finite esptool connection
attempts and per-command watchdogs. A stale process that cannot be terminated
blocks the next flash instead of allowing another esptool instance to stack on
the same COM port.

## BLE MultiProbe

V5.9.6 does not assume that a higher BLE notification count means a higher
gameplay input rate. The firmware records the normal notify/input rate plus
separate uniqueness rates:

- raw payload uniqueness: full BLE input notification bytes
- control uniqueness: buttons and stick bytes only
- motion uniqueness: parsed IMU block only
- short/sub-interval gaps: notifications closer than 3 ms / 6.5 ms
- gap buckets: `<3 ms`, `3~6.5 ms`, `6.5~10 ms`, and `>=10 ms`
- sub-interval unique counters: changed raw/control/motion frames that arrived
  inside a sub-6.5 ms gap
- repeat streaks: identical raw notifications in a row

Use `ble multiprobe` to reset the probe counters and request the fast 7.5 ms
BLE connection interval again. Then move sticks heavily and read `status lite`.
Only `ble_notify_control_unique_mhz` above ~133000 during real stick movement,
or sustained non-zero `ble_notify_sub_control_unique`, should be treated as
evidence that Pro2 control input exceeded one true control frame per 7.5 ms.

The active GATT read-poll experiment was removed from the public command path
after testing showed zero valid read responses and measurable notify-rate
degradation. The production path should treat FD2 notify as the input source.
