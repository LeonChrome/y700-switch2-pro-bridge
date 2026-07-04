param(
    [switch]$SkipDotnetInstall,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$Version = "6.2.22"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ManagerRoot = Join-Path $RepoRoot "windows\v60_viiper_app"
$ReleaseRoot = Join-Path $RepoRoot "release\v6.2.22"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$UsbipSourceRoot = Join-Path $RepoRoot "tools\usbip-win2\v0.9.7.7"
$UsbipReleaseRoot = Join-Path $ReleaseRoot "usbip-win2\v0.9.7.7"
$UsbipInstallerName = "USBip-0.9.7.7-x64.exe"
$SingleExeName = [System.Text.Encoding]::UTF8.GetString([byte[]](
    0xE6,0x96,0xB0,0xE5,0x92,0x8C,0xE8,0x81,0x94,0xE8,0x83,0x9C,0x56,0x49,0x49,0x50,
    0x45,0x52,0xE7,0x89,0x88,0xE6,0x9C,0xAC,0x2D,0x61,0x69,0x6F,0x2D,0x76,
    0x36,0x2E,0x32,0x2E,0x32,0x32,0x2E,0x65,0x78,0x65))
$AsciiExeName = "XinHeLianSheng-VIIPER-aio-v$Version.exe"
$SingleExe = Join-Path $ReleaseRoot $SingleExeName
$AsciiExe = Join-Path $ReleaseRoot $AsciiExeName
$HashFile = Join-Path $ReleaseRoot "SHA256SUMS-v$Version.txt"
$ReadmeFile = Join-Path $ReleaseRoot "README-v$Version.md"
$DotnetRoot = Join-Path $RepoRoot "work\dotnet"

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V6_2_22_PACKAGE] $Name=$Value"
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

    $readmeBase64 = "IyBWNi4yLjIyIOWbm+S6uuS4ieaooeeJiAoKIyMg5pu05paw6YeN54K5CgotIOaWsOWSjOiBlOiDnCAvIFBTNeOAgVBTNSBFZGdlIC8g6IOM6ZSu44CBUHJvMiAvIE5pbnRlbmRv44CBWGJveCAvIFhJbnB1dCDlhajmqKHlvI/mlK/mjIEgMS00IOS4queLrOeriyBQcm8yIEJMRSBTbG9044CCCi0g5q+P5LiqIFNsb3Qg54us56uL5Yib5bu6IFZJSVBFUiDomZrmi5/orr7lpIfjgIHni6znq4vov57mjqXnnJ/lrp4gUHJvMuOAgeeLrOeri+i+k+WFpeWSjOmch+WKqOWbnuS8oOOAggotIOWIh+aooeW8j+OAgeWBnOatouOAgeW8guW4uOaBouWkjeS8muiHquWKqOa4heeQhiBVU0JJUCDmrovnlZnnq6/lj6PvvIzlh4/lsJEgU3RlYW0g6YeN5aSN5p6a5Li+44CCCi0gVVNCSVAg5bey5a6J6KOF5L2G6amx5Yqo5pyq5bCx57uq5pe277yM5LiN5YaN5Y+N5aSN6Ieq5Yqo5ZCv5Yqo5a6J6KOF5Zmo77yb6YCa5bi46ZyA6KaB6YeN5ZCvIFdpbmRvd3PjgIIKLSDmlrDlop7miYvmn4Tlm77moIfvvIznu5/kuIDnqpflj6PjgIHku7vliqHmoI/lkozmiZjnm5jop4bop4njgIIKCiMjIOS9v+eUqOaPkOmGkgoKLSDpppbmrKHkvb/nlKjoi6Xlronoo4UgVVNCSVAg5ZCO5LuN5o+Q56S66amx5Yqo5pyq5bCx57uq77yM6K+35YWI6YeN5ZCvIFdpbmRvd3PjgIIKLSBQUzUgRWRnZSDmmK/ni6znq4sgYDA1NEM6MERGMmAg6Lqr5Lu977yb5bCR5pWw5ri45oiP5Y+v6IO95Y+q5a+555m95ZCN5Y2V5Lit55qE5qCH5YeGIER1YWxTZW5zZSBgMDU0QzowQ0U2YCDlkK/nlKjljp/nlJ/pmYDonrrku6rvvIzmraTnsbvmuLjmiI/or7fkvb/nlKjigJzmlrDlkozogZTog5wgLyBQUzXigJ3mqKHlvI/jgIIK"
    [System.IO.File]::WriteAllBytes($ReadmeFile, [Convert]::FromBase64String($readmeBase64))

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
