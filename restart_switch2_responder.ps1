param(
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

Write-Host "Building responder..."
powershell -NoProfile -ExecutionPolicy Bypass -File $BuildScript
if ($LASTEXITCODE -ne 0) {
    throw "responder build failed"
}

Invoke-Adb push $Jar /data/local/tmp/switch2_ffs_responder.jar

$Remote = @'
pids="$(ps -A -o PID,ARGS 2>/dev/null | grep Switch2FfsResponder | grep -v grep | awk '{print $1}' || true)"
for pid in $pids; do kill "$pid" 2>/dev/null || true; done
sleep 0.3
rm -f /data/local/tmp/switch2_ffs_ready /data/local/tmp/switch2_ffs_responder.log
setsid sh -c 'CLASSPATH=/data/local/tmp/switch2_ffs_responder.jar app_process64 /system/bin Switch2FfsResponder /dev/usb-ffs/switch2 /dev/hidg0 >>/data/local/tmp/switch2_ffs_responder.log 2>&1' >/dev/null 2>&1 & true
i=0
while [ "$i" -lt 50 ]; do [ -e /data/local/tmp/switch2_ffs_ready ] && break; sleep 0.1; i=$((i + 1)); done
tail -n 80 /data/local/tmp/switch2_ffs_responder.log 2>/dev/null
'@

$Compact = ($Remote -split "`r?`n" | ForEach-Object { $_.TrimEnd() }) -join "; "
Invoke-Adb shell su -c $Compact
