param(
    [string]$IdfPath,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$FirmwareRoot = Join-Path $RepoRoot "firmware\esp32s3_dualsense_identity_experiment"

function Import-IdfEnvironment {
    param([string]$Path)
    if (!$Path) { return }

    $idfRoot = Split-Path -Parent $Path
    $versionName = Split-Path -Leaf $idfRoot
    $toolsPath = if ($env:IDF_TOOLS_PATH) {
        $env:IDF_TOOLS_PATH
    } else {
        Join-Path $env:SystemDrive "Espressif\tools"
    }
    $eimProfile = Join-Path $toolsPath ("Microsoft.{0}.PowerShell_profile.ps1" -f $versionName)
    if (Test-Path -LiteralPath $eimProfile) {
        Write-Output "[V5_5_DS5_BUILD] idf_profile=$eimProfile"
        . $eimProfile
        return
    }

    $exportScript = Join-Path $Path "export.ps1"
    if (!(Test-Path -LiteralPath $exportScript)) {
        throw "ESP-IDF export.ps1 not found: $exportScript"
    }
    . $exportScript
}

Import-IdfEnvironment -Path $IdfPath

if (!(Get-Command idf.py -ErrorAction SilentlyContinue)) {
    throw "idf.py not found. Open an ESP-IDF PowerShell or pass -IdfPath <path-to-esp-idf>."
}

Write-Output "[V5_5_DS5_BUILD] firmware=firmware/esp32s3_dualsense_identity_experiment"
Write-Output "[V5_5_DS5_BUILD] identity=dualsense_experimental"
Write-Output "[V5_5_DS5_BUILD] v5_2_default_unchanged=true"

Push-Location $FirmwareRoot
try {
    if (!(Test-Path -LiteralPath (Join-Path $FirmwareRoot "sdkconfig"))) {
        & idf.py set-target esp32s3
        if ($LASTEXITCODE -ne 0) {
            throw "idf.py set-target esp32s3 failed: $LASTEXITCODE"
        }
    }
    if ($Clean) {
        & idf.py fullclean
        if ($LASTEXITCODE -ne 0) {
            throw "idf.py fullclean failed: $LASTEXITCODE"
        }
    }
    & idf.py build
    if ($LASTEXITCODE -ne 0) {
        throw "idf.py build failed: $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

Write-Output "[V5_5_DS5_BUILD] result=passed"
