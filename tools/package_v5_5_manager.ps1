param(
    [string]$IdfPath = "C:\Espressif\v5.3.3\esp-idf",
    [switch]$SkipFirmwareBuild,
    [switch]$SkipDotnetInstall,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ManagerRoot = Join-Path $RepoRoot "windows\v55_manager_app"
$ReleaseRoot = Join-Path $RepoRoot "release\v5.5"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$SingleExe = Join-Path $ReleaseRoot "Y700Switch2V55Manager-aio-v5.5.0.exe"
$HashFile = Join-Path $ReleaseRoot "SHA256SUMS-v5.5.0.txt"
$DotnetRoot = Join-Path $RepoRoot "work\dotnet"

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V5_5_PACKAGE] $Name=$Value"
}

function Ensure-Dotnet {
    $local = Join-Path $DotnetRoot "dotnet.exe"
    if (Test-Path -LiteralPath $local) {
        return $local
    }

    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    if ($SkipDotnetInstall) {
        throw "dotnet SDK not found and -SkipDotnetInstall was set."
    }

    Write-Step "dotnet" "install_local_8.0"
    if ($DryRun) {
        return $local
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot "work") | Out-Null
    $installer = Join-Path $RepoRoot "work\dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 8.0 -InstallDir $DotnetRoot -Architecture x64
    if (!(Test-Path -LiteralPath $local)) {
        throw "dotnet local install failed: $local"
    }
    return $local
}

function Add-FirmwareProfilePayload([string]$Profile, [string]$TargetRoot) {
    $buildDir = Join-Path $RepoRoot "work\build\v5_5_dualsense_identity\$Profile"
    if (!(Test-Path -LiteralPath $buildDir)) {
        throw "Missing firmware build directory: $buildDir"
    }

    $profileRoot = Join-Path $TargetRoot $Profile
    New-Item -ItemType Directory -Force -Path (Join-Path $profileRoot "bootloader") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $profileRoot "partition_table") | Out-Null
    Copy-Item -LiteralPath (Join-Path $buildDir "bootloader\bootloader.bin") -Destination (Join-Path $profileRoot "bootloader\bootloader.bin") -Force
    Copy-Item -LiteralPath (Join-Path $buildDir "partition_table\partition-table.bin") -Destination (Join-Path $profileRoot "partition_table\partition-table.bin") -Force
    Copy-Item -LiteralPath (Join-Path $buildDir "esp32s3_dualsense_identity_experiment.bin") -Destination (Join-Path $profileRoot "esp32s3_dualsense_identity_experiment.bin") -Force
    Copy-Item -LiteralPath (Join-Path $buildDir "flash_args") -Destination (Join-Path $profileRoot "flash_args") -Force

    $assetDefs = @(
        @{ offset = "0x0"; path = "$Profile/bootloader/bootloader.bin" },
        @{ offset = "0x8000"; path = "$Profile/partition_table/partition-table.bin" },
        @{ offset = "0x10000"; path = "$Profile/esp32s3_dualsense_identity_experiment.bin" }
    )

    $assets = @()
    foreach ($asset in $assetDefs) {
        $file = Join-Path $TargetRoot ($asset.path -replace '/', [IO.Path]::DirectorySeparatorChar)
        $assets += [ordered]@{
            offset = $asset.offset
            path = $asset.path
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
        }
    }

    $label = if ($Profile -eq "hid_only") { "HID-only recovery" } else { "V5.5 DualSense haptic 4ch" }
    return [ordered]@{
        id = $Profile
        label = $label
        app = "esp32s3_dualsense_identity_experiment.bin"
        assets = $assets
    }
}

function Refresh-EmbeddedAssets {
    Write-Step "embedded_assets" "refresh"
    if ($DryRun) { return }

    $firmwareRoot = Join-Path $ManagerRoot "embedded\firmware\v5.5"
    $toolsRoot = Join-Path $ManagerRoot "embedded\tools"
    New-Item -ItemType Directory -Force -Path $firmwareRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null

    $profiles = @()
    $profiles += Add-FirmwareProfilePayload "hid_audio_uac1_4ch_ds5like" $firmwareRoot
    $profiles += Add-FirmwareProfilePayload "hid_only" $firmwareRoot

    $manifest = [ordered]@{
        packageVersion = "v5.5.0-aio"
        firmwareVersion = "5.5.0-experimental"
        target = "esp32s3"
        flashMode = "dio"
        flashFreq = "80m"
        flashSize = "16MB"
        defaultProfile = "hid_audio_uac1_4ch_ds5like"
        profiles = $profiles
        notes = "V5.5 experimental DualSense-like HID + UAC1 4ch haptic audio to Pro2 raw02 forwarding. Live raw02 defaults off."
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path (Join-Path $firmwareRoot "firmware_manifest.json")

    $esptool = Join-Path $RepoRoot "windows\manager_app\embedded\tools\esptool.exe"
    if (!(Test-Path -LiteralPath $esptool)) { throw "Missing source esptool: $esptool" }
    Copy-Item -LiteralPath $esptool -Destination (Join-Path $toolsRoot "esptool.exe") -Force

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\send_v5_5_haptic_audio_test.ps1") -CompileOnly
    Copy-Item -LiteralPath (Join-Path $RepoRoot "tools\SendV55HapticAudioTest.exe") -Destination (Join-Path $toolsRoot "SendV55HapticAudioTest.exe") -Force

    $icon = Join-Path $RepoRoot "windows\manager_app\assets\icon.ico"
    if (Test-Path -LiteralPath $icon) {
        New-Item -ItemType Directory -Force -Path (Join-Path $ManagerRoot "assets") | Out-Null
        Copy-Item -LiteralPath $icon -Destination (Join-Path $ManagerRoot "assets\icon.ico") -Force
    }
}

if (!$SkipFirmwareBuild) {
    $buildScript = Join-Path $RepoRoot "tools\esp32s3\build_v5_5_dualsense_identity.ps1"
    foreach ($profile in @("hid_audio_uac1_4ch_ds5like", "hid_only")) {
        Write-Step "firmware_build" $profile
        if (!$DryRun) {
            & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -IdfPath $IdfPath -Profile $profile
            if ($LASTEXITCODE -ne 0) { throw "Firmware build failed: $profile" }
        }
    }
}

Refresh-EmbeddedAssets

$dotnet = Ensure-Dotnet
Write-Step "dotnet" $dotnet

if (!$DryRun) {
    New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
    if (Test-Path -LiteralPath $PublishRoot) { Remove-Item -LiteralPath $PublishRoot -Recurse -Force }
    if (Test-Path -LiteralPath $SingleExe) { Remove-Item -LiteralPath $SingleExe -Force }

    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    & $dotnet publish (Join-Path $ManagerRoot "Y700Switch2V55Manager.csproj") `
        -c Release -r win-x64 --self-contained true -o $PublishRoot `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $publishedExe = Join-Path $PublishRoot "Y700Switch2V55Manager.exe"
    if (!(Test-Path -LiteralPath $publishedExe)) {
        throw "Published exe not found: $publishedExe"
    }
    Copy-Item -LiteralPath $publishedExe -Destination $SingleExe -Force
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $SingleExe
    Set-Content -Path $HashFile -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $SingleExe)) -Encoding ascii
    if (Test-Path -LiteralPath $PublishRoot) {
        Remove-Item -LiteralPath $PublishRoot -Recurse -Force
    }
    Write-Step "exe" (($SingleExe.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    Write-Step "sha256" $hash.Hash.ToLowerInvariant()
}

Write-Step "result" "passed"
