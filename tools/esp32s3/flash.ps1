param(
    [string]$Port,
    [string]$IdfPath,
    [int]$Baud = 460800,
    [switch]$NoStub
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Firmware = Join-Path $Root "firmware\esp32s3_switch2_bridge"

Write-Host "Flashing/logging: connect CH343P Type-C."
Write-Host "HID test: connect ESP32-S3 native USB & OTG Type-C."
Write-Host "First-board note: flashing was observed once on COM12; use the COM port detected on this machine."
Write-Host "If flashing reports serial noise/corruption, retry with -Baud 115200."
Write-Host "If it still reports serial noise/corruption, retry with -NoStub -Baud 115200."

if (-not $Port) {
    & (Join-Path $PSScriptRoot "detect_ports.ps1")
    $Port = Read-Host "Enter CH343P COM port, e.g. COM54"
}

if (-not $Port) { throw "No COM port supplied." }

function Import-IdfEnvironment {
    param([string]$Path)
    if (-not $Path) { return }

    $idfRoot = Split-Path -Parent $Path
    $versionName = Split-Path -Leaf $idfRoot
    $toolsPath = if ($env:IDF_TOOLS_PATH) { $env:IDF_TOOLS_PATH } else { "C:\Espressif\tools" }
    $eimProfile = Join-Path $toolsPath ("Microsoft.{0}.PowerShell_profile.ps1" -f $versionName)
    if (Test-Path -LiteralPath $eimProfile) {
        Write-Host "Loading ESP-IDF EIM profile: $eimProfile"
        . $eimProfile
        return
    }

    $export = Join-Path $Path "export.ps1"
    if (!(Test-Path -LiteralPath $export)) { throw "ESP-IDF export.ps1 not found: $export" }
    . $export
}

Import-IdfEnvironment $IdfPath

if (!(Get-Command idf.py -ErrorAction SilentlyContinue)) {
    $Eim = Get-Command eim -ErrorAction SilentlyContinue
    if (-not $Eim) {
        $EimPath = Get-ChildItem -Path "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter eim.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "Espressif\.EIM-CLI" } |
            Select-Object -First 1 -ExpandProperty FullName
        if ($EimPath) {
            $script:EimExe = $EimPath
        }
    } else {
        $script:EimExe = $Eim.Source
    }

    if (-not $script:EimExe) {
        throw "idf.py not found. Open an ESP-IDF PowerShell, install Espressif EIM, or pass -IdfPath C:\path\to\esp-idf."
    }
    Write-Host "idf.py not found on PATH; using EIM: $script:EimExe"
}

function Invoke-IdfCommand {
    param([string]$Command)
    if (Get-Command idf.py -ErrorAction SilentlyContinue) {
        Invoke-Expression $Command
    } elseif ($script:EimExe) {
        & $script:EimExe run $Command v5.3.3
    } else {
        throw "No ESP-IDF command runner available."
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $Command"
    }
}

function Get-IdfPython {
    if ($env:IDF_PYTHON_ENV_PATH) {
        $candidate = Join-Path $env:IDF_PYTHON_ENV_PATH "Scripts\python.exe"
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    $candidate = "C:\Espressif\tools\python\v5.3.3\venv\Scripts\python.exe"
    if (Test-Path -LiteralPath $candidate) { return $candidate }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) { return $python.Source }

    throw "ESP-IDF Python environment not found."
}

Push-Location $Firmware
try {
    $Sdkconfig = Join-Path $Firmware "sdkconfig"
    if (!(Test-Path -LiteralPath $Sdkconfig) -or !(Select-String -Path $Sdkconfig -Pattern 'CONFIG_IDF_TARGET="esp32s3"' -Quiet)) {
        Invoke-IdfCommand "idf.py set-target esp32s3"
    }
    if ($NoStub) {
        Invoke-IdfCommand "idf.py build"
        Push-Location (Join-Path $Firmware "build")
        try {
            $IdfPython = Get-IdfPython
            & $IdfPython -m esptool --chip esp32s3 --no-stub -p $Port -b $Baud --before default_reset --after hard_reset write_flash "@flash_args"
            if ($LASTEXITCODE -ne 0) {
                throw "no-stub esptool flash failed: $LASTEXITCODE"
            }
        } finally {
            Pop-Location
        }
    } else {
        Invoke-IdfCommand "idf.py -p $Port -b $Baud flash"
    }
} finally {
    Pop-Location
}
