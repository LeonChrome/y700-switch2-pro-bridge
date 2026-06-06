# V5.5 Phase 2: Pro2 BLE to DualSense Input

Date: 2026-06-06

## 中文

### 目标与边界

Phase 2 在独立实验固件中连接真实 Switch 2 Pro Controller，把现有 FD2
BLE 解析结果映射为 PC 侧 DualSense `0x01 + 63 bytes` 输入报告。

本阶段不实现 USB Audio、DualSense haptic、Pro2 raw02 转发，也不修改
V5.2/V5.0 默认桥接固件或 GUI。

### 输入映射

| Pro2 输入 | DualSense 输入 |
| --- | --- |
| 左/右摇杆 12-bit | LX/LY/RX/RY 8-bit |
| B / A / Y / X | Cross / Circle / Square / Triangle |
| D-pad | DualSense 8 向 hat，空闲值 `8` |
| L / R | L1 / R1 |
| ZL / ZR | L2 / R2 按键及 `0/255` 扳机轴 |
| Minus / Plus | Create / Options |
| L3 / R3 | L3 / R3 |
| Home / Capture | PS / Touchpad click |
| FD2 motion | DualSense gyro/accel 字段 |

陀螺仪和加速度计当前保持接近 raw 的首版映射，不做平滑、死区或标定。
Pro2 motion 顺序为 accel X/Y/Z、gyro X/Y/Z；DualSense 字段按
gyro X/Z/Y、accel X/Y/Z 写入。轴向和量程仍需在 `joy.cpl`、Steam 或
gamepad tester 中实机确认。

每个 4 ms 报告都会递增 sequence、report counter 和 sensor timestamp。
BLE 未连接或输入超过 1 秒未更新时，固件回退到中性输入，但计数和时间戳
继续递增。

### 构建与烧录

从仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf -Monitor
```

Phase 2 会优先重连 NVS 中已保存的 Pro2 地址；没有可用目标时，复用现有
BLE central 的扫描连接流程。常驻重连守护仅在 BLE 状态回到 `idle` 时
重试，不会打断正在进行的扫描、连接或已建立的连接。关键日志：

```text
[PRO2_INPUT] reconnect_attempt=1 started=true err=ESP_OK
[PRO2_INPUT] connected=true state=connected
[DS5_REPORT] source=pro2 sent=true
[DS5_INPUT_MAP] buttons=... lx=... gyro=... motion_valid=true
```

### Windows 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_input.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_reports.ps1 -Seconds 6
```

然后打开 `joy.cpl`、Steam Controller Test 或浏览器 gamepad tester，
确认真实 Pro2 的按键和摇杆会驱动虚拟 DualSense。主机脚本只验证身份和
输出人工测试入口，不伪造实时输入通过结果。

### 当前结果

```text
phase1_hardware_identity=passed
phase2_build=passed
phase2_flash=passed
phase2_binary_size=0x96a00
phase2_binary_sha256=C1C3BC2659728A9A78F2EC61A5BBF46298029AA8B14C1FC9E8A810B9B92F1BD1
windows_identity=054C:0CE6/V55PHASE2
host_input_check=ready_for_manual_input_test
host_report_id=0x01
host_payload_length=63
host_report_rate_hz=250.0
host_report_timeouts=0
sequence_counter_incrementing=true
reconnect_after_timeout=true
phase2_real_pro2_input=blocked_controller_offline
audio=false
haptic=false
raw02=false
v5_2_default_unchanged=true
```

本轮测试时已保存的 Pro2 目标没有响应。第一次 30 秒连接超时后，固件
成功发起第二次连接，证明断线重连守护工作正常。由于真实手柄没有上线，
本轮不能把 `mapped_input_activity` 标为通过。

恢复正常 V5.2/V5.0 桥接固件：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

## English

Phase 2 is a standalone firmware experiment that connects to a real Switch 2
Pro Controller over BLE and maps the existing FD2 parser state into the
PC-facing DualSense `0x01 + 63-byte` input report.

It maps sticks, face buttons by physical position, D-pad, shoulders, digital
triggers, system buttons, and the latest parsed gyro/accelerometer sample.
Sequence counters and timestamps advance at the 4 ms report cadence. Missing
or stale Pro2 input produces a neutral report without freezing those counters.

Motion remains a raw-like first pass; axis direction, orientation, and scale
require hardware validation. USB Audio, DualSense haptic translation, and
Pro2 raw02 forwarding remain disabled. The existing V5.2/V5.0 firmware and GUI
are unchanged.

The Phase 2 image was flashed and verified as `V55PHASE2`. Windows received
`0x01 + 63-byte` input at 250.0 Hz with zero timeouts while Steam was running.
The reconnect watchdog also started a second connection after the first
30-second timeout. Real mapped input activity remains blocked only because the
saved Pro2 controller was offline during this closeout run.
