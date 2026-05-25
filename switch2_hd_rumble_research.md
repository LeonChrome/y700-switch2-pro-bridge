# Switch 2 Pro HD Rumble Research Notes

Date: 2026-05-22

## Current Conclusion

The preset bridge is only a proof of life. It is not HD rumble quality.

Known working chain:

```text
Steam runtime rumble
-> Y700 Nintendo HID OUT
-> responder
-> BLE preset command
-> Switch 2 Pro physical feedback
```

But the last step currently collapses a continuous rumble stream into preset IDs.
That loses texture, frequency, envelope, and fine intensity changes.

## BzzzController Capture

With `LogOnly` enabled, the bridge suppresses all non-stop BLE preset writes and only logs Steam HID OUT traffic. This prevents our own preset loop from being mistaken for HD rumble.

Observed Bzzz/Steam frames were repeated fixed rumble blocks, not rich waveform data:

```text
02 5x 87 4d 23 11 36 ... 5x 87 4d 23 11 36 ...
decoded: high amplitude about 47%, low amplitude about 48%
```

An earlier stronger sample was also a repeated fixed block:

```text
02 5x 87 15 27 51 71 ... 5x 87 15 27 51 71 ...
decoded: high amplitude about 100%, low amplitude about 100%
```

Confirmed again from a live BzzzController run at 2026-05-22 22:27:

```text
HID OUT repeated every ~10-15 ms
frame: 87 15 27 51 71 / 87 15 27 51 71
decoded: left{hf=0x187 ha=28992/100 lf=0x112 la=28992/100}
decoded: right{hf=0x187 ha=28992/100 lf=0x112 la=28992/100}
```

Only the sequence nibble changed. The rumble payload itself did not change until Steam sent the neutral stop block:

```text
87 01 20 11 00 / 87 01 20 11 00
```

So BzzzController is useful to prove that Steam can send rumble to the virtual Nintendo controller, but it is not a good HD rumble sample source. The rich sound/vibration cycle heard during earlier tests was produced by our temporary BLE preset bridge, not by varied Bzzz HD data.

## Public Evidence

Nintendo publicly lists the Switch 2 Pro Controller as supporting `HD Rumble 2`:

```text
https://www.nintendo.com/us/store/products/nintendo-switch-2-pro-controller-123674/
```

SwitchBrew's Switch 2 Pro page says the protocol is expected to be extremely similar to Switch 1, with one difference that wired USB uses a bulk OUT endpoint on interface 1:

```text
https://switchbrew.org/wiki/Switch_2:_Pro_Controller
```

SDL added preliminary Switch 2 rumble support in 2025. The current public implementation converts normal low/high rumble magnitudes into the Switch 2 HID OUT frame:

```text
02 5x 87 HH HH LL LL ... 5x 87 HH HH LL LL ...
```

Useful source:

```text
https://discourse.libsdl.org/t/sdl-switch2-preliminary-rumble-support/64164
```

The SDL patch is explicit that the scaling/frequency choices are somewhat arbitrary. This is important: Steam/SDL's ordinary rumble path is probably not full Nintendo-authored HD rumble data.

Switch 1 HD rumble is documented by reverse-engineering and by the Linux `hid-nintendo` driver as frequency/amplitude pairs encoded into 4-byte packets per actuator.

Linux driver source:

```text
https://codebrowser.dev/linux/linux/drivers/hid/hid-nintendo.c.html
```

Reverse-engineered frequency/amplitude tables:

```text
https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/rumble_data_table.md
```

SwitchBrew also documents Nintendo vibration pack formats and BNVIB files:

```text
https://switchbrew.org/w/index.php?mobileaction=toggle_view_desktop&title=Joy-Con
https://switchbrew.org/wiki/BNVIB
```

Steam Input exposes two relevant public concepts:

```text
TriggerVibration(leftSpeed, rightSpeed) -> traditional rumble only
TriggerRepeatedHapticPulse(...) -> pulse texture, supported for Nintendo Switch Pro Controller
```

Source:

```text
https://partner.steamgames.com/doc/api/ISteamInput
```

## What This Means For This Project

There are three different haptic layers:

```text
Layer A: Game/Steam traditional rumble
  left/right 16-bit intensity, coarse.

Layer B: Steam/SDL Switch 2 HID OUT
  packet 02 5x 87 ... carries a transformed low/high magnitude stream.
  This is what the Y700 currently receives.

Layer C: Real Switch 2 Pro BLE haptics
  confirmed preset command:
  0A91010200080000XX00000000000000
  HD raw command is not yet identified.
```

Full HD replication requires Layer C, not only Layer B.

## Practical Routes

### Route 1: Decode Steam/SDL HID OUT More Faithfully

Implement an inverse decoder for observed Switch 2 HID OUT frames:

```text
high_amp ~= bytes 3-4
low_amp  ~= bytes 5-6
sequence = byte 1 & 0x0f
left/right block duplicated for Pro controller
```

Then synthesize smoother BLE output instead of cycling presets.

