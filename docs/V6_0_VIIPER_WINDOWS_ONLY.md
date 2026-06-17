# V6.0 Windows-Only VIIPER Route

V6.0 is a new Windows-only route. It does not replace the V5.9 ESP32-S3
firmware line.

The goal is:

```text
Real Pro2 over BLE/GATT, captured directly by the V6 EXE
    -> Y700 V6 feeder and Pro2 BLE rumble writeback
    -> VIIPER virtual USB device
    -> Windows / Steam / games
```

## Current Finding

VIIPER is a virtual USB input framework built on USBIP. It can emulate the
three host-side device identities we need:

- `dualsensehaptic` for 新和联胜 / PS5, including DualSense HID and UAC1 audio
- `ns2pro` for Pro2 / Nintendo
- `xbox360` for Xbox / XInput

VIIPER does not connect to the real Pro2 controller over Bluetooth. The V6.0
Manager therefore owns the real-controller side itself: it scans BLE
advertisements, opens the Pro2 GATT device, sends the same initialization
sequence used by the ESP32 route, subscribes to the FD2 input characteristic,
and writes rumble packets to the cc48 rumble characteristic.

Starting the local VIIPER server does not connect the real Pro2 controller.
VIIPER only creates virtual USB devices. The real controller should be awake and
not already connected to ESP32, Switch, a phone, or an older Manager process
before `连接 Pro2 BLE` can succeed. Do not manually pair the Pro2 as a Windows
Bluetooth HID controller for this route.

## Dependency Boundary

VIIPER itself is GPL-3.0, while its client libraries are documented as MIT.
V6 communicates with the server over localhost TCP. Preview.19 embeds the
haptic-capable fork binary and license; its corresponding source is retained
under `tools/viiper/haptic-src`.

The Windows requirement is `usbip-win2`, because Windows needs a signed USBIP
kernel driver before VIIPER-created USB devices can attach locally. The V6
release package carries the official `USBip-0.9.7.7-x64.exe` installer under
`usbip-win2\v0.9.7.7`, and the EXE also embeds the installer and license in
the single-file EXE. The Manager can launch it through
`安装/修复 usbip-win2`, but it still requires UAC because a kernel driver is
installed and USB devices may restart.

At runtime the Manager searches for `usbip.exe` in PATH, local `usbip-win2`
folders, and common `Program Files\USBip` locations. When it finds one, it
prepends that directory to the local VIIPER server process PATH. If it does not
find one, the local VIIPER server is not started because it can still answer
`ping` while every virtual device attach fails later.

## V6.0 Modes

| V6.0 mode | VIIPER device type | Feedback from host |
| --- | --- | --- |
| 新和联胜 / PS5 | `dualsensehaptic` | DualSense HID output plus UAC1 4-channel 48 kHz HD audio |
| Pro2 / Nintendo | `ns2pro` | 34-byte HD rumble + player LED |
| Xbox / XInput | `xbox360` | 2-byte ordinary rumble |

## Implementation Plan

1. Start and manage a VIIPER server connection on `localhost:3242`.
2. Create or reuse a virtual bus.
3. Add the selected device type and immediately open its device stream.
4. Feed input reports at the correct cadence.
5. Receive feedback reports and route them back to the real controller when the
   Pro2 BLE rumble characteristic is available.
6. Scan and connect the real Pro2 directly over BLE/GATT.
7. Send the ESP32-proven Pro2 initialization command sequence and require a
   live FD2 input notification before marking the controller connected.
8. Apply the V5.9 rumble scheduling model:
   - Pro2 / Nintendo mode: preserve raw `0x02` / HD rumble packets.
   - Xbox mode: map ordinary left/right motors to Pro2 rumble.
   - 新和联胜 mode: follow DualSense compatibility flags, using ordinary motors
     in compatibility mode and rear-channel audio analysis in HD mode.

## Current V6.0 Preview

The initial V6.0 app lives in:

```text
windows/v60_viiper_app/
```

It can:

