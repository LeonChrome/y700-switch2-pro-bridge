# Changelog

## V5.5 Integrated Haptic raw02 Manager - 2026-06-06

### 中文

- 新增 V5.5 haptic audio -> Pro2 raw02 实验闭环：UAC1 4ch audio OUT 的 channel 2/3 被解析为左右 haptic source。
- `dualsense_haptic_audio` 现在提取 RMS、peak、mean abs、envelope、transient、active/silence packet counters 和 streaming state。
- `haptic_audio_to_raw02` 从 dry-run preview 升级为带安全门的 live forwarding 模块：默认 live off、dry-run on、BLE required、限幅、限频、静音 stop、BLE 错误自动关闭 live。
- `pro2_rumble_backend` 新增 V5.5 raw02 payload 发送入口，复用已验证的 Pro2 BLE rumble backend。
- 新增 V5.5 串口控制协议：`haptic status`、`haptic raw02 on/off`、`haptic dryrun on/off`、`haptic max/gain/transient_gain/interval/silence/threshold/mode`、`haptic test tick/punch/texture/continuous/stop`、`rumble raw02 <hex>`。
- UAC1 4ch driver 在收到 384-byte/ms 等时 OUT 包时直接喂给 haptic feature extractor，音频 alt 0 / stop 会触发 haptic stop。
- 新增 host 侧 `send_v5_5_haptic_audio_test.ps1` 和 `SendV55HapticAudioTest.cs`，支持枚举 Windows waveOut endpoint 并发送 4ch 测试 pattern。
- 新增独立 V5.5 WPF Manager：一页式集成烧录、模式、BLE、USB 检查、haptic 参数、audio pattern、raw02 live/dry-run 开关和日志。
- 新增 `package_v5_5_manager.ps1`，能构建固件、编译 host sender、编译/发布 Manager、复制工具和 firmware payload，并生成 `Y700Switch2V55Manager-aio-v5.5-experimental.zip` 与 SHA256。
- 打包脚本支持没有 .NET SDK 的机器：使用 .NET Framework `csc.exe` fallback，并动态定位 WPF reference assemblies / GAC。
- 新增 Phase 4/5/6 和 Manager 文档，明确 V5.5 不替换 V5.0 stable，也不修改 V5.2 VIIPER/raw02 默认行为。
- ESP-IDF 5.3.3 实际构建已通过 `hid_audio_uac1_4ch_ds5like` 与 `hid_only`。

### English

- Added the V5.5 haptic-audio -> Pro2 raw02 experimental loop: UAC1 4ch audio OUT channels 2/3 are parsed as left/right haptic sources.
- `dualsense_haptic_audio` now extracts RMS, peak, mean absolute value, envelope, transient, active/silence packet counters, and streaming state.
- `haptic_audio_to_raw02` now supports guarded live forwarding in addition to dry-run preview: live off by default, dry-run on by default, BLE required, amplitude clamp, rate limit, silence stop, and automatic live disable on BLE errors.
- `pro2_rumble_backend` exposes a V5.5 raw02 payload sender using the already validated Pro2 BLE rumble backend.
- Added the V5.5 serial control protocol for haptic status, dry-run/live toggles, tuning parameters, short tests, and `rumble raw02 <hex>`.
- The UAC1 4ch driver feeds each 384-byte/ms isochronous OUT packet directly into the haptic feature extractor; audio alt 0 / stop triggers haptic stop.
- Added the host-side haptic audio sender (`send_v5_5_haptic_audio_test.ps1` and `SendV55HapticAudioTest.cs`) for Windows endpoint enumeration and 4ch test patterns.
- Added the standalone V5.5 WPF Manager with flashing, mode notes, BLE controls, USB checks, haptic parameters, audio patterns, raw02 dry-run/live toggles, and logs on one page.
- Added `package_v5_5_manager.ps1` to build firmware, compile the host sender, build/publish the Manager, copy tools and firmware payloads, and generate the V5.5 experimental aio zip plus SHA256.
- The package script works without a .NET SDK by using the .NET Framework `csc.exe` fallback and dynamically resolving WPF reference assemblies / GAC.
- Added Phase 4/5/6 and Manager documentation. V5.5 remains separate from V5.0 stable and does not change the V5.2 VIIPER/raw02 default behavior.
- ESP-IDF 5.3.3 builds pass for both `hid_audio_uac1_4ch_ds5like` and `hid_only`.

