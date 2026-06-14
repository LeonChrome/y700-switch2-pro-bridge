# Technical Notes

## Firmware Profiles

The V5.9.3 Manager embeds four profiles, including one hidden recovery profile:

| Profile ID | Purpose |
| --- | --- |
| `pro2_bridge_v5_5` | Pro2 / Nintendo USB HID bridge |
| `xinput_bridge_v5_8` | Xbox / XInput bridge |
| `hid_audio_uac1_4ch_ds5like` | 新和联胜 / PS5-compatible HID + UAC1 4ch HD haptics |
| `hid_only` | Recovery HID-only profile |

Some profile IDs retain historical names for settings compatibility. Xbox Elite 2 / GIP is no longer included in the release package.

## Pro2 / Nintendo USB Identity

- VID/PID: `057E:2069`
- Manufacturer string: `Nintendo Co., Ltd.`
- Product string: `Nintendo Switch Pro Controller`
- HID input report ID: `0x05`
- Main report size: 64 bytes
- Vendor interface: MI_01
- BOS / Microsoft OS 2.0 descriptor exposed for WinUSB binding

## Rumble Paths

Pro2 / Nintendo mode keeps raw HID report `0x02` as the authoritative rumble input. Preset-style bulk fallback is ignored and counted as `rumble_preset_ignored`.

Xbox / XInput and DualSense ordinary motor paths preserve left/right strength. Because those host APIs do not carry native Pro2 frequency fields, firmware generates dynamic frequency shaping before sending Pro2 BLE rumble.

The 新和联胜 profile uses one BLE writer with two independent source states
and a host-intent mode:

- `valid_flag0` bit 0 or `valid_flag2` bit 2 validates compatibility motors.
- `valid_flag0` bit 1 selects compatibility haptics even with zero motor values.
- Compatibility mode routes only ordinary motors and blocks haptic-audio
  candidates.
- Without compatibility selection, haptic audio routes through raw `0x02` and
  stale ordinary motor bytes are ignored.
- A game may switch modes repeatedly during one session. The firmware does not
  classify games or permanently select HD output.
- LED, player-state, and trigger-only fields do not independently select a
  vibration source.
- Bounded stop packets are emitted when the currently selected source ends.

This is source arbitration, not byte-level mixing. Alternating ordinary and HD
packets at USB report cadence would add BLE load and produce discontinuities.

## DualSense USB Recovery

Host bus reset is allowed to complete before firmware considers a forced USB
disconnect/connect. The audio profile suppresses forced recovery while UAC
streaming is active and applies a 15-second post-transition grace window.
`status diag` exposes the inhibit reason, UAC transfer failures, rearm failures,
microphone alternate-setting attempts, and the selected rumble source.

## BLE Reconnect

BLE auto reconnect is state-machine guarded:

- one reconnect task at a time
- delayed retry after disconnect or failed connect
- no retry when already connected
- manual disconnect suppresses the next automatic reconnect
- normal control-port commands remain available
- `ble forget` disconnects the current controller and clears the saved target
- first-pair and replacement flows re-enable automatic reconnect before scanning

## PSRAM

The final ESP32-S3 N16R8 safe defaults avoid boot loops on AP 8 MB Octal PSRAM boards:

```text
CONFIG_SPIRAM_USE_MEMMAP=y
# CONFIG_SPIRAM_USE_MALLOC is not set
# CONFIG_SPIRAM_MEMTEST is not set
```

## Windows Manager

The final Manager is a .NET 8 WPF single-file app. It extracts embedded firmware and tools into:

```text
%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\embedded\v5.9.3-aio
```

The flasher closes the Manager serial session, cleans known project serial
consumers and stale matching esptool processes, then launches esptool without
a Manager-side `SerialPort.Open()` preflight. It uses finite esptool connection
attempts and per-command watchdogs. A stale process that cannot be terminated
blocks the next flash instead of allowing another esptool instance to stack on
the same COM port.
