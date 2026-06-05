# V5.2 HD Haptic Research

Date: 2026-06-05

This document starts V5.2 Phase 1. It is research and validation design only.
It must not change the V5.1 runtime path, the default `output_mode`, the Pro2
mode, the PS4 / DS4 raw mode, or the Manager GUI.

## Baseline

V5.1 is treated as the current stable baseline from the project handoff:

- `pro2` mode remains the default output mode.
- `ps4` / DS4 raw mode is available.
- ViGEm DS4 raw backend is integrated.
- PS4 mode has validated buttons, sticks, triggers, D-pad, gyro, and accel.
- DS4 ordinary rumble return path is validated.
- `rumble translator v1` is integrated.
- `rumble_mode` supports `off`, `direct`, and `enhanced`.
- `rumble_profile` supports `default`, `shooter`, `racing`, `soft`, and `strong`.
- BLE saved-target auto reconnect is integrated.

The V5.1 DS4 rumble path is an ordinary rumble approximation. It is good for
small/large motor style feedback and profile tuning, but it is not full HD
Rumble, not Switch 2 Pro HD rumble, and not DualSense haptic audio.

V5.2 should therefore stop treating ViGEm DS4 as the HD haptic path. V5.2 should
research real higher-resolution feedback paths first, then add isolated probes,
and only after successful probes consider a formal output mode.

## Non-Goals

- Do not break V5.1.
- Do not delete `pro2` or `ps4`.
- Do not change the default `output_mode`; it remains `pro2`.
- Do not put experiment code into the formal Manager or firmware path.
- Do not claim complete HD Rumble before probe evidence.
- Do not add GUI controls for VIIPER or DualSense before Phase 3 succeeds.
- Do not keep expanding ViGEm DS4 as the HD haptic route.

## Route Overview

| Route | Goal | Technical Base | Driver Needed | New Hardware Needed | More Detail Than DS4 Small/Large | Can Forward To Real Pro2 | Difficulty | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A. VIIPER `ns2pro` | Virtual Switch 2 Pro / ns2pro device, capture Switch 2 HD rumble output | VIIPER USBIP virtual USB, `ns2pro`, SDL Switch 2 HID path | Yes on Windows: USBIP client driver, normally `usbip-win2` | No | Yes. Feedback API exposes `LeftRumble[16]` and `RightRumble[16]` | Probably, but not proven. Requires compatibility probe against real Pro2 BLE rumble payload | High | P0 |
| B. DualSense haptic audio | Capture or reproduce real DualSense advanced haptics / haptic audio | DualSense USB HID + USB audio model, SDL / Linux hid-playstation / haptic audio research | Likely yes for virtual HID/audio on Windows | Optional; real DualSense useful for capture | Yes. Audio haptics are far richer than DS4 ordinary rumble | Only via translation, not direct. Pro2 HD format differs | Very High | P2 / V5.3 pre-research |
| C. DS5Dongle hardware bridge | Make PC see wireless DualSense as wired USB DualSense with audio haptics | Pico2W USB device + Bluetooth bridge + audio/haptic packet forwarding | Host sees a USB device; firmware handles BT bridge | Yes: Pico2W/Pico W plus real DualSense | Yes | Not direct. This is DualSense-to-DualSense, but useful for understanding haptic audio | Very High | P3 |
| D. `dualsense-bt-haptics` / SAxense | Inject haptic audio to a real Bluetooth DualSense | Virtual DS5 HID, virtual audio device, SAxense BT haptic packets | Yes: custom/forked ViGEm or virtual device and virtual audio | Real DualSense recommended | Yes | Translation required; also audio-device capture must be solved | Very High | P2 |
| E. DS4 / ViGEm ordinary rumble | Keep V5.1 PS4 mode stable | ViGEm DS4 output report, ordinary rumble translator | ViGEmBus already needed for DS4 raw | No | No. It is small/large motor intensity | Already approximated, not HD | Low | Keep, but do not pursue for HD |

