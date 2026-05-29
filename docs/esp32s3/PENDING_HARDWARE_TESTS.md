# ESP32-S3 Hardware Test Status

First board bring-up started on 2026-05-27.

Observed with real hardware:

1. CH343P serial port detected by Windows as `USB\VID_1A86&PID_55D3`, `COM12` on the first test machine.
2. `idf.py build` completed with ESP-IDF v5.3.3.
3. `idf.py -p COM12 flash` completed and verified written hashes.
4. Serial boot logs were observed by direct serial capture with `DTR=True`.
5. Serial `status` command returned a JSON line:
   `{"ok":true,"cmd":"status","mode":"generic","usb":"not_mounted","ble":"idle","hid":"running","version":"0.1.0"}`
6. Native ESP32-S3 USB & OTG Type-C enumerated on Windows as:
   - `USB\VID_CAFE&PID_4037\ESP32S3-GENERIC`
   - `HID\VID_CAFE&PID_4037\...`
   - Friendly name: `HID-compliant game controller`
7. After native USB connection, serial `status` returned:
   `{"ok":true,"cmd":"status","mode":"generic","usb":"mounted","ble":"idle","hid":"running","version":"0.1.0"}`
8. Firmware log showed periodic generic HID test reports:
   `report sent test_a=pressed` / `report sent test_a=released`
9. `tools/esp32s3/send_command.ps1 -Port COM12 -Command status -ResetBeforeCommand` returned serial logs and JSON through the .NET serial stack using `DTR=False, RTS=False`.
10. Windows joystick API observed button state toggling between `0x00000000` and `0x00000001`.
11. `joy.cpl` showed `ESP32-S3 Generic HID Gamepad`, and the device properties page showed Button 1 lighting every 2 seconds.
12. A later flash attempt at the default high baud encountered `Invalid head of packet`; retrying with `idf.py -p COM12 -b 115200 flash` succeeded.
13. Serial command `hid neutral` changed the Windows joystick button state to held-neutral `0x00000000`.
14. Serial command `hid test_a` changed the Windows joystick button state to held-A `0x00000001`.
15. Serial command `hid auto_a` restored the default 2-second A-button toggle test mode.
16. Serial command `stop` changed HID status to stopped and repeatedly sent neutral reports; Windows joystick state stayed `0x00000000`.
17. Serial command `start` restored HID output.
18. Direct no-stub flashing with ESP-IDF Python/esptool succeeded after stub-based flash showed serial noise.
19. On 2026-05-28, `tools/esp32s3/build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf` completed after adding Nintendo-default mode, report-rate control, BLE connect/GATT/notify code, and the live HID report loop.
20. On 2026-05-28, the Windows manager built and self-contained `Y700Switch2Manager.exe` was published.

Still PENDING_HARDWARE_TEST:

1. `idf.py monitor` interactive workflow should be rechecked without leaving a monitor process holding `COM12`.
2. Nintendo experimental mode Windows identity behavior is recorded.
3. Steam behavior is recorded: Nintendo / Switch / If_Hid / Generic.
4. Published Windows Manager connects to the CH343P COM port on the real board.
5. Windows Manager sends `start`, `stop`, `mode`, `rate`, `ble scan`, and `ble connect` commands from the UI.
6. Windows Manager saves and filters logs from the real board session.
7. Firmware `ble scan` logs advertisements while the real Switch 2 Pro Controller is in pairing/connect mode.
8. BLE scan finds the real Switch 2 Pro Controller with recorded MAC/type, RSSI, name, UUIDs, and manufacturer data.
9. BLE connect succeeds.
10. BLE notify raw data is received.
11. Parsed BLE notify state drives USB HID reports in Nintendo mode.
12. Rumble reverse path feasibility is tested.

Do not mark any item complete without the real board and recorded evidence.
