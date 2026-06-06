# V5.5 DS5Dongle USB Descriptor Reference

Date: 2026-06-06

Source:

~~~text
repository=research/upstream/DS5Dongle
commit=8760ee3f4fa9335e3c5e1a0d0aead92b55f23abb
file=src/usb_descriptors.cpp
configuration=default ENABLE_SERIAL=OFF, DualSense mode
~~~

The default final descriptor is UAC1 with Audio Control, four-channel Audio
Streaming OUT, two-channel Audio Streaming IN, and HID. It does not emit an
IAD and uses device class `00/00/00`. DS5Dongle only switches to device class
`EF/02/01` and adds the Audio IAD when `ENABLE_SERIAL=ON` also adds CDC.

## Device Descriptor

| Field | Value |
| --- | --- |
| bcdUSB | `0x0200` |
| bDeviceClass/SubClass/Protocol | `0x00 / 0x00 / 0x00` |
| bMaxPacketSize0 | 64 |
| VID/PID | `0x054C / 0x0CE6` |
| bcdDevice | `0x0100` |
| iManufacturer/iProduct/iSerial | 1 / 2 / 0 |
| bNumConfigurations | 1 |

~~~text
0000: 12 01 00 02 00 00 00 40 4C 05 E6 0C 00 01 01 02
0010: 00 01
~~~

## Configuration Descriptor

~~~text
0000: 09 02 E3 00 04 01 00 C0 FA 09 04 00 00 00 01 01
0010: 00 00 0A 24 01 00 01 49 00 02 01 02 0C 24 02 01
0020: 01 01 06 04 33 00 00 00 0C 24 06 02 01 01 03 00
0030: 00 00 00 00 09 24 03 03 01 03 04 02 00 0C 24 02
0040: 04 02 04 03 02 03 00 00 00 09 24 06 05 04 01 03
0050: 00 00 09 24 03 06 01 01 01 05 00 09 04 01 00 00
0060: 01 02 00 00 09 04 01 01 01 01 02 00 00 07 24 01
0070: 01 01 01 00 0B 24 02 01 04 02 10 01 80 BB 00 09
0080: 05 01 09 88 01 01 00 00 07 25 01 00 00 00 00 09
0090: 04 02 00 00 01 02 00 00 09 04 02 01 01 01 02 00
00A0: 00 07 24 01 06 01 01 00 0B 24 02 01 02 02 10 01
00B0: 80 BB 00 09 05 82 05 C4 00 01 00 00 07 25 01 00
00C0: 00 00 00 09 04 03 00 02 03 00 00 00 09 21 11 01
00D0: 00 01 22 41 01 07 05 84 03 40 00 01 07 05 03 03
00E0: 40 00 01
~~~

| Offset | Length | Type | Detail |
| ---: | ---: | --- | --- |
| `0x0000` | 9 | CONFIGURATION | wTotalLength=227 bNumInterfaces=4 attributes=0xC0 max_power=500mA |
| `0x0009` | 9 | INTERFACE | interface=0 alt=0 endpoints=0 class=0x01 subclass=0x01 protocol=0x00 iInterface=0 |
| `0x0012` | 10 | CS_INTERFACE | subtype=0x01 bcdADC=0x0100 |
| `0x001C` | 12 | CS_INTERFACE | subtype=0x02 |
| `0x0028` | 12 | CS_INTERFACE | subtype=0x06 |
| `0x0034` | 9 | CS_INTERFACE | subtype=0x03 |
| `0x003D` | 12 | CS_INTERFACE | subtype=0x02 |
| `0x0049` | 9 | CS_INTERFACE | subtype=0x06 |
| `0x0052` | 9 | CS_INTERFACE | subtype=0x03 |
| `0x005B` | 9 | INTERFACE | interface=1 alt=0 endpoints=0 class=0x01 subclass=0x02 protocol=0x00 iInterface=0 |
| `0x0064` | 9 | INTERFACE | interface=1 alt=1 endpoints=1 class=0x01 subclass=0x02 protocol=0x00 iInterface=0 |
| `0x006D` | 7 | CS_INTERFACE | subtype=0x01 |
| `0x0074` | 11 | CS_INTERFACE | subtype=0x02 channels=4 bits=16 sample_rate=48000 |
| `0x007F` | 9 | ENDPOINT | ep=0x01 OUT isochronous attributes=0x09 max_packet=392 interval=1 |
| `0x0088` | 7 | CS_ENDPOINT | subtype=0x01 |
| `0x008F` | 9 | INTERFACE | interface=2 alt=0 endpoints=0 class=0x01 subclass=0x02 protocol=0x00 iInterface=0 |
| `0x0098` | 9 | INTERFACE | interface=2 alt=1 endpoints=1 class=0x01 subclass=0x02 protocol=0x00 iInterface=0 |
| `0x00A1` | 7 | CS_INTERFACE | subtype=0x01 |
| `0x00A8` | 11 | CS_INTERFACE | subtype=0x02 channels=2 bits=16 sample_rate=48000 |
| `0x00B3` | 9 | ENDPOINT | ep=0x82 IN isochronous attributes=0x05 max_packet=196 interval=1 |
| `0x00BC` | 7 | CS_ENDPOINT | subtype=0x01 |
| `0x00C3` | 9 | INTERFACE | interface=3 alt=0 endpoints=2 class=0x03 subclass=0x00 protocol=0x00 iInterface=0 |
| `0x00CC` | 9 | HID | bcdHID=0x0111 report_length=321 |
| `0x00D5` | 7 | ENDPOINT | ep=0x84 IN interrupt attributes=0x03 max_packet=64 interval=1 |
| `0x00DC` | 7 | ENDPOINT | ep=0x03 OUT interrupt attributes=0x03 max_packet=64 interval=1 |

