# Pro2 无线 IMU 链路研究结论

## 目标

在不伪造运动、不增加滤波延迟的前提下，让 Pro2 BLE 输入经 VIIPER 输出时尽可能保持有线 Pro2 的时间语义和角速度积分。

## 公开实现交叉结论

1. SDL 的 Switch 2 驱动把陀螺仪和加速度计声明为 250 Hz，并从每份输入报告读取运动时间戳。SDL 的坐标映射为 `+X,+Z,-Y`，同时读取手柄工厂 IMU 零偏。
   - https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_switch2.c
2. JoyShockMapper 默认约 333 Hz 轮询控制器的最新状态，以改善 66.67 Hz Switch 输入在高刷新显示器上的阶梯感。它的虚拟 Sony 输出使用独立主机时间，不把源报告时间当成虚拟设备时间；文档也明确指出平滑必然增加延迟，不应覆盖快速运动。
   - https://github.com/Electronicks/JoyShockMapper
3. VIIPER 上游的虚拟 Switch 2 Pro 设备为每份最终 USB 报告生成单调运动时间，而不是复用输入源时间。
   - https://github.com/Alia5/VIIPER
4. Switch 2 BLE 公开逆向工具在订阅输入通知前向输入特征的报告率描述符写入 `0x0085`，即请求 133 Hz，然后再请求 Windows 11 的 `ThroughputOptimized` 连接参数。
   - https://gist.github.com/ndeadly/7d27aa63e2f653a902a2474dbcbc08b3
5. Windows WinRT 允许枚举、读写 GATT 描述符，也允许请求预定义连接参数；最终连接间隔仍由 Windows、蓝牙适配器和手柄共同决定。
   - https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattcharacteristic.getdescriptorsasync
   - https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattdescriptor
   - https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.requestpreferredconnectionparameters

## 已确认根因

V6.2.28 r16 的 C# 层可以按顺序把约 66 Hz 的真实 BLE 样本交给 VIIPER，USB/IP 端点也确实以约 250 Hz 被轮询。但本项目的 VIIPER 分支把 `MotionTimestampUs` 从 BLE 样本直接写入最终虚拟 Pro2 报告。

当一个 BLE 样本被重复四次时，四份 USB 报告的运动时间戳也完全相同。对依赖传感器时间的上层而言，这不是“250 Hz 输出同一最新角速度”，而是“一个约 66 Hz 的运动时钟被装进 250 Hz 报告外壳”。这与真实有线 Pro2、SDL 和 VIIPER 上游的时间语义都不一致。

## 正确链路

```text
Pro2 BLE 通知（实测 60/66/133 Hz）
  -> 保留原始轴、倍率、工厂/人工零偏
  -> 新样本按到达顺序进入短队列
  -> VIIPER USB 端点每 4 ms 取下一新样本；队列空时保持 latest_state
  -> 每份最终 USB 报告生成独立、严格单调的虚拟运动时间
  -> Steam / SDL 看到稳定 250 Hz USB 时间轴
```

这里使用零阶保持，不使用插值、外推、低通、死区或回零吸附。对两个真实采样点之间未知的运动，零阶保持不会创造虚假峰值，也不会增加额外滤波延迟。

若角速度在相邻 BLE 样本间按最新值保持，则连续角度为：

`theta = sum(omega_k * (t_(k+1) - t_k))`

把同一 `omega_k` 拆成若干个 4 ms USB 时间片后，各时间片之和仍等于原区间，因此不会因为 250 Hz 输出而改变角速度倍率或积分面积。

## r17 现场验证

1. 虚拟 Pro2 最终 HID 报告不再写入 BLE 源时间戳；每份 USB 报告使用端点自己的单调微秒时钟。
2. BLE 源时间戳仍保留在 C# 到 VIIPER 的内部数据中，只用于样本顺序和诊断。
3. 初始化完成后自动枚举 FD2 描述符，安全请求 `133 Hz` 报告率，再订阅 FD2 通知。
4. 描述符不存在、拒绝写入或平台不支持时不破坏连接，自动退回实测通知率。
5. 日志同时记录请求值、描述符句柄、写入状态、Windows 连接间隔和最终实测 `parsed_hz`，不再用目标值冒充实际频率。

r17 连续运行约一小时后：真实 BLE 新帧约 66.6 Hz，累计 235,666 帧，队列输入/输出完全相等；解析失败、队列丢弃、USB detach、设备重建和 VIIPER 断流均为 0。系统侧 HID 抓取去掉探针前两秒预热后为 250.001 Hz，运动时间戳在每份报告中都推进，IMU 数值变化率为 66.58 Hz。

同一实体手柄的有线静置基线与 r17 无线静置对比：有线 gyro raw 标准差为 0.894/0.896/1.065，r17 为 0.793/0.928/1.063；加速度模长均值分别为 4179.20 和 4188.47 raw。噪声和尺度没有出现足以解释明显手感差异的退化。

## r18 时间基准收口

现场对照同时发现，真实有线 Pro2 的 32 位报告计数在每份 4 ms 报告中固定增加 4，运动时间戳由内部传感器节拍推进；r17 的报告计数增加 1，运动时间戳虽然单调，但仍包含 Windows 调度抖动。

r18 因此进一步规定：

