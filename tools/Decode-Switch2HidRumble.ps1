param(
    [string]$Hex,
    [string]$Path,
    [switch]$Csv
)

$ErrorActionPreference = "Stop"

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

function Decode-Frame {
    param([byte[]]$Bytes, [int]$Offset)

    if ($Bytes.Length -lt ($Offset + 5)) {
        return $null
    }

    $b0 = [int]$Bytes[$Offset]
    $b1 = [int]$Bytes[$Offset + 1]
    $b2 = [int]$Bytes[$Offset + 2]
    $b3 = [int]$Bytes[$Offset + 3]
    $b4 = [int]$Bytes[$Offset + 4]

    $highFreq = $b0 -bor (($b1 -band 0x03) -shl 8)
    $highAmp = (($b1 -band 0xfc) -shl 4) -bor (($b2 -band 0x0f) -shl 12)
    $lowFreq = (($b2 -band 0xf0) -shr 4) -bor (($b3 -band 0x3f) -shl 4)
    $lowAmp = ($b3 -band 0xc0) -bor ($b4 -shl 8)

    [pscustomobject]@{
        Raw = (($Bytes[$Offset..($Offset + 4)] | ForEach-Object { $_.ToString("x2") }) -join " ")
        HighFreqHex = "0x{0:x3}" -f $highFreq
        HighFreq = $highFreq
        HighAmp = $highAmp
        HighAmpPct = [Math]::Min(100, [Math]::Max(0, [Math]::Round(($highAmp * 100.0) / 29000, 1)))
        LowFreqHex = "0x{0:x3}" -f $lowFreq
        LowFreq = $lowFreq
        LowAmp = $lowAmp
        LowAmpPct = [Math]::Min(100, [Math]::Max(0, [Math]::Round(($lowAmp * 100.0) / 29000, 1)))
    }
}

function Decode-Report {
    param([byte[]]$Bytes, [string]$Source)

    if ($Bytes.Length -lt 7 -or $Bytes[0] -ne 0x02) {
        return $null
    }

    $left = Decode-Frame $Bytes 2
    $right = Decode-Frame $Bytes 0x12
    $neutral = $false
    if ($left -and $right) {
        $neutral = $left.Raw -eq "87 01 20 11 00" -and $right.Raw -eq "87 01 20 11 00"
    }

    [pscustomobject]@{
        Source = $Source
        Seq = ([int]$Bytes[1] -band 0x0f)
        Active = -not $neutral
        LeftRaw = $left.Raw
        LeftHighFreq = $left.HighFreqHex
        LeftHighAmp = $left.HighAmp
        LeftHighAmpPct = $left.HighAmpPct
        LeftLowFreq = $left.LowFreqHex
        LeftLowAmp = $left.LowAmp
        LeftLowAmpPct = $left.LowAmpPct
        RightRaw = $right.Raw
        RightHighFreq = $right.HighFreqHex
        RightHighAmp = $right.HighAmp
        RightHighAmpPct = $right.HighAmpPct
        RightLowFreq = $right.LowFreqHex
        RightLowAmp = $right.LowAmp
        RightLowAmpPct = $right.LowAmpPct
    }
}

$reports = @()

if ($Hex) {
    $decoded = Decode-Report (Convert-HexToBytes $Hex) "literal"
    if ($decoded) {
        $reports += $decoded
    }
}

if ($Path) {
    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_
        if ($line -match 'HID OUT \d+ bytes:\s*([0-9a-fA-F ]+)') {
            $decoded = Decode-Report (Convert-HexToBytes $matches[1]) $line
            if ($decoded) {
                $reports += $decoded
            }
        } elseif ($line -match '(^|\s)(02\s+[0-9a-fA-F ]{12,})') {
            $decoded = Decode-Report (Convert-HexToBytes $matches[2]) $line
            if ($decoded) {
                $reports += $decoded
            }
        }
    }
}

if (!$Hex -and !$Path) {
    throw "Pass -Hex or -Path."
}

if ($Csv) {
    $reports | ConvertTo-Csv -NoTypeInformation
} else {
    $reports | Format-Table -AutoSize
}
