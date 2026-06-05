param(
    [string]$AdbPath,
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp",
    [int]$Seconds = 60,
    [switch]$ClearLogcat
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
$Stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$OutDir = Join-Path $Root "logs\bt_pair_$Stamp"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$BeforeBt = Join-Path $OutDir "before_bluetooth_manager.txt"
$BeforeInput = Join-Path $OutDir "before_input.txt"
$BeforeProcInput = Join-Path $OutDir "before_proc_bus_input_devices.txt"
$BeforeGetevent = Join-Path $OutDir "before_getevent_lp.txt"
$AfterBt = Join-Path $OutDir "after_bluetooth_manager.txt"
$AfterInput = Join-Path $OutDir "after_input.txt"
$AfterProcInput = Join-Path $OutDir "after_proc_bus_input_devices.txt"
$AfterGetevent = Join-Path $OutDir "after_getevent_lp.txt"
$Logcat = Join-Path $OutDir "logcat_full.txt"
$Filtered = Join-Path $OutDir "logcat_bluetooth_hid_filtered.txt"

Write-Host "Output folder: $OutDir"
& $AdbPath devices -l

Invoke-Adb shell dumpsys bluetooth_manager | Out-File -Encoding utf8 $BeforeBt
Invoke-Adb shell dumpsys input | Out-File -Encoding utf8 $BeforeInput
Invoke-Adb shell su -c "cat /proc/bus/input/devices" | Out-File -Encoding utf8 $BeforeProcInput
Invoke-Adb shell su -c "getevent -lp" | Out-File -Encoding utf8 $BeforeGetevent

if ($ClearLogcat) {
    Invoke-Adb logcat -c
}

Write-Host ""
Write-Host "Now put the controller into Bluetooth pairing mode and try pairing it from Android Settings."
Write-Host "Capturing logcat for $Seconds seconds..."

$logcatArgs = @()
if ($DeviceSerial) {
    $logcatArgs += @("-s", $DeviceSerial)
}
$logcatArgs += @("logcat", "-v", "threadtime")

$LogcatStderr = Join-Path $OutDir "logcat_stderr.txt"
$job = Start-Job -ScriptBlock {
    param($JobAdbPath, $JobArgs, $JobLogcat, $JobStderr)
    & $JobAdbPath @JobArgs 1> $JobLogcat 2> $JobStderr
} -ArgumentList $AdbPath, $logcatArgs, $Logcat, $LogcatStderr

Start-Sleep -Seconds $Seconds
if ($job.State -eq "Running") {
    Stop-Job -Job $job
}
Receive-Job -Job $job -ErrorAction SilentlyContinue | Out-Null
Remove-Job -Job $job -Force

Invoke-Adb shell dumpsys bluetooth_manager | Out-File -Encoding utf8 $AfterBt
Invoke-Adb shell dumpsys input | Out-File -Encoding utf8 $AfterInput
Invoke-Adb shell su -c "cat /proc/bus/input/devices" | Out-File -Encoding utf8 $AfterProcInput
Invoke-Adb shell su -c "getevent -lp" | Out-File -Encoding utf8 $AfterGetevent

$patterns = @(
    "Bluetooth",
    "bluetooth",
    "bt_",
    "BTM",
    "BTA",
    "btif",
    "HID",
    "Hid",
    "hid",
    "GATT",
    "Gatt",
    "bond",
    "Bond",
    "pair",
    "Pair",
    "Nintendo",
    "Switch",
    "Pro Controller",
    "Gamepad",
    "InputReader",
    "EventHub"
)

Select-String -Path $Logcat -Pattern $patterns -SimpleMatch | Out-File -Encoding utf8 $Filtered

Write-Host ""
Write-Host "Capture complete:"
Write-Host "  $OutDir"
Write-Host ""
Write-Host "Most useful files:"
Write-Host "  logcat_bluetooth_hid_filtered.txt"
Write-Host "  after_bluetooth_manager.txt"
Write-Host "  after_proc_bus_input_devices.txt"
Write-Host "  after_getevent_lp.txt"
