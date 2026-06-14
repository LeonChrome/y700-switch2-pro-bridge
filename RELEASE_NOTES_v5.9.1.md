# Release Notes V5.9.1

V5.9.1 is the final diagnostic release for the ESP32-S3 Switch 2 / Pro2 bridge.

## Highlights

- Final four-mode Manager: Pro2 / Nintendo, Xbox / XInput, Xbox Elite 2 GIP bring-up, and DualSense-like.
- Pro2 / Nintendo mode preserves raw HID report `0x02` rumble as the authoritative path.
- Xbox / XInput and DualSense-like ordinary motor paths keep left/right strength and apply dynamic Pro2 frequency shaping.
- Xbox left/right stick Y axes and generic HID Y/Rz axes use expected host polarity.
- N16R8 PSRAM defaults are safe for boot: memmap enabled, malloc heap disabled, PSRAM memtest disabled.
- Nintendo mode exposes BOS / Microsoft OS 2.0 descriptors for MI_01 WinUSB binding.
- BLE auto reconnect continues in the background after controller sleep or BLE disconnect.
- BLE wake reconnect now scans for the saved Pro2 address, so pressing any controller button after sleep can trigger a faster reconnect.
- Manager flashing flow now guards against duplicate clicks, busy COM ports, stale esptool processes, and CH343 driver stalls.
- Elite 2 bring-up exposes one `FF/47/D0` GIP interface with interrupt `OUT 0x02` and `IN 0x82`.
- Elite 2 Microsoft OS 1.0 descriptors expose string `0xEE`, vendor request `0x90`, and compatible ID `XGIP10`.
- Elite 2 uses `bcdDevice=0x0512` and serial `ELITE2-GIP-0512` to bypass stale Windows USB descriptor cache.
- Elite 2 starts with a minimal Arrival / Metadata / Idle / Active state machine. Paddle, rumble, guide, unmapped state, and 46-byte Elite input packets remain disabled until enumeration and Active are verified.
- Repository has been cleaned into a V5.9 final archive.
- DualSense host tracing confirmed that the reported disconnect starts inside ESP32 HID IN: BLE remains live and Windows PnP keeps the device, but HID completions stop after a USB bus reset/reconfiguration.
- The firmware now closes its HID submission gate directly from TinyUSB's DCD bus-reset event and reopens it only after `tud_mount_cb()` confirms configuration completion.
- HID submit failures and transfer-completion failures have separate counters. A persistent submit failure receives one cooldown-protected USB re-enumeration fallback.
- Rumble was independently verified end to end: Windows HID OUT, firmware conversion, BLE writes, and three physical Pro2 pulses all succeeded.

## Artifact

```text
release/v5.9/PRO2手柄无线接收器控制板-aio-v5.9.1.exe
sha256 04513580f0fce83ccde22fad7b2aed8731eaf37146b310f2ae13f88747de8edd
```

## Verification

- V5.9 package verification: passed
- Embedded profiles: `hid_audio_uac1_4ch_ds5like`, `hid_only`, `pro2_bridge_v5_5`, `xinput_bridge_v5_8`, `xinput_elite_bridge_v5_9`
- Embedded asset count: 15
- Manager build: .NET 8 win-x64 single-file
- Firmware target: ESP32-S3
- DualSense-like and HID-only firmware builds: passed
- DualSense-like app image SHA256: `dbe3915c20d7114b84b5e05e0aaeff7533b3a1ba80cf93ff2a2809b47b246647`
- Elite firmware build: passed, 36% of the smallest app partition remains free

## DualSense Validation

The root cause and code path are supported by host and firmware telemetry. This machine did not have the ESP32 control port connected during packaging, so long-running hardware confirmation remains required:

1. Flash DualSense-like mode from the V5.9.1 EXE and replug native USB.
2. Start diagnostic monitoring before launching the game.
3. Confirm `usb_configuration_ready=true`.
4. A normal host reset may increase `usb_bus_reset_count` and `usb_configuration_reset_count`; `hid_report_completed` must continue increasing afterward.
5. `hid_report_submit_failure_streak` should remain `0`. If it rises, the log must show whether `usb_recovery_count` restored the stream.
6. Test Steam vibration and game haptics separately. The host trace already proved ordinary rumble works; absence of game vibration can still mean the game sent no nonzero motor update or no haptic-audio stream.

## Elite 2 Windows Verification

1. Flash `xinput_elite_bridge_v5_9`, then replug the native USB / OTG port.
2. In UsbTreeView, verify `VID_045E`, `PID_0B00`, `bcdDevice 0x0512`, serial `ELITE2-GIP-0512`, one `FF/47/D0` interface, and endpoints `0x02` / `0x82`.
3. Request string descriptor `0xEE` and verify `MSFT100`, vendor code `0x90`.
4. Verify vendor request `C0 90 0000 0004` returns the 40-byte Extended Compatible ID descriptor containing `XGIP10`.
5. In Device Manager, verify there is no Code 28. Current Windows 11 can show `dc1-controller.inf` / service `dc1-controller` with `xboxgip` as an upper filter instead of showing `xboxgip.sys` as the primary service.
6. Search `C:\Windows\INF\setupapi.dev.log` for `VID_045E&PID_0B00`, `MS_COMP_XGIP10`, and a successful install exit status.
7. Watch serial logs for `Arrival`, metadata request `0x04`, device-state start `0x05`, then `Active`.
8. Only after Active is confirmed, test the standard `0x20` input packet in Steam. Do not use this build to judge paddle or rumble support.

If Windows reuses a failed descriptor query, remove the device in Device Manager and replug it. The new `bcdDevice` and serial normally avoid the old `UsbFlags` cache; manual registry deletion should be the last resort.

## Notes

- Profile IDs keep historical names for Manager settings compatibility.
- This project is now archived as the final V5.9 release.
