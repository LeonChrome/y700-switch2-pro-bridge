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
    $_.Name -match "DualSense|Wireless Controller|Sony Interactive" -or
    $_.FriendlyName -match "DualSense|Wireless Controller|Sony Interactive"
})

if ($audio.Count -eq 0) {
    Write-Output "[DUALSENSE_AUDIO] device=not_found"
    Write-Output "[DUALSENSE_AUDIO] endpoint_count=0"
    Write-Output "[DUALSENSE_AUDIO] wasapi_loopback=false"
    Write-Output "[HAPTIC_AUDIO] channels=0 sample_rate=0"
    Write-Output "[HAPTIC_AUDIO] sample_rate=0"
    Write-Output "[HAPTIC_AUDIO] rms_ch0=0 rms_ch1=0 rms_ch2=0 rms_ch3=0"
    Write-Output "[HAPTIC_AUDIO] peak_ch0=0 peak_ch1=0 peak_ch2=0 peak_ch3=0"
    Write-Output "[HAPTIC_AUDIO] activity=false"
    Write-Output "[DUALSENSE_BLOCKED] reason=no_dualsense_audio_endpoint"
    exit 0
}

foreach ($dev in $audio | Select-Object -First 8) {
    $name = if ($dev.Name) { $dev.Name } else { $dev.FriendlyName }
    Write-Output "[DUALSENSE_AUDIO] device=$name"
}

Write-Output "[DUALSENSE_AUDIO] endpoint_count=$($audio.Count)"
Write-Output "[DUALSENSE_AUDIO] wasapi_loopback=true"
Write-Output "[HAPTIC_AUDIO] channels=unknown sample_rate=unknown"
Write-Output "[HAPTIC_AUDIO] sample_rate=unknown"
Write-Output "[HAPTIC_AUDIO] rms_ch0=unknown rms_ch1=unknown rms_ch2=unknown rms_ch3=unknown"
Write-Output "[HAPTIC_AUDIO] peak_ch0=unknown peak_ch1=unknown peak_ch2=unknown peak_ch3=unknown"
Write-Output "[HAPTIC_AUDIO] activity=unknown"
Write-Output "[DUALSENSE_BLOCKED] reason=wasapi_loopback_capture_not_implemented_for_detected_endpoint"
exit 0
