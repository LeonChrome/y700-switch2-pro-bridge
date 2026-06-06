# Changelog

## V5.5 Planning - 2026-06-06

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