## HID Report Descriptor

~~~text
0000: 05 01 09 05 A1 01 85 01 09 30 09 31 09 32 09 35
0010: 09 33 09 34 15 00 26 FF 00 75 08 95 06 81 02 06
0020: 00 FF 09 20 95 01 81 02 05 01 09 39 15 00 25 07
0030: 35 00 46 3B 01 65 14 75 04 95 01 81 42 65 00 05
0040: 09 19 01 29 0F 15 00 25 01 75 01 95 0F 81 02 06
0050: 00 FF 09 21 95 0D 81 02 06 00 FF 09 22 15 00 26
0060: FF 00 75 08 95 34 81 02 85 02 09 23 95 2F 91 02
0070: 85 05 09 33 95 28 B1 02 85 08 09 34 95 2F B1 02
0080: 85 09 09 24 95 13 B1 02 85 0A 09 25 95 1A B1 02
0090: 85 0B 09 41 95 29 B1 02 85 0C 09 42 95 29 B1 02
00A0: 85 20 09 26 95 3F B1 02 85 21 09 27 95 04 B1 02
00B0: 85 22 09 40 95 3F B1 02 85 80 09 28 95 3F B1 02
00C0: 85 81 09 29 95 3F B1 02 85 82 09 2A 95 09 B1 02
00D0: 85 83 09 2B 95 3F B1 02 85 84 09 2C 95 3F B1 02
00E0: 85 85 09 2D 95 02 B1 02 85 A0 09 2E 95 01 B1 02
00F0: 85 E0 09 2F 95 3F B1 02 85 F0 09 30 95 3F B1 02
0100: 85 F1 09 31 95 3F B1 02 85 F2 09 32 95 0F B1 02
0110: 85 F4 09 35 95 3F B1 02 85 F5 09 36 95 03 B1 02
0120: 85 F6 09 37 95 3F B1 02 85 F7 09 38 95 3F B1 02
0130: 85 F8 09 39 95 3F B1 02 85 F9 09 3A 95 3F B1 02
0140: C0
~~~

## String Descriptors

| Index | Value | Source |
| ---: | --- | --- |
| 0 | language `0x0409` | fixed |
| 1 | `Sony Interactive Entertainment` | fixed |
| 2 | `DualSense Wireless Controller` | selected dynamically in DualSense mode |
| 3 | board USB serial | generated dynamically by `board_usb_get_serial` |

~~~text
index_0:
0000: 04 03 09 04

index_1:
0000: 3E 03 53 00 6F 00 6E 00 79 00 20 00 49 00 6E 00
0010: 74 00 65 00 72 00 61 00 63 00 74 00 69 00 76 00
0020: 65 00 20 00 45 00 6E 00 74 00 65 00 72 00 74 00
0030: 61 00 69 00 6E 00 6D 00 65 00 6E 00 74 00

index_2:
0000: 3C 03 44 00 75 00 61 00 6C 00 53 00 65 00 6E 00
0010: 73 00 65 00 20 00 57 00 69 00 72 00 65 00 6C 00
0020: 65 00 73 00 73 00 20 00 43 00 6F 00 6E 00 74 00
0030: 72 00 6F 00 6C 00 6C 00 65 00 72 00

index_3:
runtime-generated; no fixed raw byte sequence in the upstream source
~~~

## Validation

| Check | Result |
| --- | --- |
| wTotalLength | declared=227, actual=227, valid=true |
| bNumInterfaces | declared=4, unique=4, valid=true |
| interface continuity | true |
| interface endpoint counts | true |
| duplicate endpoint in same interface/alt | false |
| IAD | present=false, expected=false, valid=true |
| IAD interface coverage | true |
| HID report length | declared=321, actual=321, valid=true |
| string indices | max=0, descriptor_count=4, valid=true |
| audio | version=UAC1, channels=2, sample_rate=48000 |

## Final Topology

| Interface | Function | Endpoints |
| ---: | --- | --- |
| 0 | UAC1 Audio Control | none |
| 1 alt 0/1 | UAC1 Audio Streaming OUT, 4ch, 16-bit, 48 kHz | `0x01` adaptive isoch, max 392 |
| 2 alt 0/1 | UAC1 Audio Streaming IN, 2ch, 16-bit, 48 kHz | `0x82` asynchronous isoch, max 196 |
| 3 | DualSense HID | `0x84` interrupt IN and `0x03` interrupt OUT, 64 bytes |

~~~text
wTotalLength=227
bNumInterfaces=4
audio_control_total_length=73
hid_report_descriptor_length=321
iad_present=false
device_class=00/00/00
~~~
