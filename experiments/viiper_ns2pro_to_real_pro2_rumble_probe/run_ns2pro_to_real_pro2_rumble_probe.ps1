param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$Ns2ProOutputHex = "",
    [string]$LeftRumbleHex = "",
    [string]$RightRumbleHex = "",
    [ValidateSet("sample", "low", "medium", "short")]
    [string]$SafeProfile = "sample",
    [switch]$CaptureViiper,
    [Alias("dry-run")]
    [switch]$DryRun,
    [Alias("send-to-real-pro2")]
    [switch]$SendToRealPro2,
    [string]$Port = "",
    [Alias("max-packets")]
    [int]$MaxPackets = 1,
    [Alias("min-interval-ms")]
    [int]$MinIntervalMs = 100,
    [Alias("timeout-seconds")]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

if ($MinIntervalMs -lt 100) {
    throw "MinIntervalMs must be at least 100."
}
if ($MaxPackets -lt 1) {
    throw "MaxPackets must be at least 1."
}
if ($TimeoutSeconds -lt 1) {
    throw "TimeoutSeconds must be at least 1."
}

function Convert-HexToBytes {
    param([string]$Hex)
    $clean = ($Hex -replace '[^0-9a-fA-F]', '')
    if ($clean.Length % 2 -ne 0) {
        throw "hex length must be even"
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

function Get-SafeProfileBytes {
    param([string]$Name)

    switch ($Name) {
        "low" {
            return @{
                Left = Convert-HexToBytes "50870120110000000000000000000000"
                Right = Convert-HexToBytes "50870120110000000000000000000000"
            }
        }
        "medium" {
            return @{
                Left = Convert-HexToBytes "50871124403300000000000000000000"
                Right = Convert-HexToBytes "50871124403300000000000000000000"
            }
        }
        "short" {
            return @{
                Left = Convert-HexToBytes "50871527517100000000000000000000"
                Right = Convert-HexToBytes "50871527517100000000000000000000"
            }
        }
        default {
            return @{
                Left = Convert-HexToBytes "50871527517100000000000000000000"
                Right = Convert-HexToBytes "50871527517100000000000000000000"
            }
        }
    }
}

function Test-AnyNonZero {
    param([byte[]]$Bytes)
    return (($Bytes | Where-Object { $_ -ne 0 } | Select-Object -First 1) -ne $null)
}

function Invoke-ViiperFirstNonZeroCapture {
    $probeScript = Join-Path $ProjectRoot "experiments\viiper_ns2pro_hid_rumble_probe\run_viiper_ns2pro_hid_rumble_probe.ps1"
    if (!(Test-Path $probeScript)) {
        throw "VIIPER HID rumble probe not found at $probeScript"
    }

    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $probeScript -Seconds $TimeoutSeconds -Pattern single 2>&1
    foreach ($line in $output) {
        Write-Host $line
    }

    $text = $output | Out-String
    $match = [regex]::Match($text, 'left_rumble_hex=([0-9a-fA-F]{32})\s+right_rumble_hex=([0-9a-fA-F]{32})')
    if (!$match.Success) {
        throw "VIIPER capture did not produce a non-zero left/right rumble block"
    }

    return [pscustomobject]@{
        Left = $match.Groups[1].Value
        Right = $match.Groups[2].Value
    }
}

if (!$DryRun -and !$SendToRealPro2) {
    $DryRun = $true
}

if ($CaptureViiper -and !$Ns2ProOutputHex -and !$LeftRumbleHex -and !$RightRumbleHex) {
    Write-Output "[VIIPER_HD_OUTPUT] capture_viiper=true timeout_seconds=$TimeoutSeconds max_packets=$MaxPackets min_interval_ms=$MinIntervalMs"
    $capture = Invoke-ViiperFirstNonZeroCapture
    $left = Convert-HexToBytes $capture.Left
    $right = Convert-HexToBytes $capture.Right
    $flags = 0x01
    $playerLed = 0x00
} elseif ($Ns2ProOutputHex) {
    $packet = Convert-HexToBytes $Ns2ProOutputHex
    if ($packet.Length -ne 34) {
        throw "Ns2ProOutputHex must be exactly 34 bytes"
    }
    $left = $packet[0..15]
    $right = $packet[16..31]
    $flags = $packet[32]
    $playerLed = $packet[33]
} elseif ($LeftRumbleHex -or $RightRumbleHex) {
    $left = Convert-HexToBytes $LeftRumbleHex
    $right = Convert-HexToBytes $RightRumbleHex
    if ($left.Length -ne 16 -or $right.Length -ne 16) {
        throw "LeftRumbleHex and RightRumbleHex must both be exactly 16 bytes"
    }
    $flags = 0x01
    $playerLed = 0x00
} else {
    $profile = Get-SafeProfileBytes $SafeProfile
    $left = [byte[]]$profile.Left
    $right = [byte[]]$profile.Right
    $flags = 0x01
    $playerLed = 0x00
}

$payload = New-Object byte[] 64
$payload[0] = 0x02
[Array]::Copy($left, 0, $payload, 1, 16)
[Array]::Copy($right, 0, $payload, 17, 16)

$leftHex = Convert-BytesToHex $left
$rightHex = Convert-BytesToHex $right
$payloadHex = Convert-BytesToHex $payload
$leftNonzero = Test-AnyNonZero $left
$rightNonzero = Test-AnyNonZero $right
$mode = if ($DryRun) { "dry_run" } else { "direct" }

Write-Output "[VIIPER_HD_OUTPUT] left_rumble_hex=$leftHex right_rumble_hex=$rightHex flags=0x$($flags.ToString('X2')) player_led=0x$($playerLed.ToString('X2'))"
Write-Output "[NS2PRO_OUTPUT] left_nonzero=$($leftNonzero.ToString().ToLowerInvariant()) right_nonzero=$($rightNonzero.ToString().ToLowerInvariant())"
Write-Output "[PRO2_HD_RUMBLE] mode=$mode"
Write-Output "[PRO2_HD_RUMBLE] payload_0x02=$payloadHex"
Write-Output "[PRO2_HD_RUMBLE] transport=esp32_nintendo_hid_out"
Write-Output "[PRO2_HD_RUMBLE] max_packets=$MaxPackets min_interval_ms=$MinIntervalMs timeout_seconds=$TimeoutSeconds"

if ($DryRun) {
    $helper = Join-Path $ProjectRoot "tools\send_pro2_raw02.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper -Hex $payloadHex -DryRun
    $helperExit = $LASTEXITCODE
    Write-Output "[PRO2_HD_RUMBLE] sent=false"
    Write-Output "[PRO2_HD_RUMBLE] dry_run_helper_exit=$helperExit"
    Write-Output "[PRO2_HD_RUMBLE] blocked_reason=dry_run_default"
    exit $helperExit
}

if ($SendToRealPro2) {
    Write-Output "[PRO2_HD_RUMBLE] requested_port=$Port"
    Write-Output "[PRO2_HD_RUMBLE] preflight_send_to_real_pro2=true"
    Write-Output "[PRO2_HD_RUMBLE] preflight_port_present=$((![string]::IsNullOrWhiteSpace($Port)).ToString().ToLowerInvariant())"
    if ([string]::IsNullOrWhiteSpace($Port)) {
        Write-Output "[PRO2_HD_RUMBLE] sent=false"
        Write-Output "[PRO2_HD_RUMBLE] error=Port is required for -SendToRealPro2"
        exit 3
    }

    $helper = Join-Path $ProjectRoot "tools\send_pro2_raw02.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper -Hex $payloadHex -Send -Port $Port
    $helperExit = $LASTEXITCODE
    Write-Output "[PRO2_HD_RUMBLE] send_helper_exit=$helperExit"
    Write-Output "[PRO2_HD_RUMBLE] sent=$((($helperExit -eq 0).ToString()).ToLowerInvariant())"
    exit $helperExit
}
