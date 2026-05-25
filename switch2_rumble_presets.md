# Switch 2 BLE Rumble Presets

These notes are from a controlled sweep of BLE commands:

```text
cmd 0a91010200080000XX00000000000000
```

Each preset was played briefly, then stopped with:

```text
cmd 0a910102000800000000000000000000
```

## Observed Presets

| Preset | Hex | User-observed effect |
| --- | --- | --- |
| 1 | `01` | 短震 + 低音 |
| 2 | `02` | 短震 + 中音 |
| 3 | `03` | Physical response confirmed in 00..91 sweep; detailed effect needs relabeling |
| 4 | `04` | 滴滴声, 类似 Switch 手柄接入提示, 无震动 |
| 5 | `05` | 双震 |
| 6 | `06` | 滴声/中音 + 震动 |
| 7 | `07` | 滴声/高音 |
| 8..91 | `08`..`91` | No physical response in guided full sweep |

## 2026-05-23 Full Preset-ID Sweep

Byte 8 of the confirmed command family was swept from `00` through `91`:

```text
0A 91 01 02 00 08 00 00 XX 00 00 00 00 00 00 00
```

Safe timing was used for every case:

```text
stop -> wait 500 ms -> test XX -> observe 2000 ms -> stop -> cooldown 500 ms
```

Machine result:

```text
146/146 values returned the same ACK: 0A01010210780000
No disconnects or script errors.
No telemetry/extra notify distinguishing active presets from inactive IDs.
```

User-observed physical result:

```text
00 = stop
01..07 = physical feedback
08..91 = no physical feedback
```

Run artifacts:

```text
logs/preset_fuzz_20260523_001751/events.jsonl
logs/preset_fuzz_20260523_001751/machine_summary.csv
logs/preset_fuzz_20260523_001751/human_labeled_summary.csv
logs/preset_fuzz_20260523_001751/human_labels.json
```

## 2026-05-23 Payload Byte Follow-Up

Byte 9 of the same command family was swept independently from `00` through
`91`:

```text
0A 91 01 02 00 08 00 00 00 XX 00 00 00 00 00 00
```

Machine result:

```text
146/146 values returned the same ACK: 0A01010210780000
No disconnects or script errors.
No telemetry/extra notify differences were recorded.
```

The machine artifacts are:

```text
logs/preset_fuzz_20260523_003753/events.jsonl
logs/preset_fuzz_20260523_003753/machine_summary.csv
logs/preset_fuzz_20260523_003753/machine_summary.json
```

## 2026-05-23 Command Header Boundary

Blindly sweeping byte 0 is materially different from sweeping preset or
zero-padded payload bytes. The unattended `-FirstTen` run stopped at:

```text
03 91 01 02 00 08 00 00 00 00 00 00 00 00 00 00
```

The BLE bridge recorded:

```text
connection state status=8 newState=0
BLE write skipped, no current GATT
```

The same run recorded distinct ACKs before the disconnect:

| Byte 0 value | Test command prefix | Observed ACK |
| --- | --- | --- |
| `00` | `00 91 ...` | `0000010210780000` |
| `01` | `01 91 ...` | `0104010210780000` |
| `02` | `02 91 ...` | No ACK for the test command before stop |
| `03` | `03 91 ...` | Disconnect before ACK |

Run artifacts:

```text
logs/preset_fuzz_20260523_004949/events.jsonl
logs/preset_fuzz_20260523_004949/machine_summary.csv
logs/preset_fuzz_20260523_004949/machine_summary.json
```

Interpretation: byte 0 behaves like a command selector, not a haptic-effect
parameter. Header experiments should be command-specific and based on known
argument layouts instead of a long blind sweep.

The BLE bridge already has a command-family baseline from the working init
sequence:

| Name | Command |
| --- | --- |
| `INIT` | `03 91 01 0D 00 08 00 00 01 00 FF FF FF FF FF FF` |
| `VIBRATE_CFG` | `0A 91 01 08 00 14 00 00 01 FF FF FF FF FF FF FF FF 35 00 46 00 00 00 00 00 00 00 00` |
| `RUMBLE_ENABLE` | `01 91 01 01 00 04 00 00 00 00 00 00` |
| Confirmed preset/effect | `0A 91 01 02 00 08 00 00 XX 00 00 00 00 00 00 00` |

That baseline explains why a malformed `03 91 01 02 ...` test command is not
equivalent to a harmless preset variation. The next command exploration should
keep a valid command family shape fixed, then vary candidate payload fields
inside that family.