- present the three modes as an animated character-select arena with embedded
  Kratos, Mario, and Master Chief artwork;
- deploy or directly switch virtual identities by clicking a character card;
- automatically start or reuse local VIIPER on first character deployment;
- expose BLE connection as the central `进入游戏` action while keeping advanced
  server, driver, scan, and log controls in a collapsible system console;
- show first-run dependency readiness on the main screen, automatically verify
  the USBIP kernel driver with `usbip port`, launch the embedded repair
  installer when needed, and continue mode deployment after a successful
  install;
- ping a VIIPER server;
- scan BLE advertisements for Nintendo / Pro2 candidates without requiring
  Windows Bluetooth HID pairing;
- connect the real Pro2 over GATT, subscribe to the FD2 input stream, and reject
  candidates until a live parsed input report arrives;
- keep an automatic Pro2 connection guard active after one `进入游戏` click:
  retry empty scans and transient GATT errors indefinitely, monitor live input,
  close a session after two seconds without input, and resume scanning until
  the user explicitly stops automatic reconnect;
- fall back to the legacy C0F8 notify stream when FD2 subscribes but no live
  packet arrives, while logging raw notify counts and rejected headers for
  protocol work;
- create a virtual `dualsensehaptic`, `ns2pro`, or `xbox360` device;
- send live Pro2 input packets when BLE notifications are fresh; hold the last
  valid state through interruptions up to 750 ms, safely clear buttons and
  triggers after that while keeping the last stable stick axes, then let the
  two-second reconnect guard recycle the stale BLE session;
- feed all three VIIPER virtual device types at a 4 ms / 250 Hz target cadence,
  matching the V5.9 ESP32 USB report cadence instead of throttling XInput to
  62.5 Hz;
- use an absolute-deadline Windows high-resolution waitable timer while the
  VIIPER input loop is active. This avoids the ~64-66 Hz background throttling
  observed with a 4 ms `.NET PeriodicTimer` when the window is covered or
  minimized;
- log the measured VIIPER feed rate separately from the real Pro2 BLE raw and
  parsed notification rates, including latest and maximum parsed packet gaps,
  gap severity counters, and rumble queue/write/coalescing/failure counters;
- filter single-frame large stick-axis spikes until a second similar frame
  confirms the movement, and expose the count as `axis_spike`;
- preserve a native Pro2/Windows 7.5 ms connection when available, allowing
  the 133 Hz BLE class instead of immediately capping the link at 15 ms;
- target at least the 66.7 Hz class after measuring live notifications and
  apply Windows 11's 15 ms `ThroughputOptimized` request as fallback. A
  continuous, fully parsed stream below that target remains usable and is
  reported as degraded instead of being disconnected and replaced by neutral
  input;
- log the final interval negotiated by Windows, theoretical connection-event
  rate, actual notification rate, parsed input rate, and 133/66.7 Hz class;
- retain Windows 10 compatibility with driver-controlled BLE parameters when
  the Windows 11 preference API is unavailable;
- emit the 24-byte `ns2pro` input stream consumed by the tagged VIIPER v0.7.0
  runtime. The v0.7.0 prose documentation says 27 bytes, but the server source
  declares `InputWireSize = 24` and reads exactly 24 bytes; battery and power
  are device metadata rather than per-frame stream fields;
- start virtual modes immediately with neutral input, then switch dynamically
  to live Pro2 input and rumble writeback when BLE becomes available;
- serialize long-running UI actions and cancel them during shutdown, preventing
  BLE/server/start operations from racing each other;
- detect VIIPER input or feedback stream failure, clean up the stale virtual
  session, and return the UI to a restartable state;
- honor custom loopback API ports and allow slower usbip-win2 attachment with a
  15-second VIIPER handler window;
- wait for virtual-device, BLE, and locally launched VIIPER cleanup before the
  WPF window exits, preventing an orphaned `viiper.exe` after normal close;
- persist complete Manager diagnostics to
  `%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\v6_logs\manager_*.log`, so
  UI text trimming cannot erase the beginning of a BLE connection attempt;
