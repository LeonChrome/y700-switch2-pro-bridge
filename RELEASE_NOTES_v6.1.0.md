# V6.1.0 新和联胜 VIIPER Stability

V6.1.0 promotes the Windows-only VIIPER route from the V6.0 previews while keeping the three-mode goal intact:

- 新和联胜 / PS5: DualSense identity with HD audio haptics and ordinary-rumble scheduling.
- Pro2 / Nintendo: Nintendo-style virtual Pro controller mode.
- Xbox / XInput: broad XInput compatibility mode.

## What Changed

- Added VIIPER API stream self-healing. A transient input/feedback stream break now reopens the API stream without immediately detaching the virtual USB device.
- Added session auto-recovery. If the local VIIPER server exits or the stream cannot be recovered in-place, the Manager restarts VIIPER and restores the previous mode automatically.
- Increased VIIPER device-handler retention to 60 seconds so short Manager/API interruptions do not immediately remove the virtual USB device.
- Added a short USBIP settle delay when switching modes to reduce remove/add races in the usbip-win2 virtual bus.
- Fixed startup preflight timeout handling. A slow or stale VIIPER port probe is treated as a probe miss instead of a user cancellation.
- Added minimize-to-tray support with quick actions for opening the UI, switching modes, entering game/auto-reconnect, stopping the virtual device, and exiting.

## Verification

- `dotnet build windows/v60_viiper_app/Y700Switch2V60Viiper.csproj -c Release`
- `dotnet run --project tools/tests/v60_packet_mapper_test/V60PacketMapperTest.csproj -c Release`
- Final single-file EXE UI smoke:
  - 新和联胜 / PS5: 249.1 Hz
  - Pro2 / Nintendo: 249.2 Hz
  - Xbox / XInput: 250.0 Hz
  - background/minimized cadence: 250.0 Hz
  - server fault auto-recovery: pass
- Final single-file EXE HD haptic smoke:
  - DualSense 4-channel 48 kHz audio endpoint: pass
  - VIIPER kind=2 HD haptic stream: pass
  - Manager HD scheduler: pass
- Tray minimize smoke: pass

## Asset

- `XinHeLianSheng-VIIPER-aio-v6.1.0.exe`
- SHA256: `0B43F38DDC82D1295C074DFA38C5CA181D3796CD1A55771E7B374ACEC7896BE1`
