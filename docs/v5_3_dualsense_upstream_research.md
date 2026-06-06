# V5.3 DualSense Upstream Research

Date: 2026-06-06

## Engineering Goal

V5.3 is not trying to claim PS5 haptics are supported today. The goal is to
identify a real advanced haptic source and prepare probes that can later feed a
Pro2 raw02 translator.

Priority:

```text
A. real DualSense USB capture
B. DS5Dongle study / hardware bridge
C. virtual DualSense HID + virtual audio
D. haptic audio -> Pro2 raw02 translator
```

## Project Matrix

| Reference | Solves | Haptic audio | Real DualSense | Virtual USB/HID | Virtual audio | Direct value | Limit | Probe/translator use |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| DS5Dongle | Hardware bridge where Pico2W exposes a host-visible DualSense-like path while the real controller is wireless | Claims enhanced haptics support | Yes | Pico2W acts as USB bridge | Not the main public focus | Shows a hardware bridge can preserve richer DualSense behavior | Requires extra hardware and real DualSense | Study host-visible device shape and bridge timing |
| dualsense-bt-haptics | Windows Bluetooth haptic forwarding via virtual controller plus real DualSense | Yes, via virtual audio device and SAxense-derived packet path | Yes | Uses virtual DualSense/ViGEm fork | Yes | Shows Windows route needs both HID and named audio endpoint | Not universal, driver work, reported latency | Strong reference for virtual audio + haptic packet translator |
| SAxense | Linux POC converting audio/haptic stream to DualSense Bluetooth haptic packets | Yes | Yes | Uses Linux hidraw/uhid path | PipeWire sink/capture route | Shows haptic audio to controller-packet conversion and latency causes | Linux-specific, not direct Windows implementation | Reference for audio windows, low-rate conversion, latency budget |
| Unreal-Dualsense | Unreal/engine-facing DualSense feature integration | Partial, mostly engine-side HID/features depending on implementation | Usually yes | No | No | Shows game-engine APIs can emit native DualSense features | Engine/game-specific, not a capture layer | Useful for controlled game-side trigger/output tests |
| Gamepad-Core | Cross-platform DualSense/DualShock API | Not primarily haptic audio | Yes for real features | No | No | API reference for HID feature control and classification | Library focus, not source capture | Reference for output report categories and adaptive trigger handling |
| Linux hid-playstation.c | Kernel DualSense HID driver behavior | Handles DualSense device behavior, not game haptic audio source | Yes | Kernel driver, not virtual host for Windows | No | Ground truth for report IDs, CRC, motion, battery, output behavior | Linux kernel context | Reference for HID report parsing and safe output boundaries |
| SDL SDL_hidapi_ps5.c | SDL PlayStation HIDAPI backend | Handles rumble/trigger/lightbar paths in SDL context | Yes | No | No | Shows how SDL classifies PS5 reports and exposes rumble/trigger APIs | SDL API may wrap ordinary feedback, not native game haptic audio | Reference for output classification and Steam/SDL comparison |
| Proton / Steam Input | PC game compatibility and controller translation stack | Depends on game and Steam Input state | Usually yes for native capture | Steam may wrap/translate | Depends on game/audio endpoint | Explains why Steam Input on/off must be recorded | Can hide native DualSense path | Test matrix variable, not direct implementation |

## 1. DS5Dongle

It solves a wireless bridge problem: a real DualSense connects to Pico2W over
Bluetooth, while the host sees a USB device after the controller is connected.

Engineering judgment:

- It likely preserves more of the wired DualSense host contract than a DS4 or
  XInput wrapper.
- It is valuable for understanding host-visible device shape and bridge timing.
- It is not a direct Pro2 translator.
- It still requires a real DualSense.

Recommended use:

```text
study only in V5.3
do not merge into V5.2
compare with real USB DualSense capture once hardware is available
```

## 2. dualsense-bt-haptics

It solves Bluetooth haptics on Windows by combining a virtual DualSense-like
controller, a virtual audio endpoint, and Bluetooth packet forwarding to the
real controller.

Engineering judgment:

- It directly supports the idea that haptic audio may require a named audio
  endpoint such as `DualSense Wireless Controller`.
- It is not universal and may carry noticeable latency.
- It implies pure HID is probably not enough for native advanced haptics.
- It requires driver-level components and real DualSense hardware.

