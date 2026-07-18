# V6.2.31 Pro2 离线提示与异常断联诊断版

## 更新重点
- 每个已经进入 live 的 Pro2 BLE 连接离线时都会弹出一次系统提示，四个 Slot 独立记录、独立判断。
- 同时监听 Windows `BluetoothLEDevice.ConnectionStatusChanged` 与 `GattSession.SessionStatusChanged`，记录底层连接状态和 `BluetoothError`。
- 如果 Windows 明确报告蓝牙无线电不可用、资源被占用、策略禁用或未分类错误，弹出“异常断联警报”。
- 如果 Windows 只报告远端设备已断开，但没有给出物理原因，弹出“手柄已离线”，并明确说明无法区分手柄关机、没电、离开范围或无线链路丢失。
- 如果超过 2 秒没有 FD2 输入、但 Windows 尚未先报告物理断开，记录为疑似异常断流；同时提醒也可能是关机或没电后的系统状态更新延迟。
- 每次连接会话只提示一次。自动重连成功后会重新布防，下一次离线仍会提示。
- 程序主动断开、退出、扫描不到候选手柄或连接尚未达到 live 时不会误报为断联。

## 诊断日志
- `[PRO2_BLE_LINK]`：Windows 连接状态和 GATT 会话状态。
- `[PRO2_BLE_DISCONNECT_SIGNAL]`：底层离线证据。
- `[PRO2_OFFLINE_NOTICE]`：最终用户提示及严重级别。
- 日志会保留 Slot、连接序号、BLE 地址、最后输入年龄、Windows 状态、BluetoothError 和可用的最后电量信息。

## Windows 能力边界
- Windows 能确认设备断开，也能在部分场景提供 GATT/Bluetooth 错误。
- Windows BLE API 通常不会告诉应用“手柄是人工关机还是电池耗尽”，因此本版本不会伪造具体原因。
- 如果 Pro2 报文没有提供可用电量，弹窗和日志会显示“电量未知”。

## 保持不变
- 保留 V6.2.30 的按键边沿热修：BLE 短暂没有新帧时不再注入虚假松开帧。
- 继续使用真实 BLE 源节奏与 latest-state，不强制补到 250Hz。
- 摇杆、按键映射、陀螺仪、加速度、HD 震动、普通震动、VIIPER/USBIP 和四模式身份均未改。
