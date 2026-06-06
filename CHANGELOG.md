# Changelog

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
