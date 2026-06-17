# 新和联胜 VIIPER 版本 v6.2.0

## 重点变化

- BLE 连接建立后主动申请 Windows `ThroughputOptimized` 参数，不再只在检测到低频后补救。
- Pro2 GATT 发现升级为固定 UUID 优先、service/handle/property 动态 fallback，日志会输出 `gatt_mode`。
- GATT notify 订阅和初始化写命令增加小范围重试，减少瞬时 Windows BLE/GATT 抖动导致的假失败。
- UI 增加 `Backend` 档位，明确记录 VIIPER server、libVIIPER、Embedded USBIP 三条路线。
- 当前默认仍使用稳定 `VIIPER server` 三模路径，确保 PS5 HD haptic、PRO2、XBOX 的既有链路不被实验后端破坏。

## 发布文件

- `新和联胜VIIPER版本-aio-v6.2.0.exe`
- `XinHeLianSheng-VIIPER-aio-v6.2.0.exe`
- SHA256: `E3364DDCF48C9393895BB5B0A8B1EC8B380109E3C6DEA519A3173125F39F56E4`

## 说明

V6.2.0 是短链路路线的第一版落地：先把 BLE/GATT 根链路做硬化，并把后端切换边界立起来。完整内嵌 USBIP / libVIIPER 三模后端仍是后续版本的主线，当前不默认替换稳定 VIIPER server。
