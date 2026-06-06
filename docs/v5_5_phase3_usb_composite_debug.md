# V5.5 Phase 3 USB Composite Debug

Date: 2026-06-06

## Current Hardware Result

Both tested audio profiles fail before Windows creates any child:

```text
hid_audio_uac1_2ch:
  serial=V55UAC1_2CH
  parent=USB Composite Device
  status=Error
  problem=Code 10 / CM_PROB_FAILED_START
  hid_child=false
  audio_child=false

hid_audio_uac2_4ch:
  parent=USB Composite Device
  status=Error
  problem=Code 10 / CM_PROB_FAILED_START
  hid_child=false
  audio_child=false
```

This is a descriptor-level composite enumeration failure, not evidence of a
UAC1/UAC2 channel algorithm problem. Phase 4 haptics, raw02 live forwarding,
audio parsing, Pro2 BLE changes, V5.2, and VIIPER are out of scope until a
composite profile enumerates successfully.

## DS5Dongle Reference

The upstream DS5Dongle default `ENABLE_SERIAL=OFF` descriptor uses:

```text
device_class=00/00/00
iad_present=false
audio=UAC1
interfaces=4
interface_order=AudioControl, AudioStreamingOUT, AudioStreamingIN, HID
wTotalLength=227
```

Its `EF/02/01` device class and Audio IAD are enabled only when the optional
CDC serial function is also enabled. This is the reason the revised V5.5 UAC1
path now tests `00/00/00` without an IAD.

See `docs/generated/v5_5_ds5dongle_usb_descriptor_reference.md`.

## Isolation Profiles

| Profile | Serial | Device class | IAD | Interfaces | Purpose |
| --- | --- | --- | --- | ---: | --- |
| `hid_only` | `V55HIDONLY` | `00/00/00` | no | 1 | Known-good Phase 2.1 HID baseline |
| `hid_composite_dummy_interface_class_00` | `V55DUMMY00` | `00/00/00` | no | Vendor interface + unchanged HID |
| `hid_composite_dummy_interface_class_ef` | `V55DUMMYEF` | `EF/02/01` | no, intentionally | Isolate device class from all audio/IAD variables |
| `hid_audio_control_only` | `V55ACONLY` | `00/00/00` | no | UAC1 Audio Control + HID |
| `hid_audio_streaming_alt0_only` | `V55ASALT0` | `00/00/00` | no | Audio Control + AS alt 0 + HID |
| `hid_audio_uac1_2ch` | `V55UAC1_2CH` | `00/00/00` | no | Full UAC1 render alt 1 and isoch OUT |
| `hid_audio_uac2_2ch` | `V55UAC2_2CH` | `EF/02/01` | yes | Later UAC2 isolation |
| `hid_audio_uac2_4ch` | `V55UAC2_4CH` | `EF/02/01` | yes | Later four-channel isolation |

The dummy interface is class `0xFF`, has no endpoint, and is claimed by a
minimal TinyUSB application driver. The HID report descriptor and interrupt
endpoints remain the verified Phase 2.1 shape.

## Decision Matrix

| Result | Conclusion | Next action |
| --- | --- | --- |
| `hid_only` OK; both dummy profiles fail | Basic multi-interface configuration, interface claim, ordering, or transfer is wrong | Compare USBView output and compiled dump; do not test audio |
| dummy `class_00` works; dummy `class_ef` fails | `EF/02/01` without an associated IAD/function is rejected or mishandled | Keep staged UAC1 on `00/00/00`; inspect IAD policy |
| dummy `class_ef` works; dummy `class_00` fails | Windows/stack behavior depends on composite device class | Capture both device descriptors and child binding |
| both dummy profiles work; `audio_control_only` fails | UAC1 Audio Control header, collection, or class-driver claim is wrong | Compare AC bytes with DS5Dongle |
| Audio Control works; `streaming_alt0_only` fails | Audio Streaming interface declaration or AC collection reference is wrong | Fix AS alt 0 before adding any endpoint |
| Streaming alt 0 works; `uac1_2ch` fails | Alt 1, class-specific AS descriptors, isoch endpoint, or packet size is wrong | Compare endpoint and AS descriptor sequence |
| UAC1 2ch works; UAC2 2ch fails | UAC2-specific descriptor or TinyUSB audio path is wrong | Keep UAC1 as baseline |
| UAC2 2ch works; UAC2 4ch fails | Four-channel layout, bandwidth, or channel controls are wrong | Inspect channel descriptors and max packet size |

## Checker

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
```

Important fields:

```text
current_serial
current_profile
current_status
current_problem_code
device_class_hint
iad_expected
current_hid_child_found
current_audio_child_found
phase_guess
suggested_next_action
```

`phase_guess` is one of:

```text
no_usb_device
usb_device_only
composite_parent_code10
hid_child_ok_audio_missing
hid_audio_ok
```

Use `-IncludeStale` only to inspect old Windows PnP cache entries.

## Build And Flash Order

Builds can be prepared without changing the attached board:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_only -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_composite_dummy_interface_class_00 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_composite_dummy_interface_class_ef -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_control_only -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_streaming_alt0_only -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac1_2ch -IdfPath C:\Espressif\v5.3.3\esp-idf
```

Flash only one profile at a time, in this order:

```text
hid_only
-> hid_composite_dummy_interface_class_00
-> hid_composite_dummy_interface_class_ef
-> hid_audio_control_only
-> hid_audio_streaming_alt0_only
-> hid_audio_uac1_2ch
```

After each flash, unplug and reconnect native USB, run the checker, and save a
USBView capture before continuing. Do not proceed to the next profile when the
current stage fails.

## Descriptor Dumps

Regenerate exact raw bytes from compiled ELF symbols:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate_v5_5_usb_descriptor_dumps.ps1
```

The generated files validate `wTotalLength`, actual byte count,
`bNumInterfaces`, interface continuity, endpoint counts/conflicts, IAD
coverage, HID report length, and string indices.
