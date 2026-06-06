# VIIPER ns2pro to Real Pro2 Rumble Probe

V5.2 Phase 3 dry-run framework.

This probe takes VIIPER `ns2pro` output feedback:

```text
LeftRumble[16] + RightRumble[16] + Flags + PlayerLedMask
```

and reconstructs the likely Switch 2 Pro HID OUT report shape:

```text
0x02 + LeftRumble[16] + RightRumble[16] + zero padding
```

It defaults to dry-run and does not send anything to the ESP32-S3 or Pro2.

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -LeftRumbleHex 00112233445566778899AABBCCDDEEFF -RightRumbleHex 102132435465768798A9BACBDCEDFE0F
```

Default dry-run with the currently validated sample rumble:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1
```

Safe dry-run profiles:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -SafeProfile low
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -SafeProfile medium
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -SafeProfile short
```

To parse a full 34-byte VIIPER feedback packet:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -Ns2ProOutputHex <68 hex chars>
```

## VIIPER Capture

Capture the first non-zero VIIPER output packet and dry-run the raw02 command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -DryRun -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

Real send after flashing the raw02 firmware and connecting the real Pro2:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -CaptureViiper -SendToRealPro2 -Port COM12 -MaxPackets 1 -MinIntervalMs 100 -TimeoutSeconds 35
```

## raw02 Command

Firmware/control command:

```text
rumble raw02 <hex>
```

`<hex>` can be either 64 hex chars (`LeftRumble[16] + RightRumble[16]`) or 128
hex chars (full 64-byte payload starting with `0x02`). Default script mode is
dry-run; real send requires `-SendToRealPro2`.
