# V5.5 ESP32-S3 DualSense Identity Feasibility

Date: 2026-06-06

## Decision

The route is technically plausible and worth an incremental ESP32-S3
prototype. It is not yet proven. The biggest risk is not raw02 or Pro2 BLE;
those are already verified. The biggest risk is reproducing enough of the
wired DualSense composite USB, feature-report, and audio timing contract for
Windows and native games while BLE runs concurrently.

## 1. Current ESP32-S3 Capability

The current project already provides:

- ESP32-S3-N16R8 firmware on ESP-IDF 5.3.3,
- `espressif/esp_tinyusb` `1.7.6~2` in the current dependency lock,
- native USB TinyUSB HID device,
- custom device/configuration/report descriptors,
- HID IN and OUT callbacks,
- BLE central connection and reconnect to Pro2,
- FD2 buttons, sticks, triggers, gyro, and accelerometer parsing,
- Pro2 raw02 assembly and BLE write,
- physical Pro2 vibration validation,
- CH343P serial control and diagnostics.

Therefore the controller backend and physical output half already exist.

## 2. New Capability Required

Wired DualSense identity needs:

- DualSense device/configuration/string descriptors,
- HID report descriptor,
- input report `0x01`,
- output report `0x02` receiver,
- feature report responses and calibration data,
- USB Audio Control interface,
- four-channel, 48 kHz, 16-bit Audio Streaming OUT,
- likely Audio Streaming IN shape for compatibility,
- audio endpoint callbacks and fixed buffering,
- haptic feature extraction,
- raw02 translation queue,
- identity selection before USB startup.

Speaker playback is not required for MVP. The audio interface shape may still
need to expose channels 0/1 even if the firmware discards them.

## 3. Risk Assessment

| Risk | Level | Mitigation |
| --- | --- | --- |
| Composite descriptor/host acceptance | High | Start HID-only, compare enumeration and feature requests |
| Feature/calibration reports | High | Build a report matrix from DS5Dongle, Linux, SDL, and real-device captures |
| TinyUSB audio on ESP-IDF 5.3.3 | High | Isolated audio enumeration spike before translator work |
| Endpoint allocation | Medium-high | Keep control on CH343P; avoid native USB CDC/vendor in V5.5 profile |
| USB audio plus BLE scheduling | High | Fixed queues, no BLE work in callbacks, measure drops and latency |
| RAM/buffering | Medium | Small bounded internal buffers; PSRAM only for noncritical diagnostics |
| 48 kHz processing cost | Medium | Feature extraction or early decimation; no Opus for MVP |
| Pro2 raw02 rate limit | Medium | Coalesce windows, minimum interval, stale-frame drop |
| Game-specific DualSense checks | High | Test multiple native games with Steam Input on/off |
| Identity/legal distribution | High | Experimental compatibility research; no affiliation claim |

ESP32-S3 supports composite USB and six endpoints according to Espressif. The
reference base layout needs four non-control endpoints, which is encouraging,
but exact TinyUSB Audio allocation and callback behavior must be measured on
the project component version.

## 4. Minimum Viable Product

Phase 1, HID identity only:

```text
Windows recognizes Sony-style wired controller
Steam recognizes DualSense
neutral input report is stable
no audio
```

Phase 2, Pro2 input:

```text
buttons/sticks/triggers/gyro/accel
-> DualSense input report 0x01
```

Phase 3, HID output:

```text
capture report 0x02
log ordinary rumble, adaptive triggers, LEDs
no forwarding
```

Phase 4, audio enumeration:

```text
Audio Control + four-channel Audio OUT
Windows endpoint visible
discard samples safely
```

Phase 5, translator preview:

```text
haptic channels 2/3
-> RMS/peak/transient/balance
-> raw02 dry-run
```

Phase 6, live forwarding:

```text
bounded raw02
-> real Pro2 BLE
with stop, rate limit, stale timeout, counters
```

## 5. Go/No-Go Gates

Go to audio only if:

- HID-only enumeration is stable across reconnects,
- Steam accepts input,
- feature requests do not stall the device,
- Pro2 BLE remains stable.

Go to live raw02 only if:

- audio callback has no sustained overrun,
- end-to-end latency is measured,
- dry-run output rate is bounded,
- silence produces stop/zero output,
- disconnect and USB suspend produce immediate stop.

Use dual-board fallback only if measured single-board scheduling or endpoint
constraints cannot be fixed without destabilizing input.

## Sources

- [ESP32-S3 USB Device Stack](https://docs.espressif.com/projects/esp-idf/en/v5.3.3/esp32s3/api-reference/peripherals/usb_device.html)
- [Espressif esp_tinyusb](https://components.espressif.com/components/espressif/esp_tinyusb)
- [DS5Dongle](https://github.com/awalol/DS5Dongle)
