param(
    [int[]]$Levels = @(8192, 16384, 32768, 49152, 65535),
    [int]$PulseMs = 220,
    [int]$GapMs = 260,
    [ValidateSet("Both", "LowOnly", "HighOnly")]
    [string]$Channel = "Both"
)

$ErrorActionPreference = "Stop"

$probe = Join-Path $PSScriptRoot "Send-HidHapticProbe.ps1"
if (!(Test-Path -LiteralPath $probe)) {
    throw "Missing probe script: $probe"
}

foreach ($level in $Levels) {
    if ($level -lt 0 -or $level -gt 65535) {
        throw "Level out of range 0..65535: $level"
    }

    $low = 0
    $high = 0
    switch ($Channel) {
        "Both" {
            $low = $level
            $high = $level
        }
        "LowOnly" {
            $low = $level
        }
        "HighOnly" {
            $high = $level
        }
    }

    $pct = [Math]::Round(($level * 100.0) / 65535, 1)
    Write-Host ""
    Write-Host "=== HID rumble sweep $Channel level=$level approx=$pct% low=$low high=$high ==="
    powershell -NoProfile -ExecutionPolicy Bypass -File $probe -Pattern single -PulseMs $PulseMs -GapMs $GapMs -LowSpeed $low -HighSpeed $high
    if ($LASTEXITCODE -ne 0) {
        throw "Send-HidHapticProbe failed with code $LASTEXITCODE"
    }
    Start-Sleep -Milliseconds $GapMs
}
