# Y700 Switch 2 Pro Bridge

中文说明：

这个项目把一台已经 root 的联想 Y700 平板用作 Switch 2 Pro 手柄的 BLE 转 USB 桥接器。真实的 Switch 2 Pro 手柄通过蓝牙连接到 Y700，Y700 读取手柄的私有 BLE GATT 输入通知，然后在 USB 侧模拟一个 Nintendo 风格的 HID 设备，让 Windows / Steam 按 Nintendo 控制器路径识别它。

当前 v3 稳定版已经完成异机测试：把 release 文件夹复制到另一台 Windows 电脑后，可以用 `Y700Switch2Launcher.exe` 部署并启动 Y700 端桥接程序，按键转发和基础震动反馈均已验证。

English:

Use a rooted Lenovo Y700 as a BLE-to-USB bridge for a real Switch 2 Pro Controller. The controller connects to the Y700 over BLE; the Y700 exposes a Nintendo-style USB HID gadget to Windows and Steam.

Current stable release:

```text
release/v3-stable-20260525-input-rumble
```

The stable v3 package has been tested on another Windows PC with the generated `Y700Switch2Launcher.exe`.

快速上手 / Quickstart: [QUICKSTART.md](QUICKSTART.md)

## Acknowledgements

中文：

本项目受到 `switch2-controller-windows10-dual-layouts` 以及相关 Switch 2 手柄 Windows 兼容性研究的启发。这里的实现路线不同：不是让手柄直接通过 BLE 连到 Windows，而是使用 root 后的 Y700 作为中间桥，解析 Switch 2 Pro 手柄的私有 BLE GATT 数据，再通过 USB Gadget / FunctionFS 向 Windows 和 Steam 暴露 Nintendo 风格的 HID 设备。

English:

This project was inspired in part by `switch2-controller-windows10-dual-layouts` and the community research around Switch 2 controller layouts on Windows. The implementation here uses a different route: instead of connecting the controller directly to Windows over BLE, a rooted Lenovo Y700 parses the Switch 2 Pro Controller's private BLE GATT notifications and presents a Nintendo-style USB HID gadget to Windows and Steam.

完整致谢 / Full note: [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md)

## 当前能力

- Switch 2 Pro 手柄通过 BLE 连接到 Y700。
- Y700 读取手柄输入并写入 Android 本地状态文件。
- Y700 通过 USB Gadget / FunctionFS 暴露 Nintendo 风格 USB HID。
- Windows / Steam 可走 Nintendo 控制器识别路径。
- v3 已验证按键、方向键、肩键、摇杆、`C`、`GL`、`GR`。
- v3 已验证基础震动/HD rumble 转发路径。
- Windows 侧提供 `Y700Switch2Launcher.exe`，用于部署、启动、查看状态、测试震动、停止和拉取日志。

## 注意事项

- 需要 root 后的 Lenovo Y700。
- 建议使用无线 ADB 启动，因为重配 USB gadget 时可能会断开 USB ADB。
- 这是研究和实验项目，不是 Nintendo 官方驱动。
- 当前稳定包位于 `release/v3-stable-20260525-input-rumble`。

## 实验版 Release

当前建议的公开 release 是 `v0.1.0-experimental`。它不是成熟的一键安装包，建议用两个文件发布：

```text
Y700Switch2Launcher.exe
y700-switch2-y700-payload-v0.1.0-experimental.zip
```

不建议只发布单个 exe。当前 Windows 启动器可以把文件推送到 Y700，但它没有把 Y700/Android 端 jar 和 setup 脚本嵌入 exe 内部，所以单独一个 exe 并不能完整启动桥接流程。

使用时请把 payload zip 解压到和 `Y700Switch2Launcher.exe` 同一个文件夹，再运行 launcher。详细说明见 [RELEASE_NOTES_v0.1.0-experimental.md](RELEASE_NOTES_v0.1.0-experimental.md)。

## Project Notes

This folder contains a minimal USB HID gamepad setup for Lenovo Y700 2025.

## Current Stable Checkpoint: v3 Input + HD Rumble

Date: 2026-05-25

The current known-good baseline is documented in:

```text
STABLE_CHECKPOINT_20260525_V3_INPUT_RUMBLE.md
release/v3-stable-20260525-input-rumble/MANIFEST.md
```