- write host feedback back to the real Pro2 through the BLE cc48 rumble
  characteristic:
  - Pro2 / Nintendo mode preserves VIIPER's 16+16-byte `0x02` HD rumble blocks.
  - Xbox / XInput maps left/right motors into the same raw02-compatible HID
    frame shape used by the V5.9 haptic path.
  - 新和联胜 / PS5 exposes a strict `054C:0CE6` composite HID/UAC1 identity,
    receives 4-channel 48 kHz audio haptics, analyzes the rear channels, and
    arbitrates them with DualSense ordinary compatibility motors.
- convert raw02 rumble into the same Pro2 BLE vibration packet shape used by
  the ESP32 cc48 path instead of copying HID bytes directly.
- pace USB/IP isochronous audio at the endpoint's 1 ms service interval so HD
  effects retain their intended duration instead of arriving as a burst.
- expose and persist a unified 0.0x-3.0x vibration multiplier. Final BLE
  amplitudes saturate at the Pro2 hardware maximum.
- queue rumble output on a separate asynchronous worker, keep only the newest
  pending command, and rate-limit physical writes to the negotiated BLE
  connection interval so host feedback cannot block the 250 Hz input feeder or
  create unnecessary BLE write bursts.

It does not yet:

- install the `usbip-win2` kernel driver silently;
- guarantee every Bluetooth adapter/driver can open unpaired GATT access to the
  Pro2;
- guarantee zero BLE notification pauses. Windows, the Bluetooth adapter,
  radio interference, and controller firmware can still delay notifications;
  Preview.14 masks short interruptions and records them instead of presenting
  them as a false 250 Hz real-controller rate.

## Automated No-Controller Validation

`tools/tests/v60_ui_smoke.ps1` launches the real WPF EXE and verifies:

- bundled local VIIPER startup and reuse;
- `054C:0CE6`, `057E:2069`, and `045E:028E` virtual USB identities;
- a measured 250 Hz-class neutral feed for all three modes;
- a measured 250 Hz feed while the application window is minimized;
- device and bus cleanup after each stop;
- invalid-port handling and a completed scan when no Pro2 is available;
- recovery after the VIIPER child process is forcibly terminated;
- complete persistent Manager session logging;
- no remaining Manager or VIIPER process after normal window close.
- a second automatic BLE scan after no controller is found, plus explicit
  cancellation through `停止自动重连并断开`.
- the 0-3x vibration multiplier binding.

`tools/tests/v60_haptic_end_to_end_smoke.ps1` additionally creates the PS5
composite device, plays a two-second four-channel waveform through the real
Windows DualSense render endpoint, and requires both USB/IP `kind=2` feedback
and Manager `dualsense-hd-audio` scheduling.

Real Pro2 input, negotiated BLE rate, and physical rumble still require a
controller-in-hand test and are intentionally not claimed by this validation.

The repository carries the haptic-capable runtime under
`tools/viiper/haptic-v0.8.0`, and the V6.0 preview EXE embeds that runtime plus
license text. The `启动本地 VIIPER` button first looks for the repo copy and
otherwise extracts the embedded runtime to:

```text
%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\embedded\v6.0.0-preview\viiper\haptic-v0.8.0
```

You can still run `tools/viiper/haptic-v0.8.0/viiper-haptic.exe server` manually during
development.

## References

- VIIPER repository: <https://github.com/Alia5/VIIPER>
- VIIPER API: <https://github.com/Alia5/VIIPER/blob/main/docs/api/overview.md>
- VIIPER DualSense device: <https://github.com/Alia5/VIIPER/blob/main/docs/devices/dualsense.md>
- VIIPER Switch 2 Pro device: <https://github.com/Alia5/VIIPER/blob/main/docs/devices/ns2pro.md>
- VIIPER Xbox 360 device: <https://github.com/Alia5/VIIPER/blob/main/docs/devices/xbox360.md>
- usbip-win2: <https://github.com/vadimgrn/usbip-win2>
