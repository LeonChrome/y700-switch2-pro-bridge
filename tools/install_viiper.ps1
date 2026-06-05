param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$BuildFromSource,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$ViiperSource = Join-Path $ProjectRoot "work\upstream-research\VIIPER"
$OutDir = Join-Path $ProjectRoot "work\tools\viiper"
$OutExe = Join-Path $OutDir "viiper.exe"

function Find-Go {
    $cmd = Get-Command go -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $local = Join-Path $ProjectRoot "work\deps\go\bin\go.exe"
    if (Test-Path $local) { return (Resolve-Path $local).Path }
    $programFiles = Join-Path $env:ProgramFiles "Go\bin\go.exe"
    if (Test-Path $programFiles) { return $programFiles }
    return $null
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

if ((Test-Path $OutExe) -and !$Force) {
    Write-Output "[VIIPER_INSTALL] viiper=$OutExe"
    & $OutExe --help | Select-Object -First 1
    exit 0
}

if (!(Test-Path $ViiperSource)) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $ViiperSource) | Out-Null
    Write-Output "[VIIPER_INSTALL] cloning Alia5/VIIPER"
    git clone --depth 1 https://github.com/Alia5/VIIPER.git $ViiperSource
}

$go = Find-Go
if (!$go) {
    Write-Output "[VIIPER_INSTALL] blocked: Go is not installed. Install portable Go under work\deps\go or install Go system-wide."
    exit 2
}

Write-Output "[VIIPER_INSTALL] building from source with $go"
$env:CGO_ENABLED = "0"
if (!$env:GOPROXY) { $env:GOPROXY = "https://goproxy.cn,direct" }
if (!$env:GOSUMDB) { $env:GOSUMDB = "sum.golang.google.cn" }
& $go -C $ViiperSource build -o $OutExe .\cmd\viiper
Write-Output "[VIIPER_INSTALL] viiper=$OutExe"
& $OutExe --help | Select-Object -First 1