Recommended use:

```text
reference for virtual audio + haptic packet path
do not install its driver stack automatically
borrow only the measurement questions and architecture constraints
```

## 3. SAxense

It converts audio-like haptic data into DualSense Bluetooth haptic packets on
Linux. Its README specifically calls out loopback capture latency as a possible
source of large delays.

Engineering judgment:

- It is the strongest public clue for audio-to-haptic packet translation.
- It is Linux-specific and not directly portable to this Windows bridge.
- It proves that latency must be measured at capture, conversion, transport, and
  controller boundaries.

Recommended use:

```text
copy the latency thinking, not the platform assumptions
use RMS/peak/transient windowing as V5.3 Phase 2 inspiration
```

## 4. Unreal-Dualsense

It is useful as an engine-side feature reference: games or engines can actively
emit DualSense-specific trigger/haptic output when they know a DualSense is
present.

Engineering judgment:

- It may help create controlled native DualSense scenes.
- It does not solve capture by itself.
- It is not a Pro2 translator.

Recommended use:

```text
possible controlled source for HID output and adaptive trigger tests
not required for first V5.3 capture
```

## 5. Gamepad-Core

It is a cross-platform DualSense/DualShock API reference. Its value is report
classification and feature-control thinking.

Engineering judgment:

- Useful for understanding ordinary output and adaptive trigger categories.
- Not primarily an advanced haptic audio capture route.
- Requires real controller behavior to validate.

Recommended use:

```text
reference for output report categories
compare against our [DUALSENSE_OUTPUT] classifier
```

## 6. Linux hid-playstation.c

Linux `hid-playstation.c` is a ground-truth reference for Sony controller HID
behavior in a production driver.

Engineering judgment:

- Useful for report IDs, CRC/transport differences, calibration, motion, battery
  and output boundaries.
- It does not provide a Windows haptic audio endpoint.
- It should shape safe HID parsing decisions.

Recommended use:

```text
reference for report parsing and safe output limits
do not treat it as a Windows audio capture implementation
```

## 7. SDL SDL_hidapi_ps5.c

SDL's PlayStation HIDAPI backend shows how SDL maps PS5 controller reports,
rumble, triggers, lightbar, and related APIs.

Engineering judgment:

- Useful for classifying ordinary rumble versus trigger output.
- SDL feedback may be API-level ordinary rumble, not native haptic audio.
- Steam/SDL behavior must be recorded separately from native game output.

Recommended use:

```text
reference for output category names
compare Steam Input on/off against direct native game behavior
```

## 8. Proton / Steam Input

Proton and Steam Input affect whether a game sees a native DualSense, a wrapped
controller, or an ordinary rumble path.

Engineering judgment:

- Steam Input on/off must be logged for every game capture.
- Native DualSense features may require Steam Input off in some games.
- Steam Input can also make a controller usable while hiding native advanced
  output from our capture goal.

Recommended use:

```text
always log steam_input=on/off
do not call ordinary Steam rumble advanced DualSense haptic
```

## Recommended Route Priority

1. A. Real DualSense USB capture

   Highest priority. It answers what this machine and games actually emit.

2. B. DS5Dongle study / hardware bridge

   Useful if wireless/passthrough becomes important or if USB native capture
   reveals bridge-shape requirements.

3. D. Haptic audio -> Pro2 raw02 translator

   Start offline after real haptic audio exists. Do not start live forwarding
   first.

4. C. Virtual DualSense HID + virtual audio

   Highest effort and highest driver risk. Keep as later research unless real
   capture proves the exact host contract.

## Sources

- [awalol/DS5Dongle](https://github.com/awalol/DS5Dongle)
- [awalol/dualsense-bt-haptics](https://github.com/awalol/dualsense-bt-haptics)
- [egormanga/SAxense](https://github.com/egormanga/SAxense)
- [rafaelvaloto/Unreal-Dualsense](https://github.com/rafaelvaloto/Unreal-Dualsense)
- [rafaelvaloto/Gamepad-Core](https://github.com/rafaelvaloto/Gamepad-Core)
- [Linux hid-playstation.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c)
- [SDL_hidapi_ps5.c](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c)
- [ValveSoftware/Proton](https://github.com/ValveSoftware/Proton)
