# Pending Hardware Tests

All items are PENDING_HARDWARE_TEST until the ESP32-S3 board arrives.

1. CH343P serial port is detected by Windows.
2. `idf.py` can flash the ESP32-S3.
3. `idf.py monitor` shows boot logs.
4. Native ESP32-S3 USB & OTG Type-C enumerates as HID.
5. Generic HID mode appears in `joy.cpl`.
6. Periodic A button test works.
7. Nintendo experimental mode Windows identity behavior is recorded.
8. Steam behavior is recorded: Nintendo / Switch / If_Hid / Generic.
9. Serial `status` command returns a JSON line.
10. Windows Manager connects to the COM port.
11. Windows Manager sends `start`, `stop`, and `mode` commands.
12. Windows Manager saves logs.
13. BLE scan finds the real Switch 2 Pro Controller.
14. BLE connect succeeds.
15. BLE notify raw data is received.
16. Rumble reverse path feasibility is tested.

Do not mark any item complete without the real board and recorded evidence.
