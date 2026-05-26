param(
    [string]$IdfPath
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Firmware = Join-Path $Root "firmware\esp32s3_switch2_bridge"

Write-Host "PENDING_HARDWARE_TEST: ESP-IDF build has not been verified against real ESP32-S3 hardware."
Write-Host "Flashing/logging: connect CH343P Type-C."
Write-Host "HID test: connect ESP32-S3 native USB & OTG Type-C."

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
    idf.py set-target esp32s3
    idf.py build
} finally {
    Pop-Location
}
