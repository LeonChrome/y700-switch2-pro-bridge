# V5.5 Phase 3 USB Composite Debug

Date: 2026-06-06

## Hardware Result

The first old `V55PHASE3` hardware check narrowed the failure to USB composite
enumeration:

```text
phase3_usb_found=true
VID/PID=054C:0CE6
phase3_status=Error
phase3_problem_code=10
phase3_config_error=CM_PROB_FAILED_START
phase3_hid_child_found=false
phase3_audio_child_found=false
likely_cause=composite descriptor or TinyUSB audio configuration
```

The old `V55PHASE1`, `V55PHASE2`, and `V55PHASE3` entries may be Windows device
cache entries. They must not be treated as proof that the current flashed
profile is working. The new Phase 3 profiles use distinct serial strings so the
diagnostic scripts can identify the active profile more reliably.

## Diagnostic Tool

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
```

Important output fields:

```text
[V5_5_USB_COMPOSITE] phase3_usb_found=true/false
[V5_5_USB_COMPOSITE] phase3_status=Error/OK/Unknown
[V5_5_USB_COMPOSITE] phase3_problem_code=...
[V5_5_USB_COMPOSITE] phase3_config_error=...
[V5_5_USB_COMPOSITE] phase3_hid_child_found=true/false
[V5_5_USB_COMPOSITE] phase3_audio_child_found=true/false
[V5_5_USB_COMPOSITE] stale_scan=present_only/included
[V5_5_USB_COMPOSITE] current_serial=...
[V5_5_USB_COMPOSITE] current_profile=...
[V5_5_USB_COMPOSITE] current_hid_child_found=true/false
[V5_5_USB_COMPOSITE] current_audio_child_found=true/false
[V5_5_USB_COMPOSITE] suggested_next_action=...
```

The identity checker also separates the parent USB device from the HID child:

```text
[V5_5_DS5_IDENTITY] usb_device_found=true
[V5_5_DS5_IDENTITY] hid_interface_found=false
[V5_5_DS5_IDENTITY] composite_status=Error
[V5_5_DS5_IDENTITY] current_profile=...
[V5_5_DS5_IDENTITY] suggested_next_action=...
[V5_5_DS5_IDENTITY] result=composite_error
```

By default the composite checker scans present devices only, because hidden
Windows PnP cache scans can be very slow. Use `-IncludeStale` only when you
need to inspect old `V55PHASE1` / `V55PHASE2` / `V55PHASE3` cache entries.

## Recovery And Test Order

Build and flash `hid_only` first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_only -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Profile hid_only -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf -Monitor
```

Then verify:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_identity.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_reports.ps1 -Seconds 6
```

Only after `hid_only` is healthy should the audio profiles be tested:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac1_2ch -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac1_2ch -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf -Monitor
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_audio.ps1
```

If UAC1 2ch works, continue to `hid_audio_uac2_2ch`. If UAC2 2ch works,
continue to `hid_audio_uac2_4ch`. The legacy `hid_audio_uac2` name remains as
a warning alias for `hid_audio_uac2_4ch`.

## Endpoint Map

```text
hid_only:
  serial=V55HIDONLY
  interfaces=1
  hid_interface=0
  hid_in=0x81 interrupt
  hid_out=0x01 interrupt
  audio_out=none

hid_audio_uac1_2ch:
  serial=V55UAC1_2CH
  interfaces=3
  audio_control_interface=0
  audio_streaming_out_interface=1
  hid_interface=2
  audio_out=0x02 isochronous adaptive, 192 bytes/ms nominal
  hid_in=0x81 interrupt
  hid_out=0x01 interrupt

hid_audio_uac2_2ch:
  serial=V55UAC2_2CH
  interfaces=3
  audio_control_interface=0
  audio_streaming_out_interface=1
  hid_interface=2
  audio_out=0x02 isochronous adaptive, 192 bytes/ms nominal
  hid_in=0x81 interrupt
  hid_out=0x01 interrupt

hid_audio_uac2_4ch:
  serial=V55UAC2_4CH
  interfaces=3
  audio_control_interface=0
  audio_streaming_out_interface=1
  hid_interface=2
  audio_out=0x02 isochronous adaptive, 384 bytes/ms nominal
  hid_in=0x81 interrupt
  hid_out=0x01 interrupt
```

HID report ID `0x02` is not a USB endpoint address. It does not conflict with
audio OUT endpoint `0x02`.

## Current Descriptor Changes

`hid_audio_uac1_2ch` now uses a small custom UAC1 app-class driver because the
TinyUSB built-in audio class expects UAC2 protocol. It handles UAC1
GET_INTERFACE / SET_INTERFACE and opens the isochronous OUT endpoint when
Windows selects alternate setting `1`.

`hid_audio_uac2_2ch` and `hid_audio_uac2_4ch` keep the TinyUSB UAC2 class path.
Their descriptor lengths are derived from the same descriptor macros used by
the configuration descriptor instead of hardcoded values.
