param(
    [switch]$SkipDotnetInstall,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$Version = "6.2.31"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ManagerRoot = Join-Path $RepoRoot "windows\v60_viiper_app"
$ReleaseRoot = Join-Path $RepoRoot "release\v6.2.31"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$UsbipSourceRoot = Join-Path $RepoRoot "tools\usbip-win2\v0.9.7.7"
$UsbipReleaseRoot = Join-Path $ReleaseRoot "usbip-win2\v0.9.7.7"
$UsbipInstallerName = "USBip-0.9.7.7-x64.exe"
$ChineseBrand = -join (@(0x65B0, 0x548C, 0x8054, 0x80DC) | ForEach-Object { [char]$_ })
$ChineseVersionWord = -join (@(0x7248, 0x672C) | ForEach-Object { [char]$_ })
$SingleExeName = "${ChineseBrand}VIIPER${ChineseVersionWord}-aio-v$Version.exe"
$AsciiExeName = "XinHeLianSheng-VIIPER-aio-v$Version.exe"
$SingleExe = Join-Path $ReleaseRoot $SingleExeName
$AsciiExe = Join-Path $ReleaseRoot $AsciiExeName
$HashFile = Join-Path $ReleaseRoot "SHA256SUMS-v$Version.txt"
$ReadmeFile = Join-Path $ReleaseRoot "README-v$Version.md"
$ReadmeSource = Join-Path $RepoRoot "tools\release_notes_v6_2_31.zh-CN.md"
$DotnetRoot = Join-Path $RepoRoot "work\dotnet"

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V6_2_31_PACKAGE] $Name=$Value"
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
    throw "dotnet SDK not found. Install the .NET 8 SDK first."
}

$dotnet = Ensure-Dotnet
Write-Step "dotnet" $dotnet

if (!$DryRun) {
    New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
    Remove-TreeWithRetry $PublishRoot
    Get-ChildItem -LiteralPath $ReleaseRoot -Filter "*v$Version*.exe" -ErrorAction SilentlyContinue |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $ReleaseRoot -Filter "SHA256SUMS-v$Version*.txt" -ErrorAction SilentlyContinue |
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
    Copy-Item -LiteralPath $publishedExe -Destination $AsciiExe -Force

    if (!(Test-Path -LiteralPath (Join-Path $UsbipSourceRoot $UsbipInstallerName))) {
        throw "Bundled usbip-win2 installer not found: $(Join-Path $UsbipSourceRoot $UsbipInstallerName)"
    }
    New-Item -ItemType Directory -Force -Path $UsbipReleaseRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $UsbipSourceRoot $UsbipInstallerName) -Destination (Join-Path $UsbipReleaseRoot $UsbipInstallerName) -Force
    Copy-Item -LiteralPath (Join-Path $UsbipSourceRoot "LICENSE.txt") -Destination (Join-Path $UsbipReleaseRoot "LICENSE.txt") -Force

    if (!(Test-Path -LiteralPath $ReadmeSource)) {
        throw "Release notes not found: $ReadmeSource"
    }
    Copy-Item -LiteralPath $ReadmeSource -Destination $ReadmeFile -Force

    $hashTargets = @(
        $SingleExe,
        $AsciiExe,
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
    Write-Step "github_exe" (($AsciiExe.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    Write-Step "usbip" ((Join-Path $UsbipReleaseRoot $UsbipInstallerName).Substring($RepoRoot.Length + 1) -replace '\\','/')
    Write-Step "readme" (($ReadmeFile.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    Write-Step "sha256_file" (($HashFile.Substring($RepoRoot.Length + 1)) -replace '\\','/')
}
