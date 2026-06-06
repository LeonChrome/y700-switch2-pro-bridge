# V5.3 DualSense Haptic Roadmap

Date: 2026-06-06

## Decision

DualSense haptics should stay in V5.3 research. It must not block V5.2 Pro2 HD
rumble, and it must not be merged into the V5.2 stable GUI until a real
DualSense HID device and DualSense audio endpoint are verified locally.

```text
v5_2_scope=Switch 2 Pro / ns2pro HD rumble
v5_3_scope=DualSense haptic research
current_blocker=no_real_dualsense_and_no_dualsense_audio_endpoint
```

## Three Layers

1. HID ordinary output

   This is normal controller output: basic rumble, lightbar, player LEDs, and
   simple state changes. It is useful for smoke tests, but it is not enough for
   DualSense advanced haptics.

2. Adaptive trigger HID output

   Adaptive triggers are HID output reports that configure trigger resistance
   patterns. They can be tested without solving haptic audio, but they are only
   one part of the DualSense feature set.

3. Haptic audio / audio endpoint

   The advanced grip haptics behave like a wideband audio/haptic stream, not
   just a traditional motor strength value. On PC, this usually means the
   controller must expose an audio endpoint and games must send the native
   DualSense haptic stream to it.

## Why ViGEm DS4 Is Not Enough

ViGEm DS4 gives a useful compatibility layer for ordinary gamepad input and
basic rumble. It does not expose a real DualSense device shape, adaptive trigger
semantics, or the haptic audio endpoint that native PC games expect.

## Why Virtual HID Alone May Still Be Insufficient

A virtual DualSense HID device may satisfy some enumeration and adaptive trigger
tests, but native DualSense haptics can also require an audio-class endpoint.
Without that endpoint, a game may never route haptic audio to the virtual
controller, so there is nothing meaningful to translate.

## Why A Virtual Audio Device May Be Needed

If this project wants to capture or translate PC DualSense haptic audio, it may
need to expose or monitor a dedicated audio endpoint. The endpoint would let
games output haptic audio, while the bridge captures metrics or raw PCM-like
haptic data for translation.

## DS5Dongle Study Route

DS5Dongle is valuable because it uses a Pico 2 W as a wireless bridge: the real
DualSense connects to the dongle over Bluetooth while the PC sees something
closer to a wired DualSense. Its public README describes full DualSense
connectivity, HD haptics support, and wireless Bluetooth bridging.

Core idea to study:

```text
real DualSense wireless
-> hardware bridge
-> PC-visible wired DualSense shape
-> preserve haptic/audio/adaptive trigger behavior
```

This is not a direct implementation plan for V5.2. It is a V5.3 reference for
how advanced haptics may need both HID and audio behavior, not just XInput/DS4
rumble.

## SAxense / dualsense-bt-haptics Study Route

SAxense is a Linux proof of concept for DualSense haptics over Bluetooth. Its
README demonstrates routing low-rate audio/haptic data through a haptics sink or
loopback capture, then writing generated data to a DualSense hidraw device.

Research value:

- capture or synthesize haptic audio,
- turn haptic audio into DualSense haptic packets,
- understand latency from loopback capture,
- separate real haptic audio capture from ordinary rumble.

## Candidate V5.3 Routes

A. Real DualSense capture route

Use a real USB DualSense, enumerate HID and audio endpoints, trigger known
native game haptics, and capture HID output plus audio activity. This is the
first route to run when hardware is available.

B. DS5Dongle study route

Study how a hardware bridge preserves a wired-DualSense host shape while the
controller is physically wireless. This may inform a future ESP32/Pico-class
bridge, but it is not a short V5.2 patch.

C. Virtual DualSense HID + virtual audio route

Expose a virtual DualSense-like HID device and a virtual audio endpoint. This is
the most complete but also the highest-effort route, because both device classes
must match what games expect.

D. Haptic audio to Pro2 translator route

Capture DualSense haptic audio activity and translate it to Switch 2 Pro raw02 /
HD rumble payloads. This should only be attempted after route A confirms real
DualSense haptic audio on this machine.

## Current Local Probe Status

```text
real_dualsense=false
dualsense_hid_output_probe=runnable_but_blocked
dualsense_haptic_audio_probe=runnable_but_blocked
blocker=no_real_dualsense_audio_endpoint
```

Run probes from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_dualsense_env.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

## Sources

- Sony support: [Pair a DualSense wireless controller with a computer](https://www.playstation.com/en-us/support/hardware/pair-dualsense-controller-bluetooth/)
  and [DualSense controller support](https://www.playstation.com/en-us/support/hardware/dualsense-controller-support/).
- DS5Dongle reference: [awalol/DS5Dongle](https://github.com/awalol/DS5Dongle).
- SAxense reference: [egormanga/SAxense](https://github.com/egormanga/SAxense).
- DualSense Bluetooth haptic forwarding reference: [awalol/dualsense-bt-haptics](https://github.com/awalol/dualsense-bt-haptics).
