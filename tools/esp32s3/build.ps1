param(
    [string]$IdfPath,
    [string]$BuildDir = "",
    [ValidateSet("", "GENERIC_HID_MODE", "NINTENDO_EXPERIMENT_MODE", "XINPUT_EXPERIMENT_MODE")]
    [string]$DeviceDefaultMode = "",
    [switch]$XInputElite
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Firmware = Join-Path $Root "firmware\esp32s3_switch2_bridge"

Write-Host "ESP32-S3 Pro2 Bridge firmware build."
Write-Host "Flashing/logging: connect CH343P Type-C."
Write-Host "HID test: connect ESP32-S3 native USB & OTG Type-C."

function Import-IdfEnvironment {
    param([string]$Path)
    if (-not $Path) { return }

    $idfRoot = Split-Path -Parent $Path
    $versionName = Split-Path -Leaf $idfRoot
    $toolsPath = if ($env:IDF_TOOLS_PATH) { $env:IDF_TOOLS_PATH } else { Join-Path $env:SystemDrive "Espressif\tools" }
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
    throw "idf.py not found. Open an ESP-IDF PowerShell or pass -IdfPath <path-to-esp-idf>."
}

function Invoke-IdfBuild {
    $args = @()
    if ($BuildDir) {
        $resolvedBuildDir = if ([System.IO.Path]::IsPathRooted($BuildDir)) {
            $BuildDir
        } else {
            Join-Path $Root $BuildDir
        }
        $args += @("-B", $resolvedBuildDir)
    }
    if ($DeviceDefaultMode) {
        $args += "-DDEVICE_DEFAULT_MODE=$DeviceDefaultMode"
    }
    if ($XInputElite) {
        $args += "-DXINPUT_ELITE_EXPERIMENT=1"
    }
    $args += "build"

    Write-Host ("idf.py " + ($args -join " "))
    & idf.py @args
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: idf.py " + ($args -join " ")
    }
}

Push-Location $Firmware
try {
    Invoke-IdfBuild
} finally {
    Pop-Location
}
