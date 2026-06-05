param(
    [string]$Sdl3Path = "",
    [switch]$All,
    [UInt16]$Low = 65535,
    [UInt16]$High = 65535,
    [UInt32]$DurationMs = 800
)

$ErrorActionPreference = "Stop"

$Dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if (!$Dotnet) {
    $candidate = Join-Path $env:USERPROFILE ".dotnet-codex\dotnet.exe"
    if (Test-Path $candidate) { $Dotnet = $candidate }
}
if (!$Dotnet) { throw "dotnet SDK not found" }

$Project = Join-Path $PSScriptRoot "sdl_ns2pro_rumble_test.csproj"
& $Dotnet build $Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$ProbeArgs = @("--low", "$Low", "--high", "$High", "--duration-ms", "$DurationMs")
if ($Sdl3Path) { $ProbeArgs += @("--sdl3", $Sdl3Path) }
if ($All) { $ProbeArgs += "--all" }

& $Dotnet run --project $Project -c Release --no-build -- @ProbeArgs
exit $LASTEXITCODE
