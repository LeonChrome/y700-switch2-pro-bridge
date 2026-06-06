# V5.5 Phase 3 DualSense Audio Endpoint

Date: 2026-06-06

Phase 3 adds a minimal USB Audio render endpoint to the standalone
`dualsense_identity_experiment` firmware, so Windows can enumerate the ESP32-S3
as a DualSense-like HID + audio composite device.

This phase does not change the V5.2 Pure Pro2 / VIIPER route, does not change
the V5.0/V5.1 GUI, and does not enable live Pro2 raw02 forwarding by default.

Current descriptor shape:

```text
USB identity: VID 054C / PID 0CE6
Audio Control interface: 0
Audio Streaming OUT interface: 1
HID interface: 2
Audio format: 4 channels, 48 kHz, signed 16-bit PCM
Audio OUT endpoint: 0x02 isochronous adaptive
HID IN endpoint: 0x81 interrupt
HID OUT endpoint: 0x01 interrupt
```

The firmware reads host OUT audio packets. Speaker channels 0/1 are ignored for
now; haptic channels 2/3 are summarized into:

```text
rms_l / rms_r
peak_l / peak_r
activity
transient
frame_count
overrun_count
```

`haptic_audio_to_raw02` is dry-run only. It converts haptic audio features into
candidate Pro2 `Left[16] + Right[16]` payload logs and never sends them to the
real controller in this phase.

Expected firmware logs:

```text
[DS5_AUDIO] enabled=true sample_rate=48000 channels=4 bytes_per_sample=2
[DS5_AUDIO] mounted=true interface=1 alt=1 sample_rate=48000 channels=4
[DS5_AUDIO] sample_rate=48000 channels=4 out_packet len=... ch2_peak=... ch3_peak=... activity=true/false
[DS5_HAPTIC_AUDIO] frames=... rms_l=... rms_r=... peak_l=... peak_r=... activity=true transient=...
[HAPTIC_TO_RAW02] dry_run=true template=... intensity_l=... intensity_r=... left=... right=...
```

Validation commands from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build_v5_5_dualsense_identity.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_v5_5_dualsense_identity.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_identity.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_input.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_reports.ps1 -Seconds 6
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_v5_5_dualsense_audio.ps1
```

If `check_v5_5_dualsense_audio.ps1` does not find an endpoint, it exits 0 and
prints a manual `mmsys.cpl` check hint.

Success criteria:

```text
Windows HID identity remains 054C:0CE6
input report remains 0x01 + 63 bytes around 250 Hz
real Pro2 input still drives DualSense input
ordinary HID output 0x02 rumble compatibility still works
Windows shows an audio render endpoint
firmware logs audio OUT activity or at least audio interface alt setting
USB does not disconnect
```

Failure signals:

```text
HID disappears: composite descriptor or interface ordering is wrong
audio endpoint missing: UAC2 descriptor, class config, or string/interface shape needs adjustment
unknown USB device: descriptor length or endpoint attributes are wrong
input rate drops badly: USB/BLE/audio scheduling needs throttling
ordinary rumble stops working: HID OUT path was disturbed and must be restored
```

Current limits:

- No speaker playback.
- No microphone/audio IN endpoint.
- No complete DualSense haptic audio reproduction.
- No live Pro2 raw02 forwarding.
- raw02 dry-run payloads are safe approximation templates, not final HD Rumble
  2 reproduction.