Live user validation confirmed:

- Switch 2 Pro button forwarding is accurate.
- Steam/runtime rumble produces physical feedback.
- The deployed v3 jars match the local stable artifacts.

Do not overwrite the v3 jars or restart the USB gadget before saving a new checkpoint.

Target state:

- Gadget path: `/config/usb_gadget/g1`
- Function: `functions/hid.usb0`
- UDC: `a600000.dwc3`
- Device node: `/dev/hidg0`
- Report descriptor: standard generic desktop gamepad, no Report ID
- Input report length: 8 bytes

## Current Milestone: Steam Switch 2 Pro Input

Date: 2026-05-22

This project has passed the "name spoofing only" stage. Steam now treats the Y700 virtual USB device as a Nintendo Switch 2 Pro / Nintendo Switch Pro family controller at the layout level, and the real Switch 2 Pro BLE input is forwarded through the Y700 into Steam.

Confirmed by manual Steam controller testing:

- A/B/X/Y, D-pad, `+`, `-`, L/ZL/R/ZR, stick axes, and stick clicks respond.
- The Switch 2-only controls `C`, `GR`, and `GL` also appear in Steam and produce input.
- This is not just `joy.cpl` seeing a generic HID interface. Steam is opening the Nintendo path for `VID 057e / PID 2069` and interpreting the Switch 2 button layout.

The working route is:

```text
real Switch 2 Pro BLE notify
-> Y700 BLE bridge
-> /data/local/tmp/switch2_state.txt
-> Switch2FfsResponder wired Switch 2 USB packet
-> Windows / Steam Nintendo HID path
```

Key implementation choices that made this work:

- Use `/config/usb_gadget/g1` and keep Android's existing gadget alive while adding the Switch 2 HID/FunctionFS pieces.
- Expose Nintendo identity `VID 057e / PID 2069` with Nintendo-style strings.
- Keep the BLE notify button bytes in native order in the state file, then map them into Steam's wired Switch 2 USB offsets in the responder.
- Parse state-file button fields as hexadecimal so high-bit buttons do not turn into signed or decimal mistakes.
- Map Switch 2 extras as wired USB `C = data[6] bit 0x40`, `GR = data[8] bit 0x01`, `GL = data[8] bit 0x02`.

## Current Milestone: Steam Runtime Rumble Path

Date: 2026-05-22

Steam runtime rumble has now been observed from BzzzController after selecting a real Steam Input gamepad layout for AppID `1642040`.

Confirmed chain:

```text
BzzzController / Steam runtime rumble
-> continuous Nintendo HID OUT reports to Y700
-> Switch2FfsResponder haptic mapper
-> /data/local/tmp/switch2_ble_write.txt
-> BLE write characteristic 649d4ac9-8eb7-4e6c-af44-1ea54fe5f005
-> Switch 2 Pro preset command ACK
```

Representative Steam runtime HID OUT frames:

```text
02 5x 87 05 25 11 44 ... 5x 87 05 25 11 44 ...
02 5x 87 4d 23 11 36 ... 5x 87 4d 23 11 36 ...
02 5x 87 15 27 51 71 ... 5x 87 15 27 51 71 ...
```

Current interpretation: BzzzController is useful as a Steam rumble source, but it has only shown repeated fixed high/low rumble blocks so far. The 2026-05-22 22:27 `LogOnly` capture repeated `87 15 27 51 71` every ~10-15 ms, decoded as high 100% and low 100% on both sides. The rich multi-sound/multi-vibration feel heard in early tests came from the temporary Pro2 BLE preset cycle, not from Bzzz sending a full HD rumble waveform.

Raw BLE HD probing result: direct USB/Switch-Pro-style raw rumble frames were tested against `649d/cmd`, `3dac`, `4147`, `fdf`, and `cc48`. Writes succeeded, and `649d` sometimes ACKed, but none produced physical feedback. The only confirmed physical haptic path remains the Pro2 preset command family `0A91010200080000XX00000000000000`.

The current `RichCycle` test mapper repeats Pro2 preset effects about every 700 ms while Steam keeps sending active rumble, so sustained rumble sources can exercise the known preset/sound set. This is a test bridge, not the final HD-rumble-quality translator.

