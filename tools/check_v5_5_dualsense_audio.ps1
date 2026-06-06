param()

$ErrorActionPreference = "Stop"

Write-Output "[V5_5_DS5_AUDIO] check=audio_endpoint"

$audioDevices = @()
try {
    $audioDevices = @(Get-CimInstance Win32_SoundDevice |
        Where-Object {
            ($_.Name -match "DualSense|Wireless Controller|054C|0CE6|Sony") -or
            ($_.PNPDeviceID -match "VID_054C&PID_0CE6")
        })
} catch {
    Write-Output "[V5_5_DS5_AUDIO] cim_sounddevice_error=$($_.Exception.Message)"
}

$pnpDevices = @()
try {
    $pnpDevices = @(Get-PnpDevice -PresentOnly |
        Where-Object {
            ($_.FriendlyName -match "DualSense|Wireless Controller|054C|0CE6|Sony") -or
            ($_.InstanceId -match "VID_054C&PID_0CE6")
        })
} catch {
    Write-Output "[V5_5_DS5_AUDIO] pnp_error=$($_.Exception.Message)"
}

$renderCandidates = @($pnpDevices | Where-Object {
    $_.Class -match "AudioEndpoint|Media" -or
    $_.FriendlyName -match "Speaker|Headphones|Wireless Controller|DualSense"
})

$endpointFound = ($audioDevices.Count -gt 0) -or ($renderCandidates.Count -gt 0)
$endpointName = ""
$deviceId = ""

if ($audioDevices.Count -gt 0) {
    $endpointName = [string]$audioDevices[0].Name
    $deviceId = [string]$audioDevices[0].PNPDeviceID
} elseif ($renderCandidates.Count -gt 0) {
    $endpointName = [string]$renderCandidates[0].FriendlyName
    $deviceId = [string]$renderCandidates[0].InstanceId
}

Write-Output "[V5_5_DS5_AUDIO] endpoint_found=$($endpointFound.ToString().ToLowerInvariant())"
Write-Output "[V5_5_DS5_AUDIO] endpoint_name=$endpointName"
Write-Output "[V5_5_DS5_AUDIO] channels=unknown"
Write-Output "[V5_5_DS5_AUDIO] sample_rate=unknown"
Write-Output "[V5_5_DS5_AUDIO] device_id=$deviceId"
Write-Output "[V5_5_DS5_AUDIO] render=$($endpointFound.ToString().ToLowerInvariant())"
Write-Output "[V5_5_DS5_AUDIO] likely_dualsense_audio=$($endpointFound.ToString().ToLowerInvariant())"

if (!$endpointFound) {
    Write-Output "[V5_5_DS5_AUDIO] manual_check=mmsys.cpl"
    Write-Output "[V5_5_DS5_AUDIO] expected_after_phase3_flash=DualSense-like render endpoint near VID_054C&PID_0CE6"
}

exit 0