## DS4 / ViGEm Conclusion

ViGEm DS4 remains suitable for V5.1:

- PS4/DS4 recognition.
- Gyro and accel through DS4-compatible input.
- Ordinary rumble.
- Enhanced approximation profiles.
- Stable Windows path for many games.

It is not a suitable V5.2 HD haptic solution:

- DS4 output is essentially small motor / large motor plus lights and related
  device controls.
- It does not expose a Switch HD Rumble waveform or 16+16 byte payload.
- It does not expose DualSense haptic audio.
- It cannot tell us whether Steam / SDL would generate Switch 2 Pro HD rumble.

Decision: keep DS4 / ViGEm in V5.1, but stop expanding it as the HD haptic route.

## Route A: VIIPER `ns2pro`

### What VIIPER Is

VIIPER is a virtual USB input framework. It uses USBIP under the hood and can
create virtual USB devices that appear to the OS and applications as real USB
hardware. It has two integration styles:

- Standalone VIIPER server: portable executable, TCP API, MIT client libraries.
- `libVIIPER`: shared library with C API and in-process USBIP server, GPL-3.0
  linking implications.

On Windows, VIIPER depends on a USBIP client implementation. VIIPER's own
installation docs point to `usbip-win2`, which provides a signed kernel-mode
driver. Driver installation needs administrator permission and may require a
reboot. After USBIP is installed, a bundled VIIPER server or `libVIIPER` can run
without a new kernel driver for each device type.

### `ns2pro` Device Model

VIIPER's `ns2pro` emulates a Nintendo Switch 2 Pro Controller over USB. Its
documentation says it exposes Switch 2 HID reports used by SDL, including:

- buttons,
- sticks,
- gyro / accelerometer,
- HD rumble output.

The USB identity mirrors a wired Switch 2 Pro Controller closely enough for
host-side drivers to find:

- product string `Switch 2 Pro Controller`,
- serial `00`,
- `bcdDevice=0x0200`,
- HID interface,
- vendor bulk interface,
- Microsoft OS descriptors to bind the vendor bulk interface to WinUSB on
  Windows.

The virtual device does not emulate NFC, Bluetooth GATT, or headset audio
streaming.

### Input Path

The upstream docs currently describe the raw stream input packet as 27 bytes,
but the checked VIIPER source at `device/ns2pro/const.go` defines
`InputWireSize = 24`, and `inputstate.go` marshals only the fields below:

| Field | Type | Notes |
| --- | --- | --- |
| `Buttons` | `uint32` | Bitfield. Includes A/B/X/Y, L/R/ZL/ZR, plus/minus, sticks, D-pad, Home, Capture, C, GL, GR, headset |
| `LX`, `LY`, `RX`, `RY` | `uint16` | Raw stick values clamped to `0..4095` |
| `AccelX`, `AccelY`, `AccelZ` | `int16` | Raw report values |
| `GyroX`, `GyroY`, `GyroZ` | `int16` | Raw report values |

Battery metadata is passed separately through device-specific meta state, not
through the current TCP stream input packet.

For `libVIIPER`, the C API exposes:

```c
typedef struct {
    uint32_t Buttons;
    uint16_t LX;
    uint16_t LY;
    uint16_t RX;
    uint16_t RY;
    int16_t  AccelX;
    int16_t  AccelY;
    int16_t  AccelZ;
    int16_t  GyroX;
    int16_t  GyroY;
    int16_t  GyroZ;
} NS2ProDeviceState;
```

This is a very good match for the current bridge's parsed Pro2 input state. The
Phase 2 probe can feed synthetic values first, then a later probe can feed real
BLE state.

### Output Feedback Path

VIIPER `ns2pro` output feedback is 34 bytes:

| Field | Size | Meaning |
| --- | --- | --- |
| `LeftRumble` | 16 bytes | Copied from HID output report `0x02` |
| `RightRumble` | 16 bytes | Copied from HID output report `0x02` |
| `Flags` | 1 byte | Bit 0 = rumble update, bit 1 = player LED update |
| `PlayerLedMask` | 1 byte | SDL/Steam player LED mask from bulk command `0x09/0x07` |

The `libVIIPER` callback shape is:

```c
typedef struct {
    uint8_t LeftRumble[16];
    uint8_t RightRumble[16];
    uint8_t Flags;
    uint8_t PlayerLedMask;
} NS2ProOutputState;

typedef void (*NS2ProOutputCallback)(NS2ProDeviceHandle handle, NS2ProOutputState output);
```

In VIIPER source, `handleOutputReport()` accepts HID output report `0x02` and
copies payload bytes `[0..15]` to `LeftRumble` and `[16..31]` to `RightRumble`.
That is exactly the signal V5.2 wants to probe.

### SDL / Steam Behavior To Verify

SDL's current Switch 2 HIDAPI path:

- finds the Switch 2 Pro USB bulk endpoints,
- sends a USB init sequence,
- enables feature bits and rumble,
- selects report format `0x05`,
- encodes HD rumble frames,
- sends output report `0x02` for Switch 2 Pro.

This suggests VIIPER `ns2pro` is a credible route for capturing Switch 2 Pro
HD rumble from SDL-aware software. It does not prove Steam Input or every game
will send rich non-zero 16+16 payloads. The Phase 2 probe must prove this.

Best initial test samples:

- Steam controller settings / rumble test for Switch 2 Pro.
- SDL test programs using the Switch 2 HIDAPI path.
- A known SDL game with controller rumble.
- A small custom SDL test that calls `SDL_RumbleGamepad()` and, if available,
  trigger/advanced feedback APIs.

Risk: SDL's built-in rumble code may still map ordinary high/low amplitudes into
Switch-style HD packets. That is better than DS4 small/large, because we can
capture the actual Switch 2 Pro output report shape, but it may not yet be the
richest possible game-authored haptic stream.

### Compatibility With Real Pro2 HID OUT `0x02`

VIIPER says `LeftRumble[16]` / `RightRumble[16]` are copied from HID output
report `0x02`. Our existing ESP32-S3 firmware already has a Pro2 BLE rumble path
that builds a 33-byte BLE rumble packet from 5-byte Switch-style vibration
frames and writes it to the discovered Pro2 rumble characteristic.

Important distinction:

- VIIPER callback captures USB host output report payload.
- Real Pro2 BLE expects a BLE-side packet format on the rumble characteristic.
- These are related but not proven byte-identical.

The current firmware's Pro2 rumble code converts the USB-style frame into two
5-byte motor vibration blocks, then builds a 33-byte BLE packet. That means
Phase 3 should not blindly forward all 16+16 bytes at first. It should log,
classify, and test both paths:

- direct-ish path: use the first meaningful 6-byte/5-byte Switch frame from
  each 16-byte side if it matches the known `0x50 | seq` + 5-byte motor pattern;
- conversion path: parse frequency/amplitude from each side and call the
  existing `build_ble_vibration_data()` style conversion before writing to BLE;
- raw forwarding path: only if real Pro2 traces prove the 16-byte side payload
  maps directly to a 33-byte or 32-byte BLE rumble payload.

Decision: compatibility is plausible but unproven. Phase 2 must first prove
non-zero VIIPER output; Phase 3 must then prove real Pro2 passthrough.

### How Current C# Manager Could Call VIIPER Later

Do not integrate this into the formal Manager during Phase 1 or Phase 2. For
future planning, there are three options:

| Option | Pros | Cons | Recommendation |
| --- | --- | --- | --- |
| Standalone VIIPER server + TCP API | Avoids GPL linking risk; easiest to keep as experiment; process can be started/stopped by probe | Need process management, port handling, stream protocol | Best for Phase 2 |
| `libVIIPER` via P/Invoke | Direct callbacks; no separate process; easy synthetic input loop | GPL-3.0 linking concern; native DLL packaging | Good for throwaway probe, risky for formal app unless license accepted |
| C/C++ probe using `libVIIPER` | Closest to upstream example; simpler native callback logging | Separate build toolchain | Good if C# stream client is slower to write |