## V5.5 Planning - 2026-06-06

### Descriptor 级 Composite 调试

- descriptor ladder 实机已通过 `hid_only`、两套 dummy composite、
  AudioControl、AudioStreaming alt0 与完整 UAC1 2ch；Windows 的 composite
  parent、HID、MEDIA 和音频播放端点均为 `OK`。
- 找到初始 Code 10 根因：自定义 TinyUSB app driver 虽在 `libmain.a` 中，
  但 `usbd_app_driver_get_cb` 在最终 ELF 中仍为弱符号；为 main component
  启用 `WHOLE_ARCHIVE` 后修复。
- 新增 `hid_audio_uac1_4ch_ds5like`：`00/00/00`、无 IAD、4ch/48 kHz/
  16-bit PCM OUT、通道位图 `0x0033`、384-byte max packet。
- 动态播放确认 Windows 已请求 alt 1；修复 ESP32-S3 DWC2 FIFO 不足导致的
  `usbd_edpt_open(0x02)=false`：仅 4ch UAC1 使用 slave mode 与精确
  384-byte/ms packet，其他 profile 保持原 TinyUSB 模式。
- 4ch 动态传输实机通过：`set_interface=1`、`streaming=true`，3 秒连续收到
  3000 个 384-byte 等时 OUT 包；新增自动播放、串口捕获和默认音频恢复工具。
- UAC1 自定义驱动改用 DWC2 `iso_alloc`/`iso_activate` 生命周期，并在 alt 0
  后停止重新挂接传输，修复多次播放时旧 ISO 流持续运行的问题。
- 实机确认 `hid_audio_uac1_2ch` 与旧 UAC2 4ch 都在 Windows Composite
  parent 发生 Code 10，且没有 HID/Audio child，当前问题收敛到 descriptor
  枚举层。
- 提取 DS5Dongle 默认最终 USB descriptor：UAC1、device class
  `00/00/00`、无 IAD、Audio Control + 4ch OUT + 2ch IN + HID。
- 新增 `hid_composite_dummy_interface_class_00`、
  `hid_composite_dummy_interface_class_ef`、`hid_audio_control_only` 和
  `hid_audio_streaming_alt0_only` 隔离 profile。
- UAC1 2ch 改为 DS5Dongle 默认策略：`00/00/00`、无 IAD；UAC2 profile
  保留 `EF/02/01` 与 Audio IAD。
- 新增 ELF descriptor dump 生成器、全部 profile raw hex/parsed tables、
  USBView 抓取指南、Windows composite phase guess 和决策矩阵。
- 本轮不推进 Phase 4、haptic feature、raw02 live、Pro2 BLE、V5.2/VIIPER
  或 GUI。

### Descriptor-Level Composite Debug

- The hardware descriptor ladder now passes HID-only, both dummy composites,
  AudioControl, AudioStreaming alt 0, and complete UAC1 2ch. Windows reports
  the composite parent, HID, MEDIA, and audio render endpoint as `OK`.
- Found the initial Code 10 root cause: the custom TinyUSB app driver was in
  `libmain.a`, but `usbd_app_driver_get_cb` stayed weak in the final ELF.
  Linking the main component with `WHOLE_ARCHIVE` fixes the callback.
- Added `hid_audio_uac1_4ch_ds5like`: class `00/00/00`, no IAD, four-channel
  48 kHz 16-bit PCM OUT, channel map `0x0033`, and 384-byte max packet.
- Dynamic playback confirmed that Windows requests alt 1. Fixed
  `usbd_edpt_open(0x02)=false`, caused by the ESP32-S3 DWC2 FIFO budget, by
  using slave mode and an exact 384-byte/ms packet only for UAC1 4ch.
- Passed active four-channel transport: alt 1 and streaming are enabled, with
  3000 consecutive 384-byte isochronous OUT packets over three seconds. Added
  an automated playback/UART/default-endpoint restoration test.
- Switched the custom UAC1 driver to the DWC2 ISO allocate/activate lifecycle
  and stopped transfer re-arming after alt 0, fixing stale streams across
  repeated playback cycles.
- Confirmed that both `hid_audio_uac1_2ch` and the earlier UAC2 4ch profile
  fail at the Windows composite parent with Code 10 and no HID/Audio children.
- Extracted the DS5Dongle default final USB descriptor: UAC1, device class
  `00/00/00`, no IAD, Audio Control, 4ch OUT, 2ch IN, and HID.
