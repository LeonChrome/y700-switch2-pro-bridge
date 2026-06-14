# VIIPER Runtime

This folder stores the Windows VIIPER runtime used by the V6.0 no-ESP32 route.

Current runtime:

```text
version:      v0.7.0
exe:          tools/viiper/v0.7.0/viiper.exe
exe sha256:   1868d682f4cc6d62349bbccbf0727b05d3eb6e22027ac34f0f1d9b1de56f2ddc
source zip:   https://github.com/Alia5/VIIPER/releases/download/v0.7.0/viiper-windows-amd64.zip
source sha256 a02b06751d64e43e7700aba8ee1f7e3e4f5f4e7f370a11722ff922ab075c1629
```

Start VIIPER server:

```powershell
.\tools\viiper\v0.7.0\viiper.exe server --api.addr=127.0.0.1:3242 --usb.addr=127.0.0.1:3241
```

The V6.0 preview Manager also embeds this runtime. Its `启动本地 VIIPER` button
starts the repo-side copy when available, otherwise extracts the embedded copy
to `%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\embedded\v6.0.0-preview`.
Server logs are written under `%LOCALAPPDATA%\PRO2WirelessReceiverControlBoard\v6_logs`.

Windows still needs `usbip-win2` installed for local USBIP attach:

<https://github.com/vadimgrn/usbip-win2>
