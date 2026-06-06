# V5.2.0 Experimental Notes

## 中文

V5.2.0 是 `ns2pro_viiper` 实验路线，不是默认输出模式，也不替代 V5.0/V5.1 的稳定 ESP32-S3 Pro2 桥接路线。

核心变化：

- 使用 VIIPER 创建虚拟 Switch 2 Pro / `ns2pro` USB 设备。
- 捕获非零 `LeftRumble[16]` / `RightRumble[16]`。
- 新增 firmware/control `rumble raw02 <hex>` 注入链路。
- 支持 64 hex `Left[16]+Right[16]` 和 128 hex 完整 `0x02` payload。
- 已验证 VIIPER captured payload 可以通过 raw02 转发到真实 Pro2。

实测状态：

```text
firmware_flash=true
ble_connected=true
raw02_low_sent=true
raw02_medium_sent=true
viiper_captured_sent=true
physical_vibration=true
button_input=true
gyro_input=true
rumble_writes=49
rumble_errors=0
ble_disconnect=false
```

限制：

- `ns2pro_viiper` 仍是 Experimental，不默认开启。
- Steam/SDL 普通 rumble API 不等于 ns2pro HD `0x02`。
- 可靠触发源是 direct HID `0x02` 或 VIIPER probe 捕获。
- 原生 Steam 游戏 HD rumble 支持取决于游戏、Steam Input 和输入栈。
- 不承诺所有游戏支持 HD rumble。
- 不包含 PS5 / DualSense 高级触觉支持。
- 不包含语音、耳机、麦克风或完整 HD Rumble 2 音频复刻。

推荐输出模式定义：

```text
output_mode = pro2 | ps4 | ns2pro_viiper
default = pro2
pro2 = 默认 ESP32-S3 Switch 2 Pro 桥接
ps4 = V5.1 DS4/raw 兼容路线
ns2pro_viiper = V5.2 VIIPER 实验 HD rumble 捕获 + raw02 转发路线
```

## English

V5.2.0 is the experimental `ns2pro_viiper` route. It is not the default output mode and does not replace the stable V5.0/V5.1 ESP32-S3 Pro2 bridge.

Highlights:

- Creates a virtual Switch 2 Pro / `ns2pro` USB device through VIIPER.
- Captures non-zero `LeftRumble[16]` / `RightRumble[16]`.
- Adds the firmware/control `rumble raw02 <hex>` injection path.
- Supports both 64-hex `Left[16]+Right[16]` and 128-hex full `0x02` payloads.
- Verifies that a captured VIIPER payload can be forwarded to the real Pro2 through raw02.

Verified state:

```text
firmware_flash=true
ble_connected=true
raw02_low_sent=true
raw02_medium_sent=true
viiper_captured_sent=true
physical_vibration=true
button_input=true
gyro_input=true
rumble_writes=49
rumble_errors=0
ble_disconnect=false
```

Limits:

- `ns2pro_viiper` remains Experimental and is not enabled by default.
- Steam/SDL ordinary rumble is not the same as ns2pro HD `0x02`.
- The reliable trigger source is direct HID `0x02` or VIIPER probe capture.
- Native Steam game HD rumble depends on the game, Steam Input, and the input stack.
- This release does not claim all games support HD rumble.
- This release does not include PS5 / DualSense advanced haptic support.
- Voice, headphone audio, microphone audio, and full HD Rumble 2 audio reproduction are not implemented.
