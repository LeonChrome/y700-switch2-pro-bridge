# V6.2.17-test 新和联胜 VIIPER 版

这是基于 V6.2.16 的 PS5 / DualSense IMU 测试版，只调整 PS5 输出层，不修改 Pro2 原始 BLE 解析，也不影响 Pro2 / Nintendo 和 Xbox / XInput 输出模式。

## 本次修改

- PS5 加速度输出固定修正为 `X × 2`、`Y × 2`、`Z × -2`。
- PS5 陀螺仪输出固定三轴取反。
- 新增 `Ps5GyroScalePitch`、`Ps5GyroScaleYaw`、`Ps5GyroScaleRoll` 设置项，默认均为 `1.0`。
- 主界面新增统一的 `PS5 Gyro倍率` 滑条，当前会同步三轴倍率，方便做 90° 单轴旋转积分测试。

## 验收重点

- 前端上抬：Pitch 应为正。
- 前端下压：Pitch 应为负。
- 前端向右摆 / 顺时针：Yaw 应为正。
- 前端向左摆 / 逆时针：Yaw 应为负。
- 右侧下压、左侧抬起：Roll 应为正。
- 左侧下压、右侧抬起：Roll 应为负。
- 六面静置主轴应接近 `±9.8 m/s²`，非主轴接近 `0`。

## 倍率测试

默认 `PS5 Gyro倍率 = 1.0`。

- 如果实测 90° 单轴旋转积分约为 45°，把倍率改为 `2.0`。
- 如果实测 90° 单轴旋转积分约为 180°，把倍率改为 `0.5`。
- 如果实测约为 90°，保持 `1.0`。

设置文件位于：

`%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\v6_settings.json`
