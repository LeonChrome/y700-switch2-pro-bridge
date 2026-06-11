# Technical Notes

## Firmware Profiles

The final Manager embeds four profiles:

| Profile ID | Purpose |
| --- | --- |
| `pro2_bridge_v5_5` | Pro2 / Nintendo USB HID bridge |
| `xinput_bridge_v5_8` | Xbox / XInput bridge |
| `hid_audio_uac1_4ch_ds5like` | DualSense-like HID + UAC1 4ch experiment |
| `hid_only` | Recovery HID-only profile |

Some profile IDs retain historical names for settings compatibility. The package and Manager version are V5.9.

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

## BLE Reconnect

BLE auto reconnect is state-machine guarded:

- one reconnect task at a time
- delayed retry after disconnect or failed connect
- no retry when already connected
- manual disconnect suppresses the next automatic reconnect
- normal control-port commands remain available

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
%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\embedded\v5.9.0-aio
```

The flasher performs a COM preflight before launching esptool, cleans stale matching esptool processes, stops retrying on port-busy failures, and runs flashing work off the UI thread.
