param(
    [string]$AdbPath,
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp",
    [ValidateSet("switch2", "switchpro", "restore")]
    [string]$Mode = "switch2",
    [switch]$SkipSetup,
    [switch]$RunTest
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
$Setup = Join-Path $Root "setup_y700_gamepad_v2.sh"
$Identity = Join-Path $Root "setup_y700_switch_identity_experiment.sh"
$Test = Join-Path $Root "test_y700_gamepad_reports.sh"

Write-Host "Using adb: $AdbPath"
Write-Host "Using device: $DeviceSerial"
& $AdbPath devices -l

Invoke-Adb push $Setup /data/local/tmp/setup_y700_gamepad_v2.sh
Invoke-Adb push $Identity /data/local/tmp/setup_y700_switch_identity_experiment.sh
Invoke-Adb push $Test /data/local/tmp/test_y700_gamepad_reports.sh
Invoke-Adb shell su -c "chmod 755 /data/local/tmp/setup_y700_gamepad_v2.sh /data/local/tmp/setup_y700_switch_identity_experiment.sh /data/local/tmp/test_y700_gamepad_reports.sh"

if (!$SkipSetup) {
    Invoke-Adb shell su -c "sh /data/local/tmp/setup_y700_gamepad_v2.sh"
}

Invoke-Adb shell su -c "MODE=$Mode sh /data/local/tmp/setup_y700_switch_identity_experiment.sh"

if ($RunTest) {
    Write-Host ""
    Write-Host "Running generic HID report test after identity change."
    Start-Sleep -Seconds 3
    Invoke-Adb shell su -c "sh /data/local/tmp/test_y700_gamepad_reports.sh"
}