Default game mode keeps audible cues out of the sustained loop and repeats vibration-focused preset `5/6` about every 800 ms while Steam continues to send active rumble.

Haptic mode helper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode.ps1 -Mode Rich
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode.ps1 -Mode Game
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode.ps1 -Mode LogOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\set_switch2_haptic_mode.ps1 -Mode Stop
```

For clean capture, use `LogOnly` so Steam HID OUT is decoded while non-stop BLE presets are suppressed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\capture_switch2_steam_haptic_event.ps1 -Mode LogOnly -Seconds 12
```

HD rumble investigation helpers:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Decode-Switch2HidRumble.ps1 -Hex "02 50 87 05 25 11 44 ..."
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticProbe.ps1 -LowSpeed 32768 -HighSpeed 32768 -PulseMs 220
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Send-HidHapticSweep.ps1 -Channel Both
```

Preset lab UI:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_preset_lab.ps1
```

Open `http://127.0.0.1:8787/`. The lab sends the confirmed Pro2 preset command family by default:

```text
0A 91 01 02 00 08 00 00 XX 00 00 00 00 00 00 00
```

The UI exposes preset shortcuts, all 16 command bytes as editable 0-255 values, single fire, fixed-count 1s burst, continuous 1s loop, and stop.

Long-running preset fuzzing:

```powershell
# Dry run: writes a plan only.
powershell -NoProfile -ExecutionPolicy Bypass -File .\fuzz_switch2_preset_command.ps1 -ByteIndexes 8 -StartHex 00 -EndHex 91

# Real run: sends byte 8 / preset-id values 00..91 with stop -> wait -> test -> observe -> stop.
powershell -NoProfile -ExecutionPolicy Bypass -File .\fuzz_switch2_preset_command.ps1 -ByteIndexes 8 -StartHex 00 -EndHex 91 -ConfirmRisk

# Higher-risk mode: vary command bytes 0..9 independently over 00..91.
# Requires explicit header permission after byte[0]=03 was seen to disconnect GATT.
powershell -NoProfile -ExecutionPolicy Bypass -File .\fuzz_switch2_preset_command.ps1 -FirstTen -StartHex 00 -EndHex 91 -ConfirmRisk -AllowHeaderSweep

# Payload-tail queue: wait for BLE, then sweep only preset command bytes 10..15.
powershell -NoProfile -ExecutionPolicy Bypass -File .\fuzz_switch2_preset_command.ps1 -PayloadTail -StartHex 00 -EndHex 91 -ConfirmRisk -WaitForBleReadySeconds 14400 -ReadyPollSeconds 30

# Active-preset queue with its own transcript/config log directory.
# Defaults to preset 01 and sweeps its seven trailing payload bytes 9..15.
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_payload_tail_queue.ps1
```

For a Y700-local unattended wait/sweep, push and run `sweep_switch2_preset_payload_local.sh`.
It waits for the BLE bridge `post-init` marker, then sweeps active `preset=01`
payload bytes 9..15 over `00..91` on the tablet itself. Local artifacts are
written under `/data/local/tmp/switch2_payload_sweep_*`, and each case inserts
markers into the BLE bridge and raw logs for later parsing.

For multi-preset unattended collection, push and run
`queue_switch2_payload_sweeps_local.sh`. It waits for any current local sweep to
finish, then launches the same safe payload sweep for active presets `02..07`.
The queue log is `/data/local/tmp/switch2_payload_sweep_queue.log`.

Pull the latest Y700-local payload sweep plus matching BLE logs with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\pull_switch2_payload_sweep.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Summarize-PayloadSweep.ps1 -PullDir .\logs\payload_sweep_pull_YYYYMMDD_HHMMSS
```

Sparse payload A/B tests should be used for physical listening checks. They
insert long silence windows and compare baseline/candidate/control cases without
rapidly re-triggering the same preset:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_payload_ab_test.ps1 -ObserveSeconds 8 -CooldownSeconds 8
```

Each case is appended to `logs\preset_fuzz_*\events.jsonl` immediately, with `manifest.json`, `plan.json`, and `summary.json` beside it. Do not use `-FirstTen` unless the controller is on a soft surface and BLE reconnection is confirmed.

Completed fuzz runs also write compact machine summaries:

