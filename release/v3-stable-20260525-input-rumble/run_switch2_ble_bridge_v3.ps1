param(
    [string]$AdbPath,
    [string]$DeviceSerial,
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
$BuildScript = Join-Path $Root "build_switch2_ble_bridge_v3.ps1"
$Jar = Join-Path $Root "switch2_ble_bridge_v3.jar"
$RemoteJar = "/data/local/tmp/switch2_ble_bridge_v3.jar"
$RemoteLog = "/data/local/tmp/switch2_ble_bridge_v3.log"
$RemoteRaw = "/data/local/tmp/switch2_ble_input_raw_v3.log"
$RemoteButtons = "/data/local/tmp/switch2_button_changes_v3.log"

if ($PullLogs) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $outDir = Join-Path $Root "logs\ble_bridge_v3_$stamp"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Invoke-Adb pull $RemoteLog (Join-Path $outDir "switch2_ble_bridge_v3.log")
    Invoke-Adb pull $RemoteRaw (Join-Path $outDir "switch2_ble_input_raw_v3.log")
    Invoke-Adb pull $RemoteButtons (Join-Path $outDir "switch2_button_changes_v3.log")
    Write-Host "Pulled logs to $outDir"
    return
}

Write-Host "Building BLE bridge v3..."
powershell -NoProfile -ExecutionPolicy Bypass -File $BuildScript
if ($LASTEXITCODE -ne 0) {
    throw "BLE bridge v3 build failed"
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
    if ($line -match '^\s*(\d+)\s+shell\s+.*\bSwitch2BleBridgeV3\b') {
        Invoke-Adb shell kill $Matches[1]
    }
}

Invoke-Adb shell su -c "rm -f /data/local/tmp/switch2_ble_write_v3.txt /data/local/tmp/switch2_haptic_log_only_v3"

$argsList = @("--address", $ControllerAddress)
if ($NoState) {
    $argsList += "--no-state"
}
$argText = $argsList -join " "
$cmdLine = "CLASSPATH=$RemoteJar app_process64 /system/bin Switch2BleBridgeV3 $argText"

Write-Host ""
Write-Host "Put the Switch 2 Pro Controller into Bluetooth pairing/connect mode now."
Write-Host "Bridge v3 logs:"
Write-Host "  $RemoteLog"
Write-Host "Raw input log:"
Write-Host "  $RemoteRaw"
Write-Host "Button transition log:"
Write-Host "  $RemoteButtons"
Write-Host ""

if ($Background) {
    Invoke-Adb shell "nohup sh -c '$cmdLine' >/data/local/tmp/switch2_ble_bridge_v3.stdout 2>&1 &"
    Write-Host "Started BLE bridge v3 in background."
    Write-Host "HD self-test command after BLE connects:"
    Write-Host "  $AdbPath -s $DeviceSerial shell su -c `"echo play-hd > /data/local/tmp/switch2_ble_write_v3.txt`""
} else {
    Write-Host "Starting foreground BLE bridge v3. Press Ctrl+C here to stop it."
    Invoke-Adb shell $cmdLine
}
