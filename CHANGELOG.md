# 更新记录

## V6.2.20

- 正式收口 V6.2.19 Professional IMU Test r7：PS5 / Edge 陀螺仪方向采用 R7 实测结果，正式界面移除测试模式、HID Audit、Synthetic Pulse、Static Raw 和三轴反向调试入口。
- 新和联胜 / PS5 模式继续使用 `dualsensehaptic / 054C:0CE6`，保留 HD 音频震动与普通震动调度。
- PS5 Edge / 背键模式继续使用 `dualsenseedge / 054C:0DF2`，Pro2 背键映射为 Edge `L4/R4`；Edge 反馈仍按 VIIPER 6 字节普通反馈处理。
- Xbox / XInput 模式保留 Pro2 背键映射能力，限制为固定键位、单发和连发，避免过度复杂的组合配置影响稳定性。
- 解除旧版偏保守的 20Hz 低频限制，保留动态连接参数与虚拟输出刷新率自适应。
- 收紧日志策略：启动清理上一次 V6 日志，Manager 单次日志限制 8MB，VIIPER server 降为 info 级别，避免长时间运行产生超大日志。
- 主界面与设置区域保持滚动可达，窗口高度不足时不会遮住底部按钮。

## V6.2.18

- 固化 V6.2.17-test 的 PS5 / DualSense 输出层 IMU 修正：`Accel=X×2/Y×2/Z×-2`，`Gyro` 三轴取反并乘可配置倍率。
- 新增独立 **PS5 Edge / 背键** 模式：使用 VIIPER `dualsenseedge`，USB 身份 `054C:0DF2 / DualSense Edge Wireless Controller`。
- Pro2 背键在 PS5 Edge 模式映射为 Edge `L4/R4`；普通 PS5 模式不再输出 Edge 背键位，避免旧配置被隐式迁移。
- 普通 PS5 模式继续使用 `dualsensehaptic / 054C:0CE6`，保留 HD haptic 音频震动路径。
- 新增设备诊断与清理按钮：dump VIIPER bus、usbip port、Windows PnP/HID 字段；可清理本地残留 VIIPER bus 与匹配 VID/PID 的 usbip attach。
- 更新托盘菜单和角色选择界面，四个模式可直接切换。

## V6.2.17-test

- 仅在 PS5 / DualSense 输出层测试 IMU 加速度倍率、Z 方向和三轴陀螺仪方向修正。
