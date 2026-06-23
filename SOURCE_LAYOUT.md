# V6.2.16 源码清理说明

本分支是 Windows-only VIIPER 路线的干净源码视图，刻意排除了：

- ESP32-S3 固件与 V5.9 管理器。
- VIIPER 上游完整源码镜像 `tools/viiper/haptic-src`。
- V6.0/V6.1/V6.2.0~V6.2.15 中间测试包。
- `work/`、`artifacts/`、`bin/`、`obj/` 等本地构建产物。

保留 `viiper-haptic.exe` 与 usbip-win2 安装器，是因为当前 V6.2.16 EXE 的用户体验依赖这两个运行时。对应许可证随目录保留。