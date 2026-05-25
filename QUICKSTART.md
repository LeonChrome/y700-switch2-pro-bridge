# Quickstart

This is the shortest path for using the stable v3 package on another Windows PC.

## Requirements

- Rooted Lenovo Y700 tablet with wireless debugging or USB ADB access.
- Switch 2 Pro Controller paired or ready to connect to the Y700 over Bluetooth.
- Windows PC with `adb.exe` available in `PATH`, beside `Y700Switch2Launcher.exe`, or passed with `--adb`.
- The stable artifact folder:

```text
release/v3-stable-20260525-input-rumble
```

## Files Needed

The stable folder should include at least:

```text
Y700Switch2Launcher.exe
switch2_ble_bridge_v3.jar
switch2_ffs_responder_v3.jar
setup_y700_switch2_proto_v3.sh
```

## Start

Open PowerShell in the stable release folder.

```powershell
.\Y700Switch2Launcher.exe start
```

Wireless ADB is strongly recommended, because reconfiguring the USB gadget can disconnect USB ADB:

```powershell
.\Y700Switch2Launcher.exe start --serial 192.168.x.x:port
```

If `adb.exe` is not in `PATH`:

```powershell
.\Y700Switch2Launcher.exe start --adb C:\path\to\adb.exe --serial 192.168.x.x:port
```

## Check Status

```powershell
.\Y700Switch2Launcher.exe status --serial 192.168.x.x:port
```

## Rumble Smoke Test

```powershell
.\Y700Switch2Launcher.exe haptic-test --serial 192.168.x.x:port
```

## Stop

```powershell
.\Y700Switch2Launcher.exe stop --serial 192.168.x.x:port
```

## Pull Logs

```powershell
.\Y700Switch2Launcher.exe logs --serial 192.168.x.x:port
```
