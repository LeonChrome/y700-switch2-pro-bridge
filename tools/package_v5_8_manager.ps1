param(
    [string]$IdfPath = "C:\Espressif\v5.3.3\esp-idf",
    [switch]$SkipFirmwareBuild,
    [switch]$SkipDotnetInstall,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ManagerRoot = Join-Path $RepoRoot "windows\v55_manager_app"
$ReleaseRoot = Join-Path $RepoRoot "release\v5.8"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$SingleExeName = [System.Text.Encoding]::UTF8.GetString([byte[]](
    0x50,0x52,0x4F,0x32,0xE6,0x89,0x8B,0xE6,0x9F,0x84,0xE6,0x97,0xA0,0xE7,0xBA,0xBF,
    0xE6,0x8E,0xA5,0xE6,0x94,0xB6,0xE5,0x99,0xA8,0xE6,0x8E,0xA7,0xE5,0x88,0xB6,
    0xE6,0x9D,0xBF,0x2D,0x61,0x69,0x6F,0x2D,0x76,0x35,0x2E,0x38,0x2E,0x33,0x2E,0x65,0x78,0x65))
$SingleExe = Join-Path $ReleaseRoot $SingleExeName
$LegacySingleExe = Join-Path $ReleaseRoot "Y700Switch2V55Manager-aio-v5.8.0.exe"
$HashFile = Join-Path $ReleaseRoot "SHA256SUMS-v5.8.3.txt"
$DotnetRoot = Join-Path $RepoRoot "work\dotnet"

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V5_8_PACKAGE] $Name=$Value"
}

function Get-CSharpCompiler {
    $candidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\\Framework\\v4.0.30319\\csc.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "csc.exe not found in the .NET Framework compiler locations."
}

function Build-CSharpTool([string]$SourcePath, [string]$OutputPath) {
    $compiler = Get-CSharpCompiler
    Write-Step "csharp_tool" ((Split-Path -Leaf $SourcePath) + " -> " + (Split-Path -Leaf $OutputPath))
    if ($DryRun) {
        return
    }

    & $compiler /nologo /target:exe /out:$OutputPath $SourcePath
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $OutputPath)) {
        throw "Failed to compile managed tool: $SourcePath"
    }
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

    $label = if ($Profile -eq "hid_only") { "HID-only recovery" } else { "DualSense-like bridge + audio lab" }
    return [ordered]@{
        id = $Profile
        label = $label
        app = "esp32s3_dualsense_identity_experiment.bin"
        assets = $assets
    }
}

function Add-Pro2BridgeProfilePayload([string]$TargetRoot) {
    $profile = "pro2_bridge_v5_5"
    $buildDir = Join-Path $RepoRoot "firmware\esp32s3_switch2_bridge\build"
    if (!(Test-Path -LiteralPath $buildDir)) {
        throw "Missing Pro2 bridge firmware build directory: $buildDir"
    }

    $profileRoot = Join-Path $TargetRoot $profile
    New-Item -ItemType Directory -Force -Path (Join-Path $profileRoot "bootloader") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $profileRoot "partition_table") | Out-Null
    Copy-Item -LiteralPath (Join-Path $buildDir "bootloader\bootloader.bin") -Destination (Join-Path $profileRoot "bootloader\bootloader.bin") -Force
    Copy-Item -LiteralPath (Join-Path $buildDir "partition_table\partition-table.bin") -Destination (Join-Path $profileRoot "partition_table\partition-table.bin") -Force
    Copy-Item -LiteralPath (Join-Path $buildDir "esp32s3_switch2_bridge.bin") -Destination (Join-Path $profileRoot "esp32s3_switch2_bridge.bin") -Force
    if (Test-Path -LiteralPath (Join-Path $buildDir "flash_args")) {
        Copy-Item -LiteralPath (Join-Path $buildDir "flash_args") -Destination (Join-Path $profileRoot "flash_args") -Force
    }

    $assetDefs = @(
        @{ offset = "0x0"; path = "$profile/bootloader/bootloader.bin" },
        @{ offset = "0x8000"; path = "$profile/partition_table/partition-table.bin" },
        @{ offset = "0x10000"; path = "$profile/esp32s3_switch2_bridge.bin" }
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

    return [ordered]@{
        id = $profile
        label = "Pro2 / Nintendo bridge"
        app = "esp32s3_switch2_bridge.bin"
        assets = $assets
    }
}

