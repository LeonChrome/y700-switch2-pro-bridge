# Acknowledgements

中文：

本项目受到 Switch 2 手柄 Windows 支持相关社区工作的启发，尤其是 `switch2-controller-windows10-dual-layouts`。

那个项目让我更深入地去研究 Switch 2 Pro 手柄的按键布局，以及 Windows / Steam 如何识别 Nintendo 风格的控制器设备。我的实现最后走了另一条路线：使用一台 root 后的联想 Y700 作为 BLE 转 USB 桥接器。真实 Switch 2 Pro 手柄通过 BLE 连接到 Y700，Y700 解析手柄的私有 GATT 通知，再通过 USB Gadget / FunctionFS 向 Windows 和 Steam 暴露 Nintendo 风格的 USB HID 设备。

感谢 `switch2-controller-windows10-dual-layouts` 的作者提供了重要参考和启发，也感谢相关社区研究推动了 Switch 2 手柄兼容性的探索。

English:

This project was inspired in part by the community work around Switch 2 controller support on Windows, especially `switch2-controller-windows10-dual-layouts`.

That project helped motivate a closer look at the Switch 2 Pro Controller layout and how Windows and Steam interpret Nintendo-style controller devices. This implementation uses a different architecture: a rooted Lenovo Y700 acts as a BLE-to-USB bridge. The real Switch 2 Pro Controller connects to the Y700 over BLE, the tablet parses the controller's private GATT notifications, and the tablet exposes a Nintendo-style USB HID gadget to Windows and Steam.

Thanks to the author of `switch2-controller-windows10-dual-layouts` for the useful reference point and for helping push community research around Switch 2 controller compatibility forward.
