# Release Notes V5.9.0

V5.9.0 is the final all-in-one release for the ESP32-S3 Switch 2 / Pro2 bridge.

## Highlights

- Final three-mode Manager: Pro2 / Nintendo, Xbox / XInput, and DualSense-like.
- Pro2 / Nintendo mode preserves raw HID report `0x02` rumble as the authoritative path.
- Xbox / XInput and DualSense-like ordinary motor paths keep left/right strength and apply dynamic Pro2 frequency shaping.
- Xbox left/right stick Y axes and generic HID Y/Rz axes use expected host polarity.
- N16R8 PSRAM defaults are safe for boot: memmap enabled, malloc heap disabled, PSRAM memtest disabled.
- Nintendo mode exposes BOS / Microsoft OS 2.0 descriptors for MI_01 WinUSB binding.
- BLE auto reconnect continues in the background after controller sleep or BLE disconnect.
- Manager flashing flow now guards against duplicate clicks, busy COM ports, stale esptool processes, and CH343 driver stalls.
- Repository has been cleaned into a V5.9 final archive.

## Artifact

```text
release/v5.9/PRO2手柄无线接收器控制板-aio-v5.9.0.exe
sha256 4dcbf9c19ba9f493b316bb35aba3b994ff555a876f7246ff42ba43090cd84137
```

## Verification

- V5.9 package verification: passed
- Embedded profiles: `hid_audio_uac1_4ch_ds5like`, `hid_only`, `pro2_bridge_v5_5`, `xinput_bridge_v5_8`
- Embedded asset count: 12
- Manager build: .NET 8 win-x64 single-file
- Firmware target: ESP32-S3

## Notes

- Profile IDs keep historical names for Manager settings compatibility.
- This project is now archived as the final V5.9 release.