function Add-XInputBridgeProfilePayload([string]$TargetRoot) {
    $profile = "xinput_bridge_v5_8"
    $buildDir = Join-Path $RepoRoot "work\build\v5_8_xinput_bridge"
    if (!(Test-Path -LiteralPath $buildDir)) {
        throw "Missing XInput bridge firmware build directory: $buildDir"
    }

    $profileRoot = Join-Path $TargetRoot $profile
    New-Item -ItemType Directory -Force -Path (Join-Path $profileRoot "bootloader") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $profileRoot "partition_table") | Out-Null
    Copy-Item -LiteralPath (Join-Path $buildDir "bootloader\bootloader.bin") -Destination (Join-Path $profileRoot "bootloader\bootloader.bin") -Force
    Copy-Item -LiteralPath (Join-Path $buildDir "partition_table\partition-table.bin") -Destination (Join-Path $profileRoot "partition_table\partition-table.bin") -Force
    Copy-Item -LiteralPath (Join-Path $buildDir "esp32s3_switch2_bridge.bin") -Destination (Join-Path $profileRoot "esp32s3_switch2_bridge.bin") -Force
    if (Test-Path -LiteralPath (Join-Path $buildDir "flash_args")) {
        Copy-Item -LiteralPath (Join-Path $buildDir "flash_args") -Destination (Join-Path $profileRoot "flash_args") -Force
    }

    $assetDefs = @(
        @{ offset = "0x0"; path = "$profile/bootloader/bootloader.bin" },
        @{ offset = "0x8000"; path = "$profile/partition_table/partition-table.bin" },
        @{ offset = "0x10000"; path = "$profile/esp32s3_switch2_bridge.bin" }
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

    return [ordered]@{
        id = $profile
        label = "Xbox / XInput bridge"
        app = "esp32s3_switch2_bridge.bin"
        assets = $assets
    }
}