- Added class `00` and class `EF` no-audio dummy composite profiles, Audio
  Control-only, and Audio Streaming alt 0-only isolation profiles.
- Changed UAC1 2ch to the DS5Dongle default class/no-IAD strategy while
  retaining `EF/02/01` plus Audio IAD for UAC2 profiles.
- Added compiled-ELF descriptor dump generation, raw/parsed profile reports,
  a USBView capture guide, checker phase guesses, and a hardware decision
  matrix.
- Phase 4, haptic feature work, live raw02, Pro2 BLE, V5.2/VIIPER, and GUI
  changes remain out of scope.

### 中文

- 新增独立 `firmware/esp32s3_dualsense_identity_experiment` Phase 1 固件，现有 V5.2/V5.0 默认固件完全不变。
- Phase 1 暴露 VID `054c`、PID `0ce6`、DualSense product string、`0x01` 63-byte neutral input、`0x02` 47-byte output capture。
- Phase 1 实机验证通过：Windows VID/PID 正确、输入报告约 `250 Hz`、USB 无断连。
- 新增 Phase 2 Pro2 BLE FD2 到 DualSense `0x01` 输入映射，覆盖按键、摇杆、扳机和 raw-like motion。
- Phase 2 复用 V5.2 的 BLE parser 源码但不修改其实现或默认行为；断连/输入过期时回退中性报告。
- 新增 `tools/check_v5_5_dualsense_input.ps1` 和 Phase 2 构建、烧录、实机验证文档。
- Phase 2 已实际刷入并验证 `V55PHASE2`、`0x01 + 63 bytes`、`250.0 Hz`、零 HID 读取超时。
- 新增实验固件 BLE 常驻重连守护；异步连接失败回到 `idle` 后会自动重试。
- 新增 `check_v5_5_dualsense_reports.ps1` 和专用 C# HID 读取器，用于自动验证报告形状、频率、计数和映射活动。
- 根据实机反馈反转 DualSense 映射的左右摇杆 Y 轴；其余键位由用户确认正确。
- 新增 Phase 2.1 普通 DualSense light/heavy motor 到 Pro2 BLE vibration 的限幅兼容层。
- 新增一次性低强度 `send_v5_5_dualsense_rumble_test.ps1`；实测 HID `0x02` 解析成功、BLE writes 非零且 errors 为零。
- 报告循环改为 `xTaskDelayUntil` 绝对节拍，BLE 与震动任务运行时主机实测约 `248.8 Hz`。
- 新增 Phase 3 最小 USB Audio render endpoint stub：4ch/48kHz/16-bit OUT，HID 输入和普通震动路径保持不变。
- 新增 `dualsense_haptic_audio` 统计模块，提取 haptic channels 2/3 的 RMS、peak、transient、activity。
- 新增 `haptic_audio_to_raw02` dry-run 转译模块，默认只打印 Pro2 raw02 16+16 payload，不实时发送。
- 新增 `tools/check_v5_5_dualsense_audio.ps1` 和 token 安全文档；工作区与 git history 未发现 GitHub token 模式命中。
- 新增独立 build/flash 工具和 Windows DualSense identity 检测工具。
- ESP-IDF 5.3.3 实际 build 已通过；未刷实验固件时 host check 以 blocked/exit 0 结束。
- 明确 V5.2 Pure Pro2 / VIIPER 路线封存保留，V5.5 不替换、不混入、不修改其默认行为。
- 新增 DS5Dongle fetch/analyze 工具，固定记录上游 commit、branch、license、USB descriptor、audio/haptic 和 Bluetooth backend 耦合点。
- 新增 V5.3 synthetic DualSense feature -> Pro2 raw02 dry-run pipeline。
- 新增 V5.4 hybrid haptic policy probe；没有真实 DualSense 时以 `passed_as_blocked` 安全结束。
- 新增 ESP32-S3 DualSense identity 架构、移植可行性、风险和 Phase 0-6 实验计划。
- 建议配置为独立身份：`pro2_ns2_viiper` 与 `dualsense_esp32s3_experimental`。

### English

