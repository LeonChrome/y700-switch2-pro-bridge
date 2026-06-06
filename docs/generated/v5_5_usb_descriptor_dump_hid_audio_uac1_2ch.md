# v5 5 usb descriptor dump hid audio uac1 2ch

Date: 2026-06-06

Generated from compiled ELF symbols by:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate_v5_5_usb_descriptor_dumps.ps1
~~~

## `hid_audio_uac1_2ch`

~~~text
serial=V55UAC1_2CH
build=work/build/v5_5_dualsense_identity/hid_audio_uac1_2ch/esp32s3_dualsense_identity_experiment.elf
device_class_hint=00/00/00
iad_expected=false
~~~

### Device Descriptor

| Field | Value |
| --- | --- |
| bcdUSB | `0x0200` |
| bDeviceClass/SubClass/Protocol | `0x00 / 0x00 / 0x00` |
| bMaxPacketSize0 | 64 |
| VID/PID | `0x054C / 0x0CE6` |
| bcdDevice | `0x0100` |
| iManufacturer/iProduct/iSerial | 1 / 2 / 3 |
| bNumConfigurations | 1 |

~~~text
0000: 12 01 00 02 00 00 00 40 4C 05 E6 0C 00 01 01 02
0010: 03 01
~~~

### Configuration Descriptor

~~~text
0000: 09 02 84 00 03 01 00 80 32 09 04 00 00 00 01 01
0010: 00 04 09 24 01 00 01 1E 00 01 01 0C 24 02 01 01
0020: 01 00 02 03 00 00 00 09 24 03 03 02 03 00 01 00
0030: 09 04 01 00 00 01 02 00 04 09 04 01 01 01 01 02
0040: 00 04 07 24 01 01 01 01 00 0B 24 02 01 02 02 10
0050: 01 80 BB 00 09 05 02 09 C0 00 01 00 00 07 25 01
0060: 00 00 00 00 09 04 02 00 02 03 00 00 05 09 21 11
0070: 01 00 01 22 91 00 07 05 81 03 40 00 01 07 05 01
0080: 03 40 00 01
~~~

| Offset | Length | Type | Detail |
| ---: | ---: | --- | --- |
| `0x0000` | 9 | CONFIGURATION | wTotalLength=132 bNumInterfaces=3 attributes=0x80 max_power=100mA |
| `0x0009` | 9 | INTERFACE | interface=0 alt=0 endpoints=0 class=0x01 subclass=0x01 protocol=0x00 iInterface=4 |
| `0x0012` | 9 | CS_INTERFACE | subtype=0x01 bcdADC=0x0100 |
| `0x001B` | 12 | CS_INTERFACE | subtype=0x02 |
| `0x0027` | 9 | CS_INTERFACE | subtype=0x03 |
| `0x0030` | 9 | INTERFACE | interface=1 alt=0 endpoints=0 class=0x01 subclass=0x02 protocol=0x00 iInterface=4 |
| `0x0039` | 9 | INTERFACE | interface=1 alt=1 endpoints=1 class=0x01 subclass=0x02 protocol=0x00 iInterface=4 |
| `0x0042` | 7 | CS_INTERFACE | subtype=0x01 |
| `0x0049` | 11 | CS_INTERFACE | subtype=0x02 channels=2 bits=16 sample_rate=48000 |
| `0x0054` | 9 | ENDPOINT | ep=0x02 OUT isochronous attributes=0x09 max_packet=192 interval=1 |
| `0x005D` | 7 | CS_ENDPOINT | subtype=0x01 |
| `0x0064` | 9 | INTERFACE | interface=2 alt=0 endpoints=2 class=0x03 subclass=0x00 protocol=0x00 iInterface=5 |
| `0x006D` | 9 | HID | bcdHID=0x0111 report_length=145 |
| `0x0076` | 7 | ENDPOINT | ep=0x81 IN interrupt attributes=0x03 max_packet=64 interval=1 |
| `0x007D` | 7 | ENDPOINT | ep=0x01 OUT interrupt attributes=0x03 max_packet=64 interval=1 |

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
| wTotalLength | declared=132, actual=132, valid=true |
| bNumInterfaces | declared=3, unique=3, valid=true |
| interface continuity | true |
| interface endpoint counts | true |
| duplicate endpoint in same interface/alt | false |
| IAD | present=false, expected=false, valid=true |
| IAD interface coverage | true |
| HID report length | declared=145, actual=145, valid=true |
| string indices | max=5, descriptor_count=6, valid=true |
| audio | version=UAC1, channels=2, sample_rate=48000 |
