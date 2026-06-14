param(
    [switch]$SkipDotnetInstall,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ManagerRoot = Join-Path $RepoRoot "windows\v60_viiper_app"
$ReleaseRoot = Join-Path $RepoRoot "release\v6.0"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$UsbipSourceRoot = Join-Path $RepoRoot "tools\usbip-win2\v0.9.7.7"
$UsbipReleaseRoot = Join-Path $ReleaseRoot "usbip-win2\v0.9.7.7"
$UsbipInstallerName = "USBip-0.9.7.7-x64.exe"
$SingleExeName = [System.Text.Encoding]::UTF8.GetString([byte[]](
    0xE6,0x96,0xB0,0xE5,0x92,0x8C,0xE8,0x81,0x94,0xE8,0x83,0x9C,0x56,0x49,0x49,0x50,
    0x45,0x52,0xE7,0x89,0x88,0xE6,0x9C,0xAC,0x2D,0x61,0x69,0x6F,0x2D,0x76,0x36,
    0x2E,0x30,0x2E,0x30,0x2D,0x70,0x72,0x65,0x76,0x69,0x65,0x77,0x2E,0x65,0x78,0x65))
$SingleExe = Join-Path $ReleaseRoot $SingleExeName
$HashFile = Join-Path $ReleaseRoot "SHA256SUMS-v6.0.0-preview.txt"
$DotnetRoot = Join-Path $RepoRoot "work\dotnet"

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V6_0_PACKAGE] $Name=$Value"
}

function Remove-TreeWithRetry([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -lt 8) { Start-Sleep -Milliseconds 500 }
        }
    }
    throw "Unable to remove directory after retries: $Path"
}

function Ensure-Dotnet {
    $local = Join-Path $DotnetRoot "dotnet.exe"
    if (Test-Path -LiteralPath $local) { return $local }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    if ($SkipDotnetInstall) { throw "dotnet SDK not found and -SkipDotnetInstall was set." }
    throw "dotnet SDK not found. Run tools/setup_dev_environment.ps1 first."
}

$dotnet = Ensure-Dotnet
Write-Step "dotnet" $dotnet

if (!$DryRun) {
    New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
    Remove-TreeWithRetry $PublishRoot
    Get-ChildItem -LiteralPath $ReleaseRoot -Filter "*v6.0.0-preview*.exe" -ErrorAction SilentlyContinue |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $ReleaseRoot -Filter "SHA256SUMS-v6.0.0-preview*.txt" -ErrorAction SilentlyContinue |
        Remove-Item -Force
    Remove-TreeWithRetry (Join-Path $ReleaseRoot "usbip-win2")

    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    & $dotnet publish (Join-Path $ManagerRoot "Y700Switch2V60Viiper.csproj") `
        -c Release -r win-x64 --self-contained true -o $PublishRoot `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $publishedExe = Join-Path $PublishRoot "Y700Switch2V60Viiper.exe"
    if (!(Test-Path -LiteralPath $publishedExe)) {
        throw "Published exe not found: $publishedExe"
    }
    Copy-Item -LiteralPath $publishedExe -Destination $SingleExe -Force
    if (!(Test-Path -LiteralPath (Join-Path $UsbipSourceRoot $UsbipInstallerName))) {
        throw "Bundled usbip-win2 installer not found: $(Join-Path $UsbipSourceRoot $UsbipInstallerName)"
    }
    New-Item -ItemType Directory -Force -Path $UsbipReleaseRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $UsbipSourceRoot $UsbipInstallerName) -Destination (Join-Path $UsbipReleaseRoot $UsbipInstallerName) -Force
    Copy-Item -LiteralPath (Join-Path $UsbipSourceRoot "LICENSE.txt") -Destination (Join-Path $UsbipReleaseRoot "LICENSE.txt") -Force

    $hashTargets = @(
        $SingleExe,
        (Join-Path $UsbipReleaseRoot $UsbipInstallerName)
    )
    $hashLines = foreach ($target in $hashTargets) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $target
        $relative = ($target.Substring($RepoRoot.Length + 1)) -replace '\\','/'
        "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $relative
    }
    [System.IO.File]::WriteAllText($HashFile, (($hashLines -join "`r`n") + "`r`n"), [System.Text.Encoding]::UTF8)
    Remove-TreeWithRetry $PublishRoot
    Write-Step "exe" (($SingleExe.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    Write-Step "usbip" ((Join-Path $UsbipReleaseRoot $UsbipInstallerName).Substring($RepoRoot.Length + 1) -replace '\\','/')
    Write-Step "sha256_file" (($HashFile.Substring($RepoRoot.Length + 1)) -replace '\\','/')
}
