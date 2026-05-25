param(
    [Parameter(Mandatory = $true)]
    [string]$RunDir
)

$ErrorActionPreference = "Stop"

$resolved = Resolve-Path -LiteralPath $RunDir
$runPath = $resolved.Path
$eventsPath = Join-Path $runPath "events.jsonl"
$summaryPath = Join-Path $runPath "summary.json"
$csvPath = Join-Path $runPath "machine_summary.csv"
$jsonPath = Join-Path $runPath "machine_summary.json"

if (!(Test-Path -LiteralPath $eventsPath)) {
    throw "Missing fuzz events file: $eventsPath"
}

function Get-AckHex {
    param([string[]]$RawLines)

    $acks = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($RawLines)) {
        if ($line -match '^A\s+\S+\s+([0-9a-fA-F]+)$') {
            $acks.Add($Matches[1].ToUpperInvariant())
        }
    }
    return @($acks)
}

$eventsByMarker = [ordered]@{}
foreach ($line in Get-Content -LiteralPath $eventsPath) {
    if (!$line.Trim()) {
        continue
    }
    $event = $line | ConvertFrom-Json
    $key = "{0}:{1}:{2}" -f $event.run_id, $event.case_index, $event.marker
    # Fatal reconnect failures can be written once before break and again in finally.
    # Keep the final copy so every command case is summarized once.
    $eventsByMarker[$key] = $event
}

$rows = foreach ($event in $eventsByMarker.Values) {
    $logs = $event.logs
    $rawCounts = $logs.raw_counts
    $acks = Get-AckHex @($logs.raw_lines)
    [pscustomobject][ordered]@{
        case_index = [int]$event.case_index
        byte_index0 = [int]$event.byte_index0
        byte_position1 = [int]$event.byte_position1
        value_hex = $event.value_hex
        tx_hex = $event.tx_hex
        ack_count = if ($rawCounts) { [int]$rawCounts.ack } else { 0 }
        input_count = if ($rawCounts) { [int]$rawCounts.input } else { 0 }
        telemetry_count = if ($rawCounts) { [int]$rawCounts.telemetry } else { 0 }
        notify_count = if ($rawCounts) { [int]$rawCounts.notify } else { 0 }
        unknown_count = if ($rawCounts) { [int]$rawCounts.unknown } else { 0 }
        ack_unique = (@($acks | Sort-Object -Unique) -join ";")
        disconnect = [bool]$logs.disconnect_detected
        error = $event.error
    }
}

$rows = @($rows | Sort-Object byte_index0, case_index)
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$uniqueAck = @(
    $rows |
        ForEach-Object { $_.ack_unique -split ';' } |
        Where-Object { $_ } |
        Sort-Object -Unique
)
$summary = $null
if (Test-Path -LiteralPath $summaryPath) {
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
}

$byByte = foreach ($group in $rows | Group-Object byte_index0 | Sort-Object Name) {
    [pscustomobject][ordered]@{
        byte_index0 = [int]$group.Name
        byte_position1 = ([int]$group.Name) + 1
        cases = $group.Count
        ack_cases = @($group.Group | Where-Object { $_.ack_count -gt 0 }).Count
        disconnect_cases = @($group.Group | Where-Object { $_.disconnect }).Count
        error_cases = @($group.Group | Where-Object { $_.error }).Count
        telemetry_cases = @($group.Group | Where-Object { $_.telemetry_count -gt 0 }).Count
        notify_cases = @($group.Group | Where-Object { $_.notify_count -gt 0 }).Count
        unique_ack = @(
            $group.Group |
                ForEach-Object { $_.ack_unique -split ';' } |
                Where-Object { $_ } |
                Sort-Object -Unique
        )
    }
}

$machine = [ordered]@{
    run_dir = $runPath
    run_id = if ($summary) { $summary.run_id } else { $null }
    total_cases = $rows.Count
    ack_cases = @($rows | Where-Object { $_.ack_count -gt 0 }).Count
    disconnect_cases = @($rows | Where-Object { $_.disconnect }).Count
    error_cases = @($rows | Where-Object { $_.error }).Count
    telemetry_cases = @($rows | Where-Object { $_.telemetry_count -gt 0 }).Count
    notify_cases = @($rows | Where-Object { $_.notify_count -gt 0 }).Count
    unique_ack = $uniqueAck
    by_byte = @($byByte)
    files = [ordered]@{
        events = $eventsPath
        csv = $csvPath
        summary = $jsonPath
    }
}

$machine | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$machine | ConvertTo-Json -Depth 10
