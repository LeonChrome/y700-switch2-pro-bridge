# V5.2 Real Pro2 raw02 Test Checklist

Date: 2026-06-06

## 1. Test Goal

This test is not another VIIPER callback validation. The VIIPER
`LeftRumble[16] / RightRumble[16]` HD rumble callback is already proven.

The goal is to verify this real hardware chain:

```text
VIIPER / preset raw02 payload
-> ESP32 firmware raw02 command
-> BLE HID OUT
-> real Switch 2 Pro Controller
-> physical vibration
```

## 2. Prerequisites

- ESP32-S3 is connected to the PC.
- The CH343P control/flashing COM port is known.
- Firmware containing `rumble raw02 <hex>` has been flashed.
- The Switch 2 Pro Controller is connected to the ESP32 bridge over BLE.
- PowerShell is running from the repository root.
- Do not test with old firmware.
- Do not treat dry-run success as real Pro2 success.

## 3. Confirm The COM Port

Use either command:

```powershell
Get-PnpDevice -Class Ports
[System.IO.Ports.SerialPort]::GetPortNames()
```

Replace `COM12` in the examples below with the actual CH343P port.

## 4. Build Firmware

Verified project script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

If you are already inside an ESP-IDF PowerShell, the equivalent direct command
is:

```powershell
cd .\firmware\esp32s3_switch2_bridge
idf.py build
```

If this machine uses a different ESP-IDF install, replace the `-IdfPath`
argument. The script still accepts any valid ESP-IDF root and does not hardcode
the local path internally.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build.ps1 -IdfPath <path-to-esp-idf>
```

## 5. Flash Firmware

Verified project script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

Direct ESP-IDF equivalent:

```powershell
cd .\firmware\esp32s3_switch2_bridge
idf.py -p COM12 flash monitor
```

If the build already exists and you only need to flash the release artifacts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

Open monitor:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\monitor.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf
```

## 6. Confirm raw02 Exists

Dry-run first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset low -DryRun
```

Then send a real low pulse:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset low -Send -Port COM12
```

If firmware logs contain `[RUMBLE_RAW02]`, the command reached the firmware. If
the serial output says `unknown command`, the board is still running old
firmware or the wrong firmware was flashed.

Stop command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rumble stop"
```

## 7. Three-Step Test Order

Run these in order. Do not start with the captured payload.

### Test A: low preset

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset low -Send -Port COM12
```

Expected:

- PowerShell shows `sent=true`.
- ESP32 logs show `[RUMBLE_RAW02] sent=true`.
- Pro2 produces a light vibration.

### Test B: medium preset

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\send_pro2_raw02.ps1 -Preset medium -Send -Port COM12
```

Expected:

- Vibration is clearer than `low`.
- No abnormal long vibration.
- No controller disconnect.

### Test C: VIIPER captured payload

Command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -SendToRealPro2 -Port COM12 -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

Expected:

- The probe captures non-zero VIIPER `LeftRumble[16] / RightRumble[16]`.
- The probe assembles a raw02 payload.
- The payload is sent to ESP32.
- The Pro2 vibrates.

## 8. Success And Failure Diagnosis

### Success

If either `low` or `medium` makes the Pro2 vibrate, the firmware raw02 command,
host helper, and ESP32-to-Pro2 rumble path are working.

If the captured VIIPER payload also vibrates, the full
`VIIPER ns2pro HD rumble -> real Pro2 raw02 forwarding` chain is working.

### Failure A: unknown command

The board is not running the new firmware, or the wrong firmware was flashed.

### Failure B: COM port failed

The COM port is wrong or another monitor/process is holding it.

### Failure C: sent=true but no physical vibration

Record this as the key failure. Next checks:

- whether the real Pro2 BLE rumble characteristic accepts this translated raw02
  path,
- whether the payload needs a packet counter,
- whether rumble enable initialization is missing,
- whether CRC, subcommand, or ACK handling is required,
- whether VIIPER ns2pro USB output 16+16 cannot be directly reused for real
  Pro2 BLE,
- whether firmware is writing the correct BLE characteristic.

### Failure D: controller disconnects after send

Stop testing immediately, save the logs, and do not continue to `medium` or
captured payload.

## 9. Safety Requirements

- Default mode is dry-run.
- Real send requires explicit `-Send` or `-SendToRealPro2`.
- Do not loop sends.
- Do not run long high-intensity vibration.
- Captured test must use `-MaxPackets 1`.
- If vibration feels abnormal or the controller disconnects, stop immediately.
