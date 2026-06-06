param(
    [string]$Hex = "",
    [ValidateSet("low", "medium", "captured")]
    [string]$Preset = "",
    [string]$Port = "",
    [switch]$DryRun,
    [switch]$Send,
    [int]$ReadSeconds = 4,
    [int]$StopAfterMs = 250
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Convert-HexToBytes {
    param([string]$Value)
    $clean = ($Value -replace '\s+', '')
    if (($clean.Length % 2) -ne 0) {
        throw "Hex must have an even number of characters."
    }
    if ($clean -notmatch '^[0-9a-fA-F]+$') {
        throw "Hex contains non-hex characters."
    }
    $bytes = New-Object byte[] ($clean.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($clean.Substring($i * 2, 2), 16)
    }
    return $bytes
}

function Convert-BytesToHex {
    param([byte[]]$Bytes)
    return (($Bytes | ForEach-Object { $_.ToString("X2") }) -join "")
}

function Get-PresetHex {
    param([string]$Name)
    switch ($Name) {
        "low" {
            return "5087012011000000000000000000000050870120110000000000000000000000"
        }
        "medium" {
            return "5087112440330000000000000000000050871124403300000000000000000000"
        }
        "captured" {
            return "5087152751710000000000000000000050871527517100000000000000000000"
        }
        default {
            throw "Unknown preset: $Name"
        }
    }
}

function Normalize-Raw02Payload {
    param([string]$InputHex)

    $bytes = Convert-HexToBytes $InputHex
    $payload = New-Object byte[] 64
    $mode = ""

    if ($bytes.Length -eq 32) {
        $mode = "left_right_16"
        $payload[0] = 0x02
        [Array]::Copy($bytes, 0, $payload, 1, 16)
        [Array]::Copy($bytes, 16, $payload, 17, 16)
    } elseif ($bytes.Length -eq 64) {
        $mode = "full_payload"
        if ($bytes[0] -ne 0x02) {
            throw "Full raw02 payload must start with report_id 0x02."
        }
        [Array]::Copy($bytes, 0, $payload, 0, 64)
    } else {
        throw "Hex must be either 64 chars left+right or 128 chars full payload."
    }

    return [pscustomobject]@{
        Mode = $mode
        Payload = $payload
        PayloadHex = Convert-BytesToHex $payload
        LeftHex = Convert-BytesToHex $payload[1..16]
        RightHex = Convert-BytesToHex $payload[17..32]
    }
}

function Invoke-BoardCommand {
    param([string]$Command)
    $script = Join-Path $RepoRoot "tools\esp32s3\send_command.ps1"
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -Port $Port -Command $Command -ReadSeconds $ReadSeconds 2>&1
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) {
        $exitCode = 0
    }

    foreach ($line in $output) {
        Write-Host "[PRO2_RAW02_SERIAL] $line"
    }

    $text = $output | Out-String
    $errorText = ""
    if ($exitCode -ne 0) {
        $errorText = "serial command failed exit=$exitCode"
    } elseif ($text -match '"ok"\s*:\s*false') {
        $errorText = "firmware returned ok=false"
        $match = [regex]::Match($text, '"error"\s*:\s*"([^"]+)"')
        if ($match.Success) {
            $errorText = $match.Groups[1].Value
        }
    } elseif ($text -match '\[RUMBLE_RAW02\].*sent=false') {
        $errorText = "firmware raw02 reported sent=false"
    } elseif ($text -match 'unknown command') {
        $errorText = "firmware returned unknown command"
    }

    return [pscustomobject]@{
        Success = [string]::IsNullOrWhiteSpace($errorText)
        ExitCode = $exitCode
        Error = $errorText
        Output = $text
    }
}

if (!$DryRun -and !$Send) {
    $DryRun = $true
}

if (!$Hex) {
    if (!$Preset) {
        $Preset = "captured"
    }
    $Hex = Get-PresetHex $Preset
}

$raw = Normalize-Raw02Payload $Hex
$command = "rumble raw02 $($raw.PayloadHex)"

Write-Output "[PRO2_RAW02] mode=$($raw.Mode)"
Write-Output "[PRO2_RAW02] left=$($raw.LeftHex)"
Write-Output "[PRO2_RAW02] right=$($raw.RightHex)"
Write-Output "[PRO2_RAW02] payload=$($raw.PayloadHex)"
Write-Output "[PRO2_RAW02] command=$command"

if ($DryRun) {
    Write-Output "[PRO2_RAW02] dry_run=true"
    Write-Output "[PRO2_RAW02] sent=false"
    Write-Output "[PRO2_RAW02] target=not_sent"
    exit 0
}

if (!$Send) {
    Write-Output "[PRO2_RAW02] sent=false"
    Write-Output "[PRO2_RAW02] error=send flag not set"
    exit 2
}

if ([string]::IsNullOrWhiteSpace($Port)) {
    Write-Output "[PRO2_RAW02] sent=false"
    Write-Output "[PRO2_RAW02] error=Port is required for -Send"
    exit 3
}

Write-Output "[PRO2_RAW02] dry_run=false"
Write-Output "[PRO2_RAW02] target=$Port"
$result = Invoke-BoardCommand -Command $command
if (!$result.Success) {
    Write-Output "[PRO2_RAW02] sent=false"
    Write-Output "[PRO2_RAW02] error=$($result.Error)"
    if ($result.ExitCode -ne 0) {
        exit $result.ExitCode
    }
    exit 4
}

Write-Output "[PRO2_RAW02] sent=true"
if ($StopAfterMs -ge 0) {
    Start-Sleep -Milliseconds $StopAfterMs
    Write-Output "[PRO2_RAW02] stop_after_ms=$StopAfterMs"
    $stopResult = Invoke-BoardCommand -Command "rumble stop"
    if (!$stopResult.Success) {
        Write-Output "[PRO2_RAW02] stop_error=$($stopResult.Error)"
    }
}
