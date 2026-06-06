param(
    [string]$IdfPath = "C:\Espressif\v5.3.3\esp-idf",
    [switch]$SkipFirmwareBuild,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ReleaseRoot = Join-Path $RepoRoot "release\v5.5"
$PackageRoot = Join-Path $ReleaseRoot "Y700Switch2V55Manager"
$ZipPath = Join-Path $ReleaseRoot "Y700Switch2V55Manager-aio-v5.5-experimental.zip"

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V5_5_PACKAGE] $Name=$Value"
}

function Find-Csc {
    $candidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    $cmd = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "csc.exe not found"
}

function Resolve-WpfReference([string]$Name) {
    $referenceRoots = @()
    if (${env:ProgramFiles(x86)}) {
        $referenceRoots += Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework"
    }

    foreach ($root in $referenceRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        $match = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName $Name } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($match) { return $match }
    }

    $gacRoots = @(
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL"),
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_64"),
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_32")
    )
    foreach ($root in $gacRoots) {
        $assemblyDir = Join-Path $root ([IO.Path]::GetFileNameWithoutExtension($Name))
        if (!(Test-Path -LiteralPath $assemblyDir)) { continue }
        $match = Get-ChildItem -LiteralPath $assemblyDir -Recurse -Filter $Name -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if ($match) { return $match }
    }

    throw "WPF reference not found: $Name"
}

function Copy-IfExists([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Source) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
        Write-Step "copy" (($Source.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    }
}

if (!$DryRun) {
    if (Test-Path -LiteralPath $PackageRoot) {
        Remove-Item -LiteralPath $PackageRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $PackageRoot | Out-Null
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

Write-Step "host_sender" "compile"
if (!$DryRun) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "tools\send_v5_5_haptic_audio_test.ps1") -CompileOnly
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$managerOut = Join-Path $PackageRoot "Y700Switch2V55Manager.exe"
if ($dotnet) {
    Write-Step "manager_build" "dotnet_publish"
    if (!$DryRun) {
        & $dotnet.Source publish (Join-Path $RepoRoot "windows\v55_manager_app\Y700Switch2V55Manager.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $PackageRoot "app")
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
        Copy-IfExists (Join-Path $PackageRoot "app\Y700Switch2V55Manager.exe") $managerOut
    }
} else {
    Write-Step "manager_build" "framework_csc_fallback"
    if (!$DryRun) {
        $csc = Find-Csc
        $source = Join-Path $RepoRoot "windows\v55_manager_app\ManagerApp.cs"
        $presentationCore = Resolve-WpfReference "PresentationCore.dll"
        $presentationFramework = Resolve-WpfReference "PresentationFramework.dll"
        $windowsBase = Resolve-WpfReference "WindowsBase.dll"
        $systemXaml = Resolve-WpfReference "System.Xaml.dll"
        & $csc /nologo /target:winexe /platform:x64 /out:$managerOut `
            /reference:$presentationCore `
            /reference:$presentationFramework `
            /reference:$windowsBase `
            /reference:$systemXaml `
            /reference:System.dll `
            /reference:System.Core.dll `
            $source
        if ($LASTEXITCODE -ne 0) { throw "Framework WPF fallback build failed" }
    }
}

if (!$DryRun) {
    foreach ($relative in @(
        "tools\send_v5_5_haptic_audio_test.ps1",
        "tools\SendV55HapticAudioTest.cs",
        "tools\SendV55HapticAudioTest.exe",
        "tools\check_v5_5_usb_composite.ps1",
        "tools\check_v5_5_dualsense_identity.ps1",
        "tools\check_v5_5_dualsense_audio.ps1",
        "tools\check_v5_5_dualsense_reports.ps1",
        "tools\esp32s3\flash_v5_5_dualsense_identity.ps1",
        "tools\esp32s3\build_v5_5_dualsense_identity.ps1",
        "tools\esp32s3\monitor.ps1",
        "tools\esp32s3\detect_ports.ps1"
    )) {
        Copy-IfExists (Join-Path $RepoRoot $relative) (Join-Path $PackageRoot $relative)
    }

    foreach ($profile in @("hid_audio_uac1_4ch_ds5like", "hid_only")) {
        $buildDir = Join-Path $RepoRoot "work\build\v5_5_dualsense_identity\$profile"
        Copy-IfExists (Join-Path $buildDir "bootloader\bootloader.bin") (Join-Path $PackageRoot "firmware\$profile\bootloader.bin")
        Copy-IfExists (Join-Path $buildDir "partition_table\partition-table.bin") (Join-Path $PackageRoot "firmware\$profile\partition-table.bin")
        Copy-IfExists (Join-Path $buildDir "esp32s3_dualsense_identity_experiment.bin") (Join-Path $PackageRoot "firmware\$profile\esp32s3_dualsense_identity_experiment.bin")
        Copy-IfExists (Join-Path $buildDir "flash_args") (Join-Path $PackageRoot "firmware\$profile\flash_args")
    }

    $readme = @'
# Y700 Switch2 V5.5 Manager

## 中文

这是 V5.5 实验包，用于验证：

```text
PC / Steam / game
-> ESP32-S3 DualSense-like HID + UAC1 4ch audio
-> haptic audio channel 2/3
-> Pro2 raw02
-> BLE real Switch 2 Pro Controller
```

默认安全状态：

- haptic live forwarding 关闭。
- dry-run 开启。
- raw02 live 必须 BLE connected，并且显式执行 `haptic raw02 on` + `haptic dryrun off`。
- BLE 发送失败会自动关闭 live forwarding。
- `Live On` 会在 Manager 中二次确认。

首次运行：

1. 打开 `Y700Switch2V55Manager.exe`。
2. 选择 CH343P COM 口。
3. 点击 `Flash V5.5 DualSense-Pro2 Haptic`。
4. 重插 native USB / OTG。
5. 依次运行 USB Checks、BLE Connect Last、Haptic Status。
6. 先保持 Dry-run，点击 Send Audio Pattern 看 raw02 preview。
7. 确认 BLE 和 dry-run preview 正常后，再手动开启 Live。

V5.5 不替换 V5.0/V5.2 Pro2 stable firmware。

## English

This is the V5.5 experimental package for DualSense-like HID + UAC1 4ch haptic audio to Pro2 raw02 forwarding.

Default safety:

- haptic live forwarding is off.
- dry-run is on.
- raw02 live requires BLE connected plus explicit `haptic raw02 on` and `haptic dryrun off`.
- BLE send errors automatically disable live forwarding.
- `Live On` asks for confirmation in the Manager.

Recommended first run:

1. Start `Y700Switch2V55Manager.exe`.
2. Select the CH343P COM port.
3. Flash `V5.5 DualSense-Pro2 Haptic`.
4. Replug native USB / OTG.
5. Run USB Checks, BLE Connect Last, and Haptic Status.
6. Keep Dry-run on first and use Send Audio Pattern to inspect raw02 preview.
7. Enable Live manually only after BLE and dry-run preview are healthy.

V5.0/V5.2 Pro2 stable firmware is not replaced by V5.5.
'@
    Set-Content -Path (Join-Path $PackageRoot "README.md") -Value $readme -Encoding UTF8

    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $ZipPath -Force
    $hashFile = Join-Path $ReleaseRoot "SHA256SUMS.txt"
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath
    Set-Content -Path $hashFile -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $ZipPath)) -Encoding ascii
    Write-Step "zip" (($ZipPath.Substring($RepoRoot.Length + 1)) -replace '\\','/')
    Write-Step "sha256" $hash.Hash.ToLowerInvariant()
}

Write-Step "result" "passed"
