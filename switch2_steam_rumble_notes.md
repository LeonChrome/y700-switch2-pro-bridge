# Switch 2 Pro Steam Rumble Bridge Notes

Date: 2026-05-21

## Current Working Model

The Y700 project currently has three separate paths that must be kept distinct:

1. Windows/Steam sees the Y700 as a USB Nintendo-style controller.
2. The Y700 reads the real Switch 2 Pro controller over BLE GATT and writes decoded input into `/data/local/tmp/switch2_state.txt`.
3. The Y700 responder receives USB output reports from Windows/Steam and converts those events into BLE haptic preset commands for the real controller.

The haptic bridge is event based at the moment. It does not fully decode Nintendo HD rumble waveforms. When the responder sees an active HID rumble output report, it maps that active event to one BLE preset from this sequence:

```text
1 -> 2 -> 5 -> 6 -> repeat
```

Then, when it sees the neutral/stop HID output report, it sends preset `0` to stop the effect.

This means the "different vibration each click" behavior was caused by our compatibility bridge rotating presets. It should not yet be interpreted as Steam sending seven different real Switch 2 Pro haptic patterns.

## 2026-05-22 Key Milestone: Steam Layout-Level Recognition

The input side is now a major proven node, not a cheap name-spoofing experiment.

Steam has accepted the Y700 virtual USB controller as a Nintendo Switch 2 Pro / Nintendo Switch Pro family device at the layout level. The real Switch 2 Pro controller connects to the Y700 over BLE, and its input is forwarded into Steam through the Y700 USB gadget.

Confirmed in Steam controller testing:

- Normal controls work: A/B/X/Y, D-pad, `+`, `-`, L/ZL/R/ZR, sticks, and stick clicks.
- Switch 2-only controls work: `C`, `GR`, and `GL` are visible in Steam and produce input.
- Steam is not merely showing a generic renamed HID device. It is interpreting the Nintendo/Switch 2 button layout.

The route that produced this result is:

```text
Switch 2 Pro BLE 63-byte notify
-> Switch2BleBridge parses raw button/stick state
-> /data/local/tmp/switch2_state.txt stores BLE-native b5/b6/b7 button bytes
-> Switch2FfsResponder maps those bytes into the wired Switch 2 USB packet
-> Windows/Steam opens the Nintendo HID path for VID 057e / PID 2069
```

Critical mapping detail:

```text
BLE byte2 = B A Y X R ZR Plus RStick
BLE byte3 = DDown DRight DLeft DUp L ZL Minus LStick
BLE byte4 = Home Capture GR GL C

wired USB data[5] = Y X B A R ZR
wired USB data[6] = Minus Plus RStick LStick Home Capture C
wired USB data[7] = DDown DUp DRight DLeft L ZL
wired USB data[8] = GR GL
```

Two fixes were especially important:

1. Do not copy BLE button bytes directly into the USB packet. Steam's wired Switch 2 parser expects different offsets and bit order.
2. Store and parse button bytes as hexadecimal values (`0x..`) so controls with high bits set do not corrupt the state.

This milestone means the remaining hard problem is haptics/output translation, not basic controller identity or input forwarding.

## Public Steam/SDL Evidence For Rumble

Steam's closed client code for Switch 2 Pro haptics is not publicly available, so the exact Steam settings-page behavior cannot be read directly from source.

The useful public references are:

- Valve's Steam Input API exposes runtime vibration calls such as `TriggerVibration(...)` and repeated haptic pulse calls. These are game/runtime APIs, not proof that the Steam controller settings page will always emit the same USB output.
- SDL's public Switch 2 HIDAPI driver shows preliminary Switch 2 rumble support. For `USB_PRODUCT_NINTENDO_SWITCH2_PRO`, it builds a 64-byte output packet with report byte `0x02`, a `0x50 | seq` command byte, packed rumble amplitude bytes, and a mirrored second motor block at offset `0x11`.

This matches the local Windows HID haptic probe shape that the Y700 responder already sees:

```text
02 50 ... 87 ...  / 64-byte HID OUT
```

That is why the current bridge treats host-to-device 64-byte output reports as the most important signal. If Steam settings emits one, we can translate it. If it emits none, we must test a runtime path or adjust Steam/profile state until an output packet appears.

## Why The Earlier Steam Test Felt Successful

Earlier, Steam's vibration action did produce host-to-device output that reached `/dev/hidg0`. The responder logged packets like:

