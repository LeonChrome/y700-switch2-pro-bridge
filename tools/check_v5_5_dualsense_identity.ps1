param()

$ErrorActionPreference = "Stop"

function Write-IdentityLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value -or ($Value -is [string] -and $Value -eq "")) {
        $Value = "not_found"
    }
    $text = ($Value.ToString() -replace "[`r`n]+", " ").Trim()
    Write-Output "[V5_5_DS5_IDENTITY] $Key=$text"
}

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

$allDevices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue)
$candidates = @()

foreach ($device in $allDevices) {
    $instanceId = if ($device.InstanceId) { [string]$device.InstanceId } else { "" }
    $friendlyName = if ($device.FriendlyName) {
        [string]$device.FriendlyName
    } elseif ($device.Name) {
        [string]$device.Name
    } else {
        ""
    }

    if ($instanceId -notmatch "VID_054C&PID_0CE6" -and
        $friendlyName -notmatch "DualSense|Wireless Controller|Sony Interactive Entertainment") {
        continue
    }

    $vid = if ($instanceId -match "VID_([0-9A-Fa-f]{4})") {
        $Matches[1].ToUpperInvariant()
    } else {
        "not_found"
    }
    $pidValue = if ($instanceId -match "PID_([0-9A-Fa-f]{4})") {
        $Matches[1].ToUpperInvariant()
    } else {
        "not_found"
    }

    $candidates += [pscustomobject]@{
        Device = $device
        InstanceId = $instanceId
        FriendlyName = $friendlyName
        Product = if ($friendlyName) { $friendlyName } else { $device.Name }
        Vid = $vid
        Pid = $pidValue
        Status = if ($device.Status) { [string]$device.Status } else { "Unknown" }
        Class = if ($device.Class) { [string]$device.Class } else { "not_found" }
        IsHidInterface = $device.Class -eq "HIDClass" -and $instanceId -match "^HID\\VID_054C&PID_0CE6"
        IsUsbDevice = $instanceId -match "^USB\\VID_054C&PID_0CE6\\[^\\]+$"
    }
}

$hidCandidates = @($candidates | Where-Object { $_.IsHidInterface })
$usbCandidates = @($candidates | Where-Object { $_.IsUsbDevice })

$hidBest = $hidCandidates |
    Sort-Object @{ Expression = { if ($_.Status -eq "OK") { 0 } else { 1 } } },
                @{ Expression = { $_.InstanceId } } |
    Select-Object -First 1

$usbBest = $usbCandidates |
    Sort-Object @{ Expression = { if ($_.Status -eq "OK") { 0 } else { 1 } } },
                @{ Expression = { $_.InstanceId } } |
    Select-Object -First 1

$usbDeviceFound = $null -ne $usbBest
$hidInterfaceFound = $null -ne $hidBest -and $hidBest.Status -eq "OK"
$best = if ($hidInterfaceFound) { $hidBest } elseif ($usbDeviceFound) { $usbBest } else { $null }
$likelyDualSense = $hidInterfaceFound -and
    (($hidBest.Vid -eq "054C" -and $hidBest.Pid -eq "0CE6") -or
     $hidBest.Product -match "DualSense|Wireless Controller|HID-compliant game controller")
$steam = Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 1
$compositeStatus = if ($usbBest) { $usbBest.Status } else { "not_found" }
$currentSerial = if ($usbBest) { Get-SerialFromInstanceId -InstanceId $usbBest.InstanceId } else { "not_found" }
$currentProfile = Get-ProfileNameFromSerial -Serial $currentSerial
$audioEndpointFound = $false

try {
    $audioDevices = @(Get-CimInstance Win32_SoundDevice -ErrorAction Stop |
        Where-Object {
            $_.Status -eq "OK" -and
            (($_.Name -match "DualSense|Wireless Controller|054C|0CE6|Sony") -or
             ($_.PNPDeviceID -match "VID_054C&PID_0CE6"))
        })
    $audioPnpDevices = @(Get-PnpDevice -PresentOnly -ErrorAction Stop |
        Where-Object {
            $_.Status -eq "OK" -and
            ($_.Class -match "AudioEndpoint|Media") -and
            (($_.FriendlyName -match "DualSense|Wireless Controller|054C|0CE6|Sony") -or
             ($_.InstanceId -match "VID_054C&PID_0CE6"))
        })
    $audioEndpointFound = ($audioDevices.Count -gt 0) -or ($audioPnpDevices.Count -gt 0)
} catch {
    $audioEndpointFound = $false
}

