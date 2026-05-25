param(
    [string]$AdbPath = "",
    [string]$DeviceSerial = "",
    [string]$Target = "cmd",
    [string]$BaseHex = "0A910102000800000000000000000000",
    [int[]]$ByteIndexes = @(8),
    [switch]$FirstTen,
    [switch]$PayloadTail,
    [string]$StartHex = "00",
    [string]$EndHex = "91",
    [int]$WaitAfterStopMs = 500,
    [int]$ObserveMs = 2000,
    [int]$CooldownMs = 500,
    [int]$ReconnectWaitSeconds = 180,
    [int]$WaitForBleReadySeconds = 0,
    [int]$ReadyPollSeconds = 5,
    [int]$MaxCases = 0,
    [int]$StartCase = 1,
    [switch]$ConfirmRisk,
    [switch]$AllowHeaderSweep,
    [switch]$AllowBleNotReady
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$RunId = Get-Date -Format "yyyyMMdd_HHmmss"
$OutDir = Join-Path $Root "logs\preset_fuzz_$RunId"
$JsonlPath = Join-Path $OutDir "events.jsonl"
$PlanPath = Join-Path $OutDir "plan.json"
$ManifestPath = Join-Path $OutDir "manifest.json"
$SummaryPath = Join-Path $OutDir "summary.json"

$BridgeLog = "/data/local/tmp/switch2_ble_bridge.log"
$RawLog = "/data/local/tmp/switch2_ble_input_raw.log"
$WriteFile = "/data/local/tmp/switch2_ble_write.txt"
$StopHex = "0a910102000800000000000000000000"

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
    if ($RequestedPath) {
        throw "ADB not found at requested path: $RequestedPath"
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

function Invoke-AdbRoot {
    param([string]$Command)
    Invoke-Adb shell su -c $Command
}

function Get-AdbRootText {
    param([string]$Command)
    return Get-AdbText shell su -c $Command
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
    if ($devices.Count -eq 0) {
        throw "No online adb device found."
    }
    if ($devices.Count -gt 1) {
        throw "More than one adb device found. Pass -DeviceSerial. Devices: $($devices -join ', ')"
    }
    return $devices[0]
}

function Convert-HexByte {
    param([string]$Text)
    $clean = ($Text -replace '^0x', '').Trim()
    if ($clean.Length -eq 0 -or $clean.Length -gt 2 -or $clean -notmatch '^[0-9a-fA-F]+$') {
        throw "Invalid hex byte: $Text"
    }
    return [Convert]::ToInt32($clean, 16)
}

function Convert-HexToBytes {
    param([string]$Text)
    $clean = ($Text -replace '[^0-9a-fA-F]', '')
    if (($clean.Length % 2) -ne 0) {
        throw "Odd-length hex string."
    }
    $bytes = New-Object byte[] ($clean.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($clean.Substring($i * 2, 2), 16)
    }
    return $bytes
}

function Convert-BytesToHex {
    param([byte[]]$Bytes)
    return (($Bytes | ForEach-Object { $_.ToString("x2") }) -join "")
}

function New-TestHex {
    param([byte[]]$BaseBytes, [int]$ByteIndex, [int]$Value)
    if ($ByteIndex -lt 0 -or $ByteIndex -ge $BaseBytes.Length) {
        throw "Byte index out of range: $ByteIndex"
    }
    $copy = New-Object byte[] $BaseBytes.Length
    [Array]::Copy($BaseBytes, $copy, $BaseBytes.Length)
    $copy[$ByteIndex] = [byte]$Value
    return Convert-BytesToHex $copy
}

function Add-JsonLine {
    param([object]$Object, [string]$Path)
    $json = $Object | ConvertTo-Json -Depth 12 -Compress
    Add-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

function Add-RemoteMarker {
    param([string]$Marker)
    Invoke-AdbRoot "echo ===$Marker=== >> $BridgeLog; echo M $Marker >> $RawLog"
}

function Send-BleLine {
    param([string]$TargetName, [string]$Hex)
    $targetClean = $TargetName.ToLowerInvariant()
    if ($targetClean -notmatch '^[0-9a-z-]+$') {
        throw "Unsafe target name: $TargetName"
    }
    if ($Hex -notmatch '^[0-9a-fA-F]+$') {
        throw "Unsafe hex payload."
    }
    Invoke-AdbRoot "echo $targetClean $($Hex.ToLowerInvariant()) > $WriteFile"
}

function Get-LinesAfterMarker {
    param([string[]]$Lines, [string]$Marker)
    $start = -1
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -like "*$Marker*") {
            $start = $i
        }
    }
    if ($start -lt 0) {
        return @()
    }
    if ($start + 1 -ge $Lines.Count) {
        return @()
    }
    return @($Lines[($start + 1)..($Lines.Count - 1)])
}

function Capture-CaseLogs {
    param([string]$Marker)

    $bridgeTail = Get-AdbRootText "tail -n 500 $BridgeLog 2>/dev/null"
    $rawTail = Get-AdbRootText "tail -n 900 $RawLog 2>/dev/null"
    $bridgeLines = Get-LinesAfterMarker $bridgeTail $Marker
    $rawLines = Get-LinesAfterMarker $rawTail $Marker
    $ackLines = @($bridgeLines | Where-Object { $_ -match 'ack n=|BLE write|characteristic write' })
    $disconnectLines = @($bridgeLines | Where-Object {
        $_ -match 'BLE write skipped|main service missing|no current GATT|connection state.*newState=0|status=147|scan timeout'
    })
    return [ordered]@{
        bridge_lines = @($bridgeLines)
        raw_lines = @($rawLines)
        ack_lines = @($ackLines)
        disconnect_lines = @($disconnectLines)
        raw_counts = [ordered]@{
            ack = @($rawLines | Where-Object { $_ -match '^A ' }).Count
            input = @($rawLines | Where-Object { $_ -match '^I ' }).Count
            telemetry = @($rawLines | Where-Object { $_ -match '^T ' }).Count
            notify = @($rawLines | Where-Object { $_ -match '^N ' }).Count
            unknown = @($rawLines | Where-Object { $_ -match '^\? ' }).Count
        }
        disconnect_detected = @($disconnectLines).Count -gt 0
    }
}

function Test-BleReady {
    $marker = "FUZZ_READY_$([DateTimeOffset]::Now.ToUnixTimeMilliseconds())"
    Add-RemoteMarker $marker
    Send-BleLine "cmd" $StopHex
    Start-Sleep -Milliseconds 900
    $logs = Capture-CaseLogs $marker
    $bad = $logs.disconnect_detected
    $good = (@($logs.bridge_lines | Where-Object { $_ -match 'BLE write uuid=.*649d|characteristic write .*649d|ack n=' }).Count -gt 0)
    return [ordered]@{
        ready = ($good -and -not $bad)
        good = $good
        bad = $bad
        logs = $logs
    }
}

function Wait-BleReconnect {
    param([int]$Seconds, [int]$PollSeconds = 5)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $ready = Test-BleReady
            if ($ready.ready) {
                return $true
            }
        } catch {
            Write-Host "Reconnect probe failed: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds ([Math]::Max(1, $PollSeconds))
    }
    return $false
}

