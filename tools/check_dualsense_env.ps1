param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Write-EnvLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value -or $Value -eq "") {
        $Value = "not_found"
    }
    Write-Output "[DUALSENSE_ENV] $Key=$Value"
}

function Test-Admin {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-DualSensePnpDevice {
    param($Device)
    $name = if ($Device.FriendlyName) { $Device.FriendlyName } elseif ($Device.Name) { $Device.Name } else { "" }
    $id = if ($Device.InstanceId) { $Device.InstanceId } else { "" }
    return $name -match "DualSense|Wireless Controller" -or
        $id -match "VID_054C&PID_0CE6|VID&0002054C_PID&0CE6|VID_054C&PID_0DF2|VID&0002054C_PID&0DF2"
}

$pnp = @(Get-PnpDevice -ErrorAction SilentlyContinue)
$dualSenseDevices = @($pnp | Where-Object { Test-DualSensePnpDevice $_ })
$hidUsb = @($dualSenseDevices | Where-Object {
    $_.Class -match "HIDClass|USB" -or $_.InstanceId -match "USB\\|HID\\VID_054C"
})
$hidBluetooth = @($dualSenseDevices | Where-Object {
    $_.Class -match "Bluetooth" -or $_.InstanceId -match "BTHENUM|BTHLE|Bluetooth"
})

$sound = @(Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue)
$audioEndpoints = @($pnp | Where-Object {
    $_.Class -match "AudioEndpoint|MEDIA" -or
    $_.FriendlyName -match "DualSense|Wireless Controller|Sony Interactive"
})
$dualSenseAudio = @($sound + $audioEndpoints | Where-Object {
    $_.Name -match "DualSense|Wireless Controller" -or
    $_.FriendlyName -match "DualSense|Wireless Controller"
})

$steam = Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 1
$isWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$wasapiLoopback = $isWindows -and $dualSenseAudio.Count -gt 0

Write-EnvLine "project" $ProjectRoot
Write-EnvLine "admin" (Test-Admin)
Write-EnvLine "hid_usb" ($hidUsb.Count -gt 0)
Write-EnvLine "hid_bluetooth" ($hidBluetooth.Count -gt 0)
Write-EnvLine "real_dualsense" ($dualSenseDevices.Count -gt 0)
Write-EnvLine "audio_device" ($(if ($dualSenseAudio.Count -gt 0) { ($dualSenseAudio | Select-Object -First 1).Name } else { "not_found" }))
Write-EnvLine "wasapi_loopback_api" $isWindows
Write-EnvLine "wasapi_loopback" $wasapiLoopback
Write-EnvLine "steam" ($(if ($steam) { "running pid=$($steam.Id)" } else { "not_running" }))
Write-EnvLine "blocked_by_missing_real_dualsense" ($dualSenseDevices.Count -eq 0)

foreach ($dev in $dualSenseDevices | Select-Object -First 8) {
    Write-Output "[DUALSENSE_ENV] device name=$($dev.FriendlyName) class=$($dev.Class) id=$($dev.InstanceId)"
}
