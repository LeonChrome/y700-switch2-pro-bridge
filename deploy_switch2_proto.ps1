param(
    [string]$AdbPath,
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp",
    [switch]$ForegroundSetup
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
$BuildScript = Join-Path $Root "build_switch2_responder.ps1"
$Jar = Join-Path $Root "switch2_ffs_responder.jar"
$Setup = Join-Path $Root "setup_y700_switch2_proto.sh"
$DetachedSetup = Join-Path $Root "setup_y700_switch2_proto_detached.sh"
$InputProbe = Join-Path $Root "send_switch2_test_input.sh"

Write-Host "Building responder..."
powershell -NoProfile -ExecutionPolicy Bypass -File $BuildScript
if ($LASTEXITCODE -ne 0) {
    throw "responder build failed"
}

Write-Host "Using adb: $AdbPath"
Write-Host "Using device: $DeviceSerial"
& $AdbPath devices -l

Invoke-Adb push $Jar /data/local/tmp/switch2_ffs_responder.jar
Invoke-Adb push $Setup /data/local/tmp/setup_y700_switch2_proto.sh
Invoke-Adb push $DetachedSetup /data/local/tmp/setup_y700_switch2_proto_detached.sh
Invoke-Adb push $InputProbe /data/local/tmp/send_switch2_test_input.sh
Invoke-Adb shell "su -c 'chmod 755 /data/local/tmp/setup_y700_switch2_proto.sh /data/local/tmp/setup_y700_switch2_proto_detached.sh /data/local/tmp/send_switch2_test_input.sh'"

if ($ForegroundSetup) {
    Invoke-Adb shell "su -c 'sh /data/local/tmp/setup_y700_switch2_proto.sh'"
} else {
    $RunLog = "/data/local/tmp/setup_y700_switch2_proto_run.log"
    Invoke-Adb shell "su -c 'sh /data/local/tmp/setup_y700_switch2_proto_detached.sh'"
    Write-Host "Setup started detached; this survives USB gadget rebind disconnects."
    Start-Sleep -Seconds 8
    try {
        Invoke-Adb shell "su -c 'tail -n 120 $RunLog 2>/dev/null'"
    } catch {
        Write-Host "ADB disconnected while setup continued. Reconnect wireless ADB, then inspect $RunLog."
    }
}

Write-Host ""
Write-Host "To watch responder logs:"
Write-Host "  $AdbPath -s $DeviceSerial shell su -c `"tail -f /data/local/tmp/switch2_ffs_responder.log`""