```text
HID OUT 64 bytes: 02 50 87 15 27 51 71 ...
HID rumble event start preset=...
rumble bridge hid-out-active preset=... wrote cmd 0a91010200080000XX00000000000000
HID rumble event stop durationMs=...
rumble bridge hid-out-stop preset=0 wrote cmd 0a910102000800000000000000000000
```

The BLE bridge then wrote those preset commands to the real controller and received ACKs like:

```text
BLE write uuid=649d4ac9-8eb7-4e6c-af44-1ea54fe5f005 data=0A910102000800000200000000000000
ack n=8 data=0A01010210780000
```

That proved the full output chain:

```text
Steam or local HID writer
-> Windows HID OUT
-> Y700 /dev/hidg0 output reader
-> /data/local/tmp/switch2_ble_write.txt
-> Y700 BLE GATT write
-> real Switch 2 Pro vibration/sound
```

## Why The Current Steam Test Can Be Silent

The latest silent Steam settings test showed no new `HID OUT`, no new `HID rumble event`, and no new BLE write after the last known local probe. If no output packet reaches the responder, the Y700 has nothing to bridge.

Likely causes to check in order:

1. Steam did not emit a rumble output packet for that settings-page button in the current controller profile.
2. The controller was re-enumerated as a new Windows/Steam device instance, so the old Steam Input profile no longer applies.
3. The Steam setting "Use Nintendo Button Layout" changed the path: earlier, turning it off appeared to make game rumble produce real output.
4. The Windows USB gadget endpoint is not currently enumerated. In this state the responder logs `Cannot send after transport endpoint shutdown`, and Windows does not show `VID_057E&PID_2069`.
5. The BLE bridge is not connected to the real controller, or the controller is not nearby. In that case USB OUT may still be logged, but no physical vibration can happen.
6. The action being clicked is actually an identify/player-light action, not a rumble action. The temporary LED-to-rumble compatibility bridge only fires when `/data/local/tmp/switch2_bridge_led_rumble` exists and the expected bulk LED command is received.

## Verification Checklist

Use this order when testing again.

1. Confirm Y700 ADB:

```powershell
$ADB = '<path-to-adb.exe>'
& $ADB devices -l
```

2. Confirm Windows sees the emulated Nintendo USB device:

```powershell
Get-PnpDevice -PresentOnly |
  Where-Object { $_.InstanceId -like '*VID_057E&PID_2069*' -or $_.FriendlyName -like '*Nintendo*' } |
  Select-Object Class,FriendlyName,InstanceId,Status
```

3. Confirm responder and files on Y700:

```powershell
& $ADB -s 'adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp' shell su -c 'ls -l /dev/hidg0 /data/local/tmp/switch2_state.txt /data/local/tmp/switch2_bridge_led_rumble 2>/dev/null'
```

4. Confirm the real controller BLE bridge is connected:

```powershell
& $ADB -s 'adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp' shell 'tail -n 80 /data/local/tmp/switch2_ble_bridge.log'
```

Look for GATT connection, notify enable, input updates, and later BLE write ACKs.

