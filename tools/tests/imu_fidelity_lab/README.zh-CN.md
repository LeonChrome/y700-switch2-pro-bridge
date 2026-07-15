# IMU Fidelity Lab

该工具只负责读取原始记录并建立可复现的 IMU 基线，不参与生产输入链路，也不修改手柄数据。

## 目标

1. 分开观察 Windows BLE 通知到达时间和 Pro2 设备 Motion Timestamp。
2. 同时记录 FD2 的计数、磁力计、温度、加速度和陀螺仪。
3. 区分虚拟 USB `report_hz` 与真实 `imu_change_hz`。
4. 为后续 Pro2 原生保真、温度零偏模型和 Pro2 到 DualSense 坐标求解提供原始证据。

## 使用

```powershell
dotnet run --project tools\tests\imu_fidelity_lab\ImuFidelityLab.csproj -- \
  --fd2-jsonl "$env:LOCALAPPDATA\PRO2WirelessReceiverControlBoard\v6_logs\spikes" \
  --hid-csv work\pro2_gyro_ab\wired_physical_yaw.csv \
  --hid-csv work\pro2_gyro_ab\wireless_r7_static_after_manual.csv \
  --output work\imu_fidelity_lab\baseline.md
```

六面数据文件名固定为：

```text
accel_pos_x.csv  accel_neg_x.csv
accel_pos_y.csv  accel_neg_y.csv
accel_pos_z.csv  accel_neg_z.csv
```

求解：

```powershell
dotnet run --project tools\tests\imu_fidelity_lab\ImuFidelityLab.csproj -- \
  --six-face-dir work\imu_fidelity_lab\six-face \
  --output work\imu_fidelity_lab\six-face-report.md
```

由于手柄外壳边缘并非严格正交，六面结果首先用于诊断，不应直接当作最终生产矩阵。更稳妥的办法是在一次长记录里完成至少 12 个分散的任意姿态，每个姿态静置约 1 秒，姿态切换期间缓慢转动。工具只对连续静止片段取一次均值，再用单位重力球约束拟合椭球：

```powershell
dotnet run --project tools\tests\imu_fidelity_lab\ImuFidelityLab.csproj -- \
  --ellipsoid-csv work\imu_fidelity_lab\arbitrary-static-poses.csv \
  --output work\imu_fidelity_lab\ellipsoid-report.md

dotnet run --project tools\tests\imu_fidelity_lab\ImuFidelityLab.csproj -- --self-test
```

椭球拟合不会把各姿态强行标成机身主轴，因此能分离外壳摆放倾角和传感器的 bias/scale/非正交误差。它只能恢复重力球的对称校准矩阵，机身坐标朝向仍由后续三轴动态实验确定。

交互采集：

```powershell
dotnet run --project tools\tests\hid_rate_probe\HidRateProbe.csproj -- --list

powershell -ExecutionPolicy Bypass -File tools\tests\imu_fidelity_lab\capture_six_faces.ps1 \
  -PathIndex 1 \
  -OutputDirectory work\imu_fidelity_lab\six-face
```

这里使用控制器机身坐标：`+X` 指向右侧、`+Y` 指向 USB-C 前端、`+Z` 指向按键正面。六面采集只求解 Pro2 传感器到机身坐标的标定，不直接假设 DualSense 的轴顺序。

## 正式实验数据集

每轮采集都必须保存原始数据，不允许只保存统计结果。

1. 冷启动静置 10 分钟，之后继续记录到 30 分钟，用于温度零偏曲线。
2. 六面静置各 30 秒，用于加速度 bias、scale 和非正交矩阵。
3. X/Y/Z 三轴分别做正向和反向 90°，每个方向至少 5 次。
4. 同一动作分别采集真实有线 Pro2、无线 FD2、虚拟 Pro2 和虚拟 DualSense。
5. 后续若有真实 DualSense，按相同动作建立目标手柄基准。

## 禁止项

- 不在实验数据里加入 smoothing、deadzone、snap-to-zero。
- 不用 Windows 回调间隔代替设备 Motion Timestamp。
- 不把 250Hz 重复输出描述为 250Hz 新 IMU 样本。
- 不在未验证前把公开项目中的固定常量直接写入生产代码。
