# 更新记录

## V6.2.18

- 固化 V6.2.17-test 的 PS5 / DualSense 输出层 IMU 修正：`Accel=X×2/Y×2/Z×-2`，`Gyro` 三轴取反并乘可配置倍率。
- 新增独立 **PS5 Edge / 背键** 模式：使用 VIIPER `dualsenseedge`，USB 身份 `054C:0DF2 / DualSense Edge Wireless Controller`。
- Pro2 背键在 PS5 Edge 模式映射为 Edge `L4/R4`；普通 PS5 模式不再输出 Edge 背键位，避免旧配置被隐式迁移。
- 普通 PS5 模式继续使用 `dualsensehaptic / 054C:0CE6`，保留 HD haptic 音频震动路径。
- 新增设备诊断与清理按钮：dump VIIPER bus、usbip port、Windows PnP/HID 字段；可清理本地残留 VIIPER bus 与匹配 VID/PID 的 usbip attach。
- 更新托盘菜单和角色选择界面，四个模式可直接切换。

## V6.2.17-test

- 仅在 PS5 / DualSense 输出层测试 IMU 加速度倍率、Z 方向和三轴陀螺仪方向修正。
