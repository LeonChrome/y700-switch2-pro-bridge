param()

$ErrorActionPreference = "Stop"

Write-Output "[V5_5_DS5_AUDIO] check=audio_endpoint"

function Get-ProfileNameFromSerial {
    param([string]$Serial)
    switch ($Serial) {
        "V55HIDONLY" { return "hid_only" }
        "V55DUMMY00" { return "hid_composite_dummy_interface_class_00" }
        "V55DUMMYEF" { return "hid_composite_dummy_interface_class_ef" }
        "V55ACONLY" { return "hid_audio_control_only" }
        "V55ASALT0" { return "hid_audio_streaming_alt0_only" }
        "V55UAC1_2CH" { return "hid_audio_uac1_2ch" }
        "V55UAC1_4CH" { return "hid_audio_uac1_4ch_ds5like" }
        "V55UAC2_2CH" { return "hid_audio_uac2_2ch" }
        "V55UAC2_4CH" { return "hid_audio_uac2_4ch" }
        "V55PHASE3" { return "hid_audio_uac2_4ch_legacy_alias" }
        default { return "unknown" }
    }
}

function Get-SerialFromInstanceId {
    param([string]$InstanceId)
    if ($InstanceId -match "^USB\\VID_054C&PID_0CE6\\([^\\]+)$") {
        return $Matches[1]
    }
    return "not_found"
}

$audioDevices = @()
try {
    $audioDevices = @(Get-CimInstance Win32_SoundDevice |
        Where-Object {
            $_.Status -eq "OK" -and
            (($_.Name -match "DualSense|Wireless Controller|054C|0CE6|Sony") -or
             ($_.PNPDeviceID -match "VID_054C&PID_0CE6"))
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
    $_.Status -eq "OK" -and $_.Class -match "AudioEndpoint|Media"
})
$usbCandidates = @($pnpDevices | Where-Object {
    $_.InstanceId -match "^USB\\VID_054C&PID_0CE6\\[^\\]+$"
})
$usbBest = $usbCandidates |
    Sort-Object @{ Expression = { if ($_.Status -eq "OK") { 0 } else { 1 } } },
                @{ Expression = { $_.InstanceId } } |
    Select-Object -First 1
$currentSerial = if ($usbBest) { Get-SerialFromInstanceId -InstanceId ([string]$usbBest.InstanceId) } else { "not_found" }
$currentProfile = Get-ProfileNameFromSerial -Serial $currentSerial

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

$suggestedNextAction = "connect_or_flash_hid_only"
if ($currentProfile -eq "hid_only") {
    $suggestedNextAction = "test_dummy_class_00"
} elseif ($currentProfile -eq "hid_composite_dummy_interface_class_00") {
    $suggestedNextAction = if ($endpointFound) { "unexpected_audio_endpoint" } else { "test_dummy_class_ef" }
} elseif ($currentProfile -eq "hid_composite_dummy_interface_class_ef") {
    $suggestedNextAction = if ($endpointFound) { "unexpected_audio_endpoint" } else { "test_audio_control_only" }
} elseif ($currentProfile -eq "hid_audio_control_only") {
    $suggestedNextAction = if ($endpointFound) { "record_unexpected_control_only_endpoint" } else { "test_streaming_alt0_only" }
} elseif ($currentProfile -eq "hid_audio_streaming_alt0_only") {
    $suggestedNextAction = if ($endpointFound) { "record_unexpected_alt0_endpoint" } else { "test_hid_audio_uac1_2ch" }
} elseif ($currentProfile -eq "hid_audio_uac1_2ch") {
    $suggestedNextAction = if ($endpointFound) { "flash_hid_audio_uac1_4ch_ds5like" } else { "descriptor_or_composite_basic_issue" }
} elseif ($currentProfile -eq "hid_audio_uac1_4ch_ds5like") {
    $suggestedNextAction = if ($endpointFound) { "play_test_audio_and_verify_isochronous_output" } else { "fall_back_to_hid_audio_uac1_2ch" }
} elseif ($currentProfile -eq "hid_audio_uac2_2ch") {
    $suggestedNextAction = if ($endpointFound) { "flash_hid_audio_uac2_4ch" } else { "uac2_descriptor_issue" }
} elseif ($currentProfile -eq "hid_audio_uac2_4ch" -or $currentProfile -eq "hid_audio_uac2_4ch_legacy_alias") {
    $suggestedNextAction = if ($endpointFound) { "record_phase3_success" } else { "fall_back_to_hid_audio_uac2_2ch_or_uac1_2ch" }
}

Write-Output "[V5_5_DS5_AUDIO] endpoint_found=$($endpointFound.ToString().ToLowerInvariant())"
Write-Output "[V5_5_DS5_AUDIO] endpoint_name=$endpointName"
Write-Output "[V5_5_DS5_AUDIO] channels=unknown"
Write-Output "[V5_5_DS5_AUDIO] sample_rate=unknown"
Write-Output "[V5_5_DS5_AUDIO] device_id=$deviceId"
Write-Output "[V5_5_DS5_AUDIO] current_serial=$currentSerial"
Write-Output "[V5_5_DS5_AUDIO] current_profile=$currentProfile"
Write-Output "[V5_5_DS5_AUDIO] render=$($endpointFound.ToString().ToLowerInvariant())"
Write-Output "[V5_5_DS5_AUDIO] likely_dualsense_audio=$($endpointFound.ToString().ToLowerInvariant())"
Write-Output "[V5_5_DS5_AUDIO] suggested_next_action=$suggestedNextAction"

if (!$endpointFound) {
    Write-Output "[V5_5_DS5_AUDIO] manual_check=mmsys.cpl"
    Write-Output "[V5_5_DS5_AUDIO] expected_after_phase3_flash=DualSense-like render endpoint near VID_054C&PID_0CE6"
}

exit 0
