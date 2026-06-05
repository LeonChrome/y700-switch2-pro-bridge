param(
    [string]$Ns2ProOutputHex = "",
    [string]$LeftRumbleHex = "",
    [string]$RightRumbleHex = "",
    [switch]$DryRun,
    [switch]$SendToRealPro2,
    [string]$Port = ""
)

$ErrorActionPreference = "Stop"

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

if (!$DryRun -and !$SendToRealPro2) {
    $DryRun = $true
}

if ($Ns2ProOutputHex) {
    $packet = Convert-HexToBytes $Ns2ProOutputHex
    if ($packet.Length -ne 34) {
        throw "Ns2ProOutputHex must be exactly 34 bytes"
    }
    $left = $packet[0..15]
    $right = $packet[16..31]
    $flags = $packet[32]
    $playerLed = $packet[33]
} else {
    $left = Convert-HexToBytes $LeftRumbleHex
    $right = Convert-HexToBytes $RightRumbleHex
    if ($left.Length -ne 16 -or $right.Length -ne 16) {
        throw "LeftRumbleHex and RightRumbleHex must both be exactly 16 bytes"
    }
    $flags = 0x01
    $playerLed = 0x00
}

$payload = New-Object byte[] 64
$payload[0] = 0x02
[Array]::Copy($left, 0, $payload, 1, 16)
[Array]::Copy($right, 0, $payload, 17, 16)

$leftNonzero = ($left | Where-Object { $_ -ne 0 } | Select-Object -First 1) -ne $null
$rightNonzero = ($right | Where-Object { $_ -ne 0 } | Select-Object -First 1) -ne $null

Write-Output "[NS2PRO_OUTPUT] left_rumble_hex=$(Convert-BytesToHex $left) right_rumble_hex=$(Convert-BytesToHex $right) flags=0x$($flags.ToString('X2')) player_led=0x$($playerLed.ToString('X2'))"
Write-Output "[NS2PRO_OUTPUT] left_nonzero=$($leftNonzero.ToString().ToLowerInvariant()) right_nonzero=$($rightNonzero.ToString().ToLowerInvariant())"
Write-Output "[PRO2_HD_RUMBLE] mode=$(if ($DryRun) { 'dry_run' } else { 'direct' })"
Write-Output "[PRO2_HD_RUMBLE] payload_0x02=$(Convert-BytesToHex $payload)"

if ($DryRun) {
    Write-Output "[PRO2_HD_RUMBLE] sent=false"
    Write-Output "[PRO2_HD_RUMBLE] conclusion=dry_run_only"
    exit 0
}

if ($SendToRealPro2) {
    Write-Output "[PRO2_HD_RUMBLE] blocked: V5.1 control protocol has no raw 0x02+16+16 injection command yet."
    Write-Output "[PRO2_HD_RUMBLE] requested_port=$Port"
    Write-Output "[PRO2_HD_RUMBLE] sent=false"
    exit 2
}
