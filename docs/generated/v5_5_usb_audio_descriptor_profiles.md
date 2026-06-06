# V5.5 USB Audio Descriptor Profiles

Date: 2026-06-06

Generated from:

```text
firmware/esp32s3_dualsense_identity_experiment/main/usb_dualsense_descriptor.c
firmware/esp32s3_dualsense_identity_experiment/main/usb_dualsense_descriptor.h
firmware/esp32s3_dualsense_identity_experiment/main/tinyusb_config/tusb_config.h
firmware/esp32s3_dualsense_identity_experiment/main/CMakeLists.txt
```

## Common Device Identity

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

| Profile | Serial | Config length | Interfaces | Audio class | Audio channels | Audio OUT endpoint |
| --- | --- | ---: | ---: | --- | ---: | --- |
| `hid_only` | `V55HIDONLY` | 41 | 1 | None | 0 | None |
| `hid_audio_uac1_2ch` | `V55UAC1_2CH` | 140 | 3 | UAC1 | 2 | `0x02` isoch adaptive |
| `hid_audio_uac2_2ch` | `V55UAC2_2CH` | 167 | 3 | UAC2 | 2 | `0x02` isoch adaptive |
| `hid_audio_uac2_4ch` | `V55UAC2_4CH` | 175 | 3 | UAC2 | 4 | `0x02` isoch adaptive |

`hid_audio_uac2` is a warning alias for `hid_audio_uac2_4ch`.
`hid_audio_uac1_fallback` is a warning alias for `hid_audio_uac1_2ch`.

## `hid_only`

```text
serial=V55HIDONLY
configuration_length=41
bNumInterfaces=1
interface 0:
  class=HID
  subclass=0
  protocol=0
  endpoints=2
  endpoint 0x81=interrupt IN, max_packet=64, interval=1
  endpoint 0x01=interrupt OUT, max_packet=64, interval=1
audio=false
```

## `hid_audio_uac1_2ch`

```text
serial=V55UAC1_2CH
configuration_length=140
bNumInterfaces=3
iad:
  first_interface=0
  interface_count=2
  class=Audio
  protocol=undefined
interface 0:
  class=Audio Control
  audio_class=UAC1
  bcdADC=0x0100
  clock_source=implicit UAC1 fixed 48 kHz format descriptor
interface 1:
  class=Audio Streaming OUT
  alt0=endpoints 0
  alt1=endpoints 1
  channels=2
  sample_rate=48000
  bits_per_sample=16
  nominal_payload=192 bytes/ms
  endpoint 0x02=isochronous adaptive OUT, max_packet=192, interval=1
interface 2:
  class=HID
  endpoint 0x81=interrupt IN, max_packet=64, interval=1
  endpoint 0x01=interrupt OUT, max_packet=64, interval=1
```

## `hid_audio_uac2_2ch`

```text
serial=V55UAC2_2CH
configuration_length=167
bNumInterfaces=3
iad:
  first_interface=0
  interface_count=2
  class=Audio
  protocol=UAC2
interface 0:
  class=Audio Control
  audio_class=UAC2
  bcdADC=0x0200
  category=desktop_speaker
  clock_source=internal fixed clock
  input_terminal=USB streaming
  feature_unit=master + left + right mute/volume RW
  output_terminal=headphones
interface 1:
  class=Audio Streaming OUT
  alt0=endpoints 0
  alt1=endpoints 1
  channels=2
  sample_rate=48000
  bits_per_sample=16
  nominal_payload=192 bytes/ms
  endpoint 0x02=isochronous adaptive OUT, max_packet=196, interval=1
interface 2:
  class=HID
  endpoint 0x81=interrupt IN, max_packet=64, interval=1
  endpoint 0x01=interrupt OUT, max_packet=64, interval=1
```

TinyUSB computes UAC2 max packet size with one extra full-speed frame of slack:
`((48000 / 1000) + 1) * 2 bytes * 2 channels = 196`.

## `hid_audio_uac2_4ch`

```text
serial=V55UAC2_4CH
configuration_length=175
bNumInterfaces=3
iad:
  first_interface=0
  interface_count=2
  class=Audio
  protocol=UAC2
interface 0:
  class=Audio Control
  audio_class=UAC2
  bcdADC=0x0200
  category=desktop_speaker
  clock_source=internal fixed clock
  input_terminal=USB streaming
  feature_unit=master + ch1 + ch2 + ch3 + ch4 mute/volume RW
  output_terminal=headphones
interface 1:
  class=Audio Streaming OUT
  alt0=endpoints 0
  alt1=endpoints 1
  channels=4
  sample_rate=48000
  bits_per_sample=16
  nominal_payload=384 bytes/ms
  endpoint 0x02=isochronous adaptive OUT, max_packet=392, interval=1
interface 2:
  class=HID
  endpoint 0x81=interrupt IN, max_packet=64, interval=1
  endpoint 0x01=interrupt OUT, max_packet=64, interval=1
```

TinyUSB computes UAC2 max packet size with one extra full-speed frame of slack:
`((48000 / 1000) + 1) * 2 bytes * 4 channels = 392`.

## Verification Order

```text
1. Build/flash hid_only and confirm HID input.
2. Build/flash hid_audio_uac1_2ch and confirm HID child plus audio endpoint.
3. Build/flash hid_audio_uac2_2ch and confirm UAC2 2ch enumeration.
4. Build/flash hid_audio_uac2_4ch only after 2ch UAC2 works.
```