1. 32 位报告计数每份报告增加 `input_interval_ms`，默认 4。
2. 运动时间戳每份报告增加 `input_interval_ms * 1000` 微秒，默认 4000。
3. 两个时钟均不再依赖 BLE 源时间或 Windows 线程唤醒时间。
4. BLE 样本值仍按零阶保持输出，不改变轴、倍率、bias 或积分面积。

### r18 长时间现场结果

本机 r18 连续运行约 49 分钟后，系统侧再次直接抓取最终 Pro2 HID：

- 20 秒收到 4,999 份报告，平均 249.7 Hz；
- 报告计数 4,498 次相邻差值全部为 `+4`；
- 运动时间戳 4,498 次相邻差值全部为 `+4000 us`；
- USBIP detach、虚拟设备重建、VIIPER stream disconnect、队列丢弃和解析失败均为 0；
- 最终 HID 中 IMU 数值变化率约 64.1 Hz，与真实 BLE 输入一致。

因此 r18 已经排除“250 Hz 虚拟端点时间轴错误”和“跨进程链路断流”。剩余抖动发生在
Windows 向应用交付 FD2 GATT 通知的入口：整段日志累计出现大量大于 45 ms 的通知间隔，
同时总体平均仍接近 64 Hz，表现为通知延后后成批到达，而不是手柄或虚拟 USB 设备断线。

震动回写不是简单的直接根因。按遥测窗口分组，存在回写的区间平均约 64.31 Hz，未新增回写的
区间约 64.04 Hz；最差 45.7 Hz 窗口没有新增 BLE 写入。持续震动可能影响无线调度，但现有证据
不足以把全部通知抖动归因于震动。

## r19 FD2 回调热路径减负

r18 的通知处理函数在每份约 15 ms 的 FD2 报告中执行了与实时输入无关的高成本诊断：重建完整
状态字符串、多次克隆状态、把 63 字节报告转成十六进制字符串，并创建多组快照对象写入诊断
环形缓冲。这些工作位于 WinRT `ValueChanged` 回调本身，可能让后续通知在 Windows 分发队列中
等待，且不会反映为 VIIPER 写入反压。

r19 保持全部输入数值、顺序和时钟策略不变，只做以下链路减负：

1. `Status` 和完整 metrics 改为被读取时生成，不再每份 FD2 报告重建；
2. 通知摘要最多每秒更新一次；
3. `RawDirect` 不再为无变换路径创建多份等价状态克隆；
4. IMU 子样本时间戳在原数组中写入，不再额外分配数组；
5. 原始十六进制快照环形缓冲默认关闭，仅在 `PRO2_RAW_INTEGRITY_MODE=1` 或
   `PRO2_FD2_SPIKE_CAPTURE=1` 时启用；
6. 新增无字符串分配的 `notify_handler_avg_us/max_us/over1ms/over4ms/over8ms` 计数。

这不是滤波或异常掩盖。r19 不改变任何按键、摇杆、IMU、震动报文，也不伪造 BLE 样本；目标是
让 WinRT 回调尽快返回，减少应用自身造成的通知排队。

## r20 模式切换会话边界

r19 长时间现场记录确认正常运行时不存在 USBIP detach、VIIPER stream disconnect、设备重建、
FD2 解析失败或震动写失败。现场从 Pro2 切换到 PS5 Edge 时则暴露出一个独立生命周期问题：
虚拟设备重建约数秒期间 BLE 仍在入队，新会话会先消费旧会话积压，曾形成 196 帧溢出和约
950 ms 的短暂历史输入回放。

r20 在 VIIPER 数据流已经打开、输入消费线程尚未启动的原子边界清空顺序队列。BLE 连接、最新
状态、IMU 换算和输出节拍均不改变；只禁止上一虚拟设备生命周期的帧进入下一设备。日志以
`[INPUT_SESSION] ... discarded_frames=N policy=no_cross_session_replay` 明确记录交接结果。

## 能做到与不能做到

- 可以做到：保持真实 BLE 样本顺序和数值；让 250 Hz 虚拟 USB 报告拥有正确时间轴；在平台允许时请求真实 133 Hz BLE 通知；避免滤波造成的相位延迟。
- 不能做到：从 66 Hz 输入恢复真实存在但未被采样的 250 Hz 高频运动细节。任何声称能无损恢复的插值或预测都会在快速反向时制造过冲或相位错误。
- 手柄内 IMU 的安装位置不会改变刚体角速度，所以不是陀螺仪转换困难的根因。安装位置会影响快速转动时测得的线性加速度；在不知道手部旋转中心和位移的情况下，无法让两个不同外形手柄的动态加速度完全一致，但这不要求修改陀螺仪角速度。

## 验证边界

自动测试必须保证：

1. BLE 源时间戳可在内部线协议中无损保留。
2. 最终 USB 运动时间戳不等于源时间戳，并在重复 latest_state 时仍严格递增。
3. 短队列中的真实样本按顺序输出，重复阶段只重复最后值。
4. Pro2 默认 USB `bInterval=4 ms`。
5. PS5、Edge、Xbox 的 IMU 映射、倍率、震动和连接逻辑不因本研究改动。

现场日志只负责确认具体电脑最终拿到 66 Hz 还是 133 Hz，不再承担判断算法对错的工作。