```text
logs\preset_fuzz_*\machine_summary.csv
logs\preset_fuzz_*\machine_summary.json
```

To summarize an older run again:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Summarize-PresetFuzz.ps1 -RunDir .\logs\preset_fuzz_YYYYMMDD_HHMMSS
```

The first command byte is not a normal preset parameter. A 2026-05-23 header sweep reached `03 91 01 02 00 08 ...`, then the controller disconnected and did not recover through unattended GATT reconnect. Treat `byte[0]` as a high-risk `CommandCode` field; prefer the zero-padded payload bytes for blind sweeps.

Report layout:

| Byte | Meaning |
| --- | --- |
| 0-1 | 16 buttons, little-endian bitfield |
| 2 | Hat switch: `0` up, `2` right, `4` down, `6` left, `8` neutral |
| 3 | X axis, signed 8-bit, `0x81`=-127, `0x00`=center, `0x7f`=127 |
| 4 | Y axis, signed 8-bit |
| 5 | Z axis, signed 8-bit |
| 6 | Rz axis, signed 8-bit |
| 7 | Reserved constant byte |

## Run From Windows

Open `joy.cpl` first, then run:

```powershell
cd C:\path\to\y700-hid-gamepad
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_and_run.ps1
```

If adb is not in PATH:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_and_run.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe"
```

If both USB and wireless ADB are connected, run setup through the wireless serial so USB rebinds do not cut off the shell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_and_run.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -DeviceSerial "<wireless-adb-serial>"
```

Run the visible button/axis sweep:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_and_run.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -DeviceSerial "<wireless-adb-serial>" -RunTest
```

If report writes fail with `Cannot send after transport endpoint shutdown`, check:

```shell
cat /sys/class/udc/a600000.dwc3/state
```

The state must be `configured` before `/dev/hidg0` can accept input reports. If it says `not attached`, reconnect the USB-C data cable to the Windows host and wait for Windows to finish enumerating the device.

## Run Directly On Android

```shell
adb push setup_y700_gamepad_v2.sh /data/local/tmp/
adb push test_y700_gamepad_reports.sh /data/local/tmp/
adb shell su -c 'chmod 755 /data/local/tmp/setup_y700_gamepad_v2.sh /data/local/tmp/test_y700_gamepad_reports.sh'
adb shell su -c 'sh /data/local/tmp/setup_y700_gamepad_v2.sh'
adb shell su -c 'sh /data/local/tmp/test_y700_gamepad_reports.sh'
```

## Single Reports

Neutral:

```shell
printf '\x00\x00\x08\x00\x00\x00\x00\x00' > /dev/hidg0
```

Button 1 press and release:

```shell
printf '\x01\x00\x08\x00\x00\x00\x00\x00' > /dev/hidg0
sleep 0.2
printf '\x00\x00\x08\x00\x00\x00\x00\x00' > /dev/hidg0
```

X axis full right and release:

```shell
printf '\x00\x00\x08\x7f\x00\x00\x00\x00' > /dev/hidg0
sleep 0.2
printf '\x00\x00\x08\x00\x00\x00\x00\x00' > /dev/hidg0
```

## Restore

```shell
cd /config/usb_gadget/g1
echo "" > UDC
rm -f configs/b.1/hid.usb0
rmdir functions/hid.usb0
echo a600000.dwc3 > UDC
```

## Bluetooth Gamepad Bridge

Pair the controller to the Y700 over Bluetooth first, then list likely Android input event devices:

```powershell
cd C:\path\to\y700-hid-gamepad
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_bridge.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -ListOnly
```

Start the bridge through wireless ADB:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_bridge.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe"
```

If multiple gamepad candidates are listed, specify the event path:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_bridge.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -EventPath /dev/input/event10
```

Keep that PowerShell window open while playing. Press `Ctrl+C` to stop; the script sends a neutral report on exit.

If the mapping is wrong, capture raw events while pressing every button and moving both sticks:

```shell
adb -s <wireless-adb-serial> shell su -c 'sh /data/local/tmp/capture_evdev_events.sh /dev/input/event10 15'
adb -s <wireless-adb-serial> pull /data/local/tmp/gamepad_events_YYYYMMDD_HHMMSS.log .
```

## Bluetooth Pairing Diagnostics

