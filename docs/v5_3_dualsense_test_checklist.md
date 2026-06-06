# V5.3 DualSense Test Checklist

Date: 2026-06-06

## Scope

This checklist is for V5.3 research only. It does not gate V5.2 Pro2 HD rumble
and does not imply DualSense haptics are already supported.

## Required Hardware

- Real DualSense controller.
- USB-C data cable. Charge-only cables are not valid for HID/audio tests.
- Optional Bluetooth connection for comparison.
- Optional Pico 2 W / DS5Dongle route for later study.

## Step 1: USB DualSense Detection

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_dualsense_env.ps1
```

Expected:

```text
[DUALSENSE_ENV] hid_usb=true
[DUALSENSE_ENV] hid_bluetooth=false/true
[DUALSENSE_ENV] vid=054C
[DUALSENSE_ENV] pid=0CE6 or 0DF2
[DUALSENSE_ENV] audio_endpoint=<DualSense or Wireless Controller endpoint>
[DUALSENSE_ENV] wasapi_loopback=true
[DUALSENSE_ENV] steam_running=true/false
```

Blocked result on the current machine:

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

## Step 2: HID Output Probe

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1
```

Goals:

- enumerate the real DualSense HID device,
- capture output report shape,
- test lightbar,
- test adaptive trigger report paths,
- test ordinary rumble,
- record whether output reports are USB or Bluetooth shaped.
- print `[DUALSENSE_HID]` device/caps lines.
- print `[DUALSENSE_OUTPUT]` output report placeholders or captured reports.
- print `[DUALSENSE_TRIGGER]` trigger support/classification.

Stop if the controller disconnects or the output report causes repeated
unexpected state changes.

## Step 3: Haptic Audio Probe

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1
```

Goals:

- enumerate the DualSense audio endpoint,
- confirm WASAPI loopback availability,
- capture channel count,
- capture sample rate,
- capture RMS/peak activity,
- detect whether haptic audio activity changes during a native haptic scene.

If no DualSense audio endpoint exists, do not claim haptic audio support.

Expected fields:

```text
[DUALSENSE_AUDIO] device=...
[HAPTIC_AUDIO] channels=...
[HAPTIC_AUDIO] sample_rate=...
[HAPTIC_AUDIO] rms_ch0=...
[HAPTIC_AUDIO] peak_ch0=...
[HAPTIC_AUDIO] activity=true/false
```

## Step 4: Native PC Game Test

Use a PC game or scene known to support native DualSense features. Prefer games
already owned and installed. Do not buy a game solely for this checklist until
the USB HID/audio endpoint checks pass.

Candidate PC tests to verify:

- Returnal.
- Ratchet & Clank: Rift Apart.
- Marvel's Spider-Man Remastered or Miles Morales.
- Death Stranding Director's Cut.
- The Last of Us Part I.
- Horizon Forbidden West Complete Edition.
- Ghost of Tsushima Director's Cut.

Test rules:

- USB is preferred over Bluetooth for the first pass.
- Steam Input may need to be disabled for the game so native DualSense features
  reach the controller.
- Astro is not a useful PC test target.
- Log whether adaptive triggers work separately from grip haptics.
- Log whether haptic audio endpoint activity appears during the scene.

## Step 5: Result Matrix

Record:

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

## V5.3 Decision Gate

Proceed only if:

- a real DualSense is present,
- USB HID output probe is runnable,
- an audio endpoint is detected,
- at least one native PC game produces measurable haptic audio or a clearly
  identifiable output path.

Stay blocked if:

- no real DualSense is attached,
- no DualSense audio endpoint appears,
- only ordinary HID rumble works,
- results depend on Steam Input emulation instead of native DualSense behavior.

## Next Required Hardware

```text
next_required_hardware=real DualSense + USB-C data cable
optional_hardware=Pico 2 W / DS5Dongle route
```
