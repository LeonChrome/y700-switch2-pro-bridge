# V5.3 DualSense Haptic Roadmap

Date: 2026-06-06

## Decision

V5.3 is the DualSense / PS5 haptic source research phase. It must not be added
to the V5.2 GUI and must not be described as supported until real output data is
captured.

Target:

```text
game / Steam / native PC DualSense output
-> DualSense advanced haptic source
-> HID output and/or haptic audio endpoint
-> capture and analyze
-> future translation to Pro2 raw02 / HD rumble
```

Current local state:

```text
real_dualsense=false
hid_usb=false
hid_bluetooth=false
audio_endpoint=not_found
wasapi_loopback=false
dualsense_hid_output_probe=runnable_but_blocked
dualsense_haptic_audio_probe=runnable_but_blocked
current_blocker=no_real_dualsense_and_no_dualsense_audio_endpoint
```

## Signal Types To Keep Separate

Ordinary rumble:

- Traditional low/high motor strength feedback.
- Useful as a smoke test.
- Not enough to prove advanced DualSense haptics.

Adaptive trigger HID output:

- HID output reports configure L2/R2 trigger resistance and effects.
- Can be probed separately from grip haptic audio.
- Should be logged as trigger output, not as HD haptic audio.

Haptic audio / audio endpoint:

- Advanced grip haptics can be delivered through an audio-like endpoint.
- On PC, Sony states haptic feedback on PC requires USB and game support.
- A native game may route haptic content to a DualSense audio endpoint instead
  of emitting ordinary rumble.

Lightbar, mute LED, speaker, and status outputs:

- Useful for identifying the output report shape.
- Not evidence of advanced haptics by themselves.

Steam Input wrapped ordinary feedback:

- Steam may translate or wrap feedback into ordinary controller rumble.
- This is not the same as a game sending native DualSense haptic output.

Native game DualSense output:

- The most valuable source for V5.3.
- First pass should prefer USB DualSense and Steam Input disabled for the game,
  when the game supports native DualSense features.

## Why DS4 / ViGEm Is Not Enough

DS4/ViGEm compatibility is useful for ordinary input and basic rumble. It does
not prove a host-visible DualSense device shape, adaptive trigger semantics, or
a DualSense audio endpoint. A pure DS4 route cannot be treated as PS5 haptic
support.

## Why DualSense Is Worth Researching

DualSense is worth a V5.3 phase because PC games are more likely to generate an
advanced haptic source for it than for Switch 2 Pro today. Sony documents PC
USB/Bluetooth connectivity and notes that haptic feedback on PC depends on game
support and USB. That makes a real USB DualSense the cleanest first capture
target.

## Route A: Real Capture First

Use a real USB DualSense and capture:

```text
HID enumeration
VID/PID
USB vs Bluetooth transport
output report length
ordinary rumble output
adaptive trigger output
audio endpoint presence
WASAPI loopback activity
native game scene metadata
```

This is the preferred V5.3 first step because it avoids guessing the host
contract.

## Route B: DS5Dongle Study

DS5Dongle turns a Pico2W into a wireless adapter for a real DualSense. Its
project goal is a host-visible bridge after the real controller connects over
Bluetooth, while preserving enhanced haptics behavior.

Research value:

- shows a hardware-assisted DualSense bridge shape,
- suggests haptics may require preserving more than ordinary HID rumble,
- useful reference for future ESP32/Pico-class bridge ideas.

It is not a direct V5.2 solution and still depends on a real DualSense.

## Route C: dualsense-bt-haptics / SAxense

`dualsense-bt-haptics` combines a virtual DualSense-like controller, a virtual
audio device, and Bluetooth haptic packet forwarding. Its README notes that it
is not universal and can have noticeable latency.

SAxense is a Linux proof of concept that converts low-rate audio/haptic input
into DualSense Bluetooth haptic packets. Its README explicitly warns that large
delays can come from loopback audio capture latency, not necessarily from the
HID/Bluetooth conversion itself.

Research value:

- separates haptic audio from ordinary rumble,
- highlights the need for a named virtual audio endpoint,
- exposes latency sources,
- gives a route from haptic audio to controller haptic packets.

## Route D: Pure Virtual DualSense HID + Audio

A virtual DualSense HID device alone is likely incomplete. A convincing pure
software route also needs a virtual audio endpoint that games recognize as a
DualSense / Wireless Controller audio path.

On Windows, virtual audio and virtual HID drivers are driver work, and kernel
driver loading/signing policy applies. Do not install unknown virtual audio
drivers during V5.3 probing.

## Probe Commands

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_dualsense_env.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

Expected blocked state when no real DualSense is attached:

```text
[DUALSENSE_ENV] hid_usb=false
[DUALSENSE_ENV] hid_bluetooth=false
[DUALSENSE_ENV] audio_endpoint=not_found
[DUALSENSE_ENV] wasapi_loopback=false
[DUALSENSE_BLOCKED] reason=no_real_dualsense
```

## Decision Gate For Future Translation

Proceed to a Pro2 translator only after at least one of these is true:

- real DualSense HID output changes are captured from a native game,
- adaptive trigger output reports are classified,
- DualSense audio endpoint activity is captured through WASAPI loopback,
- a reliable virtual DualSense + virtual audio stack is proven in a controlled
  environment.

Until then:

```text
ps5_haptic_support=false
dualsense_in_v5_2_gui=false
safe_next=attach real DualSense over USB and rerun V5.3 probes
```

## Sources

- Sony support: [DualSense wireless controllers with PC, Mac and mobile devices](https://www.playstation.com/en-us/support/hardware/pair-dualsense-controller-bluetooth/).
- DS5Dongle reference: [awalol/DS5Dongle](https://github.com/awalol/DS5Dongle).
- dualsense-bt-haptics reference: [awalol/dualsense-bt-haptics](https://github.com/awalol/dualsense-bt-haptics).
- SAxense reference: [egormanga/SAxense](https://github.com/egormanga/SAxense).
- Linux DualSense HID reference: [hid-playstation.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c).
- SDL DualSense HIDAPI reference: [SDL_hidapi_ps5.c](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c).
- Microsoft driver signing policy: [Driver Signing Policy](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-).
