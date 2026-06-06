# V5.3 DualSense Upstream Research

Date: 2026-06-06

## 1. DS5Dongle: Why PC Can See A Wireless DualSense As A Wired-Like Device

DS5Dongle uses a Pico2W as a USB host-visible bridge while the real DualSense
connects to the Pico over Bluetooth. The public README says the Pico device
appears to the host only after the controller connects.

Practical meaning for this project:

```text
real DualSense over Bluetooth
-> hardware bridge
-> PC-visible USB controller shape
-> keep richer DualSense behavior closer to the wired path
```

This matters because many PC games treat USB DualSense as the most reliable
native path for advanced features. A hardware bridge can preserve the host
contract better than a DS4/XInput compatibility layer.

## 2. DS5Dongle Enhanced Haptics

DS5Dongle advertises enhanced / HD haptics support. Treat this as a bridge
reference, not as a copy-paste solution:

- It targets a real DualSense.
- It uses a dedicated hardware bridge.
- It does not directly translate DualSense haptics to Switch 2 Pro raw02.
- It suggests that advanced haptics need a faithful DualSense-facing host shape.

V5.3 takeaway:

```text
Study DS5Dongle for host-visible DualSense bridge design.
Do not merge it into V5.2.
Do not claim Pro2 PS5 haptic support from this alone.
```

## 3. dualsense-bt-haptics: Haptic Audio + Virtual Device

`dualsense-bt-haptics` combines:

- a virtual DualSense-like controller,
- real Bluetooth DualSense input/output forwarding,
- a virtual audio endpoint named like `DualSense Wireless Controller`,
- haptic audio conversion based on SAxense research.

Its README also warns that the route is not universal and may show around
hundreds of milliseconds of latency in some setups.

V5.3 takeaway:

```text
advanced haptics may require both HID and a game-visible audio endpoint
ordinary rumble alone is insufficient
latency must be measured, not guessed
```

## 4. SAxense Latency Sources

SAxense converts haptic audio into DualSense Bluetooth haptic packets on Linux.
Its README notes that delay can come from loopback audio capture latency rather
than from SAxense or HID/Bluetooth itself.

Latency sources to measure:

- game audio/haptic scheduling,
- virtual audio endpoint buffering,
- WASAPI/PipeWire loopback buffering,
- resampling to the controller haptic packet rate,
- HID/Bluetooth write cadence,
- controller-side buffering.

V5.3 should log timestamps at every boundary before deciding whether a
DualSense-source-to-Pro2 translator feels usable.

## 5. Pure Software Virtual DualSense Requires Virtual Audio

A virtual DualSense HID device can be enough for enumeration, basic input,
ordinary rumble, lightbar, or adaptive-trigger experiments. It is likely not
enough for native advanced haptics if the game expects to send haptic audio to a
DualSense audio endpoint.

Minimum credible pure-software route:

```text
virtual DualSense HID
virtual DualSense-like audio endpoint
WASAPI loopback or direct audio capture
HID/adaptive trigger output capture
haptic audio analyzer
translator to Pro2 raw02 / HD rumble
```

## 6. Windows Virtual Audio Driver Risk

On Windows, a reliable virtual audio device normally means driver work. Kernel
driver loading and signing policy applies, and unknown virtual audio drivers
should not be installed as part of this project without a separate safety
review.

For V5.3:

- prefer real USB DualSense capture first,
- then consider known, reversible audio loopback tools for research,
- avoid shipping or requiring an unknown signed driver,
- do not block V5.2 on this route.

## 7. Future Route Ranking

Recommended order:

1. Real capture:

   Attach a real DualSense over USB, enumerate HID/audio, run a native
   DualSense-capable PC game, and capture output.

2. DS5Dongle study:

   Use it as a hardware bridge reference for preserving a host-visible
   DualSense shape.

3. Virtual HID + audio:

   Only after real capture proves what the host emits. This is the hardest
   route because both controller and audio device shape must match.

4. Haptic audio -> Pro2 translator:

   Translate measured DualSense haptic activity into Pro2 raw02 / HD rumble
   payloads. Do this only after audio/HID capture is real.

## Source Notes

Relevant upstream references:

- [awalol/DS5Dongle](https://github.com/awalol/DS5Dongle): Pico2W DualSense bridge and enhanced haptics reference.
- [awalol/dualsense-bt-haptics](https://github.com/awalol/dualsense-bt-haptics): Windows-oriented virtual controller/audio + Bluetooth haptic forwarding experiment.
- [egormanga/SAxense](https://github.com/egormanga/SAxense): Linux DualSense haptics over Bluetooth proof of concept.
- [Paliverse/DualSenseX](https://github.com/Paliverse/DualSenseX): PC DualSense control app reference.
- [rafaelvaloto/Gamepad-Core](https://github.com/rafaelvaloto/Gamepad-Core): cross-platform DualSense/DualShock API reference.
- [Unreal-Dualsense](https://github.com/rafaelvaloto/Unreal-Dualsense): Unreal-oriented DualSense feature reference.
- [Linux hid-playstation.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c): kernel DualSense HID behavior reference.
- [SDL_hidapi_ps5.c](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c): SDL PlayStation HIDAPI implementation reference.
- [ValveSoftware/Proton](https://github.com/ValveSoftware/Proton): compatibility context for native PC game behavior under Proton.