5. Run the known-good local Windows HID probe:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\Users\leon\Documents\Codex\y700-hid-gamepad\tools\Send-HidHapticProbe.ps1' -Vid 057e -Pids 2069 -PulseMs 650
```

Expected responder evidence:

```text
HID OUT ...
HID rumble event start preset=...
rumble bridge hid-out-active preset=...
HID rumble event stop ...
```

Expected BLE evidence:

```text
BLE write ... 0A91010200080000XX00000000000000
ack n=8 ... 0A01010210780000
```

6. Only after the local probe works, click Steam's vibration test and compare logs. If the logs do not gain a new `HID OUT`, the issue is Steam not sending output in that UI path.

Useful log filters:

```powershell
& $ADB -s 'adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp' shell su -c "grep HID /data/local/tmp/switch2_ffs_responder.log | tail -n 80"
```

```powershell
& $ADB -s 'adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp' shell "grep 'BLE write' /data/local/tmp/switch2_ble_bridge.log | tail -n 40"
```

## BLE Connection Principle

The real Switch 2 Pro controller is not being used through Android's normal gamepad input stack. The Y700 acts as a BLE central and connects directly to the controller's custom GATT services.

Known controller address:

```text
38:C6:CE:27:FC:2D
```

Important input characteristic:

```text
7492866c-ec3e-4619-8258-32755ffcc0f8
```

Important haptic/write characteristic observed in logs:

```text
649d4ac9-8eb7-4e6c-af44-1ea54fe5f005
```

The bridge process is launched with `app_process64` on the Y700. It scans or directly connects to the BLE address, discovers services, enables notifications on the input characteristic, parses 63-byte input notifications, and writes a compact state file:

```text
/data/local/tmp/switch2_state.txt
```

The USB responder reads that state file and turns it into USB input reports for Windows/Steam.

For output, the flow is reversed. The responder sees a USB output report, writes a text command into:

```text
/data/local/tmp/switch2_ble_write.txt
```

The BLE bridge watches that file and writes the corresponding command to the controller's BLE characteristic. The currently known preset command shape is:

```text
cmd 0a91010200080000XX00000000000000
```

where `XX` is the preset number. Preset `00` stops vibration/sound.

## Next Test Goal

The next clean test is not "does the controller vibrate" in isolation. It is:

```text
When Steam's own UI or a game triggers rumble,
does Y700 log a new HID OUT or bulk OUT packet?
```

If yes, bind that packet shape to an appropriate BLE preset or preset family.

If no, the next work is on Steam profile/device identity/settings, not on BLE haptics.

## 2026-05-21 22:10 Report-ID Alignment Fix

The HID report descriptor now explicitly declares:

```text
input  report ID 0x30 + 63 payload bytes
output report ID 0x02 + 63 payload bytes
```

Windows now reads the emulated controller as:

```text
inLen=64
30 xx 91 ...
```

instead of the previous no-report-ID shape:

```text
inLen=65
00 30 xx 91 ...
```

This matters because the previous leading `00` shifted the Switch Pro input report by one byte. That offset can explain Steam seeing unrelated buttons such as D-pad left, plus, or minus as held.

Steam reopened the device through the expected route after redeploy:

```text
Product: Nintendo Switch Pro Controller
Controller using HIDAPI driver, vid=0x057e, pid=0x2069
Nintendo Controller subtype 0
```

## Rich Haptic Cycle Test Mode

A temporary test flag now exists:

```text
/data/local/tmp/switch2_haptic_cycle_rich
```

When this file exists, each HID rumble active event is mapped to this repeating Pro2 BLE preset sequence:

```text
1 -> 2 -> 5 -> 4 -> 6 -> 7 -> repeat
```

This mode is meant to reproduce and control the earlier "many different vibration/sound effects" behavior from Steam's vibration test. It is not the final gameplay mapping, because presets `4` and `7` are mostly audible cue effects.

Disable rich mode with:

```powershell
& $ADB -s '<serial>' shell su -c 'rm -f /data/local/tmp/switch2_haptic_cycle_rich'
```

Enable it again with:

```powershell
& $ADB -s '<serial>' shell su -c 'touch /data/local/tmp/switch2_haptic_cycle_rich'
```

## 2026-05-22 Switch 2 Wired Input Layout Alignment

Steam opened the current Y700 identity through the Nintendo HIDAPI path for `VID 057e / PID 2069`. The Pro2 BLE notify packet and the wired Switch 2 USB state packet do not place controls at the same offsets.

```text
Pro2 BLE notify byte2 = B A Y X R ZR Plus RStick
Pro2 BLE notify byte3 = DDown DRight DLeft DUp L ZL Minus LStick
Pro2 BLE notify byte4 = Home Capture GR GL C

wired USB data[5] = Y X B A R ZR
wired USB data[6] = Minus Plus RStick LStick Home Capture C
wired USB data[7] = DDown DUp DRight DLeft L ZL
wired USB data[8] = GR GL
wired USB data[11..16] = left/right packed sticks
```

The responder keeps the known BLE bytes in native state-file order and maps them into the wired USB packet before writing `/dev/hidg0`. The output report declaration remains `0x02` for the already verified Windows HID OUT -> Y700 -> BLE preset haptic bridge.

## 2026-05-22 Steam Settings Test Result

The Steam settings-page vibration test was clicked multiple times by the user while the responder and BLE logs were marked and watched.

Result:

```text
No new HID OUT
No new HID rumble event
No new bulk OUT
No new BLE write
```

Steam's own `controller.txt` and `controller_ui.txt` logs around the same time only showed personalization/config reload activity:

```text
Saving personalization
Loaded Config ...
```

No rumble/haptic/vibration output line appeared. The currently observed Steam personalization state does not by itself prove rumble is disabled. Depending on which Steam profile/cache file is read, `rumble` may appear as enabled or as `-1` default/inherited:

```text
rumble  1 or -1
haptics 1
```

So this test suggests the settings-page button did not send a device output report in this Steam UI path. It does not invalidate the bridge, because the local HID haptic probe immediately before this succeeded:

```text
Windows WriteFile -> HID OUT 64 bytes -> preset 1 -> BLE write -> ACK -> preset 0 -> ACK
```

A direct external XInput probe was also tried. All XInput slots returned `1167` (`ERROR_DEVICE_NOT_CONNECTED`), so ordinary non-Steam processes do not see Steam's reserved XInput slot.

Why an actual game or Steam-launched helper is useful:

```text
Steam settings UI button
-> may only touch personalization/identify/config UI paths
-> in this run emitted no HID OUT packet

