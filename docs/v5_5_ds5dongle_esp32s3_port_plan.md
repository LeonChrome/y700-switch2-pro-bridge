# V5.5 DS5Dongle ESP32-S3 Port Plan

Date: 2026-06-06

## Goal

```text
PC sees ESP32-S3 as wired DualSense
-> game emits DualSense HID/audio/haptics
-> ESP32-S3 translates useful output
-> real Switch 2 Pro receives raw02 over BLE
```

The existing V5.2 `pro2_ns2_viiper` route remains frozen and available.

## 1. Port Versus Reference

Worth porting or reconstructing:

- wired DualSense USB descriptor topology,
- HID report `0x01` builder,
- HID output report `0x02` receiver,
- feature report compatibility table,
- USB Audio Class 4-channel OUT shape,
- haptic channels 2/3 extraction,
- bounded buffering and timing.

Reference only:

- Pico2W build and clock setup,
- CYW43 Classic Bluetooth,
- L2CAP HID control/interrupt channels,
- real DualSense feature-report proxy,
- report `0x36` Bluetooth packet,
- Opus speaker forwarding.

## 2. ESP32-S3 Feasibility

ESP32-S3 supports Full-Speed USB Device mode, custom descriptors, HID, vendor
classes, and composite devices through TinyUSB. Espressif documents a maximum
of six endpoints. The DS5Dongle base shape uses four non-control endpoint
addresses, so the endpoint count appears feasible before adding native USB
CDC. This must still be proven with the exact ESP-IDF 5.3.3 TinyUSB component
and allocation rules.

The current dependency lock uses `espressif/esp_tinyusb` `1.7.6~2` with the
Espressif TinyUSB component. Current firmware already has:

- TinyUSB installation and custom descriptors,
- HID IN/OUT,
- Pro2 BLE central and saved-target reconnect,
- parsed buttons/sticks/triggers/motion,
- raw02 BLE writes,
- CH343P serial control independent of the native USB identity.

The separate CH343P port is useful: V5.5 does not need to spend native USB
endpoints on CDC for normal control.

Current `tusb_config.h` enables HID and vendor only. V5.5 requires an
experimental build profile with TinyUSB Audio enabled and a new descriptor
set. This must be a separate identity selected before `tinyusb_driver_install`.

## 3. Reuse Plan

Reuse without changing V5.2 behavior:

| Existing module | V5.5 use |
| --- | --- |
| `ble/ble_central.c` | `Pro2BleInputBackend` transport |
| Pro2 FD2 parser/motion mapping | Source for DualSense input builder |
| `usb_switch2_vendor_send_raw02_payload` path | `Pro2Raw02Backend` base |
| control protocol/status counters | Diagnostics and emergency stop |
| CH343P serial port | Configuration and logs |
| saved BLE target reconnect | Real Pro2 startup behavior |

New modules should live behind the identity boundary:

```text
usb/dualsense_descriptors.*
usb/dualsense_device.*
usb/dualsense_feature_reports.*
haptics/dualsense_audio_receiver.*
haptics/dualsense_output_decoder.*
haptics/pro2_raw02_translator.*
backend/controller_backend.*
```

No V5.5 descriptor should be mixed into the active V5.2 descriptor at
runtime. The selected identity is immutable until reboot/re-enumeration.

## 4. Backend Replacement

Upstream:

```text
PcFacingDualSenseDevice
<-> Ds5BtBackend
<-> real DualSense
```

V5.5:

```text
Pro2BleInputBackend
-> DualSense input builder
-> PcFacingDualSenseDevice
-> HID/audio receivers
-> translator
-> Pro2Raw02Backend
-> real Pro2
```

A real DualSense is useful for comparison captures but is not part of the
target product.

## 5. Single-Board Plan

Preferred architecture:

```text
ESP32-S3 native USB OTG:
  DualSense HID + Audio device

ESP32-S3 BLE:
  central connection to real Pro2

ESP32-S3 tasks:
  USB event task
  audio receive/feature task
  Pro2 input task
  raw02 output task
  serial control/status task
```

Task boundaries should use fixed-size queues. USB audio callbacks must not wait
for BLE writes. The raw02 task consumes translated frames with a bounded queue
and drops stale frames.

PSRAM may store larger diagnostic buffers, but real-time audio endpoint buffers
and callback state should prefer internal DMA-capable memory where required.

## 6. Dual-Board Fallback

Keep a fallback if single-board USB audio plus BLE cannot meet timing:

```text
Board A: Pico2W or ESP32-S3 PC-facing DualSense HID/audio
Board B: ESP32-S3 Pro2 BLE/raw02 backend
Link: framed UART or USB serial with sequence, timestamp, stop, and CRC
```

The fallback is not the first implementation because it adds latency, wiring,
clock-domain handling, and recovery complexity. It becomes justified only
after measured single-board failure.

## 7. Build Profiles

```text
output_identity=pro2_ns2_viiper
  existing descriptor and behavior

output_identity=dualsense_esp32s3_experimental
  DualSense HID first
  Audio added only after HID recognition passes
```

The experimental identity should have an independent compile-time or persisted
boot setting. It must never become the default during V5.5 experiments.

## 8. Port Gates

1. Descriptor builds with `-Werror`.
2. Windows enumerates without device errors.
3. Steam recognizes a wired DualSense.
4. Synthetic report `0x01` input changes.
5. Pro2 input maps to buttons/sticks/triggers/motion.
6. HID output `0x02` capture works.
7. Four-channel audio OUT endpoint appears.
8. Haptic channels contain nonzero game data.
9. Dry-run raw02 translation is bounded.
10. Explicit live forwarding passes safety tests.

Sources:

- [ESP32-S3 USB Device Stack](https://docs.espressif.com/projects/esp-idf/en/v5.3.3/esp32s3/api-reference/peripherals/usb_device.html)
- [Espressif esp_tinyusb component](https://components.espressif.com/components/espressif/esp_tinyusb)
- [DS5Dongle](https://github.com/awalol/DS5Dongle)
