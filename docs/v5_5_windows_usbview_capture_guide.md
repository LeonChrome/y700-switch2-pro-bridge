# V5.5 Windows USBView Capture Guide

Date: 2026-06-06

## Purpose

Use Microsoft USBView or USB Device Tree Viewer to determine the exact point
where Windows stops accepting the V5.5 composite descriptor. A Device Manager
Code 10 alone does not show whether the failure is in the device descriptor,
configuration layout, interface grouping, or an audio alternate setting.

## Tools

- Microsoft USBView from the Windows SDK.
- USB Device Tree Viewer as a convenient third-party alternative.
- `tools/check_v5_5_usb_composite.ps1` for a compact PnP status summary.

Run the repository checker from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
```

## Locate The Device

1. Connect the ESP32-S3 native USB port.
2. Refresh the USB tree.
3. Search for `VID_054C&PID_0CE6`.
4. Confirm the serial matches the flashed profile:

| Profile | Serial |
| --- | --- |
| `hid_only` | `V55HIDONLY` |
| `hid_composite_dummy_interface_class_00` | `V55DUMMY00` |
| `hid_composite_dummy_interface_class_ef` | `V55DUMMYEF` |
| `hid_audio_control_only` | `V55ACONLY` |
| `hid_audio_streaming_alt0_only` | `V55ASALT0` |
| `hid_audio_uac1_2ch` | `V55UAC1_2CH` |

Do not use a hidden or disconnected entry with an older serial as proof of the
currently flashed profile.

## Capture

For each profile, save or copy the complete node report. Include:

- Device Descriptor.
- Configuration Descriptor.
- Every Interface Descriptor and alternate setting.
- Every Endpoint Descriptor.
- Current Configuration Value.
- Device Bus Speed.
- Connection status and Problem Code.
- Open Pipes.
- Child HID, AudioEndpoint, and Media nodes when present.

Use profile-based names such as:

```text
work/usbview/v55_hid_only.txt
work/usbview/v55_dummy00.txt
work/usbview/v55_dummyef.txt
work/usbview/v55_audio_control_only.txt
work/usbview/v55_streaming_alt0_only.txt
work/usbview/v55_uac1_2ch.txt
```

`work/` is local output and must not be committed.

## Required Comparison

Capture the known-good `hid_only` profile first. Compare its HID interface,
HID descriptor, report length, and interrupt endpoints against the first
failing composite profile. Then compare:

1. `hid_only` against `V55DUMMY00`.
2. `V55DUMMY00` against `V55DUMMYEF`.
3. The working dummy profile against `V55ACONLY`.
4. `V55ACONLY` against `V55ASALT0`.
5. `V55ASALT0` against `V55UAC1_2CH`.

The compiled reference bytes are in `docs/generated/v5_5_usb_descriptor_dump_*.md`.

## Interpretation

| Observation | Interpretation |
| --- | --- |
| USBView cannot parse the configuration descriptor | Device/configuration descriptor basics are wrong, including `wTotalLength`, `bNumInterfaces`, ordering, or transfer truncation. |
| Configuration parses but no child appears | Interface ownership, class code, IAD/function grouping, or class-driver claim failed. |
| HID child appears but audio does not | Basic composite is working; continue at the Audio Control descriptor stage. |
| Audio Control works but streaming alt 0 fails | Audio Streaming interface declaration or interface association is wrong. |
| Streaming alt 0 works but full UAC1 fails | Alternate setting 1, isochronous endpoint, packet size, or class-specific streaming descriptors are wrong. |
| Parent and both children are healthy | Descriptor enumeration has passed; only then resume endpoint behavior testing. |

## Flash Order

Do not flash all profiles automatically. Flash and capture one at a time:

```text
hid_only
-> hid_composite_dummy_interface_class_00
-> hid_composite_dummy_interface_class_ef
-> hid_audio_control_only
-> hid_audio_streaming_alt0_only
-> hid_audio_uac1_2ch
```

After each flash, unplug and reconnect the ESP32-S3 native USB port, run the
checker, and capture the corresponding USBView report before proceeding.
