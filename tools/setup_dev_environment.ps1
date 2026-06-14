param(
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ToolchainRoot = Join-Path $RepoRoot ".toolchain"
$IdfPath = Join-Path $ToolchainRoot "esp-idf-v5.4.2"
$ToolsPath = Join-Path $ToolchainRoot "espressif-tools"
$PythonPath = Join-Path $ToolchainRoot "python-3.11.9\python.exe"

git config --global core.longpaths true
New-Item -ItemType Directory -Force -Path $ToolchainRoot | Out-Null

if (!(Test-Path -LiteralPath (Join-Path $IdfPath "export.ps1"))) {
    Write-Host "[Y700_SETUP] cloning ESP-IDF v5.4.2"
    git -c core.longpaths=true clone `
        --branch v5.4.2 `
        --depth 1 `
        --recursive `
        --shallow-submodules `
        https://github.com/espressif/esp-idf.git `
        $IdfPath
    if ($LASTEXITCODE -ne 0) {
        throw "ESP-IDF clone failed: $LASTEXITCODE"
    }
}

& (Join-Path $RepoRoot "tools\esp32s3\patch_idf_short_paths.ps1") -IdfPath $IdfPath

if (!(Test-Path -LiteralPath $PythonPath)) {
    $systemPythonExe = $null
    $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
    if ($pyLauncher) {
        $candidate = (& $pyLauncher.Source -3.11 -c "import sys; print(sys.executable)" 2>$null | Out-String).Trim()
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            $systemPythonExe = $candidate
        }
    }

    if ($systemPythonExe) {
        Write-Host "[Y700_SETUP] copying Python 3.11 into the project toolchain"
        Copy-Item -LiteralPath (Split-Path -Parent $systemPythonExe) `
            -Destination (Split-Path -Parent $PythonPath) `
            -Recurse `
            -Force
    } else {
        $distRoot = Join-Path $ToolchainRoot "dist"
        $installer = Join-Path $distRoot "python-3.11.9-amd64.exe"
        New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
        if (!(Test-Path -LiteralPath $installer)) {
            Write-Host "[Y700_SETUP] downloading Python 3.11.9"
            Invoke-WebRequest `
                -Uri "https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe" `
                -OutFile $installer
        }

        $pythonRoot = Split-Path -Parent $PythonPath
        $arguments = @(
            "/quiet",
            "InstallAllUsers=0",
            "TargetDir=$pythonRoot",
            "Include_launcher=0",
            "InstallLauncherAllUsers=0",
            "PrependPath=0",
            "Shortcuts=0",
            "Include_test=0",
            "Include_doc=0",
            "Include_debug=0",
            "Include_symbols=0",
            "Include_tcltk=0",
            "Include_pip=1",
            "Include_lib=1",
            "Include_dev=1"
        )
        $process = Start-Process `
            -FilePath $installer `
            -ArgumentList $arguments `
            -Wait `
            -PassThru `
            -WindowStyle Hidden
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $PythonPath)) {
            throw "Project-local Python installation failed: $($process.ExitCode)"
        }
    }
}

if (!$SkipInstall) {
    $env:IDF_TOOLS_PATH = $ToolsPath
    $env:PATH = "$(Split-Path -Parent $PythonPath);$(Split-Path -Parent $PythonPath)\Scripts;$env:PATH"
    $env:IDF_GITHUB_ASSETS = "dl.espressif.com/github_assets"
    Write-Host "[Y700_SETUP] installing ESP32-S3 tools"
    & (Join-Path $IdfPath "install.ps1") esp32s3
    if ($LASTEXITCODE -ne 0) {
        throw "ESP-IDF tool installation failed: $LASTEXITCODE"
    }
}

. (Join-Path $RepoRoot "tools\esp32s3\idf_environment.ps1")
$localPythonEnv = Get-Y700LocalPythonEnvironment
if (!$localPythonEnv) {
    throw "ESP-IDF Python environment was not created."
}

$venvConfig = Join-Path $localPythonEnv "pyvenv.cfg"
if (!(Test-Path -LiteralPath $venvConfig) -or
    !(Select-String -LiteralPath $venvConfig -SimpleMatch $ToolchainRoot -Quiet)) {
    $resolvedToolchain = (Resolve-Path -LiteralPath $ToolchainRoot).Path
    $resolvedVenv = (Resolve-Path -LiteralPath $localPythonEnv).Path
    if (!$resolvedVenv.StartsWith($resolvedToolchain, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace Python environment outside the project: $resolvedVenv"
    }

    Write-Host "[Y700_SETUP] rebuilding the ESP-IDF Python environment with project-local Python"
    Remove-Item -LiteralPath $resolvedVenv -Recurse -Force
    $env:IDF_TOOLS_PATH = $ToolsPath
    $env:PATH = "$(Split-Path -Parent $PythonPath);$(Split-Path -Parent $PythonPath)\Scripts;$env:PATH"
    & $PythonPath (Join-Path $IdfPath "tools\idf_tools.py") `
        --idf-path $IdfPath `
        install-python-env
    if ($LASTEXITCODE -ne 0) {
        throw "Project-local ESP-IDF Python environment failed: $LASTEXITCODE"
    }
}

& (Join-Path $RepoRoot "tools\esp32s3\check_environment.ps1")
