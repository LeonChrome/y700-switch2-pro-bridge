param(
    [switch]$SkipDotnetInstall,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$Version = "6.2.26"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ManagerRoot = Join-Path $RepoRoot "windows\v60_viiper_app"
$ReleaseRoot = Join-Path $RepoRoot "release\v6.2.26"
$PublishRoot = Join-Path $ReleaseRoot "publish"
$UsbipSourceRoot = Join-Path $RepoRoot "tools\usbip-win2\v0.9.7.7"
$UsbipReleaseRoot = Join-Path $ReleaseRoot "usbip-win2\v0.9.7.7"
$UsbipInstallerName = "USBip-0.9.7.7-x64.exe"
$SingleExeName = "新和联胜VIIPER版本-aio-v$Version.exe"
$AsciiExeName = "XinHeLianSheng-VIIPER-aio-v$Version.exe"
$SingleExe = Join-Path $ReleaseRoot $SingleExeName
$AsciiExe = Join-Path $ReleaseRoot $AsciiExeName
$HashFile = Join-Path $ReleaseRoot "SHA256SUMS-v$Version.txt"
$ReadmeFile = Join-Path $ReleaseRoot "README-v$Version.md"
$DotnetRoot = Join-Path $RepoRoot "work\dotnet"

function Write-Step([string]$Name, [string]$Value) {
    Write-Output "[V6_2_26_PACKAGE] $Name=$Value"
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

    $readme = @'
# V6.2.26 完结后稳定性审计版

## 更新重点
- PS5 / PS5 Edge IMU 输出现在会利用 Pro2/Switch 输入报告里的 3 个 5ms IMU 子样本做轻量平均，再进入 DualSense 坐标与量纲转换；不改变按键、摇杆、BLE、VIIPER 设备创建逻辑，目标是降低高速转动时的抖动和单样本偶发误差。
- Pro2 BLE 输入源会按 5ms 间隔给 3 个 IMU 子样本补上采样时间戳，日志和后续分析能看见更真实的源采样节奏，而不是把同一包里的 3 个样本都当成同一时刻。
- DualSense HD 音频震动解析继续优先使用后声道；如果游戏/驱动只在前声道输出有效波形，会自动回退到前声道，避免“有音频但无 HD 震动”的兼容性盲区。
- 在 VIIPER 创建虚拟设备后增加强身份校验：新和联胜 / PS5 必须是 054C:0CE6，PS5 Edge 必须是 054C:0DF2，Pro2 / Nintendo 必须是 057E:2069，Xbox / XInput 必须是 045E:028E。若 VIIPER/USBIP 返回的设备身份和当前模式不一致，会直接报错并提示清理残留虚拟设备，避免 Steam 里出现“名字是 DualSense、布局是 Pro2”的错位状态。
- 切换模式前继续自动清理本地 VIIPER bus 与匹配的 USBIP 端口；多 Slot 模式下每个 Slot 独立创建、独立校验、独立失败提示。
- 在主界面和托盘右键菜单加入“开机自启动”开关，使用当前用户 HKCU Run 注册表，不需要管理员权限。
- 加入“启动后自动进入上次模式”开关：启动后会恢复上次选择的新和联胜 / PS5、PS5 Edge、Pro2 / Nintendo 或 Xbox / XInput，并自动进入游戏连接流程。
- 每个 Pro2 Slot 会保存上一次成功 live 的 BLE 地址；开机自动连接时优先寻找上次手柄，避免误连附近的新手柄。
- 开机自动连接采用温和退避策略，不会长期高频猛连 Windows BLE；5 分钟仍未连上会弹出通知并暂停，等待用户手动操作。
- 用户手动点击主界面或托盘菜单里的任何模式/连接/停止操作，会取消本次开机自动连接，转为手动控制。

## 保留能力
- 新和联胜 / PS5：DualSense 身份、HD 音频震动与普通震动调度、PS5 IMU 优化。
- PS5 Edge：Edge 身份与背键支持。
- Pro2 / Nintendo：Steam 原生 Pro Controller 路线。
- Xbox / XInput：广泛兼容路线与背键映射。
- 四个正式模式均支持 1-4 个独立 Pro2 BLE Slot。

## 首次使用提醒
- 如果安装 USBIP 后仍提示驱动未就绪，通常需要重启 Windows。
- 蓝牙扫描不到手柄时，请确认手柄已唤醒、未被 Switch/手机/ESP32/旧 EXE 占用，并确认 USB 蓝牙接收器支持 BLE Central。
'@
    [System.IO.File]::WriteAllText($ReadmeFile, $readme, [System.Text.Encoding]::UTF8)

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


