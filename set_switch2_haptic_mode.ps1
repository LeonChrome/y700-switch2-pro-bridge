param(
    [ValidateSet("Game", "Rich", "LogOnly", "Stop")]
    [string]$Mode = "Game",
    [string]$AdbPath = "C:\Users\leon\Desktop\工具\platform-tools\adb.exe",
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp"
)

$ErrorActionPreference = "Stop"

function Invoke-AdbShellRoot {
    param([string]$Command)

    & $AdbPath -s $DeviceSerial shell "su -c '$Command'"
    if ($LASTEXITCODE -ne 0) {
        throw "adb shell exited with code $LASTEXITCODE"
    }
}

switch ($Mode) {
    "Rich" {
        Invoke-AdbShellRoot "rm -f /data/local/tmp/switch2_haptic_log_only; touch /data/local/tmp/switch2_haptic_cycle_rich"
        Write-Host "Switch 2 haptic mode: RichCycle test mode."
        Write-Host "Sustained Steam rumble cycles presets 1, 2, 5, 4, 6, 7."
    }
    "Game" {
        Invoke-AdbShellRoot "rm -f /data/local/tmp/switch2_haptic_cycle_rich /data/local/tmp/switch2_haptic_log_only"
        Write-Host "Switch 2 haptic mode: Game mode."
        Write-Host "Sustained Steam rumble uses vibration-focused presets 5 and 6."
    }
    "LogOnly" {
        Invoke-AdbShellRoot "rm -f /data/local/tmp/switch2_haptic_cycle_rich; touch /data/local/tmp/switch2_haptic_log_only; printf 'cmd 0a910102000800000000000000000000\n' > /data/local/tmp/switch2_ble_write.txt"
        Write-Host "Switch 2 haptic mode: LogOnly mode."
        Write-Host "Steam HID OUT is decoded and logged, but non-stop BLE haptic presets are suppressed."
    }
    "Stop" {
        Invoke-AdbShellRoot "printf 'cmd 0a910102000800000000000000000000\n' > /data/local/tmp/switch2_ble_write.txt"
        Write-Host "Sent Switch 2 haptic stop preset."
    }
}
