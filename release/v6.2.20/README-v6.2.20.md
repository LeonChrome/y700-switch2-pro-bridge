# V6.2.20 新和联胜 VIIPER Windows 正式版

V6.2.20 是无 ESP32 开发板路线的阶段性正式版。它通过 Windows BLE 直接连接真实 Pro2，再用内置 VIIPER/usbip-win2 创建虚拟 USB 手柄。

## 本版重点

- 解除旧版偏保守的 20Hz 低频限制，加入动态连接参数与虚拟输出刷新率自适应。
- 新和联胜 / PS5 模式继续使用 `dualsensehaptic / 054C:0CE6`，保留 HD 音频震动与普通震动调度。
- 新增并稳定 PS5 Edge / 背键模式：`dualsenseedge / 054C:0DF2 / DualSense Edge Wireless Controller`，Pro2 背键映射为 Edge `L4/R4`。
- PS5 家族模式完成专项 IMU 优化，采用 R7 实测确认的陀螺仪方向；正式界面不再暴露 Professional IMU Test、HID Audit、Synthetic Pulse、Static Raw 和三轴反向调试按钮。
- Xbox / XInput 模式支持 Pro2 背键映射，限制在固定键位、单发和连发，设置页中有对应提示。
- 日志策略收紧：启动时会清理上一次 V6 日志；Manager 单次持久化日志限制为 8MB；VIIPER server 日志降为 info 级别，避免几分钟生成超大日志。
- 主界面与设置区域保留滚动框，窗口高度不足时不会出现按钮点不到的问题。

## 首次使用

1. 运行 EXE。
2. 如果提示 USBIP 未安装，点击“安装/修复 USBIP”，安装完成后必要时重启 Windows。
3. 选择角色模式，点击“连接 PRO2 · 进入游戏”。
4. 程序会持续扫描最匹配的 Pro2，并在断联后自动重连；正常使用不需要手动反复点击 BLE 控制区。

## 已知说明

- PS5 Edge 当前主打背键和陀螺仪优化；Edge 的反馈链路是 VIIPER `dualsenseedge` 6 字节普通反馈，不等同于新和联胜 / PS5 的 HD haptic contract。
- V6.2.x 是 Windows-only VIIPER/USBIP 路线，不需要 ESP32；后续 ESP32 5.9 系列仍可独立优化，不互相替代。
- 如果刚安装 usbip-win2 后仍无法启动虚拟设备，请先重启 Windows，再重新打开本 EXE。

## 文件

- `新和联胜VIIPER版本-aio-v6.2.20.exe`
- `usbip-win2/v0.9.7.7/USBip-0.9.7.7-x64.exe`
- `SHA256SUMS-v6.2.20.txt`

## 校验

请使用 `SHA256SUMS-v6.2.20.txt` 校验 EXE 与 usbip-win2 安装器。
