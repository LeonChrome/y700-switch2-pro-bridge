# V5.9 源码清理说明

本分支是 ESP32-S3 路线的干净源码视图，刻意排除了以下内容：

- `work/`、`artifacts/`、`bin/`、`obj/` 等本地构建产物。
- ESP-IDF 自动下载的 `managed_components/`。
- V6.2.16 Windows-only VIIPER 管理器。
- 旧 V6.0/V6.1/V6.2 中间测试包。
- 本地 release EXE/ZIP 库存。

保留的二进制仅限 V5 管理器内置刷机资源或必要工具。后续若要进一步纯源码化，可把内置固件二进制改为发布流程生成，而不是跟随源码分支。