- Added the standalone `firmware/esp32s3_dualsense_identity_experiment` Phase 1 firmware; the existing V5.2/V5.0 default firmware is unchanged.
- Phase 1 exposes VID `054c`, PID `0ce6`, DualSense product strings, neutral `0x01` 63-byte input, and `0x02` 47-byte output capture.
- Phase 1 passed hardware validation with the expected VID/PID, about 250 Hz input, and no USB disconnect.
- Added Phase 2 Pro2 BLE FD2 to DualSense `0x01` mapping for buttons, sticks, triggers, and raw-like motion.
- Phase 2 reuses the V5.2 BLE parser sources without changing their implementation or default behavior; stale or disconnected input falls back to neutral reports.
- Added `tools/check_v5_5_dualsense_input.ps1` and Phase 2 build, flash, and hardware validation documentation.
- Flashed and verified Phase 2 as `V55PHASE2` with `0x01 + 63-byte` input at 250.0 Hz and zero HID read timeouts.
- Added a persistent experimental BLE reconnect watchdog that retries after asynchronous connection failure returns to `idle`.
- Added `check_v5_5_dualsense_reports.ps1` and a dedicated C# HID reader for report shape, rate, counter, and mapped-activity validation.
- Reversed both mapped stick Y axes from hardware feedback; the user confirmed the remaining controls.
- Added a bounded Phase 2.1 ordinary DualSense light/heavy motor to Pro2 BLE vibration compatibility layer.
- Added the one-shot low-intensity `send_v5_5_dualsense_rumble_test.ps1`; HID `0x02` parsing passed with non-zero BLE writes and zero errors.
- Switched the report loop to an absolute `xTaskDelayUntil` cadence; host-observed rate is about 248.8 Hz with BLE and rumble tasks active.
- Added the Phase 3 minimal USB Audio render endpoint stub: 4ch/48 kHz/16-bit OUT while preserving HID input and ordinary rumble behavior.
- Added V5.5 profile isolation for `hid_only`, `hid_audio_uac2`, and `hid_audio_uac1_fallback`; `hid_only` restores the Phase 2.1 HID-only descriptor.
- Corrected the identity checker so a `USB Composite Device` no longer counts as a successful HID interface.
- Added `tools/check_v5_5_usb_composite.ps1` for Phase 3 Windows composite error diagnostics.
- Adjusted the UAC2 descriptor associated-terminal fields for the clock source and output terminal.
- Added the `dualsense_haptic_audio` stats module for haptic channels 2/3 RMS, peak, transient, and activity extraction.
- Added the `haptic_audio_to_raw02` dry-run translator, which logs Pro2 raw02 16+16 payloads and does not send live packets by default.
- Added `tools/check_v5_5_dualsense_audio.ps1` and token hygiene documentation; worktree and git history scans found no GitHub token pattern hits.
- Added standalone build/flash tools and a Windows DualSense identity checker.
- Verified a real ESP-IDF 5.3.3 build; before flashing, the host checker exits zero with a blocked result.
- Froze and preserved the V5.2 Pure Pro2 / VIIPER route; V5.5 does not replace, merge into, or change its default behavior.
- Added DS5Dongle fetch/analyze tools that record upstream commit, branch, license, USB descriptors, audio/haptic paths, and Bluetooth backend coupling.
- Added a V5.3 synthetic DualSense feature to Pro2 raw02 dry-run pipeline.
- Added a V5.4 hybrid haptic policy probe that exits safely as `passed_as_blocked` without a real DualSense.
- Added ESP32-S3 DualSense identity architecture, port feasibility, risk analysis, and Phase 0-6 experiment planning.
- Defined separate identities: `pro2_ns2_viiper` and `dualsense_esp32s3_experimental`.

## V5.3 In Progress - 2026-06-06

### 中文

V5.3 进入 DualSense haptic source research。当前不是已支持功能，必须接入真实 DualSense 后才能验证 advanced haptic source。

新增：

- 增强 `tools/check_dualsense_env.ps1`，输出 DualSense HID、USB/BT、VID/PID、product、instance_id、device_path、audio endpoint、Steam、ViGEmBus、usbip-win2、VIIPER、ESP32 raw02 tools 状态。
- 增强 `experiments/dualsense_hid_output_probe`，支持计时、JSONL/raw hex 日志和 blocked-safe 输出。
- 增强 `experiments/dualsense_haptic_audio_probe`，支持 endpoint 枚举、JSONL、per-channel RMS/peak 字段和 blocked-safe 输出。
- 新增 `tools/run_v5_3_dualsense_capture.ps1`，为未来真实 DualSense + 游戏场景提供一条命令捕获入口。
- 新增 `tools/run_v5_3_night_probe.ps1`，没有 DualSense 时也 exit 0。
- 新增 `docs/v5_3_dualsense_to_pro2_translation_plan.md`，规划 haptic audio -> Pro2 raw02 近似转译。
- 深化 `docs/v5_3_dualsense_upstream_research.md`，把 DS5Dongle、SAxense、dualsense-bt-haptics、SDL/Linux HIDAPI 等路线落到工程优先级。