Limitation:

```text
If Steam only gives traditional rumble, this can improve intensity and timing,
but it cannot recover true Nintendo-authored frequency texture that was never sent.
```

### Route 2: Find The Real Switch 2 Pro BLE HD Command

This is the best route.

Needed experiments:

```text
1. Capture real Switch 2 console -> Pro2 BLE haptic traffic during HD rumble.
2. Or systematically probe all known BLE write characteristics with wrapped USB HID OUT forms.
3. Compare ACK, notify/status side effects, and physical feedback.
4. Once the raw HD BLE command is found, forward or synthesize HD frames directly.
```

Current known BLE write characteristic:

```text
649d4ac9-8eb7-4e6c-af44-1ea54fe5f005
```

Known preset command family:

```text
0A91010200080000XX00000000000000
```

Previously tested direct HD-like shapes ACKed but did not vibrate, so the raw HD BLE path is probably not a plain `02 5x ...` HID frame written to that characteristic.

2026-05-22 guided raw-HD probing expanded this negative result across the currently known write characteristics. The only physical feedback in the session was the positive-control preset command. All of the following wrote successfully at the Android GATT layer, but produced no user-observed vibration or sound:

| Target | Tested shapes | Physical result |
| --- | --- | --- |
| `649d` / `cmd` | `raw64-mid`, `raw10-mid`, `env4`, `env5` | none |
| `3dac` | `raw10-mid`, `raw64-mid`, `env4` | none |
| `4147` | `raw10-mid`, `raw64-mid`, `env4`, `raw64-full` | none |
| `fdf` | `raw10-mid`, `raw64-mid`, `env4` | none |
| `cc48` | `raw10-mid`, `raw64-mid`, `env4`, `env5` | none |

Working interpretation:

```text
GATT write success / ACK is not the same as haptic execution.
The exposed write characteristics do not appear to accept plain USB/Switch-Pro-style raw HD rumble frames in the current paired BLE mode.
The real raw-HD path likely needs an additional command wrapper, session state, handshake, checksum/encryption, or traffic captured from a real Switch 2 host.
```

### Route 3: Use Switch 1 HD Rumble Encoder As A Synthesizer

If the Pro2 BLE HD command can accept Switch 1-style AM/FM data, use the known Switch 1 encoder:

```text
low frequency range:  41..626 Hz
high frequency range: 82..1253 Hz
amplitude range:      0..1-ish
packet: 4 bytes per actuator
```

This could create much better haptics than presets.

Blocking issue:

```text
We still need the correct Switch 2 Pro BLE command wrapper.
```

## Next Recommended Work

1. Replace preset-only mapping with a HID OUT decoder/logger that prints decoded high/low magnitudes.
2. Build a controlled Steam rumble source that sweeps left/right intensities, pulse widths, and repeated haptic pulse timing.
3. Probe BLE write characteristics for the raw HD wrapper using known positive control and very short low-amplitude signals.
4. If no BLE raw HD path is found, document the ceiling clearly: Steam-to-Pro2 over BLE can be made responsive, but not true HD.

## Working Assessment

Can we make the current feedback less crude?

```text
Yes.
```

Can we fully reproduce Switch 2 HD Rumble 2 today with only the currently known BLE preset command?

```text
No.
```

Can it become possible?

```text
Yes, if we identify the raw BLE HD haptic command/wrapper or capture it from a real Switch 2 session.
```

## Tooling Added

Responder logs now decode Nintendo HID OUT rumble blocks as:

```text
hf = high-frequency encoded value
ha = high-frequency amplitude, normalized against SDL's current 29000 scale
lf = low-frequency encoded value
la = low-frequency amplitude, normalized against SDL's current 29000 scale
```

Offline parser:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Decode-Switch2HidRumble.ps1 -Hex "02 50 87 05 25 11 44 ..."
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Decode-Switch2HidRumble.ps1 -Path .\some_hid_output.log -Csv
```

Controlled local HID intensity probes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticProbe.ps1 -LowSpeed 32768 -HighSpeed 32768 -PulseMs 220
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticSweep.ps1 -Channel Both
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticSweep.ps1 -Channel LowOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticSweep.ps1 -Channel HighOnly
```

BLE raw-HD candidate script:

```sh
sh /data/local/tmp/test_switch2_ble_hd_rumble_candidates.sh preset 0.4 cmd
sh /data/local/tmp/test_switch2_ble_hd_rumble_candidates.sh raw64-mid 0.25 cmd
sh /data/local/tmp/test_switch2_ble_hd_rumble_candidates.sh raw64-bzzz 0.25 3dac
```

Do not run broad target sweeps blindly. Use a positive control first, then one candidate/target pair at a time, and stop immediately if input notifications stop or the controller enters an LED-only/abnormal state.

For capture-only analysis, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode.ps1 -Mode LogOnly
```

This keeps decoded HID OUT logging active but suppresses non-stop BLE preset writes, so Steam/Bzzz/game rumble can be studied without the crude preset bridge masking the physical result.