$script:AdbPath = Resolve-AdbPath $AdbPath
$script:DeviceSerial = Resolve-DeviceSerial $DeviceSerial
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$baseBytes = Convert-HexToBytes $BaseHex
if ($baseBytes.Length -ne 16) {
    throw "BaseHex must be 16 bytes. Current length: $($baseBytes.Length)"
}
if ($FirstTen -and $PayloadTail) {
    throw "Use only one of -FirstTen or -PayloadTail."
}
if ($FirstTen) {
    $ByteIndexes = 0..9
}
if ($PayloadTail) {
    $ByteIndexes = 10..15
}
$startValue = Convert-HexByte $StartHex
$endValue = Convert-HexByte $EndHex
if ($startValue -gt $endValue) {
    throw "StartHex must be <= EndHex."
}
foreach ($idx in $ByteIndexes) {
    if ($idx -lt 0 -or $idx -gt 15) {
        throw "ByteIndexes values must be 0..15."
    }
}
if ((@($ByteIndexes | Where-Object { $_ -lt 8 }).Count -gt 0) -and -not $AllowHeaderSweep) {
    throw "Byte indexes 0..7 are command header fields. Re-run with -AllowHeaderSweep only for command-family/header experiments."
}

$plan = New-Object System.Collections.Generic.List[object]
$caseNo = 0
foreach ($idx in $ByteIndexes) {
    for ($value = $startValue; $value -le $endValue; $value++) {
        $caseNo++
        if ($MaxCases -gt 0 -and $plan.Count -ge $MaxCases) {
            break
        }
        $hex = New-TestHex $baseBytes $idx $value
        $plan.Add([ordered]@{
            case_index = $caseNo
            byte_index0 = $idx
            byte_position1 = $idx + 1
            value = $value
            value_hex = ("{0:x2}" -f $value)
            target = $Target
            tx_hex = $hex
        })
    }
    if ($MaxCases -gt 0 -and $plan.Count -ge $MaxCases) {
        break
    }
}

