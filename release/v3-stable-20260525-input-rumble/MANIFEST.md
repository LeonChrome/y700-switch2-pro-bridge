# v3 stable artifact manifest

Date: 2026-05-25

This folder freezes the current working Y700 + Switch 2 Pro v3 bridge after live validation of accurate button forwarding and rumble feedback.

## Artifacts

```text
cce84946298db72eff9f9498adacc038  build_switch2_ble_bridge_v3.ps1
562cbb15223944f9701ced22ed190071  build_switch2_responder_v3.ps1
460072bdf4b96fb7ca40a3d4ac47ae75  capture_switch2_button_map.ps1
e65508b061785e411e3471c0e1b274a5  restart_switch2_responder_v3.ps1
506707c39fdc2de705df79bf60627f25  run_switch2_ble_bridge_v3.ps1
87b4f21289cb99c47941de7e97f54ee8  setup_y700_switch2_proto_v3.sh
a838c8d038cf4bdd8c65ea6e0c76fae8  setup_y700_switch2_proto_v3_detached.sh
5a449bfe5ff6d6425460939bf9d3811d  set_switch2_haptic_mode_v3.ps1
d5146215966668dcb74c0ece5111a78a  switch2_ble_bridge_v3.jar
9403c3f992ad6d237fb302b6755d6c27  switch2_ffs_responder_v3.jar
dfe9340ba9e2c65116157981d2a68749  switch2_v3_hd_bridge_notes.md
bdd73b99eae6ab4659d8ed2b4b549963  Y700Switch2Launcher.exe
```

Windows launcher SHA256:

```text
b39e3961bd8497776a9c3e00f82f58f6e3d72c1e4f19aea24de504e2828f03ab  Y700Switch2Launcher.exe
```

## Matching deployed jars

```text
d5146215966668dcb74c0ece5111a78a  /data/local/tmp/switch2_ble_bridge_v3.jar
9403c3f992ad6d237fb302b6755d6c27  /data/local/tmp/switch2_ffs_responder_v3.jar
```

## Stable runtime commands

BLE bridge:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_switch2_ble_bridge_v3.ps1 -AdbPath "<path-to-adb.exe>" -DeviceSerial "<serial>" -Background
```

Responder/gadget:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\restart_switch2_responder_v3.ps1 -AdbPath "<path-to-adb.exe>" -DeviceSerial "<serial>"
```

Guided v3 button capture:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\capture_switch2_button_map.ps1 -V3 -AdbPath "<path-to-adb.exe>" -DeviceSerial "<serial>"
```
