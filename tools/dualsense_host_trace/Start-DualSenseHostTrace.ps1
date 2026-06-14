param(
    [int]$DurationSeconds = 1800,
    [string]$ComPort = "",
    [switch]$RumbleTest,
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Continue"
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path ([Environment]::GetFolderPath("Desktop")) "DualSenseHostTrace_$stamp"
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$start = Get-Date
$exe = Join-Path $PSScriptRoot "DualSenseHostTrace.exe"
if (!(Test-Path -LiteralPath $exe)) {
    throw "Missing DualSenseHostTrace.exe beside this script."
}

$args = @("--duration-seconds", $DurationSeconds, "--output", $OutputRoot)
if (![string]::IsNullOrWhiteSpace($ComPort)) {
    $args += @("--com", $ComPort)
}
if ($RumbleTest) {
    $args += "--rumble-test"
}

Write-Host "Starting DualSense host trace. Keep this window open while testing."
Write-Host "Output: $OutputRoot"
& $exe @args

$eventOut = Join-Path $OutputRoot "windows_events.txt"
$providers = @(
    "Microsoft-Windows-Kernel-PnP",
    "Microsoft-Windows-DriverFrameworks-UserMode",
    "Microsoft-Windows-UserPnp",
    "Microsoft-Windows-USB-USBHUB3"
)

"Window start: $($start.ToString('O'))" | Set-Content -Encoding UTF8 -LiteralPath $eventOut
"Window end: $((Get-Date).ToString('O'))" | Add-Content -Encoding UTF8 -LiteralPath $eventOut
foreach ($provider in $providers) {
    try {
        Get-WinEvent -FilterHashtable @{
            LogName = "System"
            ProviderName = $provider
            StartTime = $start.AddSeconds(-5)
        } -ErrorAction Stop |
            Select-Object TimeCreated, Id, LevelDisplayName, ProviderName, Message |
            Format-List |
            Out-String -Width 4096 |
            Add-Content -Encoding UTF8 -LiteralPath $eventOut
    } catch {
        "Provider $provider unavailable: $($_.Exception.Message)" |
            Add-Content -Encoding UTF8 -LiteralPath $eventOut
    }
}

$setupApi = Join-Path $env:WINDIR "INF\setupapi.dev.log"
if (Test-Path -LiteralPath $setupApi) {
    Copy-Item -LiteralPath $setupApi -Destination (Join-Path $OutputRoot "setupapi.dev.end.log") -Force
}

$zip = "$OutputRoot.zip"
Compress-Archive -LiteralPath $OutputRoot -DestinationPath $zip -Force
Write-Host "Capture complete: $zip"
