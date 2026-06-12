# Changelog

## V5.9.0 Final

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
- V5.9-only repository cleanup: old release artifacts, prototype scripts, temporary references, and outdated docs removed.

### Final Release Artifact

```text
release/v5.9/PRO2手柄无线接收器控制板-aio-v5.9.0.exe
sha256 dc9e211631786cd05bd04211130a1d0e8bbb249badfc8a277f339d909d19d405
```

No further feature releases are planned.
