# Pro2 Calibration Probe

这是只读实验工具。它只通过 Pro2 的 `MI_01` WinUSB Bulk 接口发送 flash-read 命令，不发送初始化、写入、固件或模式切换命令。

```powershell
dotnet run --project tools\tests\pro2_calibration_probe\Pro2CalibrationProbe.csproj -- --list

dotnet run --project tools\tests\pro2_calibration_probe\Pro2CalibrationProbe.csproj -- \
  --instance-id "真实设备实例的一部分" \
  --output work\imu_fidelity_lab\physical-pro2-calibration.json
```

存在多个候选但未指定 `--instance-id` 时，工具会拒绝读取。正式比较必须同时保存真实 Pro2 和 VIIPER 虚拟 Pro2 的结果。
