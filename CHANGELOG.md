# Changelog

## V6.2.6 新和联胜 VIIPER Latest-Held Gyro

- Promoted the V6.2 Windows-only VIIPER route to `v6.2.6`.
- Changed the default gyro timing model from `source_60hz` zeroing to
  `hold_latest`. BLE still updates at the real Pro2 sample cadence, while every
  virtual USB report carries the latest gyro/accel sample until a new source
  sample arrives.
- Migrates the old saved `source_60hz（推荐）` setting to the new default, so
  existing users do not stay on the choppy motion path after upgrading.
- Kept `source_60hz_zero` as an explicit diagnostic mode for reproducing the old
  behavior where repeated USB frames clear motion.
- Updated UI copy and telemetry labels to describe the actual latest-held IMU
  strategy.

## V6.2.5 新和联胜 VIIPER Calibrated Motion

- Promoted the V6.2 Windows-only VIIPER route to `v6.2.5`.
- Added Pro2 BLE IMU rest calibration at the HID parser boundary: stationary
  flat samples learn gyro zero bias and small accelerometer offsets without
  changing the raw stick gameplay path.
- Added explicit motion-axis mapping for the virtual DualSense and NS2Pro
  devices. DualSense now receives a flat-rest `AccelZ=-8192` vector, and both
  PS5/NS2Pro paths flip the source Y gyro axis according to the VIIPER/reference
  report-space mapping.
- Kept Xbox motion-free as before.
- Changed tray right-click actions to run fully in the background; only
  double-clicking the tray icon restores the main window.
- Added packet-mapper coverage for static gravity, gyro sign mapping, and
  parser-side motion calibration.
- Published `release/v6.2.5/新和联胜VIIPER版本-aio-v6.2.5.exe`.

## V6.2.4 新和联胜 VIIPER Raw Stick

- Promoted the V6.2 Windows-only VIIPER route to `v6.2.4`.
- Default stick processing is now `Raw Direct`: real Pro2 BLE stick axes go
  straight to the virtual USB controller without axis hold, ramp, or candidate
  confirmation on the gameplay path.
- Kept `Stability Guard` as an explicit diagnostic option for suspected raw BLE
  axis glitches instead of using it by default.
- Added `stick_mode=raw_direct/stability_guard` telemetry and a UI selector so
  field logs can separate raw input issues from virtual mapping issues.
- Startup cleanup still removes stale Manager/VIIPER processes and each mode
  creates a dedicated VIIPER bus/device.
- Latest field run showed stable virtual USB behavior: no device recreate, no
  stream reconnect, no Steam device hash change, VIIPER push near 125 Hz, and
  USB write p95 around 0.10 ms.
- Published `release/v6.2.4/新和联胜VIIPER版本-aio-v6.2.4.exe`.

## V6.1.0 新和联胜 VIIPER Stability

- Promoted the Windows-only VIIPER route to V6.1.0 with the three modes intact:
  新和联胜 / PS5, Pro2 / Nintendo, and Xbox / XInput.
- Hardened the virtual USB stability path by reopening interrupted VIIPER API
  streams without immediately detaching the USB device.
- Added automatic VIIPER server/session recovery so a server-side stream failure
  restores the previous mode instead of leaving the user to manually restart.
- Increased VIIPER device-handler retention to 60 seconds and added a short
  usbip-win2 settle delay during direct mode switches.
- Fixed startup timeout classification so a stale port probe does not look like
  a user cancellation.
- Added minimize-to-tray with right-click actions for opening the UI, selecting
  the three modes, entering game/auto-reconnect, stopping the virtual device,
  and exiting.
- Published `release/v6.1/新和联胜VIIPER版本-aio-v6.1.0.exe`.

## V6.0.0-preview VIIPER Windows-only

- Preview.20 fixes stick drift caused by a one-frame BLE axis spike being
  accepted and then held/decayed by the gap-protection policy. Large axis jumps
  now require a second similar frame before becoming live.
- Temporary BLE gaps no longer synthesize a gradual stick return to center.
  After 750 ms the app releases buttons/triggers but keeps the last stable
  stick axes until the two-second reconnect guard recycles the session.
- Added `axis_spike` diagnostics and regression coverage for discarded
  one-frame spikes, confirmed intentional full-stick moves, and the new
  safe-hold behavior.
- Preview.19 restores true DualSense audio haptics to 新和联胜 / PS5. The
  bundled `VIIPER 0.8.0-haptic.6` fork enumerates `054C:0CE6` as a composite
  HID/UAC1 device and transports 4-channel 48 kHz audio through USB/IP.
