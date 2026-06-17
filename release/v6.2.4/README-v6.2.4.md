# 新和联胜 VIIPER 版本 v6.2.4

这是 V6.2.3 后的 Raw Stick 默认版。现场日志显示链路已经干净时，摇杆稳定滤波器会把真实快速拨杆误判成异常，造成体感不跟手。因此本版把正常游戏路径改为摇杆原始值直通。

## 文件

- `新和联胜VIIPER版本-aio-v6.2.4.exe`
- `XinHeLianSheng-VIIPER-aio-v6.2.4.exe`
- `SHA256SUMS-v6.2.4.txt`

## 本版重点

- 默认 `Stick = Raw Direct（推荐）`：Pro2 BLE 解析出的摇杆轴值直接进入虚拟 USB 手柄。
- `Raw Direct` 不再执行 axis hold、axis ramp、active candidate 确认等待。
- 保留 `Stability Guard（诊断）`：只有怀疑真实 BLE 轴值坏跳时才手动打开。
- UI 新增 `Stick` 档位，可在前端切换并保存到用户设置。
- 日志新增 `stick_mode=raw_direct/stability_guard`，方便区分现场数据。

## 测试结论

- `dotnet build windows/v60_viiper_app/Y700Switch2V60Viiper.csproj -c Release` 通过。
- `v60_packet_mapper_test` 通过。
- `v60_fd2_replay --synthetic --quiet` 通过。

## 现场验证重点

这版测试时优先保持 `Stick = Raw Direct（推荐）`。如果仍然不跟手，就不应再归因于滤波器，而应继续看：

- BLE 真实 `ble_parsed_hz` 与 `source_age_p95/source_age_max`
- VIIPER push 是否稳定在所选 `push_hz`
- Steam/游戏对 PS5/XInput/Pro2 模式的输入处理差异

## 关于“某个方向推不满”

不要先做补偿放大。先看日志里的 raw/filtered/latest 轴值：

- `Raw Direct` 下 `raw_*`、`filtered_*`、`latest_*` 应该一致。
- 如果某个方向的 `raw_left_y` 或 `raw_right_y` 本身没有接近 `0` / `4095`，说明真实 Pro2 BLE 原始行程或手柄校准没有给满，不是虚拟 USB 输出把它压小。
- 如果 `raw_*` 已经到端点，但 `latest_*` 或游戏侧不到端点，才继续查映射/目标模式/Steam 校准。
- 当前版本不做单方向强行拉满，避免把硬件个体差异误修成新的漂移或边缘抖动。
