# V5.5 USB Descriptor Profiles

Date: 2026-06-06

The exact raw bytes and parsed descriptor tables are generated from compiled
ELF symbols by:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate_v5_5_usb_descriptor_dumps.ps1
```

## Common Identity

```text
VID=0x054c
PID=0x0ce6
manufacturer=Sony Interactive Entertainment
product=DualSense Wireless Controller
device_bcd=0x0100
hid_input_report=0x01 + 63 data bytes
hid_output_report=0x02 + 47 data bytes
hid_in_endpoint=0x81 interrupt, 64 bytes, 1 ms
hid_out_endpoint=0x01 interrupt, 64 bytes, 1 ms
audio_sample_rate=48000 Hz
audio_sample_width=16-bit
```

## Profile Summary

| Profile | Serial | Config bytes | Interfaces | Device class | IAD | Audio |
| --- | --- | ---: | ---: | --- | --- | --- |
| `hid_only` | `V55HIDONLY` | 41 | 1 | `00/00/00` | no | none |
| `hid_composite_dummy_interface_class_00` | `V55DUMMY00` | 50 | 2 | `00/00/00` | no | none |
| `hid_composite_dummy_interface_class_ef` | `V55DUMMYEF` | 50 | 2 | `EF/02/01` | no, intentionally | none |
| `hid_audio_control_only` | `V55ACONLY` | 58 | 2 | `00/00/00` | no | UAC1 control only |
| `hid_audio_streaming_alt0_only` | `V55ASALT0` | 68 | 3 | `00/00/00` | no | UAC1 control + AS alt 0 |
| `hid_audio_uac1_2ch` | `V55UAC1_2CH` | 132 | 3 | `00/00/00` | no | UAC1 2ch OUT |
| `hid_audio_uac2_2ch` | `V55UAC2_2CH` | 177 | 3 | `EF/02/01` | yes | UAC2 2ch OUT |
| `hid_audio_uac2_4ch` | `V55UAC2_4CH` | 185 | 3 | `EF/02/01` | yes | UAC2 4ch OUT |

`hid_audio_uac2` remains a warning alias for `hid_audio_uac2_4ch`.
`hid_audio_uac1_fallback` remains a warning alias for `hid_audio_uac1_2ch`.

## Isolation Design

The dummy profiles preserve the verified Phase 2.1 HID descriptor and add one
minimal class `0xFF` interface with no endpoints. A small TinyUSB application
driver claims that interface so SET_CONFIGURATION can complete.

The UAC1 staged profiles deliberately follow the DS5Dongle default device
class strategy:

```text
device_class=00/00/00
iad=false
interface_0=Audio Control
interface_1=Audio Streaming when enabled
last_interface=HID
```

The full UAC1 profile exposes:

```text
channels=2
sample_rate=48000
bits_per_sample=16
audio_out=0x02 isochronous adaptive
max_packet=192
```

UAC2 retains `EF/02/01` plus an Audio IAD because those profiles use TinyUSB's
UAC2 composite descriptor path. They are not the next hardware test while the
basic composite stages remain unverified.

## Verification Order

```text
hid_only
-> hid_composite_dummy_interface_class_00
-> hid_composite_dummy_interface_class_ef
-> hid_audio_control_only
-> hid_audio_streaming_alt0_only
-> hid_audio_uac1_2ch
```

Do not advance after a failing stage. Capture the failing descriptor with
USBView and compare it with the corresponding generated dump.
