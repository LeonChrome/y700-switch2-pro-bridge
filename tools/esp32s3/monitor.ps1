param(
    [string]$Port,
    [string]$IdfPath
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Firmware = Join-Path $Root "firmware\esp32s3_switch2_bridge"

Write-Host "Flashing/logging: connect CH343P Type-C."
Write-Host "HID test: connect ESP32-S3 native USB & OTG Type-C."
Write-Host "First-board note: PowerShell/.NET serial reads worked with DTR=False, RTS=False."
Write-Host "If this monitor shows no output, make sure no previous idf_monitor process is holding the port."

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

Push-Location $Firmware
try {
    idf.py -p $Port monitor
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: idf.py -p $Port monitor"
    }
} finally {
    Pop-Location
}