Steam runtime rumble
-> game or Steam Input API asks for vibration
-> Steam must decide whether to send a device output packet
-> responder can log and translate the real packet/timing
```

In other words, a game test is not more "correct" than the Steam controller page in theory. It is useful because it exercises the runtime rumble path that games actually use. If the settings page emits no USB output, there is nothing for the Y700 bridge to forward.

## 2026-05-22 BzzzController Runtime Test

BzzzController was launched from Steam:

```text
AppID 1642040
Process: C:\Program Files (x86)\Steam\steamapps\common\BzzzController\Bzzz.exe
```

Steam logs confirmed the app focus and config activation:

```text
OnFocusWindowChanged to game window type: AppID 1642040
Queueing activation for controller: 0 app: 1642040
Controller 0 mapping uses xinput : false
Loaded Config ... controller_base/empty.vdf
```

Y700 capture while BzzzController vibration was enabled:

```text
No new HID OUT
No HID rumble event
No BLE write
```

A direct Windows XInput probe also returned:

```text
XInput index 0..3 -> 1167 ERROR_DEVICE_NOT_CONNECTED
```

Interpretation:

```text
BzzzController is a valid runtime app, but in the current Steam controller configuration it does not drive the Y700 Nintendo HID output path.
The likely cause is that AppID 1642040 is using an empty/no-XInput controller mapping rather than a normal gamepad/Steam Input mapping.
```

Next test:

```text
Set BzzzController's Steam controller override/layout to enable Steam Input and use a normal Gamepad template.
Then repeat the Y700 HID OUT capture while BzzzController vibrates.
```

Follow-up after changing the BzzzController Steam controller layout:

```text
Selected layout: controller_switch_pro_gamepad_flickstick.vdf
Steam log: Controller 0 uses xinput : true
Steam log: Controller HA2F83JF selected config for AppID 1642040
```

Result:

```text
Steam/BzzzController now emits continuous HID OUT reports to the Y700 Nintendo HID gadget.
The responder sees the real runtime rumble path, not just a local probe.
```

Representative runtime HID OUT frame:

```text
02 5e 87 05 25 11 44 00 00 00 00 00 00 00 00 00
00 5e 87 05 25 11 44 00 00 00 00 00 00 00 00 00 ...
```

Observed pattern:

```text
Report ID: 02
Sequence byte: 50..5f cycling
Left/right haptic blocks appear mirrored:
  87 05 25 11 44
  87 05 25 11 44
```

BLE bridge output was also observed during the same runtime test:

```text
0A910102000800000400000000000000 -> ACK
0A910102000800000600000000000000 -> ACK
0A910102000800000700000000000000 -> ACK
0A910102000800000200000000000000 -> ACK
0A910102000800000000000000000000 -> ACK
```

Interpretation:

```text
This proves the practical runtime chain:

BzzzController / Steam runtime rumble
-> Steam Nintendo HID OUT report
-> Y700 FunctionFS/HID responder
-> BLE bridge write characteristic 649d4ac9-8eb7-4e6c-af44-1ea54fe5f005
-> Switch 2 Pro preset command ACK

The remaining work is no longer discovery of whether Steam can send rumble.
It is translation quality: classify Steam's continuous HD-rumble-style frames and map them to Pro2 BLE presets with usable timing.
```

Current test-mode mapper behavior after the continuous-rumble finding:

```text
RichCycle mode keeps its edge-triggered first preset, then repeats the next preset every about 700 ms while Steam keeps sending active rumble frames.
This is intentionally a test mapper, so BzzzController can expose the available Pro2 haptic/sound effects during a sustained rumble source.

