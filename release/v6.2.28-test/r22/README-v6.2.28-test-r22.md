# V6.2.28 Test r22

## 更新内容

- VIIPER 运行时与 USBIP 0.9.7.7 正式安装器均内嵌在单文件 EXE 中，用户无需额外下载 VIIPER。
- USBIP 是 Windows 内核驱动，首次安装仍需确认 UAC；安装器要求重启时必须重启 Windows。
- USBIP 查找同时覆盖 PATH、常见安装目录和卸载注册表中的 InstallLocation / DisplayIcon，减少“已安装但识别不到”。
- 明确区分“未安装”“已安装但驱动未就绪”“完全可用”三种状态。
- 如果使用不写注册表、不加入 PATH、也不位于常见目录的绿色版/便携版，失败提示会说明无法自动发现，并推荐使用 EXE 内置的正式安装器。
- 保留 R21 的 Pro2 无线输入、USBIP 有序 detach、Steam If_Hid 缓存诊断与四 Slot 功能。

## 校验

- `dotnet build`：0 警告、0 错误。
- `v60_packet_mapper_test`：通过。
- 发布包内嵌资源：VIIPER、VIIPER LICENSE、USBIP 安装器、USBIP LICENSE。

## 安装建议

优先直接运行本 Release 的单文件 EXE。第一次选择角色时，程序会在 USBIP 未安装的情况下打开内置正式安装器；安装结束后若提示重启，请先重启再继续。
