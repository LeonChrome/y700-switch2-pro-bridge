param(
    [int]$Seconds = 5
)

$ErrorActionPreference = "Stop"

$Reader = Join-Path $PSScriptRoot "ReadSwitchHidInput.exe"
if (-not (Test-Path -LiteralPath $Reader)) {
    throw "Missing HID reader: $Reader"
}

if ($Seconds -lt 1) {
    $Seconds = 1
}

Write-Host "Measuring host-observed HID input report rate for VID_057E PID_2069 MI_00..."
Write-Host "Window: $Seconds second(s)"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$output = & $Reader $Seconds 2>&1
$exitCode = $LASTEXITCODE
$sw.Stop()

if ($exitCode -ne 0) {
    Write-Host ($output | Select-Object -First 20)
    throw "ReadSwitchHidInput.exe failed with exit code $exitCode"
}

$pathLine = $output | Where-Object { $_ -like "Path:*" } | Select-Object -First 1
$readLines = @($output | Where-Object { $_ -like "Read *" })
$timeouts = @($output | Where-Object { $_ -like "Read timeout*" })
$elapsed = [Math]::Max($sw.Elapsed.TotalSeconds, 0.001)
$rate = $readLines.Count / $elapsed

if ($pathLine) {
    Write-Host $pathLine
}
Write-Host ("reports={0} elapsed={1:n3}s measured_hz={2:n1}" -f $readLines.Count, $elapsed, $rate)
Write-Host ("timeouts={0}" -f $timeouts.Count)

if ($readLines.Count -gt 0) {
    Write-Host ("first={0}" -f $readLines[0])
    Write-Host ("last ={0}" -f $readLines[$readLines.Count - 1])
}
