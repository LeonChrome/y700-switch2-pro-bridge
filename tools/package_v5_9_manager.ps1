param(
    [string]$IdfPath,
    [switch]$SkipFirmwareBuild,
    [switch]$SkipEmbeddedRefresh,
    [switch]$SkipDotnetInstall,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ManagerRoot = Join-Path $RepoRoot "windows\v55_manager_app"
$ReleaseRoot = Join-Path $RepoRoot "release\v5.9"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$SingleExeName = [System.Text.Encoding]::UTF8.GetString([byte[]](
    0xE6,0x96,0xB0,0xE5,0x92,0x8C,0xE8,0x81,0x94,0xE8,0x83,0x9C,0xE7,0x89,0x88,
    0xE6,0x9C,0xAC,0x2D,0x61,0x69,0x6F,0x2D,0x76,0x35,0x2E,0x39,0x2E,0x38,0x2E,
    0x65,0x78,0x65))
$SingleExe = Join-Path $ReleaseRoot $SingleExeName
$LegacySingleExe = Join-Path $ReleaseRoot "Y700Switch2V55Manager-aio-v5.9.8.exe"
$HashFile = Join-Path $ReleaseRoot "SHA256SUMS-v5.9.8.txt"
$DotnetRoot = Join-Path $RepoRoot "work\dotnet"
. (Join-Path $RepoRoot "tools\esp32s3\idf_environment.ps1")

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V5_9_PACKAGE] $Name=$Value"
}

function Remove-TreeWithRetry([string]$Path, [switch]$Required) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -lt 8) {
                Start-Sleep -Milliseconds 500
            }
        }
    }
    if ($Required) {
        throw "Unable to remove directory after retries: $Path"
    }
    Write-Step "cleanup_deferred" $Path
}

function Remove-FileWithRetry([string]$Path, [switch]$Required) {
    if (!(Test-Path -LiteralPath $Path)) { return $true }
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
            return $true
        } catch {
            if ($attempt -lt 6) {
                Start-Sleep -Milliseconds 300
            }
        }
    }
    if ($Required) {
        throw "Unable to remove file after retries: $Path"
    }
    Write-Step "cleanup_skip_locked" $Path
    return $false
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

