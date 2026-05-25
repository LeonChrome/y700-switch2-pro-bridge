param(
    [string]$AdbPath,
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp",
    [string]$ControllerAddress = "38:C6:CE:27:FC:2D",
    [switch]$NoState,
    [switch]$Background,
    [switch]$PullLogs
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
$BuildScript = Join-Path $Root "build_switch2_ble_bridge.ps1"
$Jar = Join-Path $Root "switch2_ble_bridge.jar"
$RemoteJar = "/data/local/tmp/switch2_ble_bridge.jar"
$RemoteLog = "/data/local/tmp/switch2_ble_bridge.log"
$RemoteRaw = "/data/local/tmp/switch2_ble_input_raw.log"
$RemoteButtons = "/data/local/tmp/switch2_button_changes.log"

if ($PullLogs) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $outDir = Join-Path $Root "logs\ble_bridge_$stamp"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Invoke-Adb pull $RemoteLog (Join-Path $outDir "switch2_ble_bridge.log")
    Invoke-Adb pull $RemoteRaw (Join-Path $outDir "switch2_ble_input_raw.log")
    Invoke-Adb pull $RemoteButtons (Join-Path $outDir "switch2_button_changes.log")
    Write-Host "Pulled logs to $outDir"
    return
}

Write-Host "Building BLE bridge..."
powershell -NoProfile -ExecutionPolicy Bypass -File $BuildScript
if ($LASTEXITCODE -ne 0) {
    throw "BLE bridge build failed"
}

Write-Host "Using adb: $AdbPath"
Write-Host "Using device: $DeviceSerial"
& $AdbPath devices -l

Invoke-Adb push $Jar $RemoteJar

$psArgs = @()
if ($DeviceSerial) {
    $psArgs += @("-s", $DeviceSerial)
}
$psArgs += @("shell", "ps", "-A", "-o", "PID,USER,ARGS")
$psOut = & $AdbPath @psArgs
foreach ($line in $psOut) {
    if ($line -match '^\s*(\d+)\s+shell\s+.*\bSwitch2BleBridge\b') {
        Invoke-Adb shell kill $Matches[1]
    }
}

Invoke-Adb shell su -c "rm -f /data/local/tmp/switch2_state.txt"
Invoke-Adb shell "echo '' > /data/local/tmp/switch2_state.txt"

$argsList = @("--address", $ControllerAddress)
if ($NoState) {
    $argsList += "--no-state"
}
$argText = $argsList -join " "
$cmdLine = "CLASSPATH=$RemoteJar app_process64 /system/bin Switch2BleBridge $argText"

Write-Host ""
Write-Host "Put the Switch 2 Pro Controller into Bluetooth pairing/connect mode now."
Write-Host "Bridge logs:"
Write-Host "  $RemoteLog"
Write-Host "Raw input log:"
Write-Host "  $RemoteRaw"
Write-Host "Button transition log:"
Write-Host "  $RemoteButtons"
Write-Host ""

if ($Background) {
    Invoke-Adb shell "nohup sh -c '$cmdLine' >/data/local/tmp/switch2_ble_bridge.stdout 2>&1 &"
    Write-Host "Started in background."
    Write-Host "Watch logs with:"
    Write-Host "  $AdbPath -s $DeviceSerial shell tail -f $RemoteLog"
} else {
    Write-Host "Starting foreground BLE bridge. Press Ctrl+C here to stop it."
    Invoke-Adb shell $cmdLine
}
