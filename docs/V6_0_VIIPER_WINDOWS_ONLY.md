# V6.0 Windows-Only VIIPER Route

V6.0 is a new Windows-only route. It does not replace the V5.9 ESP32-S3
firmware line.

The goal is:

```text
Real Pro2 over Windows Bluetooth
    -> Y700 V6 feeder and Pro2 output writeback
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
Manager therefore treats Windows as the Bluetooth pairing layer: first pair the
real controller in Windows, then the Manager reads the resulting HID input
device through HidSharp, feeds VIIPER, and uses the same HID handle for
best-effort host rumble writeback.

## Dependency Boundary

VIIPER itself is GPL-3.0, while its client libraries are documented as MIT.
For V6.0 we should keep VIIPER as a separately distributed / user-installable
server process and communicate with it over localhost TCP. This keeps our
Manager and feeder code cleanly separated from the GPL server implementation.

The Windows requirement is `usbip-win2`, because Windows needs a signed USBIP
kernel driver before VIIPER-created USB devices can attach locally.

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
   Windows HID stack allows output writes.
6. Read an already-paired Windows HID Pro2/Switch Pro input source.
7. Port the V5.9 rumble scheduling ideas:
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
- scan and open Windows HID Pro2/Switch Pro input devices (`057E:2009` and
  `057E:2069`);
- create a virtual `dualsense`, `ns2pro`, or `xbox360` device;
- send live Pro2 input packets when the HID source is fresh, and fall back to
  neutral input if the source is missing or stale;
- write host feedback back to the real Pro2 where Windows exposes a writable
  HID output report:
  - Pro2 / Nintendo mode preserves VIIPER's 16+16-byte `0x02` HD rumble blocks.
  - Xbox / XInput maps left/right motors into the same raw02-compatible HID
    frame shape used by the V5.9 haptic path.
  - 新和联胜 / PS5 maps DualSense ordinary motors through that compatible rumble
    shape; VIIPER does not expose DualSense audio haptics.

It does not yet:

- install `usbip-win2`;
- perform Windows Bluetooth pairing itself;
- guarantee rumble on Windows HID stacks that refuse output writes to the real
  controller;
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
