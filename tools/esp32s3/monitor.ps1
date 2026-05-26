param(
    [string]$Port,
    [string]$IdfPath
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Firmware = Join-Path $Root "firmware\esp32s3_switch2_bridge"

Write-Host "PENDING_HARDWARE_TEST: monitor output is not verified until the ESP32-S3 board arrives."
Write-Host "Flashing/logging: connect CH343P Type-C."
Write-Host "HID test: connect ESP32-S3 native USB & OTG Type-C."

if (-not $Port) {
    & (Join-Path $PSScriptRoot "detect_ports.ps1")
    $Port = Read-Host "Enter CH343P COM port, e.g. COM54"
}

if (-not $Port) { throw "No COM port supplied." }

if ($IdfPath) {
    $export = Join-Path $IdfPath "export.ps1"
    if (!(Test-Path -LiteralPath $export)) { throw "ESP-IDF export.ps1 not found: $export" }
    . $export
}

if (!(Get-Command idf.py -ErrorAction SilentlyContinue)) {
    throw "idf.py not found. Open an ESP-IDF PowerShell or pass -IdfPath C:\path\to\esp-idf."
}

Push-Location $Firmware
try {
    idf.py -p $Port monitor
} finally {
    Pop-Location
}