Normal mode remains conservative:
short pulse -> alternating preset 1/2
double pulse -> preset 5
long pulse -> preset 6
sustained active rumble -> repeat preset 5/6 about every 800 ms
stop -> preset 0
```

## 2026-05-21 BLE HD Rumble Candidate Test

The BLE haptic path was tested with the real Switch 2 Pro controller connected over GATT. A known preset command was used as a positive control:

```text
0A910102000800000100000000000000
```

Result:

```text
Physical short vibration confirmed by user.
BLE ACK observed: 0A01010210780000
```

Then several HD-rumble-like candidates were written to the same BLE command characteristic `649d4ac9-8eb7-4e6c-af44-1ea54fe5f005`:

| Candidate | Payload | Result |
| --- | --- | --- |
| `env4` | `0A910102000800008715275187152751` | ACK, no vibration or sound |
| `env5` | `0A910102000800008715275171871527` | ACK, no vibration or sound |
| `raw10` | `10508715275187152751` | ACK, no vibration or sound |
| `raw64` | `025087152751710000...` direct 64-byte HID-like frame | ACK, no vibration or sound |

Interpretation:

```text
The controller accepts/ACKs several byte shapes, but the tested HD-rumble-like payloads do not trigger physical output.
The currently confirmed physical haptic path is the Pro2 BLE preset/effect command family.
```

2026-05-22 follow-up probing tested the same raw-HD shapes against every currently known writable characteristic, using a positive-control preset between grouped tests:

| Target alias | UUID | Tested | User-observed result |
| --- | --- | --- | --- |
| `649d` / `cmd` | `649d4ac9-8eb7-4e6c-af44-1ea54fe5f005` | `raw64-mid`, `raw10-mid`, `env4`, `env5` | no feedback except preset positive control |
| `3dac` | `3dacbc7e-6955-40b5-8eaf-6f9809e8b379` | `raw10-mid`, `raw64-mid`, `env4` | none |
| `4147` | `4147423d-fdae-4df7-a4f7-d23e5df59f8d` | `raw10-mid`, `raw64-mid`, `env4`, `raw64-full` | none |
| `fdf` | `ab7de9be-89fe-49ad-828f-118f09df7fdf` | `raw10-mid`, `raw64-mid`, `env4` | none |
| `cc48` | `cc483f51-9258-427d-a939-630c31f72b05` | `raw10-mid`, `raw64-mid`, `env4`, `env5` | none |

Updated conclusion:

```text
Plain USB/Switch-Pro-style raw rumble frames are not enough to drive Switch 2 Pro BLE haptics through the exposed write characteristics.
ACK or Android GATT status=0 only proves the write was accepted at the transport layer.
The remaining routes are either a preset-based translation layer or capturing the real Switch 2 BLE haptic protocol.
```

This does not absolutely prove that BLE HD rumble is impossible, but it makes the direct-pass-through route unlikely with the packet shapes tested so far. The practical next route is to translate Steam/Switch-Pro-style rumble events into Pro2 BLE presets.

Recommended translation model:

```text
Steam HID OUT active frame + timing
-> classify intensity/duration/pulse pattern
-> send one or more Pro2 BLE preset commands
-> stop with preset 00
```

## 2026-05-21 USB HID Rumble Bridge Test

The Nintendo USB gadget was redeployed and Windows enumerated the Y700 as:

```text
USB\VID_057E&PID_2069
Nintendo Switch 2 bulk
ADB Interface
HID interface
```

The local HID haptic probe successfully wrote 64-byte Switch2-style output reports to the HID interface. The Y700 responder saw these frames:

```text
active: 87 15 27 51 71 / 87 15 27 51 71
stop:   87 01 20 11 00 / 87 01 20 11 00
```

Confirmed bridge behavior:

| Probe pattern | Responder classification | BLE preset command |
| --- | --- | --- |
| Single short pulse, about 160 ms | `kind=short` | `preset=1` or `preset=2` alternating |
| Second short pulse within about 200 ms | `kind=double` | `preset=5` |
| Sustained pulse past about 360 ms | `kind=long` | `preset=6` |
| Stop frame | stop | `preset=0` |

All tested BLE preset writes were ACKed by the controller. This proves the complete software path:

```text
Windows HID OUT -> /dev/hidg0 -> Switch2FfsResponder -> switch2_ble_write.txt -> BLE bridge -> Pro2 controller
```

Current responder thresholds:

```text
double pulse window: 260 ms
long pulse threshold: 360 ms
```

Next validation step:

```text
Use Steam's own controller rumble test or an actual game rumble event.
Capture the responder log around that click.
Classify Steam's real HID OUT timing against the table above.
Tune preset choices and thresholds from the physical feel.
```