$manifest = [ordered]@{
    run_id = $RunId
    created_at = (Get-Date).ToString("o")
    adb_path = $script:AdbPath
    device_serial = $script:DeviceSerial
    target = $Target
    base_hex = (Convert-BytesToHex $baseBytes)
    byte_indexes = @($ByteIndexes)
    start_hex = ("{0:x2}" -f $startValue)
    end_hex = ("{0:x2}" -f $endValue)
    wait_after_stop_ms = $WaitAfterStopMs
    observe_ms = $ObserveMs
    cooldown_ms = $CooldownMs
    reconnect_wait_seconds = $ReconnectWaitSeconds
    wait_for_ble_ready_seconds = $WaitForBleReadySeconds
    ready_poll_seconds = $ReadyPollSeconds
    allow_header_sweep = [bool]$AllowHeaderSweep
    total_cases = $plan.Count
    jsonl_path = $JsonlPath
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
$plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $PlanPath -Encoding UTF8

Write-Host "Switch 2 Pro preset fuzz plan"
Write-Host "Run: $RunId"
Write-Host "ADB: $script:AdbPath"
Write-Host "Device: $script:DeviceSerial"
Write-Host "Output: $OutDir"
Write-Host "Cases: $($plan.Count)"
Write-Host "Byte indexes: $($ByteIndexes -join ', ')"
Write-Host "Values: $('{0:x2}' -f $startValue)..$('{0:x2}' -f $endValue)"
Write-Host ""

if (-not $ConfirmRisk) {
    Write-Host "Dry run only. Re-run with -ConfirmRisk to send commands."
    Write-Host "Plan written: $PlanPath"
    return
}

if (-not $AllowBleNotReady) {
    Write-Host "Checking BLE bridge readiness..."
    $ready = Test-BleReady
    if (-not $ready.ready -and $WaitForBleReadySeconds -gt 0) {
        Write-Host "BLE bridge is not ready. Waiting up to $WaitForBleReadySeconds seconds..."
        $readyAgain = Wait-BleReconnect $WaitForBleReadySeconds $ReadyPollSeconds
        if ($readyAgain) {
            $ready = Test-BleReady
        }
    }
    if (-not $ready.ready) {
        $ready | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutDir "ble_not_ready.json") -Encoding UTF8
        throw "BLE bridge is not ready. Put the controller in connect mode, restart bridge if needed, then retry. Details: $(Join-Path $OutDir 'ble_not_ready.json')"
    }
}