If VIIPER runs as a separate process, the future GUI should:

- keep it under an advanced / experimental section,
- show driver installed / missing,
- start with a custom localhost port,
- log VIIPER stdout/stderr,
- stop the process on Manager exit,
- never enable it by default,
- preserve `output_mode=pro2` unless user explicitly selects
  `output_mode=ns2pro_viiper`.

### Route A Decision

VIIPER `ns2pro` is suitable for V5.2 Phase 2 probe.

It is not yet suitable as a formal third output mode. Formal integration should
wait until:

1. Windows recognizes the VIIPER device as Switch 2 Pro / ns2pro.
2. Steam / SDL sees the virtual controller.
3. Synthetic input changes are visible.
4. Rumble tests produce non-zero `LeftRumble[16]` / `RightRumble[16]`.
5. A separate probe proves those bytes can drive the real Pro2 rumble path.

## Route A Probe Design

### Phase 2: `experiments/viiper_ns2pro_probe`

Purpose: verify VIIPER `ns2pro` virtual device creation, synthetic input, and
host output feedback capture. This probe must not connect to the V5.1 Manager,
firmware, or GUI.

Expected files:

```text
experiments/viiper_ns2pro_probe/
  README.md
  run_probe.ps1
  src/...
```

Implementation options:

- Preferred first implementation: standalone VIIPER server + C# or small C
  client using raw stream or generated client library.
- Fallback: `libVIIPER` C probe based on upstream `examples/libVIIPER/C/ns2pro_cli`.

Required logs:

```text
[VIIPER] starting
[VIIPER] backend=server|libVIIPER usbip=...
[NS2PRO] virtual device connected
[NS2PRO_INPUT] buttons=... lx=... ly=... rx=... ry=... gyro=(...) accel=(...)
[NS2PRO_OUTPUT] flags=... led=... left_rumble_hex=... right_rumble_hex=...
[NS2PRO_OUTPUT] left_nonzero=true right_nonzero=true
```

Acceptance:

1. Windows sees a virtual Switch 2 Pro / ns2pro device.
2. Steam / SDL sees the controller.
3. Synthetic buttons, sticks, and gyro values change.
4. A game, Steam, or SDL test triggers rumble and the probe receives non-zero
   `LeftRumble[16]` / `RightRumble[16]`.

Minimum runbook:

1. Install `usbip-win2` or confirm it is installed.
2. Start the VIIPER probe.
3. Confirm device appears in Device Manager / Steam / SDL.
4. Run synthetic input loop.
5. Trigger rumble in Steam or SDL.
6. Save the log.

### Phase 3: `experiments/viiper_ns2pro_to_real_pro2_rumble_probe`

Start this only if Phase 2 receives non-zero 16+16 rumble.

Purpose:

```text
VIIPER ns2pro output rumble
-> translation / compatibility check
-> real Pro2 BLE HID OUT 0x02 / rumble characteristic
```

Required extra logs:

```text
[NS2PRO_OUTPUT] left_rumble_hex=... right_rumble_hex=...
[PRO2_RUMBLE_MAP] strategy=direct|switch_frame|freq_amp left5=... right5=...
[PRO2_BLE_OUT] len=33 packet=...
[PRO2_BLE_OUT] write_ok=true
```

Acceptance:

1. Real Pro2 is connected over BLE.
2. Probe receives VIIPER rumble output.
3. Probe maps the output to the existing Pro2 BLE rumble packet shape.
4. Real controller produces physical haptic feedback.
5. Stop packets reliably stop feedback.

## Route B: DualSense / PS5 Haptic Research

### Terms

DualSense ordinary rumble:

