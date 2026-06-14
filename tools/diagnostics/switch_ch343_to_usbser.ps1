param(
    [string]$InstanceId = "",
    [string]$BackupDirectory = "",
    [string]$LogPath = ""
)

$ErrorActionPreference = "Stop"

if (![string]::IsNullOrWhiteSpace($LogPath)) {
    $logDirectory = Split-Path -Parent $LogPath
    if (![string]::IsNullOrWhiteSpace($logDirectory)) {
        New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    }
    Start-Transcript -Path $LogPath -Force | Out-Null
}

trap {
    Write-Error $_
    if (![string]::IsNullOrWhiteSpace($LogPath)) {
        Stop-Transcript | Out-Null
    }
    exit 1
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (!(Test-IsAdministrator)) {
    throw "This script must run as administrator."
}

if ([string]::IsNullOrWhiteSpace($InstanceId)) {
    $matches = @(Get-CimInstance Win32_PnPSignedDriver |
        Where-Object { $_.DeviceID -like "USB\VID_1A86&PID_55D3*" })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one CH343 VID_1A86&PID_55D3 device; found $($matches.Count). Pass -InstanceId explicitly."
    }
    $InstanceId = $matches[0].DeviceID
}

if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
    $BackupDirectory = Join-Path $repoRoot "work\driver_backup\ch343"
}

New-Item -ItemType Directory -Force -Path $BackupDirectory | Out-Null

$driver = Get-CimInstance Win32_PnPSignedDriver |
    Where-Object { $_.DeviceID -eq $InstanceId } |
    Select-Object -First 1
if (!$driver -or $driver.InfName -notmatch "^oem\d+\.inf$") {
    throw "No third-party CH343 driver package was found for $InstanceId."
}

$publishedName = $driver.InfName
Write-Output "[CH343_DRIVER] instance=$InstanceId"
Write-Output "[CH343_DRIVER] current=$publishedName"

& pnputil /export-driver $publishedName $BackupDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Unable to back up $publishedName to $BackupDirectory."
}
Write-Output "[CH343_DRIVER] backup=$BackupDirectory"

& pnputil /delete-driver $publishedName /uninstall /force
$deleteExitCode = $LASTEXITCODE
if ($deleteExitCode -ne 0 -and $deleteExitCode -ne 3010) {
    throw "Unable to uninstall $publishedName."
}
if ($deleteExitCode -eq 3010) {
    Write-Output "[CH343_DRIVER] replug_required=true"
    Write-Output "[CH343_DRIVER] result=pending_usbser"
    if (![string]::IsNullOrWhiteSpace($LogPath)) {
        Stop-Transcript | Out-Null
    }
    exit 0
}

& pnputil /scan-devices
if ($LASTEXITCODE -ne 0) {
    throw "Driver rescan failed."
}

Start-Sleep -Seconds 4
$after = (& pnputil /enum-devices /instanceid $InstanceId /drivers | Out-String)
Write-Output $after

$activeDriver = Get-CimInstance Win32_PnPSignedDriver |
    Where-Object { $_.DeviceID -eq $InstanceId } |
    Select-Object -First 1
if (!$activeDriver -or $activeDriver.InfName -ne "usbser.inf") {
    throw "CH343 did not bind to Microsoft's usbser driver."
}

Write-Output "[CH343_DRIVER] result=usbser"

if (![string]::IsNullOrWhiteSpace($LogPath)) {
    Stop-Transcript | Out-Null
}
