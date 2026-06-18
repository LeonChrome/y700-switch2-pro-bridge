# V6.2.9 新和联胜 VIIPER 版本

Windows-only VIIPER route for direct Pro2 BLE input and three virtual modes:

- 新和联胜 / PS5
- Pro2 / Nintendo
- Xbox / XInput

## Changes

- Adds paired VIIPER port preflight before server launch.
- Logs API and USBIP port availability as `VIIPER_PREFLIGHT`.
- Automatically falls back from the preferred `3242/3241` pair to alternate
  pairs such as `33242/33241` when a port is clearly occupied or reserved.
- Captures VIIPER stdout/stderr tail and `viiper_server_*.log` tail into the EXE
  log when the server exits early or cannot answer ping.
- Adds `VIIPER_DIAG`, `VIIPER_LOG_TAIL`, and `VIIPER_PROCESS_TAIL` lines so
  `exit=1` field reports show where startup failed.

## Notes

- This release does not change BLE input, stick processing, gyro mapping, or
  rumble scheduling.
- If startup is categorized as `port_conflict`, the EXE will try the next port
  pair automatically.
- If startup is categorized as `usbip_driver_or_permission`, switching ports is
  not useful. Install/repair usbip-win2 and reboot Windows if the installer just
  ran.