$suggestedNextAction = "connect_or_flash_hid_only"
if ($currentProfile -eq "hid_only") {
    $suggestedNextAction = if ($hidInterfaceFound) { "test_dummy_class_00" } else { "fix_hid_regression" }
} elseif ($currentProfile -eq "hid_composite_dummy_interface_class_00") {
    $suggestedNextAction = if ($hidInterfaceFound) { "test_dummy_class_ef" } else { "fix_basic_composite_descriptor" }
} elseif ($currentProfile -eq "hid_composite_dummy_interface_class_ef") {
    $suggestedNextAction = if ($hidInterfaceFound) { "test_audio_control_only" } else { "use_class_00_without_iad" }
} elseif ($currentProfile -eq "hid_audio_control_only") {
    $suggestedNextAction = if ($hidInterfaceFound) { "test_streaming_alt0_only" } else { "fix_audio_control_descriptor" }
} elseif ($currentProfile -eq "hid_audio_streaming_alt0_only") {
    $suggestedNextAction = if ($hidInterfaceFound) { "test_hid_audio_uac1_2ch" } else { "fix_audio_streaming_alt0_descriptor" }
} elseif ($currentProfile -eq "hid_audio_uac1_2ch") {
    $suggestedNextAction = if ($hidInterfaceFound -and $audioEndpointFound) { "flash_hid_audio_uac1_4ch_ds5like" } else { "descriptor_or_composite_basic_issue" }
} elseif ($currentProfile -eq "hid_audio_uac1_4ch_ds5like") {
    $suggestedNextAction = if ($hidInterfaceFound -and $audioEndpointFound) { "play_test_audio_and_verify_isochronous_output" } else { "fall_back_to_hid_audio_uac1_2ch" }
} elseif ($currentProfile -eq "hid_audio_uac2_2ch") {
    $suggestedNextAction = if ($hidInterfaceFound -and $audioEndpointFound) { "flash_hid_audio_uac2_4ch" } else { "uac2_descriptor_issue" }
} elseif ($currentProfile -eq "hid_audio_uac2_4ch" -or $currentProfile -eq "hid_audio_uac2_4ch_legacy_alias") {
    $suggestedNextAction = if ($hidInterfaceFound -and $audioEndpointFound) { "record_phase3_success" } else { "fall_back_to_hid_audio_uac2_2ch_or_uac1_2ch" }
}

Write-IdentityLine "usb_device_found" $usbDeviceFound
Write-IdentityLine "hid_interface_found" $hidInterfaceFound
Write-IdentityLine "hid_found" $hidInterfaceFound
Write-IdentityLine "identity_found" $likelyDualSense
Write-IdentityLine "vid" ($(if ($best) { $best.Vid } else { "not_found" }))
Write-IdentityLine "pid" ($(if ($best) { $best.Pid } else { "not_found" }))
Write-IdentityLine "product" ($(if ($best) { $best.Product } else { "not_found" }))
Write-IdentityLine "instance_id" ($(if ($best) { $best.InstanceId } else { "not_found" }))
Write-IdentityLine "composite_status" $compositeStatus
Write-IdentityLine "current_serial" $currentSerial
Write-IdentityLine "current_profile" $currentProfile
Write-IdentityLine "steam_running" ($null -ne $steam)
Write-IdentityLine "likely_dualsense" $likelyDualSense
Write-IdentityLine "audio_endpoint_found" $audioEndpointFound
Write-IdentityLine "output_0x02_seen" "use_tools/send_v5_5_dualsense_rumble_test.ps1"
Write-IdentityLine "haptic_audio_activity" "firmware_log_required"
Write-IdentityLine "ordinary_rumble_to_pro2_status" "phase2_1_supported"
Write-IdentityLine "suggested_next_action" $suggestedNextAction
Write-IdentityLine "candidate_count" $candidates.Count

foreach ($candidate in $candidates | Select-Object -First 12) {
    Write-Output ("[V5_5_DS5_IDENTITY] candidate vid={0} pid={1} status={2} product={3} class={4} instance_id={5}" -f
        $candidate.Vid,
        $candidate.Pid,
        $candidate.Status,
        ($candidate.Product -replace "[`r`n]+", " "),
        $candidate.Class,
        $candidate.InstanceId)
}

if ($likelyDualSense) {
    Write-IdentityLine "result" "passed"
    exit 0
}

if ($usbDeviceFound -and $compositeStatus -ne "OK") {
    Write-IdentityLine "result" "composite_error"
    exit 0
}

if ($usbDeviceFound) {
    Write-IdentityLine "result" "failed_or_composite_missing_hid"
    exit 0
}

Write-IdentityLine "result" "blocked_no_dualsense_identity"
exit 0
