# V6.2.18 新和联胜 VIIPER Windows 版

V6.2.18 是无 ESP32 开发板路线的正式版。它继续使用 Windows BLE 直连真实 Pro2，并通过内置 VIIPER/usbip-win2 创建虚拟 USB 手柄。

## 本版重点

- 四个模式：新和联胜 / PS5、PS5 Edge / 背键、Pro2 / Nintendo、Xbox / XInput。
- 普通 PS5 仍是 `dualsensehaptic / 054C:0CE6`，保留 HD haptic 音频震动路径。
- PS5 Edge 是新增独立模式：`dualsenseedge / 054C:0DF2 / DualSense Edge Wireless Controller`。
- Pro2 背键在 Edge 模式映射为 `L4/R4`；普通 PS5 不输出 Edge 背键位。
- PS5/Edge 共用输出层 IMU 修正：`Accel=X×2/Y×2/Z×-2`，`Gyro` 三轴取反并乘可调倍率。
- 新增设备枚举、残留虚拟设备清理、诊断导出按钮，用于排查 `If_Hid`、重复控制器、USBIP 残留和 VIIPER bus 状态。

## 首次使用

1. 运行 EXE。
2. 如果提示 USBIP 未安装，点击“安装/修复 USBIP”，安装完成后必要时重启 Windows。
3. 选择角色模式，点击“连接 PRO2 · 进入游戏”。
4. 手柄唤醒后程序会持续扫描并自动重连。

## 文件

- `新和联胜VIIPER版本-aio-v6.2.18.exe`
- `usbip-win2/v0.9.7.7/USBip-0.9.7.7-x64.exe`
- `SHA256SUMS-v6.2.18.txt`

## 校验

请使用 `SHA256SUMS-v6.2.18.txt` 校验 EXE 与 usbip-win2 安装器。
