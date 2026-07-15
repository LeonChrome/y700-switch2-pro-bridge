# Pro2 IMU 保真课题

## 研究目标

本课题同时验证两条独立链路：

1. Pro2 BLE 输入到虚拟 Pro2，目标是尽可能逐字段复刻真实有线 Pro2。
2. Pro2 BLE 输入到虚拟 DualSense/Edge，目标是在物理量、坐标、标定和时间语义上符合 DualSense 契约。

本课题不以滤波、死区或异常值替换掩盖链路问题。

## 已冻结基线

- 真实有线 Pro2：约 250Hz，每个 HID 报告几乎都有新 IMU。
- Windows BLE FD2：当前机器约 66.7Hz，连接间隔约 15ms。
- r7 虚拟 Pro2：USB 端约 250Hz，约 73% 报告复用最近一次 BLE 状态。
- r7 虚拟报告为每次 250Hz 输出生成新的 Motion Timestamp，而不是保留 66.7Hz 源采样时间。
- 当前虚拟 Pro2 的磁力计和温度字段为零。
- 当前虚拟 Pro2 对加速度和陀螺仪 Y 轴做了额外取反；真实有线和 BLE 源的静置 Y 方向一致。
- 当前 VIIPER Pro2 传感器校准 flash 区返回零块。

## 数据模型

### Pro2 原始源

```text
counter          payload 0..3
magnetometer     payload 25..30
motion timestamp payload 42..45
temperature      payload 46..47
accelerometer    payload 48..53
gyroscope        payload 54..59
```

Windows 到达时间只描述操作系统调度，不作为传感器积分主时钟。

### 物理标定

```text
omega_pro2 = M_gyro(T) * (gyro_raw - gyro_bias(T))
accel_pro2 = M_accel * (accel_raw - accel_bias)
```

`M_gyro` 和 `M_accel` 允许包含每轴比例、非正交和交叉轴误差，不预设为只有符号和轴交换的矩阵。

### DualSense 输出

```text
omega_ds = R_ds_from_pro2 * omega_pro2
accel_ds = R_ds_from_pro2 * accel_pro2
```

同一刚体坐标旋转必须同时适用于陀螺仪和加速度。只有在确认传感器位置差造成显著动态误差后，才评估：

```text
accel_ds += alpha x r + omega x (omega x r)
```

最终 raw 必须由虚拟 DualSense `0x05` 标定报告反推，不能让报告声明的比例与写入比例不一致。

## 阶段门槛

### Gate A：原始协议完整性

- 读取真实 Pro2 校准区并保存原始字节。
- 捕获真实有线 Pro2 的完整 FD2 字段。
- 证明虚拟 Pro2 的字段、符号、时间戳和校准响应与目标一致。

### Gate B：静态标定

- 六面主轴接近 `±1g`。
- 加速度残差 RMS 小于 0.02g。
- 静置陀螺仪均值每轴小于 0.02dps，且记录温度条件。
- 不允许通过 snap-to-zero 达成指标。

### Gate C：动态标定

- 三轴正反 90 度积分误差小于 2%。
- 非主轴积分最好小于主轴的 3%。
- 正反方向比例对称，不以单方向测试确定倍率。

### Gate D：时间保真

- 分别报告源采样率、USB 报告率和复用率。
- 对比源时间保持、零阶保持和有界短时预测。
- 预测模式不得冒充原始保真模式。

### Gate E：主观 A/B

- 同一 Steam Input 配置下比较真实有线 Pro2、虚拟 Pro2、虚拟 DualSense。
- 在通过 A 至 D 后才使用主观手感决定默认时间重构策略。

## 当前禁止进入生产的内容

- 未经六面和三轴求解得到的固定轴矩阵。
- 从其他型号 Switch 控制器直接复制的校准常量。
- 将 Mahony/Madgwick 姿态融合结果重新伪装成原始 HID 角速度。
- 把 250Hz 重复状态宣传为 250Hz 新传感器数据。