- HID output report fields for compatible rumble.
- Typically two intensity bytes, left/right or strong/weak equivalent.
- SDL and Linux can drive this path.
- It is not the full haptic audio path.

DualSense advanced haptics:

- DualSense uses voice-coil style haptic actuators.
- Advanced haptics are closely tied to audio routing and haptic audio streams.
- PC USB DualSense is more likely to expose these features because the host sees
  the USB HID and USB audio interfaces.

Adaptive triggers:

- Separate DualSense output report areas control L2/R2 trigger effects.
- These can include resistance, feedback, and vibration modes.
- Trigger effects do not map directly to Pro2 rumble; they are a separate
  translation problem.

### Why USB DualSense Is Easier Than Windows Bluetooth

Linux `hid-playstation.c` makes the distinction clear:

- DualSense has USB and Bluetooth output reports.
- Its common output report includes compatible rumble, audio controls, LEDs, and
  other fields.
- The driver handles audio plug state for USB.
- The driver explicitly notes that Bluetooth audio is not supported in that
  path.

SDL's PS5 HIDAPI path also separates ordinary rumble from audio haptics:

- When ordinary rumble is active, SDL sets compatible rumble fields.
- SDL comments that audio haptics are disabled during compatible rumble and
  restored when the emulated rumble bits are left off.

This supports the key V5.2 judgment: virtual HID alone is probably not enough
for true DualSense haptics. A convincing DualSense route needs the host-side
audio device and a way to capture or route haptic audio.

### DS5Dongle

DS5Dongle turns a Pico2W into a wireless adapter for DualSense:

- PC sees a USB device that behaves like a wired DualSense bridge.
- The Pico2W connects to the real DualSense over Bluetooth.
- The project advertises HD haptics support.
- Pico W builds support haptics but not speaker audio because of performance.

Its audio code reads 4-channel USB audio, separates channels 3/4 for haptics,
resamples from 48 kHz to 3 kHz, converts to signed 8-bit haptic samples, and
packs haptic audio data into Bluetooth packets. This is valuable research for
understanding DualSense haptic audio, but it requires extra hardware and targets
DualSense hardware, not Pro2.

V5.2 decision: useful reference, not the first project integration route.

### `dualsense-bt-haptics` / SAxense

`dualsense-bt-haptics` is a Windows-oriented experiment that:

- creates a virtual DualSense-like controller through a forked ViGEmBus path,
- listens to a real Bluetooth DualSense,
- creates or depends on a virtual audio device,
- captures game haptic audio,
- injects Bluetooth haptics based on SAxense's packet research.

SAxense is a Linux POC for DualSense / Edge Bluetooth haptics. It can take
audio-like input, convert it to a low-rate haptic stream, and write it to the
DualSense hidraw device. It recommends a dedicated PipeWire haptics sink and
warns that loopback capture can add latency.

These projects strongly imply that haptic audio capture is the real hard part.
For Windows, a pure virtual DualSense HID device is likely insufficient unless
the game also sees and writes to a DualSense audio endpoint. A complete pure
software path may require:

- virtual DualSense HID,
- virtual USB audio device with the expected DualSense name/interface shape,
- audio stream capture,
- haptic audio analysis,
- Pro2 HD rumble translation.

V5.2 decision: document and design probes only. Do not implement unless the
Pro2/VIIPER route fails or a real DualSense is available for capture.

### Steam Input Impact

Steam Input can help or hurt depending on the target:

- For Switch 2 Pro / ns2pro, Steam/SDL recognizing the virtual controller is
  useful because it may generate Switch 2 Pro output reports.
- For DualSense advanced haptics, Steam Input may translate controller output
  into ordinary rumble unless the game is allowed to access the real/virtual
  DualSense path directly.
- DualSense haptic audio often depends on a game selecting the DualSense audio
  endpoint. If Steam Input hides or remaps the controller, advanced haptics may
  disappear.

Probe design should test both Steam Input on and off.

