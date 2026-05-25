param(
    [string]$AdbPath = "",
    [string]$DeviceSerial = "",
    [string]$RemoteRunDir = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$OutDir = Join-Path $Root "logs\payload_sweep_pull_$Stamp"

function Resolve-AdbPath {
    param([string]$RequestedPath)

    if ($RequestedPath -and (Test-Path -LiteralPath $RequestedPath)) {
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }
    if ($env:ADB_PATH -and (Test-Path -LiteralPath $env:ADB_PATH)) {
        return (Resolve-Path -LiteralPath $env:ADB_PATH).Path
    }
    $cmd = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    $desktop = [Environment]::GetFolderPath("Desktop")
    if ($desktop -and (Test-Path -LiteralPath $desktop)) {
        $found = Get-ChildItem -LiteralPath $desktop -Filter adb.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*platform-tools*adb.exe" } |
            Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }
    throw "ADB not found. Pass -AdbPath or set ADB_PATH."
}

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $fullArgs = @()
    if ($script:DeviceSerial) {
        $fullArgs += @("-s", $script:DeviceSerial)
    }
    $fullArgs += $Arguments
    & $script:AdbPath @fullArgs
    if ($LASTEXITCODE -ne 0) {
        throw "adb exited with code ${LASTEXITCODE}: $($fullArgs -join ' ')"
    }
}

function Get-AdbText {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $fullArgs = @()
    if ($script:DeviceSerial) {
        $fullArgs += @("-s", $script:DeviceSerial)
    }
    $fullArgs += $Arguments
    $text = & $script:AdbPath @fullArgs
    if ($LASTEXITCODE -ne 0) {
        throw "adb exited with code ${LASTEXITCODE}: $($fullArgs -join ' ')"
    }
    return @($text)
}

function Resolve-DeviceSerial {
    param([string]$RequestedSerial)

    if ($RequestedSerial) {
        return $RequestedSerial
    }
    $lines = & $script:AdbPath devices -l
    if ($LASTEXITCODE -ne 0) {
        throw "adb devices failed"
    }
    $devices = @()
    foreach ($line in $lines) {
        $idx = $line.IndexOf(" device ")
        if ($idx -gt 0) {
            $devices += $line.Substring(0, $idx).Trim()
        }
    }
    if ($devices.Count -ne 1) {
        throw "Expected one online adb device. Pass -DeviceSerial. Devices: $($devices -join ', ')"
    }
    return $devices[0]
}

$script:AdbPath = Resolve-AdbPath $AdbPath
$script:DeviceSerial = Resolve-DeviceSerial $DeviceSerial
if (!$RemoteRunDir) {
    $latest = Get-AdbText shell su -c "ls -td /data/local/tmp/switch2_payload_sweep_20* 2>/dev/null"
    $RemoteRunDir = @($latest | Where-Object { $_ -like "/data/local/tmp/switch2_payload_sweep_*" } | Select-Object -First 1)
}
if (!$RemoteRunDir) {
    throw "No remote Switch 2 payload sweep run directory found."
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Invoke-Adb pull $RemoteRunDir (Join-Path $OutDir "payload_sweep")
Invoke-Adb pull "/data/local/tmp/switch2_ble_bridge.log" (Join-Path $OutDir "switch2_ble_bridge.log")
Invoke-Adb pull "/data/local/tmp/switch2_ble_input_raw.log" (Join-Path $OutDir "switch2_ble_input_raw.log")
Invoke-Adb pull "/data/local/tmp/switch2_payload_sweep_launcher.log" (Join-Path $OutDir "switch2_payload_sweep_launcher.log")

[ordered]@{
    pulled_at = (Get-Date).ToString("o")
    adb_path = $script:AdbPath
    device_serial = $script:DeviceSerial
    remote_run_dir = $RemoteRunDir
    output_dir = $OutDir
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir "pull_manifest.json") -Encoding UTF8

Write-Host "Pulled payload sweep to $OutDir"
