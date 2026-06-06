# Changelog

## V5.5 Planning - 2026-06-06

### 中文

- 明确 V5.2 Pure Pro2 / VIIPER 路线封存保留，V5.5 不替换、不混入、不修改其默认行为。
- 新增 DS5Dongle fetch/analyze 工具，固定记录上游 commit、branch、license、USB descriptor、audio/haptic 和 Bluetooth backend 耦合点。
- 新增 V5.3 synthetic DualSense feature -> Pro2 raw02 dry-run pipeline。
- 新增 V5.4 hybrid haptic policy probe；没有真实 DualSense 时以 `passed_as_blocked` 安全结束。
- 新增 ESP32-S3 DualSense identity 架构、移植可行性、风险和 Phase 0-6 实验计划。
- 建议配置为独立身份：`pro2_ns2_viiper` 与 `dualsense_esp32s3_experimental`。

### English

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