## Route B Probe Designs

Do not implement these in Phase 1.

### Probe B1: DualSense Output Report Probe

Requires a real DualSense, preferably USB first.

Goal:

```text
real DualSense
-> supported PC game or SDL/engine sample
-> capture HID output reports
-> classify ordinary rumble, adaptive triggers, lightbar, audio controls
```

Expected logs:

```text
[DUALSENSE_HID] output_report_id=...
[DUALSENSE_TRIGGER] left=... right=...
[DUALSENSE_RUMBLE] left=... right=... compatible=true enhanced=...
[DUALSENSE_LIGHTBAR] rgb=...
```

Acceptance:

- Probe captures USB output report `0x02` and/or BT output report `0x31`.
- Ordinary rumble bytes are distinguishable from trigger effect bytes.
- Adaptive trigger payload changes in a known DualSense-capable test.
- Logs are enough to design a translator, even if no Pro2 output is attempted.

### Probe B2: DualSense Haptic Audio Probe

Requires a game/app that outputs DualSense haptic audio. A real USB DualSense is
strongly recommended for discovery.

Goal:

```text
game/app
-> DualSense audio device or virtual capture endpoint
-> haptic audio stream
-> activity / waveform analysis
```

Expected logs:

```text
[DUALSENSE_AUDIO] device=...
[HAPTIC_AUDIO] channels=... sample_rate=... rms=... peak=...
[HAPTIC_AUDIO] activity=true
```

Acceptance:

- Windows exposes or can emulate a target audio endpoint that games will choose.
- Probe sees non-silent haptic audio during a known haptic event.
- Channel layout and sample rate are known.
- Latency is measurable.

Possible Windows capture approaches:

- WASAPI loopback from a real DualSense audio endpoint.
- Virtual audio cable / virtual audio device named like DualSense.
- A custom virtual audio driver only if off-the-shelf capture proves inadequate.

V5.2 judgment: B1/B2 are important, but they should not block Route A. They are
more appropriate for V5.3 unless VIIPER `ns2pro` fails.

## Phase Plan

### Phase 1: Document And Decide

Output:

```text
docs/v5_2_hd_haptic_research.md
```

Status: this document.

### Phase 2: VIIPER `ns2pro` Probe

Output:

```text
experiments/viiper_ns2pro_probe/
  README.md
  run_probe.ps1
```

Only validates virtual ns2pro creation, synthetic input, and HD rumble output
capture.

### Phase 3: VIIPER To Real Pro2 Rumble Probe

Only start after Phase 2 gets non-zero 16+16 rumble.

Output:

```text
experiments/viiper_ns2pro_to_real_pro2_rumble_probe/
```

Goal: translate VIIPER ns2pro output to real Pro2 BLE rumble.

### Phase 4: Formal V5.2 Integration

Only after Phase 3 success.

Possible formal mode:

```text
output_mode = ns2pro_viiper
```

Rules:

- experimental/advanced GUI section only,
- off by default,
- `pro2` remains default,
- no removal of `ps4`,
- no hard dependency on VIIPER for normal V5.1/V5.2 operation.

### Phase 5: DualSense Haptic Research

Continue as research branch / V5.3 pre-research:

- B1: DualSense HID output report probe.
- B2: DualSense haptic audio capture probe.
- Decide whether a real DualSense and virtual audio device are required.

## Validation Matrix

| Test | Hardware | Software | Pass Signal |
| --- | --- | --- | --- |
| VIIPER driver readiness | PC only | `usbip-win2`, VIIPER | virtual USB device can attach |
| ns2pro identity | PC only | Device Manager, Steam, SDL | visible as Switch 2 Pro / ns2pro |
| synthetic input | PC only | probe + Steam/SDL | buttons/sticks/gyro move |
| ns2pro rumble output | PC only | Steam rumble test / SDL test / game | non-zero `LeftRumble[16]` and `RightRumble[16]` |
| Pro2 passthrough | ESP32-S3 + real Pro2 | Phase 3 probe | real Pro2 vibrates and stops |
| DualSense HID output | real DualSense | B1 probe + game | output report logs classify trigger/rumble/light |
| DualSense haptic audio | real DualSense or virtual audio | B2 probe + haptic sample game | non-silent haptic audio stream |