function Refresh-EmbeddedAssets {
    Write-Step "embedded_assets" "refresh"
    if ($DryRun) { return }

    $firmwareRoot = Join-Path $ManagerRoot "embedded\firmware\v5.8"
    $toolsRoot = Join-Path $ManagerRoot "embedded\tools"
    New-Item -ItemType Directory -Force -Path $firmwareRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null

    $profiles = @()
    $profiles += Add-FirmwareProfilePayload "hid_audio_uac1_4ch_ds5like" $firmwareRoot
    $profiles += Add-FirmwareProfilePayload "hid_only" $firmwareRoot
    $profiles += Add-Pro2BridgeProfilePayload $firmwareRoot
    $profiles += Add-XInputBridgeProfilePayload $firmwareRoot

    $manifest = [ordered]@{
        packageVersion = "v5.8.3-aio"
        firmwareVersion = "5.8.3-manager"
        target = "esp32s3"
        flashMode = "dio"
        flashFreq = "80m"
        flashSize = "16MB"
        defaultProfile = "pro2_bridge_v5_5"
        profiles = $profiles
        notes = "V5.8 manager bundle for PRO2 wireless receiver control board: Pro2 / Nintendo bridge, Xbox/XInput bridge, DualSense-like bridge with controller-audio lab kept on the side, HID-only recovery, embedded esptool, and XInput host rumble probe."
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path (Join-Path $firmwareRoot "firmware_manifest.json")

    $esptool = Join-Path $RepoRoot "windows\manager_app\embedded\tools\esptool.exe"
    if (!(Test-Path -LiteralPath $esptool)) { throw "Missing source esptool: $esptool" }
    Copy-Item -LiteralPath $esptool -Destination (Join-Path $toolsRoot "esptool.exe") -Force

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\send_v5_5_haptic_audio_test.ps1") -CompileOnly
    Copy-Item -LiteralPath (Join-Path $RepoRoot "tools\SendV55HapticAudioTest.exe") -Destination (Join-Path $toolsRoot "SendV55HapticAudioTest.exe") -Force
    $xinputProbeSource = Join-Path $RepoRoot "tools\SteamXInputRumbleProbe.cs"
    $xinputProbeExe = Join-Path $RepoRoot "tools\SteamXInputRumbleProbe.exe"
    Build-CSharpTool $xinputProbeSource $xinputProbeExe
    Copy-Item -LiteralPath $xinputProbeExe -Destination (Join-Path $toolsRoot "SteamXInputRumbleProbe.exe") -Force

    $icon = Join-Path $RepoRoot "windows\manager_app\assets\icon.ico"
    if (Test-Path -LiteralPath $icon) {
        New-Item -ItemType Directory -Force -Path (Join-Path $ManagerRoot "assets") | Out-Null
        Copy-Item -LiteralPath $icon -Destination (Join-Path $ManagerRoot "assets\icon.ico") -Force
    }
}

if (!$SkipFirmwareBuild) {
    Write-Step "firmware_build" "pro2_bridge_v5_5"
    if (!$DryRun) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\esp32s3\build.ps1") -IdfPath $IdfPath
        if ($LASTEXITCODE -ne 0) { throw "Firmware build failed: pro2_bridge_v5_5" }
    }

    Write-Step "firmware_build" "xinput_bridge_v5_8"
    if (!$DryRun) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\esp32s3\build.ps1") `
            -IdfPath $IdfPath `
            -BuildDir "work\build\v5_8_xinput_bridge" `
            -DeviceDefaultMode XINPUT_EXPERIMENT_MODE
        if ($LASTEXITCODE -ne 0) { throw "Firmware build failed: xinput_bridge_v5_8" }
    }

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
    if (Test-Path -LiteralPath $LegacySingleExe) { Remove-Item -LiteralPath $LegacySingleExe -Force }
    Get-ChildItem -LiteralPath $ReleaseRoot -Filter "*aio-v5.8.3.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $SingleExe -and $_.FullName -ne $LegacySingleExe } |
        Remove-Item -Force

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

    $verifyLog = Join-Path $RepoRoot "work\v5_8_manager_package_verify.txt"
    Remove-Item -LiteralPath $verifyLog -Force -ErrorAction SilentlyContinue
    $verifyProcess = Start-Process -FilePath $SingleExe `
        -ArgumentList @("--verify-package", ('"{0}"' -f $verifyLog)) `
        -Wait -PassThru
    if ($verifyProcess.ExitCode -ne 0) {
        $verifyDetails = if (Test-Path -LiteralPath $verifyLog) {
            Get-Content -LiteralPath $verifyLog -Raw
        } else {
            "verification log was not created"
        }
        throw "Published manager package verification failed (exit=$($verifyProcess.ExitCode)):`n$verifyDetails"
    }
    $verifyDetails = Get-Content -LiteralPath $verifyLog -Raw
    $verifyData = @{}
    foreach ($line in Get-Content -LiteralPath $verifyLog) {
        $parts = $line -split "=", 2
        if ($parts.Count -eq 2) {
            $verifyData[$parts[0].Trim()] = $parts[1].Trim()
        }
    }
    if ($verifyData["result"] -ne "passed" -or
        $verifyData["profiles"] -ne "hid_audio_uac1_4ch_ds5like,hid_only,pro2_bridge_v5_5,xinput_bridge_v5_8" -or
        $verifyData["asset_count"] -ne "12") {
        throw "Published manager package verification returned unexpected data:`n$verifyDetails"
    }
    Write-Step "package_verify" "passed"

    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $SingleExe
    $hashLine = "{0}  {1}`r`n" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $SingleExe)
    [System.IO.File]::WriteAllText($HashFile, $hashLine, [System.Text.Encoding]::UTF8)
    if (Test-Path -LiteralPath $PublishRoot) {
        Remove-Item -LiteralPath $PublishRoot -Recurse -Force
    }
    Write-Step "exe" (($SingleExe.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    Write-Step "sha256" $hash.Hash.ToLowerInvariant()
}
