# Release 保留策略

本目录只保留两条仍有维护意义的版本线，避免历史测试包、publish 目录和临时 EXE 继续堆积。

## 当前保留

- `v5.9`：ESP32-S3 开发板路线，保留 5.9.2 之后的成果包与 DualSense/HD 震动相关记录。后续如果继续优化 ESP32 高回报率、PS5 HD 震动和 Pro2 稳定性，应沿这条线继续发布。
- `v6.2.16`：VIIPER / Windows BLE 路线的当前固化版。PS5 陀螺仪映射已确认并固化，作为这条路线的稳定基准。

## 不再保留

- V6.0 preview
- V6.1 系列
- V6.2.0 到 V6.2.15 的中间测试包
- 各类 `publish-*` 目录、构建 `artifacts`、临时本地 EXE

这些历史版本如果需要追溯，可以通过 git tag 或 GitHub Release 历史查找；当前工作区不再保存本地库存。

## 分发原则

- 正式 EXE 通过 GitHub Release 发布。
- 仓库里保留中文 README、SHA256 校验和必要的源码。
- 本地构建产物不进入 git，避免下一轮开发被旧库存干扰。
