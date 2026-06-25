# V6.2.21 IMU Test r2 / PS5 手感验证版

这是一个测试版，不是“宣称已经完美”的正式版。它的目标是把 PS5 / DualSense / Edge 模式的 IMU 转换从“经验调轴”推进到可审计的物理量纲链路。

## 本轮关键修正

- PS5 / Edge 普通输出层统一使用 `G=+X,+Z,-Y`、`A=+X,+Z,-Y`。
- Pro2 / Switch 源加速度按 `4096 raw/g` 解读，再转换为 DualSense 最终 `8192 raw/g`。
- DualSense gyro 输出按当前 VIIPER / 外部 tester 验证路径使用 `16.384 raw/dps`。
- Neutral DualSense accel 固定为 `0,0,-8192`。
- 新增 `[PS5_IMU_PIPE]` 日志字段，明确打印：
  - `raw_pro2`
  - `physical_pro2`
  - `mapped_ds`
  - `ds_raw`
  - `gyro_raw_per_dps=16.384`
  - `pro2_accel_raw_per_g=4096`
  - `ds_accel_raw_per_g=8192`

## 为什么要改 4096/g

成熟实现里 Switch / Pro2 系列常见加速度量纲是 `4096 raw/g`，DualSense 则是 `8192 raw/g`。此前普通 PS5 / Edge 路径把 Pro2 源加速度也按 `8192 raw/g` 解读，会导致重力向量只有半截。游戏或 tester 如果用 gyro + accel 做姿态融合，就容易出现“陀螺仪能动，但多晃几次以后很偏”的手感。

本轮修复后，平放无真实输入时应看到：

```text
ds_raw=gyro:0,0,0;accel:0,0,-8192
pro2_accel_raw_per_g=4096
ds_accel_raw_per_g=8192
```

## 参考依据

- SDL DualSense 驱动使用 DualSense accel `8192/g`，并对 gyro/accel 做设备校准：<https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c>
- SDL Switch 驱动使用 Switch accel `4096/g`、gyro 约 `14.2842 raw/dps`：<https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_switch.c>
- Linux hid-playstation 也把 DualSense accel 标为 `8192/g`，gyro 标为 `1024 raw/(deg/s)`，说明真实硬件还涉及校准 feature report：<https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c>

注意：真实 DualSense 硬件驱动的 `1024 raw/dps` 与当前 VIIPER / Web tester 路径看到的量纲不完全等价。本版没有假装已经完全复刻真实 DualSense 校准 feature report，而是把当前虚拟 HID 链路中可验证的最终 raw 输出先修正到一致。

## 已执行验证

- `dotnet run --project tools\tests\v60_packet_mapper_test\V60PacketMapperTest.csproj -c Release`：通过。
- `dotnet build windows\v60_viiper_app\Y700Switch2V60Viiper.csproj -c Release`：通过，0 warning，0 error。
- UI smoke：
  - 三模创建通过：新和联胜 / PS5、PS5 Edge、Pro2 / Nintendo、Xbox / XInput。
  - 后台推送约 125Hz 通过。
  - `-SkipServerFaultTest` 复测同样通过三模创建。
  - 失败项是测试脚本层面的旧日志匹配 / UI 控件 enabled 时序，不是本轮 IMU 输出崩溃；后续会单独修测试脚本。

## 仍需实机判断

本轮能证明量纲链路更正确，但不能单凭自动化证明“游戏手感已经和 Pro2 原生一样丝滑”。请实测时重点看：

- 静置时 PS5 / Edge tester 的 accel 是否稳定接近 `0,0,-8192`。
- 单轴旋转时 `[PS5_IMU_PIPE] mapped_ds` 是否只有一个主轴明显变化。
- 高速晃动后静置，gyro 是否快速回到接近 0，accel 是否回到稳定重力方向。

如果 r2 仍明显偏，下一步应补齐“真实 DualSense calibration feature report / timestamp / host 侧解释”的模拟，而不是继续盲调轴。