- Added ordinary/HD arbitration matching the host's DualSense compatibility
  flags. Native audio-haptics uses rear-channel spectral analysis and Pro2 HD
  frequency/amplitude packets; compatibility mode uses ordinary motors, with
  explicit transition stops between modes.
- Added real-time USB isochronous pacing from endpoint `bInterval`. The
  two-second haptic waveform now measures 2.026-2.033 seconds instead of being
  consumed as a short burst.
- Added a persisted 0.0x-3.0x vibration multiplier for all three modes. Final
  BLE amplitudes clamp at the Pro2 hardware maximum.
- Embedded runtime updates now use SHA-256 content comparison instead of file
  length, preventing stale same-sized VIIPER binaries after an EXE upgrade.
- Added end-to-end HD tests covering the Windows DualSense audio endpoint,
  USB/IP `kind=2`, `dualsense-hd-audio` scheduling, and release-package
  extraction. Full release UI smoke measured PS5 249.8 Hz, Pro2 250.0 Hz, Xbox
  250.0 Hz, and 250.0 Hz while minimized.
- Preview.15 turns the first-run and controller connection path into a guided
  workflow. The official usbip-win2 installer and license are now embedded in
  the single-file EXE, while VIIPER remains embedded and auto-extracted.
- Selecting a character now checks both `usbip.exe` and the USBIP kernel driver
  command. A missing or unhealthy driver opens the built-in repair installer;
  successful installation continues into VIIPER and mode deployment.
- `进入游戏` now enables a persistent Pro2 connection guard. It repeatedly
  scans for the highest-scoring/strongest Pro2 candidate, survives transient
  GATT failures, and retries after unsuccessful scans without another click.
- A live connection with no input for two seconds is treated as disconnected:
  the stale GATT session is closed and automatic scanning resumes. Automatic
  reconnect continues until `停止自动重连并断开` is clicked or the app exits.
- The main screen now states dependency readiness and the advanced BLE panel
  explains that normal users do not need its manual diagnostic controls.
- UI smoke coverage now requires a second no-controller reconnect attempt and
  verifies that manual stop cancels the persistent guard. Three-mode output
  measured 247.6-250.0 Hz, including 250.0 Hz while minimized.
- Preview.14 adds a BLE jitter guard based on the June 15 real-controller
  traces. Input gaps up to 750 ms now hold the last valid state instead of
  snapping every control to neutral; longer gaps clear buttons immediately
  and smoothly decay axes/triggers before reaching neutral at 1.5 seconds.
- Pro2 rumble writes now run on a separate asynchronous worker, coalesce stale
  commands, and send at most once per negotiated BLE connection interval.
  Host output can no longer block the 250 Hz virtual input feeder.
- Added `ble_gap45`, `ble_gap250`, `ble_gap750`, `rumble_q`, `rumble_w`,
  `rumble_merge`, and `rumble_fail` diagnostics so radio/driver pauses and
  output pressure can be distinguished in field logs.
- Added exact regressions for the observed 540 ms interruption, safe decay,
  and final neutral timeout. The complete three-mode UI smoke remains at
  249.7-250.1 Hz, including 250.0 Hz while minimized.
- Preview.13 fixes the real-controller failure seen in the June 15 field log:
  Windows negotiated a 15 ms / 66.7 Hz-class BLE interval and delivered 173
  fully parsed packets at 58.5 Hz, but Preview.12 treated the short-window
  measurement as a fatal failure and deliberately disconnected the usable
  controller. V6 now keeps continuous parsed input live, reports the link as
  degraded, and reserves rejection for genuinely unusable or unparsed streams.
- Replaced the 4 ms `.NET PeriodicTimer` feeder with an absolute-deadline
  Windows high-resolution waitable timer. All three identities now remain at
  a measured 250.0 Hz when the window is minimized or covered instead of
  falling to the Windows background scheduler rate of roughly 66 Hz.
- Reduced periodic UI diagnostics to a four-second cadence and added complete
  persistent `manager_*.log` session files under the V6 log directory. The UI
  can trim old text without destroying the BLE connection evidence needed for
  diagnosis.
- Added regressions for the exact 58.5 Hz real-Pro2 session, minimized-window
  250 Hz output, persistent-log completeness, all three USB identities, direct
  mode switching, stream failure recovery, and clean shutdown.
