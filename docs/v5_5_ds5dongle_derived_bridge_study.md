# V5.5 DS5Dongle-Derived Bridge Study

Date: 2026-06-06

Reference: [awalol/DS5Dongle](https://github.com/awalol/DS5Dongle) at commit
`8760ee3f4fa9335e3c5e1a0d0aead92b55f23abb`, branch `master`, MIT license.
The checkout under `research/upstream/DS5Dongle` is ignored and is not
submitted to this repository.

This study extracts the PC-facing DualSense contract for an ESP32-S3 port. It
does not propose copying the Pico2W-to-real-DualSense Bluetooth backend into
the final product.

## 1. USB Identity

Key implementation:

- `src/usb_descriptors.cpp`: device, configuration, HID report, and string
  descriptors.
- `src/main.cpp`: HID GET/SET callbacks and input report transmission.
- `src/usb.cpp`: USB Audio Class control requests.
- `src/tusb_config.h`: TinyUSB HID/audio buffers and sample format.

Observed identity:

| Item | DualSense mode |
| --- | --- |
| VID | `0x054C` |
| PID | `0x0CE6` |
| Edge PID | `0x0DF2` |
| Manufacturer | Sony Interactive Entertainment |
| Product | DualSense Wireless Controller |
| USB speed | Full speed |
| HID input | report `0x01`, 63 data bytes |
| HID output | report `0x02`, 47 data bytes in DS descriptor |
| DS HID descriptor | 321 bytes |
| Edge HID descriptor | 437 bytes |

It is a composite-style device with:

1. Audio Control interface 0.
2. Audio Streaming OUT interface 1.
3. Audio Streaming IN interface 2.
4. HID interface 3.
5. Optional CDC interfaces when `ENABLE_SERIAL` is enabled.

Endpoints:

| Endpoint | Direction/type | Role |
| --- | --- | --- |
| `0x01` | OUT isochronous adaptive | 4-channel PC audio/haptics |
| `0x82` | IN isochronous asynchronous | 2-channel microphone/audio return |
| `0x84` | IN interrupt, 64 bytes, 1 ms | DualSense input |
| `0x03` | OUT interrupt, 64 bytes, 1 ms | DualSense output |

The descriptor includes many feature report IDs. DS5Dongle proxies important
feature GET/SET operations to the real DualSense, so its feature behavior is
not self-contained. V5.5 must emulate enough calibration, pairing, firmware,
and capability feature reports for Windows, Steam, and games instead of
assuming a real DualSense is present.

Using Sony VID/PID is suitable only for controlled compatibility research. It
does not imply Sony affiliation and requires legal/product review before any
distribution claim.

## 2. Audio And Haptic Path

`src/audio.cpp` receives PC data through `tud_audio_available()` and
`tud_audio_read()`.

Format:

```text
sample_rate=48000
sample_format=signed PCM 16-bit
usb_out_channels=4
usb_in_channels=2
```

Channel meaning in the implementation:

```text
channel 0/1 = speaker/headset left/right
channel 2/3 = haptic left/right
```

The haptic path:

```text
USB 4-channel PCM
-> select channels 2/3
-> gain and clamp
-> resample 48 kHz to 3 kHz
-> signed int8 stereo
-> 64-byte haptic block
-> Bluetooth report 0x36
-> bt_write()
-> real DualSense
```

Speaker channels 0/1 are separately buffered, resampled, and Opus encoded on
the Pico2W second core. `DISABLE_SPEAKER_PROC` already proves that speaker
processing is separable.

For our V5.5 MVP:

- ignore speaker playback,
- accept the four-channel endpoint so games see the expected device shape,
- extract only haptic channels 2/3,
- calculate RMS/peak/transient/stereo features,
- bypass DualSense Bluetooth report `0x36`,
- translate directly to bounded Pro2 raw02 frames.

This avoids Opus, the real DualSense packet encoder, and a real DualSense
backend.

## 3. HID Output Path

`tud_hid_set_report_cb` in `src/main.cpp` receives interrupt OUT data. Report
`0x02` is decoded by `state_update()` in `src/state_mgr.cpp`.

Handled state includes:

- ordinary rumble emulation flags and left/right intensities,
- adaptive trigger arrays,
- mute light,
- light fade and brightness,
- player indicators,
- RGB LED.

Value to Pro2:

| DualSense output | V5.5 action |
| --- | --- |
| Ordinary rumble | Map to low/medium bounded raw02 |
| Haptic audio | Primary high-detail translation source |
| Adaptive trigger events | Optional event cue, never presented as trigger reproduction |
| Lightbar/player/mute | Log or ignore |
| Speaker volume/audio | Ignore for MVP |

Adaptive trigger resistance cannot be reproduced by Pro2 hardware. It may be
used only as an event classifier input, for example a short weapon cue.

## 4. Input Report Path

DS5Dongle copies a real DualSense Bluetooth report into the PC-facing USB
report `0x01`. V5.5 instead builds report `0x01` from Pro2 BLE data.

Mapping:

| Pro2 source | DualSense field |
| --- | --- |
| Face buttons/D-pad/shoulders | Matching gamepad buttons |
| Left/right sticks | 8-bit DualSense axes with calibrated center/range |
| Analog L/R trigger | L2/R2 bytes |
| Gyro/accelerometer | DualSense signed motion fields with axis/sign conversion |
| Home | PS button |
| Capture or configurable chord | Touchpad/mic fallback command if needed |
| Battery | Quantized DualSense battery/status field |

Missing hardware:

- touch contacts remain neutral/not-touching,
- touch timestamps advance only if required,
- mic button defaults released,
- headset state defaults disconnected,
- connection status reports USB/wired,
- fixed synthetic serial and neutral capability state are used only where the
  host contract requires them.

Calibration feature reports are likely mandatory for correct gyro and host
acceptance. They must be generated from measured Pro2-to-DualSense scaling or
safe fixed calibration data. Returning zeros blindly is not acceptable.

## 5. Backend Abstraction

```text
PcFacingDualSenseDevice
  input_report_builder
  output_report_receiver
  audio_haptic_receiver

ControllerBackend
  Ds5BtBackend
  Pro2BleInputBackend
  Pro2Raw02Backend
```

`Ds5BtBackend` models upstream behavior and supports reference validation. The
target product uses both Pro2 backends:

```text
Pro2BleInputBackend -> PcFacingDualSenseDevice.input_report_builder
PcFacingDualSenseDevice.output/audio -> translator -> Pro2Raw02Backend
```

## 6. What To Port

Port or reconstruct:

- descriptor topology and report descriptor behavior,
- HID input/output callbacks,
- Audio Class interface and control requests,
- haptic channel extraction,
- timing and buffering observations,
- host-visible feature report contract.

Reference only:

- Pico SDK setup,
- CYW43 Classic Bluetooth/L2CAP transport,
- real DualSense feature proxy,
- Bluetooth checksum/sequence framing,
- Opus speaker forwarding,
- Pico2W multicore implementation.

Automated symbol scan:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\fetch_v5_5_ds5dongle.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\analyze_v5_5_ds5dongle.ps1
```

Generated result: `docs/generated/v5_5_ds5dongle_symbol_scan.md`.

## Sources

- [DS5Dongle](https://github.com/awalol/DS5Dongle)
- [ESP32-S3 USB Device Stack](https://docs.espressif.com/projects/esp-idf/en/v5.3.3/esp32s3/api-reference/peripherals/usb_device.html)
- [Espressif esp_tinyusb](https://components.espressif.com/components/espressif/esp_tinyusb)
- [TinyUSB device audio examples](https://github.com/hathach/tinyusb/tree/master/examples/device)
