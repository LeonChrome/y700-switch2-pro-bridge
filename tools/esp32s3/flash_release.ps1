param(
    [string]$Port,
    [int]$Baud = 460800,
    [switch]$NoStub
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Build = Join-Path $Root "firmware\esp32s3_switch2_bridge\build"
$Bootloader = Join-Path $Build "bootloader\bootloader.bin"
$Partition = Join-Path $Build "partition_table\partition-table.bin"
$App = Join-Path $Build "esp32s3_switch2_bridge.bin"

Write-Host "ESP32-S3 Pro2 Bridge release flasher"
Write-Host "1. Connect the CH343P Type-C port."
Write-Host "2. Keep native USB connected only if you want Windows/Steam to enumerate after flashing."

foreach ($file in @($Bootloader, $Partition, $App)) {
    if (!(Test-Path -LiteralPath $file)) {
        throw "Firmware binary missing: $file. Build firmware first with tools\esp32s3\build.ps1."
    }
}

if (-not $Port) {
    & (Join-Path $PSScriptRoot "detect_ports.ps1")
    $Port = Read-Host "Enter CH343P COM port, e.g. COM12"
}
if (-not $Port) { throw "No COM port supplied." }

function Get-IdfPython {
    if ($env:IDF_PYTHON_ENV_PATH) {
        $candidate = Join-Path $env:IDF_PYTHON_ENV_PATH "Scripts\python.exe"
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    $candidate = "C:\Espressif\tools\python\v5.3.3\venv\Scripts\python.exe"
    if (Test-Path -LiteralPath $candidate) { return $candidate }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) { return $python.Source }

    throw "Python/esptool environment not found. Install ESP-IDF tools or use tools\esp32s3\flash.ps1 with -IdfPath."
}

$Python = Get-IdfPython
$args = @(
    "-m", "esptool",
    "--chip", "esp32s3"
)
if ($NoStub) {
    $args += "--no-stub"
}
$args += @(
    "-p", $Port,
    "-b", "$Baud",
    "--before", "default_reset",
    "--after", "hard_reset",
    "write_flash",
    "--flash_mode", "dio",
    "--flash_freq", "80m",
    "--flash_size", "16MB",
    "0x0", $Bootloader,
    "0x8000", $Partition,
    "0x10000", $App
)

& $Python @args
if ($LASTEXITCODE -ne 0) {
    throw "Release flash failed with exit code $LASTEXITCODE"
}

Write-Host "Flash complete. Replug native USB if Windows/Steam does not re-enumerate immediately."
