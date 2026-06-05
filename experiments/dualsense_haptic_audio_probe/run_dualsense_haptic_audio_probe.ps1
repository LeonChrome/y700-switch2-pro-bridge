param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$DurationSeconds = 10
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

Write-Output "[DUALSENSE_AUDIO] starting duration_seconds=$DurationSeconds"
& (Join-Path $ProjectRoot "tools\check_dualsense_env.ps1") -ProjectRoot $ProjectRoot

$sound = @(Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue)
$pnp = @(Get-PnpDevice -ErrorAction SilentlyContinue)
$audio = @($sound + $pnp | Where-Object {
    $_.Name -match "DualSense|Wireless Controller" -or
    $_.FriendlyName -match "DualSense|Wireless Controller"
})

if ($audio.Count -eq 0) {
    Write-Output "[HAPTIC_AUDIO] blocked: no DualSense audio device found"
    Write-Output "[HAPTIC_AUDIO] activity=false"
    exit 2
}

foreach ($dev in $audio | Select-Object -First 8) {
    $name = if ($dev.Name) { $dev.Name } else { $dev.FriendlyName }
    Write-Output "[DUALSENSE_AUDIO] device=$name"
}

Write-Output "[HAPTIC_AUDIO] blocked: real WASAPI loopback capture not implemented until a target DualSense audio endpoint is present"
Write-Output "[HAPTIC_AUDIO] captured=false"
exit 3