$summary = [ordered]@{
    run_id = $RunId
    started_at = (Get-Date).ToString("o")
    completed_at = $null
    completed_cases = 0
    disconnect_cases = @()
    error_cases = @()
    jsonl_path = $JsonlPath
}

foreach ($case in $plan) {
    if ($case.case_index -lt $StartCase) {
        continue
    }

    $marker = "FUZZ_CASE_$('{0:d5}' -f $case.case_index)_B$('{0:d2}' -f $case.byte_index0)_V$($case.value_hex)_$([DateTimeOffset]::Now.ToUnixTimeMilliseconds())"
    $started = Get-Date
    $result = [ordered]@{
        run_id = $RunId
        case_index = $case.case_index
        started_at = $started.ToString("o")
        completed_at = $null
        target = $case.target
        base_hex = $manifest.base_hex
        tx_hex = $case.tx_hex
        byte_index0 = $case.byte_index0
        byte_position1 = $case.byte_position1
        value = $case.value
        value_hex = $case.value_hex
        marker = $marker
        timings_ms = [ordered]@{
            wait_after_stop = $WaitAfterStopMs
            observe = $ObserveMs
            cooldown = $CooldownMs
        }
        error = $null
        logs = $null
    }

    Write-Host ("Case {0}/{1}: byte[{2}] = 0x{3} tx={4}" -f $case.case_index, $plan.Count, $case.byte_index0, $case.value_hex, $case.tx_hex)

    try {
        Add-RemoteMarker $marker
        Send-BleLine "cmd" $StopHex
        Start-Sleep -Milliseconds $WaitAfterStopMs
        Send-BleLine $case.target $case.tx_hex
        Start-Sleep -Milliseconds $ObserveMs
        Send-BleLine "cmd" $StopHex
        Start-Sleep -Milliseconds $CooldownMs

        $logs = Capture-CaseLogs $marker
        $result.logs = $logs
        if ($logs.disconnect_detected) {
            $summary.disconnect_cases += @([ordered]@{
                case_index = $case.case_index
                byte_index0 = $case.byte_index0
                value_hex = $case.value_hex
                tx_hex = $case.tx_hex
            })
            Write-Host "  Disconnect/service warning detected for case $($case.case_index). Waiting for reconnect..."
            $ok = Wait-BleReconnect $ReconnectWaitSeconds
            if (-not $ok) {
                $result.error = "BLE did not recover within $ReconnectWaitSeconds seconds"
                $summary.error_cases += @([ordered]@{
                    case_index = $case.case_index
                    error = $result.error
                    tx_hex = $case.tx_hex
                })
                $result.completed_at = (Get-Date).ToString("o")
                Add-JsonLine $result $JsonlPath
                break
            }
        }
    } catch {
        $result.error = $_.Exception.Message
        $summary.error_cases += @([ordered]@{
            case_index = $case.case_index
            error = $result.error
            tx_hex = $case.tx_hex
        })
        Write-Host "  ERROR: $($result.error)"
    } finally {
        $result.completed_at = (Get-Date).ToString("o")
        Add-JsonLine $result $JsonlPath
        $summary.completed_cases++
        $summary.completed_at = (Get-Date).ToString("o")
        $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryPath -Encoding UTF8
    }
}

try {
    Send-BleLine "cmd" $StopHex
} catch {
    Write-Host "Final stop failed: $($_.Exception.Message)"
}

$summary.completed_at = (Get-Date).ToString("o")
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryPath -Encoding UTF8

Write-Host ""
Write-Host "Done."
Write-Host "Events: $JsonlPath"
Write-Host "Summary: $SummaryPath"

$summarizer = Join-Path $Root "tools\Summarize-PresetFuzz.ps1"
if (Test-Path -LiteralPath $summarizer) {
    powershell -NoProfile -ExecutionPolicy Bypass -File $summarizer -RunDir $OutDir | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Machine summary: $(Join-Path $OutDir 'machine_summary.json')"
    } else {
        Write-Host "Machine summary failed with code $LASTEXITCODE"
    }
}
