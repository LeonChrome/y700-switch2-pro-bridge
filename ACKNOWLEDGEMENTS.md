# Acknowledgements

This project was inspired in part by the community work around Switch 2 controller support on Windows, especially `switch2-controller-windows10-dual-layouts`.

That project helped motivate a closer look at the Switch 2 Pro Controller layout and how Windows and Steam interpret Nintendo-style controller devices. This implementation uses a different architecture: a rooted Lenovo Y700 acts as a BLE-to-USB bridge. The real Switch 2 Pro Controller connects to the Y700 over BLE, the tablet parses the controller's private GATT notifications, and the tablet exposes a Nintendo-style USB HID gadget to Windows and Steam.

Thanks to the author of `switch2-controller-windows10-dual-layouts` for the useful reference point and for helping push community research around Switch 2 controller compatibility forward.
