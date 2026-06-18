# V6.2.14 新和联胜 VIIPER 版本

这是 V6.2.14 的稳定诊断版，目标是把 V6.2.x 已经可用的三模链路继续打磨到更适合普通用户使用。

## 主要更新

- 主界面布局锁定：进入游戏、LIVE 状态、自动重连状态变化时不再挤动界面。
- PS5 音频保护：PS5 模式启动后会检查 Windows 默认播放、默认通信播放、默认麦克风、默认通信麦克风；如果 DualSense 被设成默认，会自动切回真实声卡/耳机/麦克风。
- 新增“修复音频默认”按钮：不用进 Windows 声音面板，点一下即可手动修正默认音频设备。
- BLE 适配器诊断：扫描时会输出默认蓝牙适配器、BLE Central 支持、Radio 状态、active/passive 扫描结果、原始 BLE 广播计数和过滤样本。
- 主界面直接显示 BLE 失败原因：区分“Windows 没收到任何 BLE 广播”和“收到 BLE 广播但没有匹配到 Pro2”。
- 自动重连冷却退避：参考 joycon2cpp 对 Switch 2 / Pro 2 控制器频繁连接会触发控制器级冷却的观察，连续失败时从 2.5s 逐步退避到 5s、10s、30s，仍持续自动守护，但不再猛连手柄。
- 保留 V6.2.13 的 PS5 IMU Map 下拉选择，默认 SDL/Nintendo 基线，PRO2 模式 IMU 不被改动。

## 关于 joycon2cpp 参考

检查了 TheFrano/joycon2cpp 后，没有直接搬入它的 ViGEm/DS4 输出实现，因为本项目当前稳定链路是 VIIPER + usbip-win2 + 三模虚拟 USB，架构不同。实际吸收的是两个经验：

- 输出频率应区分 BLE 真实采样和虚拟设备刷新。joycon2cpp 的 LowLatency / Balanced120Hz / Legacy60Hz 与本项目现有 66Hz / 125Hz / 250Hz 档位方向一致。
- Switch 2 / Pro 2 控制器在短时间内被反复连接/配对可能进入几分钟冷却。V6.2.14 已把这个经验落实成自动重连退避。

## 使用建议

- 推荐先使用默认 `125Hz` 推送。
- PS5 模式下建议保持 `PS5 音频保护` 开启。
- 如果蓝牙接收器识别不到手柄，请导出日志，重点查看 `[BLE_ADAPTER]`、`[BLE_RADIO]`、`[PRO2_BLE_DIAG]`。
- 如果 `[PRO2_BLE_DIAG] raw_ads=0`，优先检查蓝牙接收器、驱动、蓝牙开关或 Windows 蓝牙服务。
- 如果 `raw_ads>0` 但 `candidates=0`，优先确认 Pro2 是否处于配对广播、是否被 Switch/ESP32/手机/旧进程占用。

## 校验

- `dotnet build windows/v60_viiper_app/Y700Switch2V60Viiper.csproj -c Release`
- `dotnet run --project tools/tests/v60_packet_mapper_test/V60PacketMapperTest.csproj -c Release`
- `dotnet run --project tools/tests/v60_fd2_replay/V60Fd2Replay.csproj -c Release -- --synthetic --quiet`
