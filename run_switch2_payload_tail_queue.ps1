param(
    [string]$AdbPath = "",
    [string]$DeviceSerial = "",
    [int]$WaitForBleReadySeconds = 14400,
    [int]$ReadyPollSeconds = 30,
    [ValidateRange(0, 255)]
    [int]$ActivePreset = 1
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$FuzzScript = Join-Path $Root "fuzz_switch2_preset_command.ps1"
$Stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$QueueDir = Join-Path $Root "logs\payload_tail_queue_$Stamp"
$Transcript = Join-Path $QueueDir "queue_transcript.txt"
$Config = Join-Path $QueueDir "queue_config.json"

if (!(Test-Path -LiteralPath $FuzzScript)) {
    throw "Missing fuzz script: $FuzzScript"
}

New-Item -ItemType Directory -Force -Path $QueueDir | Out-Null
[ordered]@{
    created_at = (Get-Date).ToString("o")
    fuzz_script = $FuzzScript
    adb_path = $AdbPath
    device_serial = $DeviceSerial
    wait_for_ble_ready_seconds = $WaitForBleReadySeconds
    ready_poll_seconds = $ReadyPollSeconds
    active_preset = $ActivePreset
    queue_dir = $QueueDir
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Config -Encoding UTF8

Start-Transcript -LiteralPath $Transcript | Out-Null
try {
    Write-Host "Switch 2 payload-tail queue"
    Write-Host "Queue: $QueueDir"
    Write-Host "Waiting for BLE before sweeping preset payload bytes 9..15."

    $presetHex = "{0:X2}" -f $ActivePreset
    $baseHex = "0A91010200080000${presetHex}00000000000000"
    $fuzzArgs = @{
        ByteIndexes = 9..15
        BaseHex = $baseHex
        StartHex = "00"
        EndHex = "91"
        ConfirmRisk = $true
        WaitForBleReadySeconds = $WaitForBleReadySeconds
        ReadyPollSeconds = $ReadyPollSeconds
    }
    if ($AdbPath) {
        $fuzzArgs.AdbPath = $AdbPath
    }
    if ($DeviceSerial) {
        $fuzzArgs.DeviceSerial = $DeviceSerial
    }

    & $FuzzScript @fuzzArgs
} finally {
    Stop-Transcript | Out-Null
}
