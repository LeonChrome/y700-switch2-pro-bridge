param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$DurationSeconds = 20,
    [int]$Seconds = 0,
    [switch]$NoAutoAttach,
    [switch]$MonitorOnly,
    [switch]$ExitOnNonZero
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$Dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if (!$Dotnet) {
    $candidate = Join-Path $env:USERPROFILE ".dotnet-codex\dotnet.exe"
    if (Test-Path $candidate) { $Dotnet = $candidate }
}
if (!$Dotnet) { throw "dotnet SDK not found" }

$Viiper = Join-Path $ProjectRoot "work\tools\viiper\viiper.exe"
if (!(Test-Path $Viiper)) {
    & (Join-Path $ProjectRoot "tools\install_viiper.ps1") -ProjectRoot $ProjectRoot -BuildFromSource
}
if (!(Test-Path $Viiper)) { throw "VIIPER executable not found at $Viiper" }

$LogDir = Join-Path $ProjectRoot "logs\v5_2"
New-Item -ItemType Directory -Force $LogDir | Out-Null
$LogPath = Join-Path $LogDir ("viiper_ns2pro_probe_{0}.log" -f (Get-Date -Format "yyyyMMdd_HHmmss"))

if ($MonitorOnly) {
    if ($Seconds -gt 0) {
        $DurationSeconds = $Seconds
    } elseif ($DurationSeconds -eq 20) {
        $DurationSeconds = 300
    }
}

& $Dotnet build (Join-Path $PSScriptRoot "viiper_ns2pro_probe.csproj") -c Release
$ProbeArgs = @(
    "--viiper", $Viiper,
    "--duration-seconds", "$DurationSeconds",
    "--log", $LogPath
)
if ($NoAutoAttach) { $ProbeArgs += "--no-auto-attach" }
if ($MonitorOnly) { $ProbeArgs += "--monitor-only" }
if ($ExitOnNonZero) { $ProbeArgs += "--exit-on-nonzero" }
& $Dotnet run --project (Join-Path $PSScriptRoot "viiper_ns2pro_probe.csproj") -c Release --no-build -- @ProbeArgs
$exit = $LASTEXITCODE
Write-Output "[NS2PRO_PROBE] log=$LogPath"
exit $exit
