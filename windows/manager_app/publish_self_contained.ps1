param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Project = Join-Path $PSScriptRoot "Y700Switch2Manager.csproj"
$LocalDotnet = Join-Path $env:USERPROFILE ".dotnet-codex\dotnet.exe"

$Dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($Dotnet) {
    $DotnetExe = $Dotnet.Source
} elseif (Test-Path -LiteralPath $LocalDotnet) {
    $DotnetExe = $LocalDotnet
} else {
    throw "dotnet SDK not found. Install .NET 8 SDK or run dotnet-install.ps1 into $env:USERPROFILE\.dotnet-codex."
}

& $DotnetExe publish $Project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "publish failed"
}

$PublishDir = Join-Path $PSScriptRoot "bin\$Configuration\net8.0-windows\$Runtime\publish"
Write-Host "Published manager exe:"
Write-Host (Join-Path $PublishDir "Y700Switch2Manager.exe")
