# V5.8 / V6.0 Multimode Roadmap

Date: 2026-06-08

This document defines the next two product lines after the current V5.5 experimental manager and firmware work.

## 1. Scope Split

### V5.8

Goal: ship a practical multimode hardware receiver with three USB output identities:

- Xbox-compatible route
- Pro2 / Nintendo / `ns2pro`-leaning route
- DualSense-like route

V5.8 explicitly targets:

- shared input state
- shared ordinary rumble path
- stable mode switching
- no HD haptics claim

### V6.0

Goal: keep V5.8 multimode stable, then separately explore whether the DualSense-like controller-audio route can be recognized and opened by upstream PC titles.

V6.0 explicitly separates:

- `probe` mode: record only, do not vibrate Pro2
- `hd_only` mode: only forward haptic-like controller-audio events
- `pcm_haptic` mode: fallback audio-to-rumble approximation, not native HD

## 2. Shared Foundation

The first V5.8 requirement is a unified internal gamepad representation that is independent from USB identity.

Current foundation added in this branch:

- `firmware/esp32s3_switch2_bridge/main/bridge/internal_gamepad_state.h`
- `firmware/esp32s3_switch2_bridge/main/bridge/internal_gamepad_state.c`

The new `internal_gamepad_state_t` contains:

- buttons
- left/right sticks
- analog triggers
- gyro
- accel
- battery fields
- connection status
- input counters

Current conversion bridge:

- `switch2_state_to_internal(...)`
- `switch2_state_from_internal(...)`

Files:

- `firmware/esp32s3_switch2_bridge/main/bridge/switch2_state.h`
- `firmware/esp32s3_switch2_bridge/main/bridge/switch2_state.c`

This means BLE Pro2 input can now be normalized once, then mapped into multiple USB backends later.

## 3. Shared Ordinary Rumble

V5.8 should stop treating ordinary rumble as backend-specific byte shuffling.

Current foundation added in this branch:

- `firmware/esp32s3_switch2_bridge/main/bridge/normalized_rumble.h`
- `firmware/esp32s3_switch2_bridge/main/bridge/normalized_rumble.c`

The normalized model currently contains:

- `weak`
- `strong`
- `duration_ms`
- `left_gain_percent`
- `right_gain_percent`
- `stop`

Current first integration:

- `firmware/esp32s3_dualsense_identity_experiment/main/pro2_rumble_backend.c`

The DualSense-like ordinary rumble compatibility path now first converts DS5 output motors into `normalized_rumble_t`, then builds the Pro2 BLE vibration payload from that normalized representation.

This is the intended V5.8 direction for all non-HD rumble:

```text
host output report
-> backend-specific parser
-> normalized_rumble_t
-> backend-specific physical rumble encoder
-> real Pro2
```

## 4. Backend Layering

Current mapper layering improved in this branch:

- `firmware/esp32s3_dualsense_identity_experiment/main/dualsense_report_mapper.c`
- `firmware/esp32s3_switch2_bridge/main/bridge/report_mapper.c`

Both now accept or can derive from the shared internal state model:

- `dualsense_report_mapper_from_internal(...)`
- `report_mapper_internal_to_generic_report(...)`
- `report_mapper_internal_to_nintendo_report(...)`

This is the intended V5.8 layering:

```text
BLE Pro2 input
-> internal_gamepad_state_t
-> xbox backend
-> pro2 / nintendo backend
-> dualsense-like backend
```

## 5. V5.8 Manager Direction

V5.8 manager requirements:

1. Save desired mode.
2. Flash / switch target backend profile.
3. Wait for USB re-enumeration.
4. Confirm expected identity.
5. If confirmation fails, keep rollback path obvious.

Current foundation added in this branch:

- `windows/v55_manager_app/OutputModeCatalog.cs`
- `windows/v55_manager_app/ManagerSettingsStore.cs`

Current capabilities from this foundation:

- persistent last port
- persistent BLE target
- persistent audio device name
- saved last successful profile id
- saved previous successful profile id
- pending mode-switch verification marker

Current manager behavior:

- after flashing, remember the requested profile and expected USB marker
- on next USB check, confirm the marker if it appears
- if the expected marker still does not appear after a delay, surface rollback guidance

This is not the full V5.8 UX yet, but it is the first mode-switch state machine foundation.

## 6. V6.0 DualSense Audio Compat Probe

Before chasing feel, V6.0 first needs to answer a binary question:

Can the upstream PC stack see the DualSense-like HID and the `Wireless Controller Audio` endpoint as one compatible controller-audio pair?

Current foundation added in this branch:

- `windows/v55_manager_app/DualSenseAudioCompatProbe.cs`

The probe currently records:

- HID-like DualSense candidates
- audio PnP candidates
- MMDevice audio endpoints
- HID ContainerID
- audio PnP ContainerID
- endpoint names
- match mode:
  - `container_id`
  - `name_only`
  - `no_audio_endpoint`
  - `no_hid`
  - `none`

Current manager integration:

- the existing audio list action now also runs the DualSense audio compatibility probe
- probe output is appended to the manager log with `[DS5_AUDIO_COMPAT]`

This is the first V6.0-alpha step.

## 7. V6.0 Progressive Stages

### V6.0-alpha

Acceptance target:

- test program can find HID and audio endpoint
- endpoint name contains `Wireless Controller` or equivalent
- `audio_packets` can increase
- probe mode keeps Pro2 silent

### V6.0-beta

Acceptance target:

- `hd_only` path prefers ch2/ch3
- ordinary PCM is filtered
- haptic-like events can produce distinct `tick / punch / texture`

### V6.0-rc

Acceptance target:

- at least one native DualSense-capable PC title opens the controller-audio endpoint
- main game audio still stays on the default desktop output
- `Wireless Controller Audio` receives independent data

## 8. Known Boundaries

What this branch does not claim yet:

- a finished Xbox backend
- a finished V5.8 three-button mode switch UI
- that controller-audio HD is broadly supported by PC games
- that `name_only` audio matching is sufficient for upstream game support
- that ordinary PCM-to-rumble fallback is native DualSense HD

## 9. Next Implementation Steps

### V5.8 next

1. Add a real Xbox-compatible USB backend.
2. Move live input producers to populate `internal_gamepad_state_t` directly.
3. Route all ordinary rumble through `normalized_rumble_t`.
4. Expose three explicit mode cards in the manager using `OutputModeCatalog`.
5. Add safe detach / re-enumerate / rollback UI.

### V6.0 next

1. Expand the compat probe to correlate HID and MMDevice endpoints more aggressively.
2. Add explicit `probe`, `hd_only`, and `pcm_haptic` runtime modes.
3. Add byte counters, RMS, peaks, transient stats, and packet age in one structured status snapshot.
4. Test at least one upstream title with Steam Input off.
