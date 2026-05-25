param(
    [string]$AdbPath = "C:\Users\leon\Desktop\工具\platform-tools\adb.exe",
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp",
    [int]$WindowSeconds = 3,
    [switch]$V3,
    [string[]]$Buttons = @(
        "A", "B", "X", "Y",
        "DPadUp", "DPadRight", "DPadDown", "DPadLeft",
        "L", "ZL", "R", "ZR",
        "Minus", "Plus", "LStick", "RStick",
        "Home", "Capture", "C", "GL", "GR"
    )
)

$ErrorActionPreference = "Stop"

$RemoteButtons = if ($V3) { "/data/local/tmp/switch2_button_changes_v3.log" } else { "/data/local/tmp/switch2_button_changes.log" }
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDirName = if ($V3) { "button_map_v3_$stamp" } else { "button_map_$stamp" }
$outDir = Join-Path $root "logs\$outDirName"
$outFileName = if ($V3) { "switch2_button_changes_v3.log" } else { "switch2_button_changes.log" }
$outFile = Join-Path $outDir $outFileName

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

    & $AdbPath @fullArgs
    if ($LASTEXITCODE -ne 0) {
        throw "adb exited with code $LASTEXITCODE"
    }
}

function Add-RemoteMarker {
    param([string]$Text)

    $safe = $Text.Replace("'", "")
    Invoke-Adb shell "su -c 'echo $safe >> $RemoteButtons'"
}

Write-Host "The Switch 2 BLE bridge must already be connected to the real controller."
Write-Host "For each step, press and release only the named control inside the capture window."
Write-Host "Button transition log: $RemoteButtons"
Write-Host ""

Add-RemoteMarker "=== BUTTON_MAP_CAPTURE_$stamp_BEGIN ==="

$step = 0
foreach ($button in $Buttons) {
    $step++
    Read-Host "Release all controls. Press Enter for step $step/$($Buttons.Count): $button"
    Add-RemoteMarker "=== STEP_$step`_$button`_START ==="
    Write-Host "Press and release $button now. Capturing for $WindowSeconds second(s)..."
    Start-Sleep -Seconds $WindowSeconds
    Add-RemoteMarker "=== STEP_$step`_$button`_END ==="
}

Add-RemoteMarker "=== BUTTON_MAP_CAPTURE_$stamp_END ==="

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Invoke-Adb pull $RemoteButtons $outFile

Write-Host ""
Write-Host "Saved capture:"
Write-Host "  $outFile"
Write-Host ""
Write-Host "Recent button transitions:"
Invoke-Adb shell "su -c 'tail -n 220 $RemoteButtons'"
