param(
    [string]$PullDir = "",
    [string]$RunDir = "",
    [string]$BridgeLog = ""
)

$ErrorActionPreference = "Stop"

if (!$RunDir) {
    if (!$PullDir) {
        throw "Pass -PullDir or -RunDir."
    }
    $RunDir = Join-Path $PullDir "payload_sweep"
    if (!$BridgeLog) {
        $BridgeLog = Join-Path $PullDir "switch2_ble_bridge.log"
    }
}

$runPath = (Resolve-Path -LiteralPath $RunDir).Path
$eventsPath = Join-Path $runPath "events.tsv"
if (!(Test-Path -LiteralPath $eventsPath)) {
    throw "Missing payload sweep events file: $eventsPath"
}
if (!$BridgeLog -or !(Test-Path -LiteralPath $BridgeLog)) {
    throw "Missing bridge log. Pass -BridgeLog or use -PullDir."
}
$bridgePath = (Resolve-Path -LiteralPath $BridgeLog).Path

$csvPath = Join-Path $runPath "payload_sweep_summary.csv"
$jsonPath = Join-Path $runPath "payload_sweep_summary.json"

function Split-HexBytes {
    param([string]$Hex)
    $clean = ($Hex -replace '\s+', '').ToLowerInvariant()
    $bytes = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $clean.Length; $i += 2) {
        $bytes.Add($clean.Substring($i, [Math]::Min(2, $clean.Length - $i)))
    }
    return @($bytes)
}

$events = @(Import-Csv -LiteralPath $eventsPath -Delimiter "`t")
$bridgeLines = @(Get-Content -LiteralPath $bridgePath)

$markerIndexes = [ordered]@{}
$markerOrder = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $bridgeLines.Count; $i++) {
    if ($bridgeLines[$i] -match '^===(LOCAL_PAYLOAD_CASE_[^=]+)===$') {
        $marker = $Matches[1]
        $markerIndexes[$marker] = $i
        $markerOrder.Add($marker)
    }
}

$rows = foreach ($event in $events) {
    $marker = [string]$event.marker
    $start = if ($markerIndexes.Contains($marker)) { [int]$markerIndexes[$marker] } else { -1 }
    $end = $bridgeLines.Count
    if ($start -ge 0) {
        for ($i = 0; $i -lt $markerOrder.Count; $i++) {
            if ($markerOrder[$i] -eq $marker -and ($i + 1) -lt $markerOrder.Count) {
                $end = [int]$markerIndexes[$markerOrder[$i + 1]]
                break
            }
        }
    }

    $segment = @()
    if ($start -ge 0 -and $end -gt $start) {
        $segment = $bridgeLines[$start..($end - 1)]
    }

    $acks = New-Object System.Collections.Generic.List[string]
    $writes = New-Object System.Collections.Generic.List[string]
    $statuses = New-Object System.Collections.Generic.List[string]
    foreach ($line in $segment) {
        if ($line -match 'ack n=\d+ .*data=([0-9a-fA-F]+)') {
            $acks.Add($Matches[1].ToUpperInvariant())
        }
        if ($line -match 'BLE write uuid=.* data=([0-9a-fA-F]+)') {
            $writes.Add($Matches[1].ToUpperInvariant())
        }
        if ($line -match 'characteristic write .* status=([0-9-]+)') {
            $statuses.Add($Matches[1])
        }
    }

    $txBytes = @(Split-HexBytes $event.tx_hex)
    [pscustomobject][ordered]@{
        case_index = [int]$event.case_index
        active_preset = if ($txBytes.Count -gt 8) { $txBytes[8].ToUpperInvariant() } else { "" }
        byte_index0 = [int]$event.byte_index0
        byte_position1 = [int]$event.byte_position1
        value_hex = ([string]$event.value_hex).ToUpperInvariant()
        tx_hex = ([string]$event.tx_hex).ToUpperInvariant()
        marker = $marker
        started_at = $event.started_at
        marker_found = ($start -ge 0)
        ack_count = $acks.Count
        ack_unique = (@($acks | Sort-Object -Unique) -join ";")
        write_count = $writes.Count
        test_write_seen = @($writes | Where-Object { $_ -eq ([string]$event.tx_hex).ToUpperInvariant() }).Count
        status_unique = (@($statuses | Sort-Object -Unique) -join ";")
        input_count = @($segment | Where-Object { $_ -match ' input n=' }).Count
        disconnect = @($segment | Where-Object { $_ -match 'connection state status=.* newState=0|BLE write skipped, no current GATT|BLE write skipped, main service missing' }).Count -gt 0
    }
}

$rows = @($rows | Sort-Object case_index)
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$byPreset = foreach ($group in $rows | Group-Object active_preset | Sort-Object Name) {
    [pscustomobject][ordered]@{
        active_preset = $group.Name
        cases = $group.Count
        ack_cases = @($group.Group | Where-Object { $_.ack_count -gt 0 }).Count
        disconnect_cases = @($group.Group | Where-Object { $_.disconnect }).Count
        marker_missing_cases = @($group.Group | Where-Object { !$_.marker_found }).Count
        unique_ack = @(
            $group.Group |
                ForEach-Object { $_.ack_unique -split ';' } |
                Where-Object { $_ } |
                Sort-Object -Unique
        )
    }
}

$byByte = foreach ($group in $rows | Group-Object byte_index0 | Sort-Object Name) {
    [pscustomobject][ordered]@{
        byte_index0 = [int]$group.Name
        byte_position1 = ([int]$group.Name) + 1
        cases = $group.Count
        ack_cases = @($group.Group | Where-Object { $_.ack_count -gt 0 }).Count
        disconnect_cases = @($group.Group | Where-Object { $_.disconnect }).Count
    }
}

$summary = [ordered]@{
    run_dir = $runPath
    bridge_log = $bridgePath
    total_cases = $rows.Count
    marker_cases = @($rows | Where-Object { $_.marker_found }).Count
    ack_cases = @($rows | Where-Object { $_.ack_count -gt 0 }).Count
    disconnect_cases = @($rows | Where-Object { $_.disconnect }).Count
    by_preset = @($byPreset)
    by_byte = @($byByte)
    files = [ordered]@{
        events = $eventsPath
        bridge_log = $bridgePath
        csv = $csvPath
        summary = $jsonPath
    }
}

$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$summary | ConvertTo-Json -Depth 10
