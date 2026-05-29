# Y700 / ESP32-S3 Switch 2 Pro Bridge

## Project Positioning / 项目定位

### 中文

这是一个低成本、低延迟的 Switch 2 Pro Controller BLE-to-USB 硬件桥接项目。当前主线是 ESP32-S3 开发板方案：真实 Switch 2 Pro Controller 通过 BLE 连接开发板，开发板通过 USB HID 输出到主机设备。Windows / Steam 端优先优化 Nintendo / Switch Pro 风格识别路径。

本项目最早从 root 后的 Lenovo Y700 平板方案开始。Y700 Android USB Gadget 路线仍保留在仓库中作为历史稳定路线和 research path，但当前开发重心已经转向 ESP32-S3 独立硬件桥接器。

### English

A low-cost, low-latency BLE-to-USB hardware bridge for the Switch 2 Pro Controller. The current mainline is the ESP32-S3 receiver: the real controller connects over BLE, and the board exposes a USB HID gamepad to the host, with Windows / Steam optimized for a Nintendo / Switch Pro-style path.

This project started as a rooted Lenovo Y700 Android USB Gadget bridge. The Y700 route is still kept as a historical stable/research path, but the current development focus is the standalone ESP32-S3 hardware bridge.

## Current Status / 当前状态

| Track | Status | Use case | Notes |
| --- | --- | --- | --- |
| V4.0.0 ESP32-S3 Pro2 Bridge | Stable / 推荐稳定版 | 普通测试者优先使用 | ESP32-S3 BLE input + USB HID output + Windows / Steam Nintendo-style path |
| V5.0.0 All-in-one Manager Preview | Preview / 预览版 | 仅适合测试用户 | A Windows EXE that bundles the V4 firmware payload, flasher, and manager UI. It does not fully replace V4 stable yet. |
| Y700 Android USB Gadget route | Legacy / 历史方案 | Research path / 历史稳定路线 | Kept for reference and previous Y700 users. The mainline has moved to ESP32-S3. |

### 中文说明

- **推荐稳定版：V4.0.0 ESP32-S3 Pro2 Bridge**。适合普通测试者，重点是 ESP32-S3 BLE 输入、USB HID 输出、Windows / Steam 路径。
- **预览版：V5.0.0 All-in-one Manager Preview**。它是刷机与管理工具预览版，内置 V4 固件 payload，方便不会手动烧录的用户测试，但不要理解为 V5 已经完全取代 V4。
- **历史方案：Y700 Android USB Gadget**。保留为历史稳定路线和研究资料，当前主线已经转向 ESP32-S3。

### English Notes

- **Stable: V4.0.0 ESP32-S3 Pro2 Bridge**. Recommended for normal testers. Focus: ESP32-S3 BLE input, USB HID output, and the Windows / Steam path.
- **Preview: V5.0.0 All-in-one Manager Preview**. This is a flasher/manager preview that bundles the V4 firmware payload. It should not be read as a full replacement for V4 stable yet.
- **Legacy: Y700 Android USB Gadget route**. Kept as the historical stable/research route. The mainline is now ESP32-S3.

## Verified vs Planned / 已验证与规划

### Verified / 已验证

- Real Switch 2 Pro Controller BLE input on ESP32-S3.
- 133Hz-class BLE input rate with BLE connection interval request `6` / `7.5 ms` in the current tested environment.
- 1000Hz-class USB HID report output; host-side Windows test has measured about `993 Hz` over a 10-second window.
- Windows / Steam Nintendo-style controller path.
- Basic input mapping: buttons, D-pad, sticks, stick clicks, `+`, `-`, `Home`, `Capture`, `C`, `GL`, `GR`.
- Basic rumble / haptic forwarding has produced physical feedback.
- Windows Manager / status view for USB state, BLE state, actual report rate, BLE input Hz, BLE interval, HID OUT/GET, bulk control, and rumble counters.
- All-in-one Manager preview can flash, erase, reflash, detect CH343/CH340/WCH ports, and show BLE candidates.

### Planned / 规划中

- macOS Generic USB HID Gamepad Mode. **Planned / Not yet verified as stable.**
- Android OTG Generic USB HID Gamepad Mode. **Planned / Not yet verified as stable.**
- Dual Controller Mode. **Planned / Not tested.**
- Profile switching across Windows / Steam, macOS Generic, and Android Generic modes.
- Cross-platform test matrix with host-observed input rates.
- Better release packaging, SHA256 summaries, and clearer stable/preview download flow.

macOS, Android, and Dual Controller Mode are future planning or experimental directions. They are not current V4 stable capabilities.

## Performance Notes / 性能说明

- Real BLE input freshness is represented by `BLE input Hz` / `ble_input_actual_mhz`.
- USB HID report output can run faster than BLE input. When USB report rate is higher than BLE input rate, the USB side repeats the latest BLE controller state.
- 1000Hz-class USB output does not mean the physical controller generates 1000 new BLE samples per second.
- Host applications may show lower rates because OS/game APIs can coalesce or throttle events.
- Performance claims should be checked with firmware logs and host-side test tools.

## Release Downloads / 发布下载

普通用户建议优先从 GitHub Releases 下载 EXE、JAR、firmware zip 等发布包，而不是从仓库根目录手动挑二进制文件。

