# v5 5 usb descriptor dump hid audio uac2 4ch

Date: 2026-06-06

Generated from compiled ELF symbols by:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate_v5_5_usb_descriptor_dumps.ps1
~~~

## `hid_audio_uac2_4ch`

~~~text
serial=V55UAC2_4CH
build=work/build/v5_5_dualsense_identity/hid_audio_uac2_4ch/esp32s3_dualsense_identity_experiment.elf
device_class_hint=EF/02/01
iad_expected=true
~~~

### Device Descriptor

| Field | Value |
| --- | --- |
| bcdUSB | `0x0200` |
| bDeviceClass/SubClass/Protocol | `0xEF / 0x02 / 0x01` |
| bMaxPacketSize0 | 64 |
| VID/PID | `0x054C / 0x0CE6` |
| bcdDevice | `0x0100` |
| iManufacturer/iProduct/iSerial | 1 / 2 / 3 |
| bNumConfigurations | 1 |

~~~text
0000: 12 01 00 02 EF 02 01 40 4C 05 E6 0C 00 01 01 02
0010: 03 01
~~~

### Configuration Descriptor

~~~text
0000: 09 02 B9 00 03 01 00 80 32 08 0B 00 02 01 00 20
0010: 00 09 04 00 00 00 01 01 20 04 09 24 01 00 02 01
0020: 48 00 00 08 24 0A 04 01 05 00 00 11 24 02 01 01
0030: 01 00 04 04 00 00 00 00 00 00 00 00 1A 24 06 02
0040: 01 0F 00 00 00 0F 00 00 00 0F 00 00 00 0F 00 00
0050: 00 0F 00 00 00 00 0C 24 03 03 02 03 00 02 04 00
0060: 00 00 09 04 01 00 00 01 02 20 04 09 04 01 01 01
0070: 01 02 20 04 10 24 01 01 00 01 01 00 00 00 04 00
0080: 00 00 00 00 06 24 02 01 02 10 07 05 02 09 88 01
0090: 01 08 25 01 00 00 01 01 00 09 04 02 00 02 03 00
00A0: 00 05 09 21 11 01 00 01 22 91 00 07 05 81 03 40
00B0: 00 01 07 05 01 03 40 00 01
~~~

| Offset | Length | Type | Detail |
| ---: | ---: | --- | --- |
| `0x0000` | 9 | CONFIGURATION | wTotalLength=185 bNumInterfaces=3 attributes=0x80 max_power=100mA |
| `0x0009` | 8 | IAD | first_interface=0 count=2 class=0x01 subclass=0x00 protocol=0x20 |
| `0x0011` | 9 | INTERFACE | interface=0 alt=0 endpoints=0 class=0x01 subclass=0x01 protocol=0x20 iInterface=4 |
| `0x001A` | 9 | CS_INTERFACE | subtype=0x01 bcdADC=0x0200 |
| `0x0023` | 8 | CS_INTERFACE | subtype=0x0A |
| `0x002B` | 17 | CS_INTERFACE | subtype=0x02 |
| `0x003C` | 26 | CS_INTERFACE | subtype=0x06 |
| `0x0056` | 12 | CS_INTERFACE | subtype=0x03 |
| `0x0062` | 9 | INTERFACE | interface=1 alt=0 endpoints=0 class=0x01 subclass=0x02 protocol=0x20 iInterface=4 |
| `0x006B` | 9 | INTERFACE | interface=1 alt=1 endpoints=1 class=0x01 subclass=0x02 protocol=0x20 iInterface=4 |
| `0x0074` | 16 | CS_INTERFACE | subtype=0x01 channels=4 |
| `0x0084` | 6 | CS_INTERFACE | subtype=0x02 subslot_bytes=2 bits=16 |
| `0x008A` | 7 | ENDPOINT | ep=0x02 OUT isochronous attributes=0x09 max_packet=392 interval=1 |
| `0x0091` | 8 | CS_ENDPOINT | subtype=0x01 |
| `0x0099` | 9 | INTERFACE | interface=2 alt=0 endpoints=2 class=0x03 subclass=0x00 protocol=0x00 iInterface=5 |
| `0x00A2` | 9 | HID | bcdHID=0x0111 report_length=145 |
| `0x00AB` | 7 | ENDPOINT | ep=0x81 IN interrupt attributes=0x03 max_packet=64 interval=1 |
| `0x00B2` | 7 | ENDPOINT | ep=0x01 OUT interrupt attributes=0x03 max_packet=64 interval=1 |

### HID Report Descriptor

~~~text
0000: 05 01 09 05 A1 01 85 01 09 30 09 31 09 32 09 35
0010: 09 33 09 34 15 00 26 FF 00 75 08 95 06 81 02 06
0020: 00 FF 09 20 95 01 81 02 05 01 09 39 15 00 25 07
0030: 35 00 46 3B 01 65 14 75 04 95 01 81 42 65 00 05
0040: 09 19 01 29 0F 15 00 25 01 75 01 95 0F 81 02 06
0050: 00 FF 09 21 95 0D 81 02 06 00 FF 09 22 15 00 26
0060: FF 00 75 08 95 34 81 02 85 02 09 23 95 2F 91 02
0070: 85 05 09 33 95 28 B1 02 85 08 09 34 95 2F B1 02
0080: 85 09 09 24 95 13 B1 02 85 20 09 26 95 3F B1 02
0090: C0
~~~

### Validation

| Check | Result |
| --- | --- |
| wTotalLength | declared=185, actual=185, valid=true |
| bNumInterfaces | declared=3, unique=3, valid=true |
| interface continuity | true |
| interface endpoint counts | true |
| duplicate endpoint in same interface/alt | false |
| IAD | present=true, expected=true, valid=true |
| IAD interface coverage | true |
| HID report length | declared=145, actual=145, valid=true |
| string indices | max=5, descriptor_count=6, valid=true |
| audio | version=UAC2, channels=4, sample_rate=48000 |
