param(
    [string]$AdbPath,
    [string]$DeviceSerial,
    [switch]$RunTest
)

$ErrorActionPreference = "Stop"

function Resolve-Adb {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        if (Test-Path -LiteralPath $ExplicitPath) {
            return (Resolve-Path -LiteralPath $ExplicitPath).Path
        }
        throw "ADB path does not exist: $ExplicitPath"
    }

    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $candidates = @()
    foreach ($sdk in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA "Android\Sdk"))) {
        if ($sdk) {
            $candidates += (Join-Path $sdk "platform-tools\adb.exe")
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "adb.exe was not found. Add adb to PATH, set ANDROID_HOME/ANDROID_SDK_ROOT, or pass -AdbPath <path-to-adb.exe>."
}

function Invoke-Adb {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $fullArgs = @()
    if ($script:DeviceSerial) {
        $fullArgs += @("-s", $script:DeviceSerial)
    }
    $fullArgs += $Arguments

    Write-Host "> adb $($fullArgs -join ' ')"
    & $script:Adb @fullArgs
    if ($LASTEXITCODE -ne 0) {
        throw "adb exited with code $LASTEXITCODE"
    }
}

$script:Adb = Resolve-Adb -ExplicitPath $AdbPath
$script:DeviceSerial = $DeviceSerial
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$Setup = Join-Path $Root "setup_y700_gamepad_v2.sh"
$Test = Join-Path $Root "test_y700_gamepad_reports.sh"

if (!(Test-Path -LiteralPath $Setup)) { throw "Missing $Setup" }
if (!(Test-Path -LiteralPath $Test)) { throw "Missing $Test" }

Write-Host "Using adb: $script:Adb"
if ($script:DeviceSerial) {
    Write-Host "Using device: $script:DeviceSerial"
}
& $script:Adb devices -l
if ($LASTEXITCODE -ne 0) {
    throw "adb devices exited with code $LASTEXITCODE"
}
Invoke-Adb push $Setup /data/local/tmp/setup_y700_gamepad_v2.sh
Invoke-Adb push $Test /data/local/tmp/test_y700_gamepad_reports.sh
Invoke-Adb shell su -c "chmod 755 /data/local/tmp/setup_y700_gamepad_v2.sh /data/local/tmp/test_y700_gamepad_reports.sh"
Invoke-Adb shell su -c "sh /data/local/tmp/setup_y700_gamepad_v2.sh"

if ($RunTest) {
    Write-Host ""
    Write-Host "Running report test. Keep joy.cpl open on Windows."
    Start-Sleep -Seconds 3
    Invoke-Adb shell su -c "sh /data/local/tmp/test_y700_gamepad_reports.sh"
} else {
    Write-Host ""
    Write-Host "Setup done. To run visible joy.cpl report tests:"
    Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy_and_run.ps1 -RunTest"
}
