# DualSense-to-Pro2 Roadmap

## Goal

Expose a stable DualSense-compatible USB device while using the real Pro2
controller as the physical input and vibration backend.

The target is full host-facing protocol compatibility where practical.
Physical parity has hardware limits: Pro2 does not provide DualSense adaptive
triggers, touchpad, speaker, microphone, light bar, or identical voice-coil
haptics. Those features must be represented as neutral, synthesized, or
explicitly unsupported rather than falsely reported as physically complete.

## Current Baseline

- DualSense-like USB identity: `054C:0CE6`
- 250 Hz HID input path from live BLE state
- ordinary DualSense motor output converted to Pro2 BLE rumble
- experimental four-channel UAC1 haptic-audio path
- host-intent arbitration between compatibility vibration and haptic audio
- bus-reset HID submission gate and bounded USB recovery
- Manager telemetry covering BLE, HID, USB resets, audio, and rumble

## Work Phases

### 1. Stability First

- complete repeated 30-minute and multi-hour runs
- exercise idle, suspend/resume, audio endpoint changes, and Steam restarts
- verify HID completions recover after every observed USB bus reset
- add fault-injection hooks for endpoint reset and delayed BLE notifications
- preserve raw firmware and host traces for every failure

Acceptance:

- no permanent HID input stall
- no uncontrolled USB re-enumeration loop
- BLE input remains fresh or recovers without Manager intervention
- no stuck vibration after host output stops

### 2. Input Fidelity

- verify every button and axis against a real DualSense trace
- characterize polling cadence, report sequence fields, timestamps, and IMU
- define deterministic mappings for controls that Pro2 cannot physically emit
- compare report captures byte-for-byte where values are expected to match

### 3. Output Report Coverage

- catalog DualSense output report flags and valid motor modes
- keep ordinary motor conversion independent from haptic-audio conversion
- treat `valid_flag0` bit 0 and `valid_flag2` bit 2 as valid compatibility
  motor commands
- treat `valid_flag0` bit 1 as compatibility-mode selection even when the
  current motor values are zero
- allow haptic-audio output only while compatibility mode is not selected
- switch modes per host command; do not classify or permanently cache a game
  as HD-capable
- issue bounded stop packets when the selected source becomes inactive
- implement LED/player-state responses as internal state even when no physical
  Pro2 equivalent exists
- reject malformed or incomplete reports without disturbing HID input

### 4. Haptic-Audio Conversion

- capture known DualSense haptic streams with synchronized host and firmware
  timestamps
- separate ordinary PCM from intended haptic content
- map transient energy and frequency bands into Pro2 left/right raw `0x02`
- enforce rate, amplitude, thermal, and stuck-output limits
- maintain a dry-run path that logs conversion without moving the controller

### 5. Regression System

- turn captured HID OUT and audio packets into replay fixtures
- add host-side assertions for counters, reset recovery, and rumble stop timing
- archive firmware hashes with every hardware test result
- keep Nintendo and XInput profiles as regression controls

## Engineering Guardrails

- do not change BLE reconnect logic to fix a USB-only failure
- do not mix ordinary motor and haptic-audio counters
- do not let LED or trigger-only HID output cancel either rumble source
- do not blend or interleave HD and ordinary BLE packets; host mode selects one
  actuator source at a time
- do not claim physical support for unavailable DualSense hardware
- do not make USB re-enumeration the normal recovery mechanism
- do not tune conversion from subjective feel without a synchronized trace
