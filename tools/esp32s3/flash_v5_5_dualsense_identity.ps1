param(
    [Parameter(Mandatory = $true)]
    [string]$Port,
    [string]$IdfPath,
    [ValidateSet(
        "hid_only",
        "hid_composite_dummy_interface_class_00",
        "hid_composite_dummy_interface_class_ef",
        "hid_audio_control_only",
        "hid_audio_streaming_alt0_only",
        "hid_audio_uac1_2ch",
        "hid_audio_uac2_2ch",
        "hid_audio_uac2_4ch",
        "hid_audio_uac2",
        "hid_audio_uac1_fallback"
    )]
    [string]$Profile = "hid_audio_uac2_4ch",
    [int]$Baud = 460800,
    [switch]$Monitor
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$FirmwareRoot = Join-Path $RepoRoot "firmware\esp32s3_dualsense_identity_experiment"
$RequestedProfile = $Profile
if ($Profile -eq "hid_audio_uac2") {
    Write-Warning "hid_audio_uac2 is an alias for hid_audio_uac2_4ch"
    $Profile = "hid_audio_uac2_4ch"
}
if ($Profile -eq "hid_audio_uac1_fallback") {
    Write-Warning "hid_audio_uac1_fallback is an alias for hid_audio_uac1_2ch"
    $Profile = "hid_audio_uac1_2ch"
}
$BuildRoot = Join-Path $RepoRoot ("work\build\v5_5_dualsense_identity\{0}" -f $Profile)

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
        Write-Output "[V5_5_DS5_FLASH] idf_profile=$eimProfile"
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

Write-Output "[V5_5_DS5_FLASH] firmware=firmware/esp32s3_dualsense_identity_experiment"
Write-Output "[V5_5_DS5_FLASH] build_dir=work/build/v5_5_dualsense_identity/$Profile"
Write-Output "[V5_5_DS5_FLASH] requested_profile=$RequestedProfile"
Write-Output "[V5_5_DS5_FLASH] profile=$Profile"
Write-Output "[V5_5_DS5_FLASH] target=$Port"
Write-Output "[V5_5_DS5_FLASH] native_usb_action=replug_after_flash"
Write-Warning "This replaces the firmware currently on the board. Reflash esp32s3_switch2_bridge to return to V5.2/V5.0 behavior."

Push-Location $FirmwareRoot
try {
    & idf.py -B $BuildRoot -p $Port -b $Baud flash
    if ($LASTEXITCODE -ne 0) {
        throw "idf.py flash failed: $LASTEXITCODE"
    }
    Write-Output "[V5_5_DS5_FLASH] result=passed"

    if ($Monitor) {
        Write-Output "[V5_5_DS5_FLASH] monitor=true"
        & idf.py -B $BuildRoot -p $Port monitor
        if ($LASTEXITCODE -ne 0) {
            throw "idf.py monitor failed: $LASTEXITCODE"
        }
    }
} finally {
    Pop-Location
}
