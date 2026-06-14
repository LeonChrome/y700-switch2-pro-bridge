# V5.9.2 新和联胜版本

V5.9.2 promotes the mature PS5-compatible path to the main `新和联胜` mode.

## Three Modes

- 新和联胜 / PS5: strict DualSense-compatible USB identity, ordinary rumble, and four-channel HD haptic audio.
- Pro2 / Nintendo: native Pro2-style input and raw `0x02` rumble.
- Xbox / XInput: broad XInput compatibility and ordinary dual-motor rumble.

The old Xbox Elite 2 / GIP bring-up profile is not included.

## Controller Connection

The Manager now treats controller setup as three separate user scenarios:

- `首次连接`: clears any stale target, finds a new Pro2, and saves the successful address.
- `重连已配对`: prioritizes the saved address without changing pairing state.
- `更换手柄`: confirms the replacement, forgets the old target, and stores the new controller.

Automatic reconnect remains enabled for normal sleep, wake, and transient disconnect recovery.

The Manager accepts both historical Pro2 `live_*` status fields and PS5
`input_*` fields. Reading Pro2 rumble status no longer reports a false BLE
input error when the controller is already working.

## Stick Range

All three modes now share tested Pro2 stick normalization. A physical throw of
`1600` counts from the learned center reaches the full internal `0..4095`
range on all four axes.

- 新和联胜 / PS5 emits exact `0..255` stick endpoints.
- Pro2 / Nintendo preserves exact `0..4095` 12-bit endpoints.
- Xbox / XInput emits exact `-32768..32767` endpoints.

Native regression tests cover center deadzone, shifted centers, saturation,
axis inversion, and endpoint packing. Controller-specific mechanical range
should still be checked with a full-circle hardware test after flashing.

## Flash Reliability And Demo Reset

- Every flash and erase now records the selected COM port's active driver
  provider, version, INF, and Windows build before esptool starts.
- On Windows build 26300 or newer, WCH CH343 driver `2.1.2025.7` is blocked
  before opening the port. This exact combination was reproduced leaving both
  esptool and espflash stuck in a non-terminating kernel call. The Manager
  directs the user to the Microsoft `USB Serial Device` / `usbser` driver.
- Firmware switching closes the Manager serial handle, cleans known project
  consumers, then gives the CH343 driver a short settle period before starting
  esptool.
- Firmware switching detects and stops this project's `DualSenseHostTrace`,
  serial monitor, command helper, and stale esptool processes when they target
  the selected COM port. The reported COM4 failure was traced to a two-hour
  `DualSenseHostTrace --com COM4` session, not to the firmware image.
- Removed all Manager-side `SerialPort.Open()` preflight probes. A blocked
  CH343 open cannot be cancelled safely and could leave the Manager itself
  holding an orphaned COM task. The isolated esptool process is now the only
  authority that opens the port during flashing.
- `chip_id` now uses `115200 + --no-stub`, finite connection attempts, and a
  20-second watchdog. Erase and write commands have separate bounded
  watchdogs.
- A stale esptool that cannot be terminated now aborts the operation before a
  new process is started. The Manager no longer stacks flashing attempts on a
  blocked COM port.
- Only one Manager instance can run at a time.
- Added `清理固件（整片擦除）`. After a warning confirmation it erases the
  complete Flash, including firmware, NVS, BLE pairing, and USB identity, so
  the board can be demonstrated as a fresh ESP32-S3.
- ESP32-S3 whole-chip erase now uploads the esptool RAM stub. ROM-only
  `erase_flash` is unsupported on ESP32-S3 and was the cause of the first
  erase-button failure.
- `DualSenseHostTrace` now samples serial status using short periodic sessions
  instead of holding the CH343 port open for the full trace duration.

## Mode Switching Reliability

- Pro2 and Xbox release profiles now lock their compiled USB mode.
- A previously saved NVS mode can no longer make a successfully flashed Pro2
  profile enumerate as Xbox, or vice versa.
- BLE target, BLE autoconnect, and report-rate settings remain persistent
  across normal mode flashes.

## Hardware Validation

The final flow was exercised on the ESP32-S3 N16R8 board through the same
`FirmwareFlasher` used by the Manager:

- Pro2 profile flashed and enumerated as `057E:2069`, including HID and Switch
  2 bulk interfaces.
- Xbox profile flashed and enumerated as `045E:028E`, including XInput and HID
  interfaces.
- 新和联胜 profile flashed and enumerated as `054C:0CE6`, including composite,
  DualSense media/audio, and HID interfaces.
- Whole-chip erase completed successfully and removed all three release USB
  identities.
- 新和联胜 was restored after erase and all `054C:0CE6` interfaces returned.

## Release

```text
release/v5.9/新和联胜版本-aio-v5.9.2.exe
sha256 5ca65c85970795fa66fdc88a9fbdac7de0af4daf5407eb2e625a8e710eef806d

release/v5.9/DualSenseHostTrace-v5.9.2.zip
sha256 07fd5e4e9b22a5ef6f653f68d1017f624cda4c02e5e8e6eedf8bfd77e7e2d2dc
```
