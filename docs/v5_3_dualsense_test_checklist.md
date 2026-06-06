# V5.3 DualSense Real-Device Test Checklist

Date: 2026-06-06

## Scope

This checklist is the V5.3 real-device entry point. It does not gate V5.2 Pro2
HD rumble and does not imply DualSense haptics are already supported.

## Required Hardware

- Real DualSense controller.
- USB-C data cable. Charge-only cables are not valid.
- Windows PC.
- Optional Bluetooth comparison after USB is understood.

## Step 1: Plug In DualSense

Start with USB, not Bluetooth.

Rules:

- Use a USB-C data cable.
- Do not open a game yet.
- Do not start with Bluetooth.
- First confirm Windows exposes both HID and, if available, a DualSense /
  Wireless Controller audio endpoint.
- If the controller appears only as a generic Bluetooth gamepad, switch back to
  USB before continuing.

## Step 2: Environment Check

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_dualsense_env.ps1
```

Expected fields:

```text
[DUALSENSE_ENV] hid_usb=true
[DUALSENSE_ENV] vid=054C
[DUALSENSE_ENV] pid=0CE6 or 0DF2
[DUALSENSE_ENV] audio_endpoint=<DualSense or Wireless Controller endpoint>
[DUALSENSE_ENV] wasapi_loopback=true
```

Steam is only a state field here:

```text
[DUALSENSE_ENV] steam_running=true/false
```

If the audio endpoint does not appear:

```text
[DUALSENSE_BLOCKED] reason=no_dualsense_audio_endpoint
```

In that state, ordinary HID output or adaptive trigger research may still be
possible, but DualSense advanced haptic audio cannot be proven on this machine
yet.

Current blocked result on this machine:

```text
hid_usb=false
hid_bluetooth=false
vid=not_found
pid=not_found
real_dualsense=false
audio_endpoint=not_found
wasapi_loopback=false
[DUALSENSE_BLOCKED] reason=no_real_dualsense
```

## Step 3: HID Output Probe

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
```

Goals:

- enumerate the real DualSense HID device,
- capture output report size and transport shape,
- identify possible lightbar output,
- identify ordinary rumble output,
- identify adaptive trigger output,
- identify any possible haptic control report.

Expected log prefixes:

```text
[DUALSENSE_HID]
[DUALSENSE_OUTPUT]
[DUALSENSE_TRIGGER]
[DUALSENSE_BLOCKED]
```

Stop if the controller disconnects or output causes repeated unexpected state
changes.

## Step 4: Haptic Audio Probe

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

Goals:

- find the DualSense audio endpoint,
- confirm WASAPI loopback availability,
- record channels,
- record sample rate,
- record RMS/peak,
- report `activity=true/false`.

Expected fields:

```text
[DUALSENSE_AUDIO] device=...
[DUALSENSE_AUDIO] endpoint_count=...
[DUALSENSE_AUDIO] wasapi_loopback=true/false
[HAPTIC_AUDIO] channels=...
[HAPTIC_AUDIO] sample_rate=...
[HAPTIC_AUDIO] rms_ch0=...
[HAPTIC_AUDIO] peak_ch0=...
[HAPTIC_AUDIO] activity=true/false
```

If no DualSense audio endpoint exists, do not claim advanced haptic audio
support.

## Step 5: Native Game Validation

Do not assume any game supports DualSense haptics. Use a PC game or scene known
to support native DualSense features.

Principles:

- Prefer USB for the first pass.
- Test Steam Input on and off.
- Native DualSense output may require Steam Input off.
- Record whether HID output changes.
- Record whether haptic audio activity changes.
- Record adaptive trigger behavior separately from grip haptics.
- Do not treat ordinary rumble as advanced haptic audio.

Record:

```text
game_name=<name>
connection=usb/bluetooth
steam_input=on/off
native_dualsense_mode=true/false
hid_output_changed=true/false
ordinary_rumble=true/false
adaptive_trigger=true/false
audio_endpoint=true/false
haptic_audio_activity=true/false
notes=<short result>
```

One-command capture entry:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_dualsense_capture.ps1 -DurationSeconds 90
```

This runner does not start a game automatically. It runs the environment check,
then starts HID output and haptic audio probes when a real DualSense is present.
Logs go to `logs\v5_3_dualsense\`.

Night-run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run_v5_3_night_probe.ps1
```

Without a real DualSense, both commands must report blocked and exit 0.

## V5.3 Next-Phase Decision

Case 1:

```text
hid_output=true
audio_endpoint=true
```

Enter V5.3 Phase 1: analyze haptic audio and correlate it with HID output.

Case 2:

```text
hid_output=true
audio_endpoint=false
```

Continue ordinary HID output / adaptive trigger research only. This cannot prove
advanced haptic audio.

Case 3:

```text
audio_endpoint=true
haptic_audio_activity=false
```

Try a different native DualSense-capable PC game or change Steam Input state.
The endpoint exists, but no useful haptic audio source was observed.

Case 4:

```text
hid_output=false
audio_endpoint=false
```

The device is not being recognized correctly. Check cable, Windows driver state,
USB vs Bluetooth connection, and whether the controller is truly a DualSense.

## Result Matrix

```text
real_dualsense_present=true/false
hid_usb=true/false
hid_bluetooth=true/false
vid=...
pid=...
audio_endpoint=true/false
wasapi_loopback=true/false
steam_running=true/false
ordinary_rumble=true/false
adaptive_trigger=true/false
haptic_audio_activity=true/false
game_name=<name>
steam_input=on/off
connection=usb/bluetooth
native_dualsense_mode=true/false
notes=<short result>
```

## Current Blocker

```text
current_blocker=no_real_dualsense
next_required_hardware=real DualSense + USB-C data cable
```
