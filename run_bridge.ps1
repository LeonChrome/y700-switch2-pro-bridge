param(
    [string]$AdbPath,
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp",
    [string]$EventPath,
    [switch]$SkipSetup,
    [switch]$ListOnly
)

$ErrorActionPreference = "Stop"

if (!$AdbPath) {
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) {
        $AdbPath = $cmd.Source
    } else {
        throw "adb.exe was not found in PATH. Pass -AdbPath <path-to-adb.exe>"
    }
}

function Invoke-Adb {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $fullArgs = @()
    if ($DeviceSerial) {
        $fullArgs += @("-s", $DeviceSerial)
    }
    $fullArgs += $Arguments

    Write-Host "> adb $($fullArgs -join ' ')"
    & $AdbPath @fullArgs
    if ($LASTEXITCODE -ne 0) {
        throw "adb exited with code $LASTEXITCODE"
    }
}

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Scripts = @(
    "setup_y700_gamepad_v2.sh",
    "list_gamepad_events.sh",
    "capture_evdev_events.sh",
    "bridge_evdev_to_hidg0.sh"
)

Write-Host "Using adb: $AdbPath"
Write-Host "Using device: $DeviceSerial"
& $AdbPath devices -l

foreach ($script in $Scripts) {
    Invoke-Adb push (Join-Path $Root $script) "/data/local/tmp/$script"
}

Invoke-Adb shell su -c "chmod 755 /data/local/tmp/setup_y700_gamepad_v2.sh /data/local/tmp/list_gamepad_events.sh /data/local/tmp/capture_evdev_events.sh /data/local/tmp/bridge_evdev_to_hidg0.sh"

if (!$SkipSetup) {
    Invoke-Adb shell su -c "sh /data/local/tmp/setup_y700_gamepad_v2.sh"
}

Write-Host ""
Write-Host "Gamepad candidates:"
Invoke-Adb shell su -c "sh /data/local/tmp/list_gamepad_events.sh"

if ($ListOnly) {
    exit 0
}

Write-Host ""
Write-Host "Starting bridge. Leave this PowerShell window open; press Ctrl+C to stop."
if ($EventPath) {
    Invoke-Adb shell su -c "EVENT=$EventPath sh /data/local/tmp/bridge_evdev_to_hidg0.sh"
} else {
    Invoke-Adb shell su -c "sh /data/local/tmp/bridge_evdev_to_hidg0.sh"
}
