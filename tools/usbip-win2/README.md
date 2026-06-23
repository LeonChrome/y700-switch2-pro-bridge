# usbip-win2 Runtime

This folder stores the Windows USB/IP driver installer used by the V6.0
VIIPER route.

Current runtime:

```text
version:       v.0.9.7.7
installer:     tools/usbip-win2/v0.9.7.7/USBip-0.9.7.7-x64.exe
installer sha: 51620fa5f9f8be5932bc9d786deee557ce06d5407a99cab490dcfac71f185fea
source:        https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.7.7
asset:         https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.7.7/USBip-0.9.7.7-x64.exe
license:       BSD-2-Clause
```

The Manager does not install the driver silently. The `安装/修复 usbip-win2`
button launches this installer with UAC because the package installs a Windows
USB/IP kernel driver. USB devices may restart during installation, and a reboot
may be required by the installer.
