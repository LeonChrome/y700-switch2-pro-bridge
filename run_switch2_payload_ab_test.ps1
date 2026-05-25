param(
    [string]$AdbPath = "",
    [string]$DeviceSerial = "",
    [int]$ObserveSeconds = 8,
    [int]$CooldownSeconds = 8,
    [int]$Repeat = 1,
    [switch]$NoSend
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$RunId = Get-Date -Format "yyyyMMdd_HHmmss"
$OutDir = Join-Path $Root "logs\payload_ab_$RunId"
$EventsPath = Join-Path $OutDir "events.csv"
$WriteFile = "/data/local/tmp/switch2_ble_write.txt"
$BridgeLog = "/data/local/tmp/switch2_ble_bridge.log"
$Stop = "0a910102000800000000000000000000"

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

function Invoke-AdbRoot {
    param([string]$Command)

    $args = @("-s", $script:DeviceSerial, "shell", "su", "-c", $Command)
    & $script:AdbPath @args
    if ($LASTEXITCODE -ne 0) {
        throw "adb root command failed with code ${LASTEXITCODE}: $Command"
    }
}

function Send-BleCommand {
    param(
        [string]$Target,
        [string]$Hex
    )

    $clean = ($Hex -replace '\s+', '').ToLowerInvariant()
    Invoke-AdbRoot "printf '%s\n' '$Target $clean' > $WriteFile"
}

function Mark-BridgeLog {
    param([string]$Marker)

    Invoke-AdbRoot "printf '%s\n' '===$Marker===' >> $BridgeLog"
}

function New-Case {
    param(
        [string]$Name,
        [string]$Hex,
        [string]$Purpose
    )
    [pscustomobject][ordered]@{
        name = $Name
        hex = $Hex.ToLowerInvariant()
        purpose = $Purpose
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$script:AdbPath = Resolve-AdbPath $AdbPath
$script:DeviceSerial = Resolve-DeviceSerial $DeviceSerial

$cases = @(
    New-Case "stop_only_control" $Stop "Silence/control window; should not create a new positive haptic effect."
    New-Case "preset01_baseline" "0a910102000800000100000000000000" "Known preset 01 with zero payload."
    New-Case "preset01_b10_4e" "0a9101020008000001004e0000000000" "Candidate near the user's first same-feel observation."
    New-Case "preset01_b10_4f" "0a9101020008000001004f0000000000" "Adjacent B10 value control."
    New-Case "preset01_b15_3a" "0a91010200080000010000000000003a" "Last command seen before the previous run was killed."
    New-Case "preset00_b10_4e" "0a9101020008000000004e0000000000" "Payload-only control with preset byte disabled."
)

$manifest = [ordered]@{
    run_id = $RunId
    adb_path = $script:AdbPath
    device_serial = $script:DeviceSerial
    observe_seconds = $ObserveSeconds
    cooldown_seconds = $CooldownSeconds
    repeat = $Repeat
    no_send = [bool]$NoSend
    output_dir = $OutDir
    cases = $cases
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir "manifest.json") -Encoding UTF8

$events = New-Object System.Collections.Generic.List[object]
Write-Host "Switch 2 sparse payload A/B test"
Write-Host "Run: $RunId"
Write-Host "Output: $OutDir"
Write-Host "Observe=${ObserveSeconds}s Cooldown=${CooldownSeconds}s Repeat=$Repeat"
Write-Host "Device: $script:DeviceSerial"

$caseIndex = 0
for ($r = 1; $r -le $Repeat; $r++) {
    foreach ($case in $cases) {
        $caseIndex++
        $marker = "PAYLOAD_AB_${RunId}_CASE_$('{0:D3}' -f $caseIndex)_R${r}_$($case.name)"
        $started = Get-Date
        $row = [pscustomobject][ordered]@{
            case_index = $caseIndex
            repeat = $r
            name = $case.name
            tx_hex = $case.hex
            marker = $marker
            purpose = $case.purpose
            started_at = $started.ToString("o")
            observe_seconds = $ObserveSeconds
            cooldown_seconds = $CooldownSeconds
            sent = !$NoSend
        }
        $events.Add($row)
        $events | Export-Csv -LiteralPath $EventsPath -NoTypeInformation -Encoding UTF8

        Write-Host ("Case {0}: {1} tx={2}" -f $caseIndex, $case.name, $case.hex)
        if (!$NoSend) {
            Mark-BridgeLog $marker
            Send-BleCommand "cmd" $Stop
            Start-Sleep -Seconds $CooldownSeconds
            if ($case.name -ne "stop_only_control") {
                Send-BleCommand "cmd" $case.hex
            }
            Start-Sleep -Seconds $ObserveSeconds
            Send-BleCommand "cmd" $Stop
            Start-Sleep -Seconds $CooldownSeconds
        }
    }
}

if (!$NoSend) {
    Send-BleCommand "cmd" $Stop
}

$events | Export-Csv -LiteralPath $EventsPath -NoTypeInformation -Encoding UTF8
Write-Host "Done. Events: $EventsPath"