Normal users should prefer GitHub Releases for EXE, JAR, firmware zip, and packaged tools.

### Stable / 推荐稳定版

```text
esp32s3-pro2-bridge-v4.0.0-20260529.zip
esp32s3-pro2-bridge-firmware-v4.0.0-20260529.zip
Y700Switch2Manager-v4.0.0.exe
```

### Preview / 预览版

```text
Y700Switch2Manager-aio-v5.0.0-preview.exe
```

The V5 preview is useful if you want one Windows EXE that includes the firmware payload, flasher, driver hints, BLE scan list, and manager panel. It is still a preview.

## Hardware / 硬件

Current ESP32-S3 tested board shape:

- ESP32-S3-N16R8.
- 16MB flash.
- 8MB PSRAM.
- CH343P Type-C for flashing, logs, and serial control.
- ESP32-S3 native USB & OTG Type-C for USB HID output to the host.

Other board ports are welcome, but they should be documented with hardware model, SDK, USB path, BLE path, and measured test results.

## Quick Start / 快速开始

### V4 Stable Path

1. Download and extract the V4 stable release package.
2. Connect the board's `CH343P Type-C` port to Windows.
3. Flash the firmware:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash_release.ps1 -Port COM12
```

4. If the port is not `COM12`, detect the CH343P port first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\detect_ports.ps1
```

5. Replug the native USB & OTG port if Windows / Steam does not refresh HID enumeration after flashing.
6. Use the Manager or serial commands to reconnect BLE and inspect status.

### V5 Preview Path

1. Download `Y700Switch2Manager-aio-v5.0.0-preview.exe` from GitHub Releases.
2. Connect the board's `CH343P Type-C` port.
3. Open the EXE, choose the COM port, then use one-click flash.
4. Use erase-only or erase-and-flash only if you are intentionally testing recovery/blank-board flows.

## Useful Commands / 常用命令

```powershell
# Query firmware status
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command status -ReadSeconds 5

# Force reconnect to the saved Pro2 target
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "ble reconnect" -ReadSeconds 30

# Set USB report loop to 1000 Hz
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rate 1000" -ReadSeconds 3

# Measure host-observed HID report rate on Windows
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Measure-SwitchHidRate.ps1 -Seconds 5

# Rumble smoke test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\send_command.ps1 -Port COM12 -Command "rumble hold 3000" -ReadSeconds 5
```

## Documentation / 文档

- [Quickstart](QUICKSTART.md)
- [V4.0.0 Release Notes](RELEASE_NOTES_v4.0.0.md)
- [V5.0.0 Preview Release Notes](RELEASE_NOTES_v5.0.0-preview.md)
- [ESP32-S3 documentation](docs/esp32s3/README_ESP32S3.md)
- [Control protocol](docs/esp32s3/CONTROL_PROTOCOL.md)
- [Next generation plan](docs/NEXT_GENERATION_PLAN.md)
- [Test matrix](docs/TEST_MATRIX.md)
- [Release packaging plan](docs/RELEASE_PACKAGING_PLAN.md)
- [Contributing](CONTRIBUTING.md)

## Repository Layout / 仓库结构

```text
firmware/esp32s3_switch2_bridge/   ESP-IDF firmware for the ESP32-S3 bridge
windows/manager_app/               .NET 8 WPF Manager and all-in-one flasher source
tools/esp32s3/                     Build, flash, monitor, and serial command scripts
tools/                             HID, Steam, haptic, and rate-test helper tools
docs/esp32s3/                      ESP32-S3 protocol, design, and troubleshooting docs
release/                           Local packaged artifacts; public downloads should use GitHub Releases
src/                               Historical Y700 Android bridge/responder sources
```

## Contributing / 贡献

Contributions are welcome. Good areas include board ports, HID descriptor experiments, macOS / Android compatibility testing, BLE rate and latency testing, documentation, and release packaging improvements.

See [CONTRIBUTING.md](CONTRIBUTING.md) for a simple contribution guide.

## Disclaimer / 免责声明

This project is not affiliated with, endorsed by, or sponsored by Nintendo, Valve, Microsoft, Apple, Google, Espressif, or any other hardware/software vendor. Nintendo Switch, Switch Pro Controller, Steam, Windows, macOS, Android, ESP32, and related names are trademarks of their respective owners.

本项目不是 Nintendo、Valve、Microsoft、Apple、Google、Espressif 或其他厂商的官方项目，也未获得上述公司的认可或赞助。Nintendo Switch、Switch Pro Controller、Steam、Windows、macOS、Android、ESP32 等名称归各自权利人所有。

This is an experimental research project. Results may vary with different ESP32-S3 boards, Windows builds, Steam versions, USB cables, BLE environments, and controller firmware.

这是一个实验性研究项目。不同 ESP32-S3 开发板、Windows 版本、Steam 版本、USB 线材、BLE 环境和手柄固件版本都可能影响结果。

## License / 许可证

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.

本项目采用 Apache License 2.0 开源协议，详见 [LICENSE](LICENSE) 文件。

Ports to other boards are welcome, as long as the Apache-2.0 license terms are followed.

欢迎移植到其他开发板，但请遵守 Apache-2.0 开源协议条款。
