# 用户指南

本指南对应 V5.9.6 新和联胜 Edge 背键版一体化 Manager。

## Hardware Setup

Use two USB connections when available:

- CH343P / WCH control port: flashing, logs, BLE commands, Manager control.
- ESP32-S3 native USB / OTG port: the actual USB gamepad seen by Windows / Steam.

The control port can be unplugged after flashing and first pairing are finished.
Daily play only needs the native USB gamepad port. The firmware stores the
controller address and keeps BLE auto reconnect active.

## Flashing a Mode

1. Close older Manager windows and serial monitors.
2. Connect the CH343P control port.
3. Open `新和联胜版本-aio-v5.9.6.exe`.
4. Click refresh serial if the COM port is not selected.
5. Click the desired mode card.
6. Wait for flashing to finish.
7. Replug the native USB / OTG gamepad cable.
8. Click USB check.

If Windows reports that the COM port is busy or names an esptool PID that
cannot be terminated, unplug the CH343P control cable for 3-5 seconds, plug it
back in, refresh serial, and try again. Do not repeatedly click the mode card;
V5.9.6 will refuse to stack another flashing process.

## Recommended Mode

使用新和联胜 / PS5 Edge 模式测试原生 DualSense / DualSense Edge 支持、控制器音频 HD 震动，以及普通 DualSense 马达指令。固件按主机输出意图在 HD 音频和普通震动之间调度，不按游戏名硬切。

使用 Pro2 / Nintendo 模式走 Steam Input 和原生 Pro2 风格行为。

使用 Xbox / XInput 模式兼容只认 XInput 的游戏；V5.9.6 增加了固件侧 Pro2 GL/GR 背键映射。

## Xbox 背键映射

Xbox / XInput 标准本身没有独立背键字段，所以 V5.9.6 不再尝试伪装 Elite GIP，而是在固件侧把 Pro2 的两个背键映射成普通 XInput 按键或组合键。

1. 刷入并切到 Xbox / XInput 模式。
2. 在 Xbox 面板打开 `Pro2 背键映射`。
3. 点 `读取配置` 查看固件 NVS 当前保存值。
4. 勾选 GL / GR 开关，目标键可填 `B`、`A`、`ZR`、`B+A` 等。
5. 动作可选 `hold`（按住）、`tap`（单发）、`turbo`（持续连发）。
6. 点 `应用映射`。成功后配置会保存到 ESP32 NVS。

串口也可直接调试，例如：

```text
xbox paddle left hold B+A
xbox paddle right turbo ZR 45 45
xbox paddle status
xbox paddle reset
```

默认背键映射关闭，升级后不会自动改变用户按键。

## Connecting the Pro2

### First connection after flashing

1. Turn off other Pro2 controllers nearby.
2. Make sure the new controller is not connected to a PC, phone, or console.
3. Wake the controller and keep it available for connection.
4. Keep the CH343P control port connected, because `首次连接` is a serial control command.
5. Keep the native USB / OTG gamepad port connected if you want the Manager to verify the active USB mode while pairing.
6. Click `首次连接`.
7. Wait until the Manager shows `手柄连接完成`.

The firmware stores the successful BLE address in NVS and enables automatic
reconnect. After that, normal power-on, sleep wake, and short disconnect recovery
do not require the CH343P control port.

### Reconnect a saved controller

Wake the same controller. Firmware normally reconnects automatically after sleep or a temporary disconnect. Click `重连已配对` only when you want to request it immediately.

### Replace the controller

1. Turn off the old controller.
2. Wake the new controller.
3. Click `更换手柄` and confirm.
4. The Manager disconnects the old target, clears its saved address, scans, connects, and stores the new address.

If automatic selection cannot finish, open `高级连接工具`, scan, select the correct result, and click `连接所选目标`.

## BLE MultiProbe

V5.9.6 保留 MultiProbe，用于检查 Pro2 BLE 是否真的提供超过
than the usual ~133 Hz, instead of just repeating identical samples.

1. 刷入新和联胜 / PS5 Edge 或任意 5.9.6 profile。
2. Connect the Pro2 normally.
3. In the serial command box, send `ble multiprobe`.
4. Move both sticks aggressively for 10-20 seconds.
5. Send `status lite`, or keep the Manager monitor running and export the log.

Key fields:

- `ble_notify_actual_mhz`: BLE notification rate, in millihertz.
- `ble_notify_raw_unique_mhz`: rate of raw notification payloads that actually changed.
- `ble_notify_control_unique_mhz`: rate of changed control data: buttons + sticks.
- `ble_notify_motion_unique_mhz`: rate of changed motion data.
- `ble_notify_sub_interval_count`: count of notification gaps below 6.5 ms.
- `ble_notify_gap_lt3ms` / `ble_notify_gap_3_6p5ms` /
  `ble_notify_gap_6p5_10ms` / `ble_notify_gap_ge10ms`: notify gap buckets.
- `ble_notify_sub_control_unique`: real changed control frames that arrived
  inside a sub-6.5 ms gap. This is the important "two reports per 7.5 ms event"
  evidence.
- `ble_notify_raw_repeat` and `ble_notify_raw_repeat_streak_max`: repeated packets.

判断口径：如果 `ble_notify_actual_mhz` 高于 133000，但
`ble_notify_control_unique_mhz` 仍停在 133000 左右或更低，那只是通知变多，
不代表摇杆/按键真实输入超过 133 Hz。真正超过 133 Hz，需要
`ble_notify_control_unique_mhz` 在密集摇杆输入时也同步超过 133000。
现场测试已经确认主动 GATT read-poll 会干扰正常 notify 链路，
因此正式测试只保留被动 MultiProbe，不再暴露 read-poll/turbo 入口。

## Steam Check

For Pro2 / Nintendo mode:

1. Open Steam Controller Settings.
2. Confirm the device appears as a Switch Pro / Nintendo-style controller.
3. Test buttons, sticks, triggers, gyro if applicable, and rumble.
4. If the device does not appear after flashing, unplug and replug the native USB / OTG cable.

For Xbox / XInput mode:

1. Confirm Windows shows `VID_045E&PID_028E`.
2. Use Windows Game Controllers, Steam, or a game to test sticks and rumble.

## Files

The final release EXE is in `release/v5.9/`.

Source firmware and Manager code remain in `firmware/` and `windows/v55_manager_app/`.
