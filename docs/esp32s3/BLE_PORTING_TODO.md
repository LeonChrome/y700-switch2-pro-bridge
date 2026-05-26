# BLE Porting TODO

Status: PENDING_HARDWARE_TEST.

## Migration Steps

1. Bring up NimBLE Central on ESP32-S3.
2. Scan and log device name, MAC, RSSI, service UUIDs.
3. Filter likely Switch 2 Pro Controller candidates.
4. Connect by MAC or name.
5. Discover services and characteristics.
6. Subscribe to notify characteristics.
7. Log raw notify hex.
8. Compare with Y700 logs.
9. Enable `ab7...fd2` parser.
10. Enable `749...cc0f8` legacy parser.
11. Feed parsed state into report mapper.
12. Plan rumble reverse path.

## Known Notify Inputs

```text
ab7de9be-89fe-49ad-828f-118f09df7fd2
7492866c-ec3e-4619-8258-32755ffcc0f8
```

`ab7...fd2` is parsed as a 32-bit button field at `data[4:8]`.

`749...cc0f8` is parsed as legacy `byte2/byte3/byte4` input stream.

## Rumble Reverse Path

Known Y700 v3 physical-feedback route references:

```text
cc483f51-9258-427d-a939-630c31f72b05
```

ESP32-S3 should first log HID OUT reports. Only after USB and BLE notify are stable should rumble writes be attempted.

## Risks

- ESP32-S3 BLE stack timing may differ from Android.
- Switch 2 Pro Controller pairing/security behavior may require more work.
- Notify UUID availability may differ by connection state.
- Rumble write timing may be stricter than input notify.
- BLE and USB HID timing on one MCU may need task tuning.

## Hardware Tests Needed

- BLE scan sees controller.
- BLE connect succeeds.
- Service discovery finds target characteristics.
- Notify subscription works.
- Raw notify hex matches or can be aligned with Y700 logs.
- Parsed buttons match physical buttons.
- Rumble write path is safe and reversible.