限制：

- 当前没有真实 DualSense，实机 haptic/audio 成功不能伪造。
- HID output passive capture 在普通 Windows 用户态有边界，可能需要 instrumented sender、过滤驱动或真实游戏可观测路径。
- haptic audio capture 需要真实 DualSense audio endpoint。

### English

V5.3 is now in progress as DualSense haptic source research. This is not a supported feature yet; advanced haptic source validation requires a real DualSense.

Added:

- Expanded `tools/check_dualsense_env.ps1` with HID, USB/BT, VID/PID, product, instance ID, device path, audio endpoint, Steam, ViGEmBus, usbip-win2, VIIPER, and ESP32 raw02 tool status.
- Expanded `experiments/dualsense_hid_output_probe` with duration, JSONL/raw hex logs, and blocked-safe output.
- Expanded `experiments/dualsense_haptic_audio_probe` with endpoint enumeration, JSONL, per-channel RMS/peak fields, and blocked-safe output.
- Added `tools/run_v5_3_dualsense_capture.ps1` as the future one-command real-device capture entry.
- Added `tools/run_v5_3_night_probe.ps1`, which exits 0 when blocked by missing hardware.
- Added `docs/v5_3_dualsense_to_pro2_translation_plan.md` for haptic audio -> Pro2 raw02 approximation design.
- Deepened upstream research into engineering route priority.

## V5.2 Experimental - 2026-06-06

### 中文

V5.2 是 `ns2pro_viiper` 实验路线收口版本，默认输出模式仍然是 `pro2`，不会替代稳定的 ESP32-S3 Switch 2 Pro 桥接路线。

新增：

- VIIPER ns2pro 实验路线。
- firmware/control `rumble raw02 <hex>` 命令。
- Pro2 HD rumble forwarding probe。
- 从 VIIPER ns2pro 捕获 `LeftRumble[16] / RightRumble[16]`。
- 将 raw02 payload 转发到真实 Switch 2 Pro Controller。
- V5.3 DualSense 实机测试入口和阻塞式探针。

已验证：

- Pro2 buttons=true。
- gyro=true。
- raw02 low / medium / captured VIIPER payload 均已发送成功。
- VIIPER 16+16 capture=true。
- forwarding to real Pro2=true。
- physical_vibration=true。
- rumble_writes=49。
- rumble_errors=0。
- ble_disconnect=false。

限制：

- `ns2pro_viiper` 仍为 Experimental，不默认启用。
- 需要 usbip-win2、VIIPER、ESP32 raw02 firmware、真实 Pro2 BLE connected。
- Steam/SDL 普通 rumble 不等于 ns2pro HD `0x02`。
- 不承诺所有游戏原生触发 Pro2 HD rumble。
- 不包含 PS5 / DualSense haptic 支持。

### English

V5.2 is the `ns2pro_viiper` experimental closeout. The default output mode remains `pro2`; this does not replace the stable ESP32-S3 Switch 2 Pro bridge.

Added:

- VIIPER ns2pro experimental route.
- firmware/control `rumble raw02 <hex>` command.
- Pro2 HD rumble forwarding probe.
- Capture of `LeftRumble[16] / RightRumble[16]` from VIIPER ns2pro.
- raw02 forwarding to the real Switch 2 Pro Controller.
- V5.3 DualSense real-device test entry and blocked-safe probes.

Verified:

- Pro2 buttons=true.
- gyro=true.
- raw02 low / medium / captured VIIPER payloads sent successfully.
- VIIPER 16+16 capture=true.
- forwarding to real Pro2=true.
- physical_vibration=true.
- rumble_writes=49.
- rumble_errors=0.
- ble_disconnect=false.

Limits:

- `ns2pro_viiper` remains Experimental and is not enabled by default.
- Requires usbip-win2, VIIPER, ESP32 raw02 firmware, and a BLE-connected real Pro2.
- Steam/SDL ordinary rumble is not ns2pro HD `0x02`.
- This does not claim all games can natively trigger Pro2 HD rumble.
- This does not include PS5 / DualSense haptic support.