function Publish-DualNs2ProHostTool([string]$ToolsRoot) {
    $dotnet = Ensure-Dotnet
    $publishRoot = Join-Path $RepoRoot "work\dual_ns2pro_host_publish"
    Write-Step "dual_ns2pro_host" "publish"
    if (!$DryRun) {
        Remove-TreeWithRetry $publishRoot
        $env:DOTNET_ROOT = Split-Path -Parent $dotnet
        $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
        & $dotnet publish (Join-Path $RepoRoot "windows\dual_ns2pro_host\DualNs2ProHost.csproj") `
            -c Release -r win-x64 --self-contained true -o $publishRoot `
            /p:PublishSingleFile=true `
            /p:IncludeNativeLibrariesForSelfExtract=true `
            /p:EnableCompressionInSingleFile=true `
            /p:RestoreIgnoreFailedSources=true
        if ($LASTEXITCODE -ne 0) { throw "DualNs2ProHost publish failed" }

        $hostExe = Join-Path $publishRoot "DualNs2ProHost.exe"
        if (!(Test-Path -LiteralPath $hostExe)) {
            throw "Published DualNs2ProHost.exe not found: $hostExe"
        }
        Copy-Item -LiteralPath $hostExe -Destination (Join-Path $ToolsRoot "DualNs2ProHost.exe") -Force

        $viiperSource = Join-Path $RepoRoot "tools\viiper\haptic-v0.8.0\viiper-haptic.exe"
        if (!(Test-Path -LiteralPath $viiperSource)) {
            throw "Missing VIIPER haptic server: $viiperSource"
        }
        $viiperTargetRoot = Join-Path $ToolsRoot "viiper\haptic-v0.8.0"
        New-Item -ItemType Directory -Force -Path $viiperTargetRoot | Out-Null
        Copy-Item -LiteralPath $viiperSource -Destination (Join-Path $viiperTargetRoot "viiper-haptic.exe") -Force
    }
}

function Add-FirmwareProfilePayload([string]$Profile, [string]$TargetRoot) {
    $buildDir = Join-Path $RepoRoot "work\b\ds5\$Profile"
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
    $buildDir = Join-Path $RepoRoot "work\b\pro2"
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

function Add-XInputBridgeProfilePayload(
    [string]$TargetRoot,
    [string]$Profile = "xinput_bridge_v5_8",
    [string]$BuildDirRelative = "work\b\xinput",
    [string]$Label = "Xbox / XInput bridge"
) {
    $profile = $Profile
    $buildDir = Join-Path $RepoRoot $BuildDirRelative
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
        label = $Label
        app = "esp32s3_switch2_bridge.bin"
        assets = $assets
    }
}

function Add-DualPro2ProbeProfilePayload([string]$TargetRoot) {
    return Add-XInputBridgeProfilePayload `
        -TargetRoot $TargetRoot `
        -Profile "dual_pro2_probe_v5_9" `
        -BuildDirRelative "work\b\dual_pro2" `
        -Label "Dual Pro2 BLE capacity probe"
}

function Refresh-EmbeddedAssets {
    Write-Step "embedded_assets" "refresh"
    if ($DryRun) { return }

    $firmwareRoot = Join-Path $ManagerRoot "embedded\firmware\v5.9"
    $toolsRoot = Join-Path $ManagerRoot "embedded\tools"
    if (Test-Path -LiteralPath $firmwareRoot) {
        Remove-Item -LiteralPath $firmwareRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $firmwareRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null

    $profiles = @()
    $profiles += Add-FirmwareProfilePayload "hid_audio_uac1_4ch_ds5like" $firmwareRoot
    $profiles += Add-FirmwareProfilePayload "hid_only" $firmwareRoot
    $profiles += Add-Pro2BridgeProfilePayload $firmwareRoot
    $profiles += Add-XInputBridgeProfilePayload $firmwareRoot
    $profiles += Add-DualPro2ProbeProfilePayload $firmwareRoot

    $manifest = [ordered]@{
        packageVersion = "v5.9.8-aio"
        firmwareVersion = "5.9.8-manager"
        target = "esp32s3"
        flashMode = "dio"
        flashFreq = "80m"
        flashSize = "16MB"
        defaultProfile = "pro2_bridge_v5_5"
        profiles = $profiles
        notes = "V5.9.8 Xin He Lian Sheng Edge bundle: PS5 Edge identity (054C:0DF2), Edge L4/R4 back paddles, four-channel HD haptics, ordinary-rumble arbitration, guided first pairing/reconnect/controller replacement, Pro2/Nintendo, Xbox/XInput with firmware-side Pro2 GL/GR mapping, embedded esptool, XInput probe, CH343 driver repair, serial watchdogs, UI log throttling, BLE MultiProbe telemetry, and a Dual Pro2 BLE capacity probe profile."
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path (Join-Path $firmwareRoot "firmware_manifest.json")

    $esptool = Join-Path $toolsRoot "esptool.exe"
    if (!(Test-Path -LiteralPath $esptool)) { throw "Missing bundled esptool: $esptool" }

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\send_v5_5_haptic_audio_test.ps1") -CompileOnly
    Copy-Item -LiteralPath (Join-Path $RepoRoot "tools\SendV55HapticAudioTest.exe") -Destination (Join-Path $toolsRoot "SendV55HapticAudioTest.exe") -Force
    $xinputProbeSource = Join-Path $RepoRoot "tools\SteamXInputRumbleProbe.cs"
    $xinputProbeExe = Join-Path $RepoRoot "tools\SteamXInputRumbleProbe.exe"
    Build-CSharpTool $xinputProbeSource $xinputProbeExe
    Copy-Item -LiteralPath $xinputProbeExe -Destination (Join-Path $toolsRoot "SteamXInputRumbleProbe.exe") -Force
    Publish-DualNs2ProHostTool $toolsRoot

    $icon = Join-Path $ManagerRoot "assets\icon.ico"
    if (!(Test-Path -LiteralPath $icon)) { throw "Missing manager icon: $icon" }
}

if (!$SkipFirmwareBuild) {
    $IdfPath = Resolve-Y700IdfPath -RequestedPath $IdfPath
    Write-Step "firmware_build" "pro2_bridge_v5_5"
    if (!$DryRun) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\esp32s3\build.ps1") `
            -IdfPath $IdfPath `
            -DeviceDefaultMode NINTENDO_EXPERIMENT_MODE
        if ($LASTEXITCODE -ne 0) { throw "Firmware build failed: pro2_bridge_v5_5" }
    }

    Write-Step "firmware_build" "xinput_bridge_v5_8"
    if (!$DryRun) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\esp32s3\build.ps1") `
            -IdfPath $IdfPath `
            -BuildDir "work\b\xinput" `
            -DeviceDefaultMode XINPUT_EXPERIMENT_MODE
        if ($LASTEXITCODE -ne 0) { throw "Firmware build failed: xinput_bridge_v5_8" }
    }

    Write-Step "firmware_build" "dual_pro2_probe_v5_9"
    if (!$DryRun) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\esp32s3\build.ps1") `
            -IdfPath $IdfPath `
            -BuildDir "work\b\dual_pro2" `
            -DeviceDefaultMode DUAL_PRO2_EXPERIMENT_MODE
        if ($LASTEXITCODE -ne 0) { throw "Firmware build failed: dual_pro2_probe_v5_9" }
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

if (!$SkipEmbeddedRefresh) {
    Refresh-EmbeddedAssets
} else {
    Write-Step "embedded_assets" "preserved"
}

$dotnet = Ensure-Dotnet
Write-Step "dotnet" $dotnet

if (!$DryRun) {
    New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
    Remove-TreeWithRetry $PublishRoot -Required
    [void](Remove-FileWithRetry $SingleExe -Required)
    [void](Remove-FileWithRetry $LegacySingleExe -Required)
    Get-ChildItem -LiteralPath $ReleaseRoot -Filter "*aio-v5.9.*.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $SingleExe -and $_.FullName -ne $LegacySingleExe } |
        ForEach-Object { [void](Remove-FileWithRetry $_.FullName) }
    Get-ChildItem -LiteralPath $ReleaseRoot -Filter "SHA256SUMS-v5.9.*.txt" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $HashFile } |
        ForEach-Object { [void](Remove-FileWithRetry $_.FullName) }

    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    & $dotnet publish (Join-Path $ManagerRoot "Y700Switch2V55Manager.csproj") `
        -c Release -r win-x64 --self-contained true -o $PublishRoot `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true `
        /p:RestoreIgnoreFailedSources=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $publishedExe = Join-Path $PublishRoot "Y700Switch2V55Manager.exe"
    if (!(Test-Path -LiteralPath $publishedExe)) {
        throw "Published exe not found: $publishedExe"
    }
    Copy-Item -LiteralPath $publishedExe -Destination $SingleExe -Force

    $verifyLog = Join-Path $RepoRoot "work\v5_9_3_manager_package_verify.txt"
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
        $verifyData["profiles"] -ne "hid_audio_uac1_4ch_ds5like,hid_only,pro2_bridge_v5_5,xinput_bridge_v5_8,dual_pro2_probe_v5_9" -or
        $verifyData["asset_count"] -ne "15" -or
        $verifyData["dual_ns2pro_host_exists"] -ne "true") {
        throw "Published manager package verification returned unexpected data:`n$verifyDetails"
    }
    Write-Step "package_verify" "passed"

    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $SingleExe
    $hashLine = "{0}  {1}`r`n" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $SingleExe)
    [System.IO.File]::WriteAllText($HashFile, $hashLine, [System.Text.Encoding]::UTF8)
    Remove-TreeWithRetry $PublishRoot
    Write-Step "exe" (($SingleExe.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    Write-Step "sha256" $hash.Hash.ToLowerInvariant()
}