If the controller usually cannot pair with the Y700, capture the Android Bluetooth logs while attempting pairing:

```powershell
cd C:\path\to\y700-hid-gamepad
powershell -NoProfile -ExecutionPolicy Bypass -File .\capture_bluetooth_pairing.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -Seconds 60 -ClearLogcat
```

During the 60 second capture window, put the controller into pairing mode and try pairing it in Android Bluetooth settings. The script writes logs under `logs\bt_pair_YYYYMMDD_HHMMSS`.

## Switch 2 Pro BLE Bridge

The Switch 2 Pro Controller does not expose itself to Android as a normal HID/HOGP input device. Use the private BLE GATT bridge instead:

```powershell
cd C:\path\to\y700-hid-gamepad
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_ble_bridge.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -Background
```

Then put the controller into Bluetooth pairing/connect mode. The bridge connects to `38:C6:CE:27:FC:2D`, subscribes to `7492866c-ec3e-4619-8258-32755ffcc0f8`, and writes `/data/local/tmp/switch2_state.txt` for `Switch2FfsResponder`.

Observed BLE button mapping:

```text
BLE byte2 01 -> B
BLE byte2 02 -> A
BLE byte2 04 -> Y
BLE byte2 08 -> X
BLE byte2 10 -> R
BLE byte2 20 -> ZR
BLE byte2 40 -> Plus

BLE byte3 01 -> D-pad Down
BLE byte3 02 -> D-pad Right
BLE byte3 04 -> D-pad Left
BLE byte3 08 -> D-pad Up
BLE byte3 10 -> L
BLE byte3 20 -> ZL
BLE byte3 40 -> Minus

BLE byte4 04 -> GR
BLE byte4 08 -> GL
BLE byte4 10 -> C
```

Logs:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_ble_bridge.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -PullLogs
```

Runtime files:

```text
/data/local/tmp/switch2_ble_bridge.log
/data/local/tmp/switch2_ble_input_raw.log
/data/local/tmp/switch2_button_changes.log
/data/local/tmp/switch2_state.txt
```

The button transition log records only raw button byte changes and the current Switch 2 state bytes. Use it to calibrate the Switch 2-only controls:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\capture_switch2_button_map.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe"
```

The guided capture asks for A/B/X/Y, D-pad, shoulders, system buttons, and the extra `C`/`GL`/`GR` controls. Keep the BLE bridge connected while running it.

The BLE bridge keeps the Switch 2 button bytes in their native notify order in the state file:

```text
state b5 = BLE byte2 = B A Y X R ZR Plus RStick
state b6 = BLE byte3 = DDown DRight DLeft DUp L ZL Minus LStick
state b7 = BLE byte4 = Home Capture GR GL C
```

The `b5..b8` state-file values are button bytes and are parsed as hexadecimal values. Axis values remain decimal.

The USB responder then maps those fields into Steam's wired Switch 2 state packet:

```text
USB data[5]  = Y X B A R ZR
USB data[6]  = Minus Plus RStick LStick Home Capture C
USB data[7]  = DDown DUp DRight DLeft L ZL
USB data[8]  = GR GL
USB data[11..16] = left and right packed 12-bit sticks
```

That placement is intentionally different from the Pro2 BLE notify packet and the older Switch Pro identity experiments.

## Steam / Nintendo Identity Experiment

The generic gamepad HID output is already verified. To test whether Steam reacts to a Nintendo USB identity, keep using wireless ADB and run:

```powershell
cd C:\path\to\y700-hid-gamepad
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_identity_experiment.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -Mode switch2
```

This changes only the USB identity:

```text
VID/PID      057e:2069
Manufacturer Nintendo Co., Ltd.
Product      Nintendo Switch 2 Pro Controller
```

Old Switch Pro identity for comparison:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_identity_experiment.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -Mode switchpro
```

Restore the original Y700 identity:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_identity_experiment.ps1 -AdbPath "C:\path\to\platform-tools\adb.exe" -Mode restore -SkipSetup
```

If Steam still shows a generic controller after changing identity, unplug/replug USB and remove the old cached device instance in Windows Device Manager. If Steam recognizes a Nintendo controller but input does not work, the next step is a Nintendo-like HID descriptor/report protocol rather than another name change.
