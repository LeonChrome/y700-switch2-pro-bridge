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

To parse a full 34-byte VIIPER feedback packet:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -Ns2ProOutputHex <68 hex chars>
```

## Current Limitation

V5.1 firmware exposes `rumble hdtest`, `rumble hold`, `rumble tune`, and
`rumble stop`, but it does not expose a control command for arbitrary raw
`0x02 + 16 + 16` payload injection. Therefore `-SendToRealPro2` is intentionally
blocked until a V5.2 integration step adds a verified raw forwarding command.
