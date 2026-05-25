param(
    [ValidateSet("hd", "preset-fallback", "log-only")]
    [string]$Mode = "hd",
    [string]$AdbPath,
    [string]$DeviceSerial
)

$ErrorActionPreference = "Stop"

if (!$AdbPath) {
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) {
        $AdbPath = $cmd.Source
    } else {
        throw "adb.exe was not found in PATH. Pass -AdbPath C:\path\to\adb.exe"
    }
}

$argsList = @()
if ($DeviceSerial) {
    $argsList += @("-s", $DeviceSerial)
}

$remote = "rm -f /data/local/tmp/switch2_haptic_log_only_v3 /data/local/tmp/switch2_haptic_preset_fallback_v3"
if ($Mode -eq "log-only") {
    $remote += "; touch /data/local/tmp/switch2_haptic_log_only_v3"
}
if ($Mode -eq "preset-fallback") {
    $remote += "; touch /data/local/tmp/switch2_haptic_preset_fallback_v3"
}

Write-Host "> adb $($argsList -join ' ') shell su -c $remote"
& $AdbPath @argsList shell su -c $remote
if ($LASTEXITCODE -ne 0) {
    throw "adb exited with code $LASTEXITCODE"
}

Write-Host "Switch 2 haptic v3 mode: $Mode"
