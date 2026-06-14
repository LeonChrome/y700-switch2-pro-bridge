# Changelog

## V6.0.0-preview VIIPER Windows-only

- Added the first V6.0 Windows-only Manager skeleton in `windows/v60_viiper_app`.
- Added a lightweight .NET 8 VIIPER TCP client instead of depending on the
  current .NET 10 generated client package.
- Added three VIIPER virtual device modes:
  - 新和联胜 / PS5 via `dualsense`
  - Pro2 / Nintendo via `ns2pro`
  - Xbox / XInput via `xbox360`
- Replaced the incorrect Windows Bluetooth HID pairing path with a direct
  Windows BLE central path: scan Pro2/Nintendo advertisements, open GATT,
  send the ESP32-proven init sequence, subscribe to FD2 input, and require live
  parsed input before a controller is marked connected.
- Added host feedback logging and writeback to the real Pro2 BLE cc48 rumble
  characteristic: Pro2 / Nintendo preserves VIIPER's HD rumble blocks, while
  新和联胜 / PS5 and Xbox / XInput map ordinary motors into the V5.9
  raw02-compatible HID frame shape before conversion.
- Added raw02-to-Pro2-BLE rumble packet encoding so V6.0 writes the same cc48
  packet shape as the ESP32 route instead of copying HID bytes into BLE.
- Added a `usbip-win2` preflight for the local VIIPER server: the Manager now
  locates `usbip.exe`, injects its directory into the VIIPER server PATH, and
  explains the dependency before all three modes fail with `usbip not found`.
- Added Pro2 BLE input fallback diagnostics: FD2 remains preferred, but V6.0 now
  tries the legacy C0F8 notify characteristic if FD2 subscribes without live
  input, and logs raw notify counts plus rejected packet headers for mapping.
- Added a bundled VIIPER v0.7.0 Windows runtime under `tools/viiper/v0.7.0`,
  embedded the runtime/license into the V6.0 preview EXE, and added a Manager
  button to start it locally.
- Documented the 6.0 architecture, VIIPER boundary, GPL/MIT dependency split,
  usbip-win2 requirement, and direct BLE Pro2 feeder behavior.

```text
release/v6.0/新和联胜VIIPER版本-aio-v6.0.0-preview.exe
sha256 1450931c5de60e76eb5d904c258a3a8ea3510cd2bc88bf40cca6be01ecc90e6f
```

## V5.9.3 新和联胜稳定版

- Promoted the latest V5.9.2 stability build to V5.9.3.
- Added the Manager-side CH343 driver repair flow: detect the selected CH343
  control port, request UAC, back up the current WCH OEM driver, and rebind to
  Microsoft `usbser.inf`.
- Added explicit ESP32-S3 download-mode failure detection for
  `Wrong boot mode detected (0x24)` so users get BOOT/RST guidance instead of
  a generic flash failure.
- Added serial command watchdogs and non-blocking serial shutdown paths so BLE
  buttons cannot leave the EXE frozen behind a stuck COM handle.
- Throttled and filtered UI firmware logs; full diagnostics still go to disk,
  but high-frequency firmware debug lines no longer force WPF TextBox layout
  on every line.
- Forced application shutdown on window close and verified that the Manager
  exits cleanly after startup and BLE first-pair actions.

```text
release/v5.9/新和联胜版本-aio-v5.9.3.exe
sha256 4df0167eaf74c33ada0e4370304e3db5ae3320a1288bd70c8d488b0934290f7a
```

## V5.9.2 新和联胜版本

- Replaced the old DualSense card and image with the `新和联胜` PS5 mode.
- Reduced the user-facing release to three modes: 新和联胜 / PS5, Pro2 / Nintendo, and Xbox / XInput.
- Removed the Xbox Elite 2 / GIP bring-up profile from the release bundle.
- Added guided controller workflows for first pairing, reconnecting the saved controller, and replacing it.
- Added the common `ble forget` firmware command to clear the saved BLE target safely.
- Preserved automatic reconnect after controller sleep or transient disconnect.
- Updated all firmware and Manager version reporting to V5.9.2.
- Fixed Pro2 status compatibility: Manager now accepts both `live_*` and PS5 `input_*` freshness fields.
- Pro2 status reads and tuning no longer require a live controller; physical rumble tests still require fresh BLE input.
- `重连已配对` now rebuilds a connected link when input notifications are stale.
- Added per-flash CH343 driver/version logging and a preflight block for the
  reproduced Windows 26300 + WCH `2.1.2025.7` kernel-hang combination.
- Locked Pro2 and Xbox release profiles to their compiled USB identities so a
  stale NVS mode cannot override a successful mode flash.
- Fixed whole-chip erase on ESP32-S3 by using the esptool RAM stub.
- Completed a real hardware matrix covering Pro2 `057E:2069`, Xbox
  `045E:028E`, 新和联胜 `054C:0CE6`, whole-chip erase, and PS5 restore.

```text
release/v5.9/新和联胜版本-aio-v5.9.2.exe
sha256 5ca65c85970795fa66fdc88a9fbdac7de0af4daf5407eb2e625a8e710eef806d
```

## V5.9.1 Final

This is the final archived release of the ESP32-S3 Switch 2 / Pro2 bridge.

### Final Features

- Three USB output modes: Pro2 / Nintendo, Xbox / XInput, and DualSense-like.
- Windows all-in-one Manager with bundled firmware, esptool, BLE controls, USB checks, and test tools.
- Pro2 / Nintendo raw HID `0x02` rumble preservation.
- Xbox / XInput and DualSense-like ordinary rumble conversion with left/right strength preservation and dynamic Pro2 frequency shaping.
- Corrected Xbox and generic HID Y-axis polarity.
- Safer ESP32-S3 N16R8 PSRAM defaults: memmap enabled, malloc heap disabled, PSRAM memtest disabled.
- Microsoft OS descriptor / BOS exposure for Nintendo mode WinUSB interface binding.
- BLE background auto reconnect after controller sleep or disconnect, with wake-scan matching for the saved Pro2 address.
- Manager-side protections against repeated flash clicks, stale esptool processes, busy COM ports, and UI stalls when both USB cables are connected.
- DualSense host-trace diagnostics identified HID IN endpoint submission failure after a USB bus reset while BLE and PnP remained healthy.
- HID submission is now gated from TinyUSB's DCD bus-reset event until configuration and endpoint reopening complete.
- Immediate HID submit failures and asynchronous transfer failures are tracked separately.
- Persistent HID IN failure triggers one cooldown-protected USB re-enumeration instead of retrying every 4 ms forever.
- V5.9-only repository cleanup: old release artifacts, prototype scripts, temporary references, and outdated docs removed.

### Final Release Artifact

```text
release/v5.9/PRO2手柄无线接收器控制板-aio-v5.9.1.exe
sha256 04513580f0fce83ccde22fad7b2aed8731eaf37146b310f2ae13f88747de8edd
```

No further feature releases are planned.