## References

- VIIPER repository: https://github.com/Alia5/VIIPER
- VIIPER overview: https://github.com/Alia5/VIIPER/blob/main/docs/index.md
- VIIPER installation and USBIP notes: https://github.com/Alia5/VIIPER/blob/main/docs/getting-started/installation.md
- VIIPER `ns2pro` device docs: https://github.com/Alia5/VIIPER/blob/main/docs/devices/ns2pro.md
- VIIPER `libVIIPER` docs: https://github.com/Alia5/VIIPER/blob/main/docs/libviiper/overview.md
- VIIPER `ns2pro` source: https://github.com/Alia5/VIIPER/tree/main/device/ns2pro
- VIIPER `ns2pro` C example: https://github.com/Alia5/VIIPER/tree/main/examples/libVIIPER/C/ns2pro_cli
- usbip-win2: https://github.com/vadimgrn/usbip-win2
- SDL Switch 2 HIDAPI: https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_switch2.c
- SDL PS5 HIDAPI: https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c
- Linux `hid-playstation.c`: https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c
- DS5Dongle: https://github.com/awalol/DS5Dongle
- `dualsense-bt-haptics`: https://github.com/awalol/dualsense-bt-haptics
- SAxense: https://github.com/egormanga/SAxense
- Unreal-Dualsense: https://github.com/rafaelvaloto/Unreal-Dualsense
- Gamepad-Core: https://github.com/rafaelvaloto/Gamepad-Core

## Phase 1 Conclusion

V5.2 HD Haptic Research conclusion:

1. Pro2 HD route:
   - Recommended route: VIIPER `ns2pro`.
   - Technical base: virtual Switch 2 Pro USB device via VIIPER + USBIP; capture
     HID output report `0x02` as `LeftRumble[16]` / `RightRumble[16]`.
   - Minimum validation: `experiments/viiper_ns2pro_probe` with synthetic input
     and non-zero rumble callback capture.
   - Biggest risk: the 16+16 bytes may not directly match the real Pro2 BLE
     rumble characteristic packet; Steam/SDL may only generate encoded ordinary
     rumble rather than richer game-authored haptics.
   - Enter probe: yes, Phase 2 should start with VIIPER `ns2pro`.

2. PS5 / DualSense route:
   - Recommended route: research branch only for V5.2; likely V5.3 if proven.
   - Technical base: DualSense HID output reports plus USB/audio haptic stream;
     DS5Dongle, SAxense, and `dualsense-bt-haptics` are useful references.
   - Minimum validation: B1 real DualSense output report capture and B2 haptic
     audio activity probe.
   - Biggest risk: true advanced haptics require a DualSense audio endpoint and
     haptic audio stream capture; pure virtual HID is probably insufficient.
   - Enter probe: not yet unless a real DualSense and audio capture target are
     available. Do not block Route A.

3. Abandoned route:
   - DS4 / ViGEm HD.
   - Reason: DS4 ordinary rumble exposes small/large motor intensity, not Switch
     HD Rumble and not DualSense haptic audio. Keep it as V5.1 stable ordinary
     rumble mode only.

4. Next execution:
   - Add `experiments/viiper_ns2pro_probe`.
   - Dependencies: `usbip-win2`, VIIPER server or `libVIIPER`, Steam/SDL rumble
     test target.
   - User hardware action: for Phase 2, only PC-side validation is required; no
     real Pro2 is needed until Phase 3. For Phase 3, connect ESP32-S3 and real
     Switch 2 Pro Controller. For DualSense probes, provide a real DualSense and
     a known haptic-capable game/app.
