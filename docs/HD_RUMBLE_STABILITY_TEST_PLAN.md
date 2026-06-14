# HD Rumble Stability Test Plan

## Current Findings

The three Hogwarts Legacy traces exposed separate failure classes:

1. Concurrent HD and ordinary BLE writers could overload the path and panic.
2. Host USB bus reset was followed by an unnecessary firmware disconnect/connect,
   creating a second visible controller loss.
3. Ordinary HID output cleared queued HD state before the single writer could
   transmit it.

These failures must be measured independently. A stable ordinary fallback does
not prove that HD remained active.

## Output Policy

The Pro2 actuator path has one BLE writer and two independent source states:

- HD raw `0x02` is selected while fresh.
- Ordinary motor state is retained in the background.
- Fresh ordinary state resumes immediately when HD ends.
- No stop packet is inserted during HD-to-ordinary fallback.
- Stop packets are sent only when both sources are inactive.
- Non-rumble HID output never changes either source.

HD and ordinary output therefore cooperate through priority and fallback. They
are not byte-mixed or rapidly interleaved.

## Required Evidence

Capture `status diag` every two seconds and preserve all serial lines. Each run
must include:

- `audio_streaming`, submitted packets, queue depth, and processing time
- `raw02_targets`, `raw02_ble_writes`, and raw `0x02` BLE errors
- selected `rumble_source`, transitions, preemptions, and ordinary fallbacks
- ordinary and stop BLE writes
- USB bus/configuration resets and recovery inhibition reason
- UAC transfer errors, endpoint rearm failures, and microphone alt-1 attempts
- BLE notification age, disconnect count, and input age

## Test Sequence

### 1. Arbitration Smoke Test

- Start ordinary vibration.
- Start HD haptic audio while ordinary state is still fresh.
- Confirm source changes `ordinary -> hd`.
- Refresh ordinary output while HD remains active.
- Stop HD and confirm `hd -> ordinary` with no stop write between them.
- Stop ordinary and confirm exactly three stop writes.

### 2. Game Startup Test

- Start trace before launching Hogwarts Legacy.
- Enter gameplay and continue for at least 30 minutes.
- Mark every perceived change from HD to ordinary with a timestamp.
- Compare the timestamp against audio streaming, raw target, actual BLE write,
  and source transition counters.

### 3. USB Reset Test

- Exercise game launch, audio-device changes, suspend/resume, and controller
  reconnect.
- A host bus reset may increment reset counters.
- Firmware must not force disconnect/connect while audio is streaming.
- During re-enumeration, recovery must remain inhibited for 15 seconds.
- HID reports and UAC packets must resume without a second firmware-created
  reset.

### 4. Long Run

- Run at least two hours with mixed combat, menus, cutscenes, and idle periods.
- Confirm no ESP panic, BLE disconnect, stuck vibration, or permanent HID stall.
- Confirm HD writes continue whenever the game keeps the haptic audio stream
  active.

## Diagnosis Matrix

| Observation | Interpretation |
| --- | --- |
| Audio streaming stops and raw targets stop | Host/game ended the HD path |
| Raw targets rise but HD BLE writes do not | Arbitration or BLE writer defect |
| HD BLE errors rise | BLE transport/backpressure problem |
| UAC transfer or rearm errors rise first | USB audio transport problem |
| Bus reset rises without firmware recovery | Host reset handled as intended |
| Firmware recovery rises during audio | Recovery guard regression |
| Source changes `hd -> ordinary` while audio continues | HD freshness/conversion defect |
| Source remains `hd`, writes continue, feel becomes ordinary | Mapping quality issue, not disconnect |
| Microphone alt-1 attempts/rejects rise before reset | Advertised mic stub is involved |

## Acceptance

- No firmware-created USB reconnect while UAC streaming is active.
- No ordinary or non-rumble HID report cancels fresh HD state.
- Every raw target accepted by the backend produces HD BLE writes unless BLE is
  unavailable.
- HD-to-ordinary fallback is explicit, counted, and free of an intermediate
  stop.
- No permanent HID or audio stall during a two-hour mixed gameplay run.
