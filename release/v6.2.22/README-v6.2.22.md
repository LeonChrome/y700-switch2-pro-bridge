# V6.2.22 四人三模版

## 更新重点

- 新和联胜 / PS5、PS5 Edge / 背键、Pro2 / Nintendo、Xbox / XInput 全模式支持 1-4 个独立 Pro2 BLE Slot。
- 每个 Slot 独立创建 VIIPER 虚拟设备、独立连接真实 Pro2、独立输入和震动回传。
- 切模式、停止、异常恢复会自动清理 USBIP 残留端口，减少 Steam 重复枚举。
- USBIP 已安装但驱动未就绪时，不再反复自动启动安装器；通常需要重启 Windows。
- 新增手柄图标，统一窗口、任务栏和托盘视觉。

## 使用提醒

- 首次使用若安装 USBIP 后仍提示驱动未就绪，请先重启 Windows。
- PS5 Edge 是独立 `054C:0DF2` 身份；少数游戏可能只对白名单中的标准 DualSense `054C:0CE6` 启用原生陀螺仪，此类游戏请使用“新和联胜 / PS5”模式。
