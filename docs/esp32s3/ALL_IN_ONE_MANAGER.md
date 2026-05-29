# All-in-one Manager Plan

Date: 2026-05-29

## 中文

V5 Manager 的目标是把普通用户的烧录流程收进一个 Windows EXE：

```text
插 CH343P Type-C -> 打开 Manager -> 选择 COM 口 -> 一键刷入 -> 自动验证状态
```

当前预览版已经内置：

- ESP32-S3 固件三件套：bootloader、partition table、app。
- 独立 esptool.exe，不要求用户安装 ESP-IDF 或 Python。
- 固件 manifest 和 SHA256 校验。
- CH343P / CH340 / WCH COM 口识别。
- WCH 官方驱动下载入口。
- 本地 driver 包目录入口，后续可以放可再分发驱动安装包。
- 一键刷入、修复重刷、仅清除、清除并重刷四种模式。
- 启动后自动连接控制通道并同步状态，不需要用户先点连接。
- BLE 搜索、候选列表、连接选中；候选里 `推荐` 表示广播信息看起来像 Switch Pro / Pro2。

刷录模式：

- 一键刷入：460800 baud，常规 write_flash；失败后自动降级到 115200 baud + `--no-stub`。
- 修复重刷：115200 baud + `--no-stub`，用于高速刷录失败或设备状态异常。
- 仅清除：whole-chip erase 后停止，用于把现有开发板模拟成新板/空板。
- 清除并重刷：whole-chip erase 后再写入固件，需要用户二次确认。

刷录前会先运行 `chip_id`，只有识别到 ESP32-S3 才继续写入，避免误刷其他串口设备。

驱动策略：

- Manager 不静默安装驱动。
- 如果检测到 CH343P/WCH COM 口，提示驱动正常。
- 如果看到 WCH `VID_1A86` 设备但没有 COM 口，提示安装 CH343/CH340 驱动。
- 提供官方页面：
  - https://www.wch-ic.com/downloads/CH343CDC_EXE.html
  - https://www.wch-ic.com/downloads/CH341SER.EXE.html?type=en

## English

The V5 Manager goal is to move normal user flashing into one Windows EXE:

```text
Plug CH343P Type-C -> open Manager -> select COM port -> flash -> verify status
```

The current preview embeds:

- ESP32-S3 firmware binaries: bootloader, partition table, app.
- Standalone esptool.exe, so ESP-IDF and Python are not required on the user's PC.
- Firmware manifest and SHA256 verification.
- CH343P / CH340 / WCH COM-port detection.
- Official WCH driver download links.
- Local driver-package folder hook for optional redistributable driver installers.
- Upgrade, repair reflash, erase-only, and erase-and-flash modes.
- Automatic control-channel connection and status sync on startup.
- BLE search, candidate list, and connect-selected flow; `推荐` marks devices that look like Switch Pro / Pro2.

Flash modes:

- Upgrade: 460800 baud normal `write_flash`; if it fails, retry at 115200 baud with `--no-stub`.
- Repair reflash: 115200 baud with `--no-stub`.
- Erase only: whole-chip erase, then stop; useful for simulating a blank/new board with an existing ESP32-S3.
- Erase and flash: whole-chip erase, then full firmware write; requires confirmation.

Before writing flash, the Manager runs `chip_id` and only proceeds if the selected port identifies as ESP32-S3.