## Queued Active-Preset Payload Sweep

After the malformed command-header run left the Pro2 rejecting unattended LE
connections, a Y700-local waiter was armed on 2026-05-23:

```text
/data/local/tmp/sweep_switch2_preset_payload_local.sh
/data/local/tmp/switch2_payload_sweep_20260523_014849
```

The waiter does not send test commands until the BLE bridge logs its
post-init notification marker again. Once that happens it keeps the confirmed
preset command family and active `preset=01` fixed:

```text
0A 91 01 02 00 08 00 00 01 P1 P2 P3 P4 P5 P6 P7
```

It then varies payload bytes 9..15 independently over `00..91`, with the same
safe per-case cadence:

```text
stop -> wait 500 ms -> test -> observe 2000 ms -> stop -> cooldown 500 ms
```

Each case is appended to the local `events.tsv` before it is sent and inserts
markers into the BLE bridge/raw logs. If a case causes a new disconnect warning,
the local sweep records that case in `status.txt` and stops instead of filling
the log with offline writes.

On 2026-05-23 07:52, after the user reconnected the Pro2, a fresh local sweep
started successfully:

```text
/data/local/tmp/switch2_payload_sweep_20260523_075257
```

This run is sweeping `preset=01` with bytes 9..15 varied over `00..91`.
A queue manager was also armed:

```text
/data/local/tmp/queue_switch2_payload_sweeps_local.sh
/data/local/tmp/switch2_payload_sweep_queue.log
```

The queue waits for the current `preset=01` run to finish, then runs the same
payload sweep for active presets `02..07`.

Human observations during this long run are recorded in
`switch2_payload_human_observations.md`. The first useful observation is around
`2026-05-23T08:05:07+08:00`, near `case=225 byte_index0=10 value=4E`, where the
user reported repeated multi-segment short pulses: same-feeling short pulse
duration, several pulses in sequence, not one sustained vibration.

The user later clarified at `2026-05-23T08:42:25+08:00` that the same-frequency,
same-feel repeated short pulses had continued from that first report point all
the way to the later observation. ADB was recovered at `08:43:22`, when the
sweep was already near `case=924 byte_index0=15 value=2F`; the run was killed
after `case=935 byte_index0=15 value=3A` and three stop commands were sent. The
pulled snapshot `logs/payload_sweep_pull_20260523_084435` summarized `935/935`
ACKed cases, `0` disconnects, and the same ACK `0A01010210780000`.

Important correction: this long same-feel vibration period is most likely not
evidence of a latched/self-running haptic mode by itself. From `case=225`
through `case=935`, all `711` test commands still kept the active preset byte
fixed at `01`; only the trailing payload byte under test changed. The sweep was
therefore re-triggering known short-vibration preset `01` roughly every 3-4
seconds by design.

The next physical-listening test must avoid this confound. Use
`run_switch2_payload_ab_test.ps1`, which spaces cases with silence windows and
compares:

```text
stop-only control
preset 01 baseline: 0A910102000800000100000000000000
preset 01 B10=4E:  0A9101020008000001004E0000000000
preset 01 B10=4F:  0A9101020008000001004F0000000000
preset 01 B15=3A:  0A91010200080000010000000000003A
preset 00 B10=4E:  0A9101020008000000004E0000000000
```

Until that sparse A/B run says otherwise, treat the `00..91` trailing payload
byte sweeps as protocol-accepted but not proven to change the physical haptic
effect.

## Current Interpretation

Steam's HID rumble output observed through the Y700 currently carries one active frame and one stop frame:

| State | HID rumble frame |
| --- | --- |
| Active | `87 89 23 91 38` |
| Stop | `87 01 20 11 00` |

The rich effects are produced by mapping each active rumble event to one of the BLE presets above. The responder now logs rumble event starts and stops with the selected preset and duration, so future captures can distinguish short, long, and repeated game rumble events more cleanly.

Default gameplay mapping currently uses only vibration-capable presets:

```text
1 -> 2 -> 5 -> 6 -> repeat
```

Presets `4` and `7` are audible cue presets, so they are kept out of the default game-rumble loop. Preset `3` needs a more careful manual relabel after the full sweep confirmed that `01..07` all have some physical response. Presets `08..91` ACK over BLE but had no physical effect in the guided full sweep.

The temporary LED/player-command-to-rumble bridge is disabled unless this runtime flag exists:

```text
/data/local/tmp/switch2_bridge_led_rumble
```

This keeps real game rumble clean, but allows Steam's settings-page vibration/identify button to produce a compatibility pulse when needed.
