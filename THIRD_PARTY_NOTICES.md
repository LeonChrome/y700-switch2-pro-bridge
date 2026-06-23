# 第三方组件与许可证

本源码分支可能包含或引用以下第三方组件：

- Espressif ESP-IDF / ESP TinyUSB：由 ESP-IDF 组件管理器或本地 IDF 环境提供，遵循对应上游许可证。
- TinyUSB：通过 ESP-IDF 组件链路使用，遵循 TinyUSB 上游许可证。
- esptool：用于 ESP32-S3 刷机，遵循 Espressif/esptool 上游许可证。
- VIIPER haptic runtime：仅作为实验/诊断路径的运行时依赖时使用，许可证见 `tools/viiper/haptic-v0.8.0/LICENSE.txt`。

仓库清理时没有把 `managed_components/` 和 VIIPER 上游完整源码镜像放入本分支；需要追溯上游源码时请访问对应官方仓库。