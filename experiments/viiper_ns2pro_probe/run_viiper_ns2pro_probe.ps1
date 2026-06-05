param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$DurationSeconds = 20,
    [switch]$NoAutoAttach
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

& $Dotnet build (Join-Path $PSScriptRoot "viiper_ns2pro_probe.csproj") -c Release
$ProbeArgs = @(
    "--viiper", $Viiper,
    "--duration-seconds", "$DurationSeconds",
    "--log", $LogPath
)
if ($NoAutoAttach) { $ProbeArgs += "--no-auto-attach" }
& $Dotnet run --project (Join-Path $PSScriptRoot "viiper_ns2pro_probe.csproj") -c Release --no-build -- @ProbeArgs
$exit = $LASTEXITCODE
Write-Output "[NS2PRO_PROBE] log=$LogPath"
exit $exit
