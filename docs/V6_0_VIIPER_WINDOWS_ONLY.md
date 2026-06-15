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

- `dualsense` for 新和联胜 / PS5
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
For V6.0 we should keep VIIPER as a separately distributed / user-installable
server process and communicate with it over localhost TCP. This keeps our
Manager and feeder code cleanly separated from the GPL server implementation.

The Windows requirement is `usbip-win2`, because Windows needs a signed USBIP
kernel driver before VIIPER-created USB devices can attach locally. The V6
release package carries the official `USBip-0.9.7.7-x64.exe` installer under
`usbip-win2\v0.9.7.7`. The Manager can launch it through
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
| 新和联胜 / PS5 | `dualsense` | 6-byte ordinary rumble + LED |
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
8. Port the V5.9 rumble scheduling ideas:
   - Pro2 / Nintendo mode: preserve raw `0x02` / HD rumble packets.
   - Xbox mode: map ordinary left/right motors to Pro2 rumble.
   - 新和联胜 mode: treat VIIPER's DualSense output as ordinary rumble first;
     later extend if VIIPER exposes audio/HD haptic channels.

## Current V6.0 Preview

The initial V6.0 app lives in:

```text
windows/v60_viiper_app/
```

It can:

- ping a VIIPER server;
- scan BLE advertisements for Nintendo / Pro2 candidates without requiring
  Windows Bluetooth HID pairing;
- connect the real Pro2 over GATT, subscribe to the FD2 input stream, and reject
  candidates until a live parsed input report arrives;
- fall back to the legacy C0F8 notify stream when FD2 subscribes but no live
  packet arrives, while logging raw notify counts and rejected headers for
  protocol work;
- create a virtual `dualsense`, `ns2pro`, or `xbox360` device;
- send live Pro2 input packets when the BLE source is fresh, and fall back to
  neutral input if the source is missing or stale;
- feed all three VIIPER virtual device types at a 4 ms / 250 Hz target cadence,
  matching the V5.9 ESP32 USB report cadence instead of throttling XInput to
  62.5 Hz;
- request Windows' 1 ms multimedia timer resolution while the VIIPER input
  loop is active; without it, a 4 ms .NET periodic timer can be rounded to the
  default ~15.6 ms scheduler tick and appear as only ~64-66 Hz;
- log the measured VIIPER feed rate separately from the real Pro2 BLE raw and
  parsed notification rates, including latest and maximum parsed packet gaps;
- preserve a native Pro2/Windows 7.5 ms connection when available, allowing
  the 133 Hz BLE class instead of immediately capping the link at 15 ms;
- reject links below the 66.7 Hz class after measuring live notifications, and
  only then apply Windows 11's 15 ms `ThroughputOptimized` request as fallback;
- log the final interval negotiated by Windows, theoretical connection-event
  rate, actual notification rate, parsed input rate, and 133/66.7 Hz class;
- retain Windows 10 compatibility with driver-controlled BLE parameters when
  the Windows 11 preference API is unavailable;
- emit the complete VIIPER v0.7.0 27-byte `ns2pro` input report, including
  battery, charging, and powered fields;
- wait for virtual-device, BLE, and locally launched VIIPER cleanup before the
  WPF window exits, preventing an orphaned `viiper.exe` after normal close;
- write host feedback back to the real Pro2 through the BLE cc48 rumble
  characteristic:
  - Pro2 / Nintendo mode preserves VIIPER's 16+16-byte `0x02` HD rumble blocks.
  - Xbox / XInput maps left/right motors into the same raw02-compatible HID
    frame shape used by the V5.9 haptic path.
  - 新和联胜 / PS5 maps DualSense ordinary motors through that compatible rumble
    shape; VIIPER does not expose DualSense audio haptics.
- convert raw02 rumble into the same Pro2 BLE vibration packet shape used by
  the ESP32 cc48 path instead of copying HID bytes directly.

It does not yet:

- install the `usbip-win2` kernel driver silently;
- guarantee every Bluetooth adapter/driver can open unpaired GATT access to the
  Pro2;
- expose the V5.9 haptic tuning and arbitration controls in the no-ESP32 route.

The repository carries a VIIPER v0.7.0 Windows runtime under
`tools/viiper/v0.7.0`, and the V6.0 preview EXE also embeds that runtime plus
license text. The `启动本地 VIIPER` button first looks for the repo copy and
otherwise extracts the embedded runtime to:

```text
%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\embedded\v6.0.0-preview\viiper\v0.7.0
```

You can still run `tools/viiper/v0.7.0/viiper.exe server` manually during
development.

## References

- VIIPER repository: <https://github.com/Alia5/VIIPER>
- VIIPER API: <https://github.com/Alia5/VIIPER/blob/main/docs/api/overview.md>
- VIIPER DualSense device: <https://github.com/Alia5/VIIPER/blob/main/docs/devices/dualsense.md>
- VIIPER Switch 2 Pro device: <https://github.com/Alia5/VIIPER/blob/main/docs/devices/ns2pro.md>
- VIIPER Xbox 360 device: <https://github.com/Alia5/VIIPER/blob/main/docs/devices/xbox360.md>
- usbip-win2: <https://github.com/vadimgrn/usbip-win2>
