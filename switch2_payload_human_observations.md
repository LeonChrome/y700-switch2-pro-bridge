# Switch 2 Payload Sweep Human Observations

Manual observations are approximate because the unattended sweep sends one case
every few seconds. Use the case marker and timestamp as the primary anchor, then
inspect neighboring cases if the physical effect started slightly before or
after the note.

| Local time | Y700 time | Run | Nearest case | Command | Observation | Confidence |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-05-23T08:05:07+08:00 | 2026-05-23T08:05:08+0800 | `/data/local/tmp/switch2_payload_sweep_20260523_075257` | `case=225 byte_index0=10 value=4E marker=LOCAL_PAYLOAD_CASE_00225_B10_V4e_1779494706` | `0A9101020008000001004E0000000000` | User reported repeated multi-segment short pulses. Each pulse felt like the same short duration, but the effect was several short vibrations in sequence, not one sustained vibration. Check neighboring cases `224..226` if exact onset is off by one case. | Medium |
| 2026-05-23T08:42:25+08:00 | 2026-05-23T08:43:22+0800 when ADB was recovered | `/data/local/tmp/switch2_payload_sweep_20260523_075257` | User report happened while the sweep was around `case=907 byte_index0=15 value=1E`; first ADB recovery snapshot was around `case=924 byte_index0=15 value=2F`; script was killed after `case=935 byte_index0=15 value=3A` | From `case=225` through `case=935`, every test command still had active preset byte `01`; only trailing payload bytes changed | User clarified that the same-frequency, same-feel repeated short pulses continued from the first report point through this later time. Log analysis shows this was probably the sweep repeatedly re-triggering fixed `preset=01` every ~3-4 seconds while varying trailing payload bytes, not necessarily a latched/self-running effect. The sweep and queued follow-up runs were killed, and three stop commands were sent. | High |
