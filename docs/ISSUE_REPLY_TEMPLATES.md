# Issue Reply Templates

## No License File

### English

```text
Thanks for the suggestion. I’ve added the Apache-2.0 license to the repository.

Ports to other boards such as Pico 2W / RP2350 are welcome, but please keep in mind that this project is still experimental and hardware compatibility may need further testing.
```

### 中文解释

```text
感谢提醒，我已经给仓库添加了 Apache-2.0 开源协议。

欢迎移植到 Pico 2W / RP2350 等其他开发板，但这个项目目前仍然是实验性项目，硬件兼容性需要进一步实测确认。
```

## Board Port Proposal

### English

```text
Thanks for the porting proposal. Board ports are welcome.

If you test this board, please include the exact hardware model, SDK version, BLE connection method, USB HID path, host OS, BLE input rate, USB report rate, and any known issues. This helps keep the compatibility table honest and reproducible.
```

### 中文解释

```text
感谢移植建议，项目欢迎新的开发板适配。

如果你测试了这块板子，请尽量提供硬件型号、SDK 版本、BLE 连接方式、USB HID 输出方式、主机系统、BLE 输入频率、USB report 频率和已知问题。这样兼容性记录才更可信、可复现。
```

## macOS / Android Support Question

### English

```text
macOS and Android support are planned as Generic USB HID Gamepad modes. They are not currently listed as stable verified features.

The goal is not native Switch 2 Pro Bluetooth identity on macOS/Android. The receiver board will connect to the real controller over BLE and expose a standard USB HID gamepad to the host.
```

### 中文解释

```text
macOS 和 Android 支持目前规划为 Generic USB HID Gamepad 模式，还不是当前稳定版已验证能力。

目标不是让 macOS/Android 原生蓝牙识别为 Switch 2 Pro，而是由开发板通过 BLE 连接真实手柄，再通过 USB HID 向主机输出标准手柄。
```