- Preview.12 replaces the engineering-dashboard front end with a dark
  character-select arena. Kratos represents 新和联胜 / PS5, Mario represents
  Pro2 / Nintendo, and Master Chief represents Xbox / XInput. The supplied
  transparent character art is embedded into the single-file EXE.
- Character cards now deploy the selected identity and can switch directly
  between live virtual modes without manually stopping first. Selection glow,
  hover lift, breathing effects, `LIVE` badges, a dark Windows title bar, and
  a dedicated BLE "进入游戏" action provide the requested game-lobby feel.
- The first character selection automatically starts or reuses local VIIPER.
  The BLE entry action deploys the selected identity when needed, then connects
  the real Pro2; if no controller is available, the selected virtual mode
  remains safely active on neutral input.
- Moved VIIPER, USBIP, BLE scan, and diagnostic controls into a collapsible
  system console while preserving full logs and recovery controls.
- Extended the real-WPF smoke test with collapsible-console discovery, direct
  PS5-to-Xbox switching, and enter-game behavior without a connected Pro2.
- Preview.11 audited the complete no-Pro2 application flow and fixed the
  `ns2pro` stream to the 24-byte packet size actually consumed by the bundled
  VIIPER v0.7.0 server. The upstream v0.7.0 prose says 27 bytes, but its tagged
  source and runtime read 24; the previous 27-byte feeder misaligned all
  packets after the first.
- Virtual modes now start immediately with neutral input and retain the live
  Pro2 source/sink, so connecting or disconnecting BLE later switches between
  live and neutral without recreating the virtual USB device.
- Serialized UI operations, added cancellation on window close, disabled
  conflicting controls while work is active, and added visible scan/connect
  progress so BLE actions no longer look like a frozen EXE.
- Fixed the asynchronous WPF close sequence so cleanup completion schedules a
  new Dispatcher close instead of re-entering `Close()` from the active
  `Closing` handler; UI smoke tests now require process exit code `0`.
- Added VIIPER stream-fault detection and automatic session cleanup. A killed
  or disconnected server now returns the UI to a restartable state instead of
  leaving a false "connected" status.
- Local VIIPER startup now honors the selected loopback API port, reuses an
  existing server, detects child-process startup failure, validates ports, and
  extends the device-handler connection window to tolerate usbip-win2 attach
  jitter.
- Added `tools/tests/v60_ui_smoke.ps1`, which drives the real WPF EXE through
  all three modes and verifies USB identities, measured feed cadence, invalid
  port handling, no-Pro2 scan completion, server-fault recovery, and process
  cleanup.
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
- Bundled the official `USBip-0.9.7.7-x64.exe` installer as a V6 release
  sidecar and added an `安装/修复 usbip-win2` button that launches it with UAC.
- Added Pro2 BLE input fallback diagnostics: FD2 remains preferred, but V6.0 now
  tries the legacy C0F8 notify characteristic if FD2 subscribes without live
  input, and logs raw notify counts plus rejected packet headers for mapping.
- Added a bundled VIIPER v0.7.0 Windows runtime under `tools/viiper/v0.7.0`,
  embedded the runtime/license into the V6.0 preview EXE, and added a Manager
  button to start it locally.
- Documented the 6.0 architecture, VIIPER boundary, GPL/MIT dependency split,
  usbip-win2 requirement, and direct BLE Pro2 feeder behavior.

```text
release/v6.0/新和联胜VIIPER版本-aio-v6.0.0-preview.14.exe
sha256 6c505bebc21d22e7d274c15060d8c67685df9d0c1592b237af59207bc598e76c

release/v6.0/新和联胜VIIPER版本-aio-v6.0.0-preview.15.exe
sha256 a0fd629a4f2ca511ce5efd194b7019b062f77b218d78b1fb276cda5cb897505f

release/v6.0/新和联胜VIIPER版本-aio-v6.0.0-preview.19.exe
sha256 f08a88fbe6e78343a1e3cac63173c194b4154ab5a7c8f1ff437389f3bc6832eb

release/v6.0/新和联胜VIIPER版本-aio-v6.0.0-preview.20.exe
sha256 dc44408d34fe655191b3af83a2f0bc8ab5e8d9452859b7f1daae9a9d6375f9b9

release/v6.0/usbip-win2/v0.9.7.7/USBip-0.9.7.7-x64.exe
sha256 51620fa5f9f8be5932bc9d786deee557ce06d5407a99cab490dcfac71f185fea
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
