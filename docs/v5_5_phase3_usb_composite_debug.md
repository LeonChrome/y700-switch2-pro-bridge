# V5.5 Phase 3 USB Composite Debug

Date: 2026-06-06

## Hardware Result

The first `V55PHASE3` hardware check narrowed the failure to USB composite
enumeration:

```text
V55PHASE3 USB device appears=true
VID/PID=054C:0CE6
phase3_status=Error
active HID child=false
active audio endpoint=false
stale V55PHASE1/V55PHASE2 devices=present
likely_cause=composite descriptor or TinyUSB audio configuration
```

The old `V55PHASE1`, `V55PHASE2`, and generic HID entries may be Windows device
cache entries. They must not be treated as proof that Phase 3 HID is working.

## Diagnostic Tool

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
```

Expected output fields:

```text
[V5_5_USB_COMPOSITE] phase3_usb_found=true/false
[V5_5_USB_COMPOSITE] phase3_status=Error/OK/Unknown
[V5_5_USB_COMPOSITE] phase3_problem_code=...
[V5_5_USB_COMPOSITE] phase3_config_error=...
[V5_5_USB_COMPOSITE] phase3_hid_child_found=true/false
[V5_5_USB_COMPOSITE] phase3_audio_child_found=true/false
[V5_5_USB_COMPOSITE] stale_phase1_found=true/false
[V5_5_USB_COMPOSITE] stale_phase2_found=true/false
```

The identity checker now separates the parent USB device from the HID child:

```text
[V5_5_DS5_IDENTITY] usb_device_found=true
[V5_5_DS5_IDENTITY] hid_interface_found=false
[V5_5_DS5_IDENTITY] composite_status=Error
[V5_5_DS5_IDENTITY] result=composite_error
```

## Recovery Order

Build and test `hid_only` first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_only -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Profile hid_only -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf -Monitor
```

Then verify:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_identity.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_reports.ps1 -Seconds 6
```

Only after `hid_only` is healthy should `hid_audio_uac2` be flashed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac2 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Profile hid_audio_uac2 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf -Monitor
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_usb_composite.ps1
```

## Endpoint Map

```text
hid_only:
  hid_in=0x81 interrupt
  hid_out=0x01 interrupt
  audio_out=none

hid_audio_uac2:
  hid_in=0x81 interrupt
  hid_out=0x01 interrupt
  audio_out=0x02 isochronous adaptive
```

HID report ID `0x02` is not a USB endpoint address. It does not conflict with
audio OUT endpoint `0x02`.

## Current Descriptor Changes

The UAC2 descriptor keeps the minimal 4-channel 48 kHz OUT stream. The clock
source and output terminal associated-terminal fields were changed to `0x00`,
matching TinyUSB UAC2 examples and avoiding an unnecessary reference loop.

`hid_audio_uac1_fallback` is currently a safe stub profile. It builds and uses a
HID-only descriptor, giving us a named future slot for a lower-complexity UAC1
experiment without risking the HID baseline.
