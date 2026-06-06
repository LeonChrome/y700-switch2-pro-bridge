param(
    [switch]$IncludeStale
)

$ErrorActionPreference = "Stop"

function Write-CompositeLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value -or ($Value -is [string] -and $Value -eq "")) {
        $Value = "not_found"
    }
    $text = ($Value.ToString() -replace "[`r`n]+", " ").Trim()
    Write-Output "[V5_5_USB_COMPOSITE] $Key=$text"
}

function Get-DevicePropertyValue {
    param([string]$InstanceId, [string]$KeyName)
    if (!$InstanceId) { return $null }
    try {
        return (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName $KeyName -ErrorAction Stop).Data
    } catch {
        return $null
    }
}

function Convert-Value {
    param([object]$Value)
    if ($null -eq $Value) { return "not_found" }
    if ($Value -is [array]) { return (($Value | ForEach-Object { $_.ToString() }) -join ";") }
    return $Value
}

function Get-ProfileNameFromSerial {
    param([string]$Serial)
    switch ($Serial) {
        "V55HIDONLY" { return "hid_only" }
        "V55UAC1_2CH" { return "hid_audio_uac1_2ch" }
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

$presentDevices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue)
$allDevices = if ($IncludeStale) {
    @(Get-PnpDevice -ErrorAction SilentlyContinue)
} else {
    $presentDevices
}
$presentIds = @{}
$presentDevices | ForEach-Object {
    if ($_.InstanceId) {
        $presentIds[[string]$_.InstanceId] = $true
    }
}

$matching = @($allDevices | Where-Object {
    ($_.InstanceId -match "VID_054C&PID_0CE6") -or
    ($_.FriendlyName -match "DualSense|Wireless Controller|Sony")
})

$usbDevices = @($matching | Where-Object {
    $_.InstanceId -match "^USB\\VID_054C&PID_0CE6\\"
})
$currentUsb = $usbDevices |
    Sort-Object @{ Expression = { if ($presentIds.ContainsKey([string]$_.InstanceId)) { 0 } else { 1 } } },
                @{ Expression = { if ($_.Status -eq "OK") { 0 } else { 1 } } },
                @{ Expression = { $_.InstanceId } } |
    Select-Object -First 1
$currentSerial = if ($currentUsb) { Get-SerialFromInstanceId -InstanceId ([string]$currentUsb.InstanceId) } else { "not_found" }
$currentProfile = Get-ProfileNameFromSerial -Serial $currentSerial

$phase3 = $matching |
    Where-Object { $_.InstanceId -match "^USB\\VID_054C&PID_0CE6\\V55PHASE3$" } |
    Select-Object -First 1

$phase3UsbFound = $null -ne $phase3
$phase3Present = $phase3UsbFound -and $presentIds.ContainsKey([string]$phase3.InstanceId)
$phase3Status = if ($phase3) { [string]$phase3.Status } else { "not_found" }
$phase3Class = if ($phase3) { [string]$phase3.Class } else { "not_found" }
$phase3Name = if ($phase3) { [string]$phase3.FriendlyName } else { "not_found" }
$phase3InstanceId = if ($phase3) { [string]$phase3.InstanceId } else { "not_found" }
$phase3ProblemCode = Convert-Value (Get-DevicePropertyValue -InstanceId $phase3InstanceId -KeyName "DEVPKEY_Device_ProblemCode")
$phase3Driver = "skipped_for_speed"
$phase3Service = "skipped_for_speed"
$phase3Parent = "skipped_for_speed"
$phase3Children = "skipped_for_speed"
$phase3ConfigError = $phase3ProblemCode

$hidChildren = @($matching | Where-Object {
    $_.Class -eq "HIDClass" -and
    $_.InstanceId -match "^HID\\VID_054C&PID_0CE6" -and
    $_.Status -eq "OK"
})
$audioChildren = @($matching | Where-Object {
    ($_.Class -match "AudioEndpoint|Media") -and $_.Status -eq "OK"
})

$phase3HidChildFound = $phase3UsbFound -and $phase3Status -eq "OK" -and $hidChildren.Count -gt 0
$phase3AudioChildFound = $phase3UsbFound -and $phase3Status -eq "OK" -and $audioChildren.Count -gt 0
$currentStatus = if ($currentUsb) { [string]$currentUsb.Status } else { "not_found" }
$currentHidChildFound = ($currentStatus -eq "OK" -and $hidChildren.Count -gt 0)
$currentAudioChildFound = ($currentStatus -eq "OK" -and $audioChildren.Count -gt 0)

$suggestedNextAction = "connect_or_flash_hid_only"
if ($currentProfile -eq "hid_only") {
    $suggestedNextAction = if ($currentHidChildFound) { "flash_hid_audio_uac1_2ch" } else { "fix_hid_regression" }
} elseif ($currentProfile -eq "hid_audio_uac1_2ch") {
    $suggestedNextAction = if ($currentHidChildFound -and $currentAudioChildFound) { "flash_hid_audio_uac2_2ch" } else { "descriptor_or_composite_basic_issue" }
} elseif ($currentProfile -eq "hid_audio_uac2_2ch") {
    $suggestedNextAction = if ($currentHidChildFound -and $currentAudioChildFound) { "flash_hid_audio_uac2_4ch" } else { "uac2_descriptor_issue" }
} elseif ($currentProfile -eq "hid_audio_uac2_4ch" -or $currentProfile -eq "hid_audio_uac2_4ch_legacy_alias") {
    $suggestedNextAction = if ($currentHidChildFound -and $currentAudioChildFound) { "record_phase3_success" } else { "fall_back_to_hid_audio_uac2_2ch_or_uac1_2ch" }
}

$phase1Devices = @($matching | Where-Object { $_.InstanceId -match "V55PHASE1" })
$phase2Devices = @($matching | Where-Object { $_.InstanceId -match "V55PHASE2" })
$stalePhase1Found = "not_scanned"
$stalePhase2Found = "not_scanned"
if ($IncludeStale) {
    $stalePhase1Found = @($phase1Devices | Where-Object {
        $_.Status -ne "OK" -or !$presentIds.ContainsKey([string]$_.InstanceId)
    }).Count -gt 0
    $stalePhase2Found = @($phase2Devices | Where-Object {
        $_.Status -ne "OK" -or !$presentIds.ContainsKey([string]$_.InstanceId)
    }).Count -gt 0
}

Write-CompositeLine "phase3_usb_found" $phase3UsbFound
Write-CompositeLine "phase3_status" $phase3Status
Write-CompositeLine "phase3_class" $phase3Class
Write-CompositeLine "phase3_friendly_name" $phase3Name
Write-CompositeLine "phase3_instance_id" $phase3InstanceId
Write-CompositeLine "phase3_problem_code" $phase3ProblemCode
Write-CompositeLine "phase3_config_error" $phase3ConfigError
Write-CompositeLine "phase3_present" $phase3Present
Write-CompositeLine "phase3_driver" $phase3Driver
Write-CompositeLine "phase3_service" $phase3Service
Write-CompositeLine "phase3_parent" $phase3Parent
Write-CompositeLine "phase3_children" $phase3Children
Write-CompositeLine "phase3_hid_child_found" $phase3HidChildFound
Write-CompositeLine "phase3_audio_child_found" $phase3AudioChildFound
Write-CompositeLine "stale_scan" ($(if ($IncludeStale) { "included" } else { "present_only" }))
Write-CompositeLine "stale_phase1_found" $stalePhase1Found
Write-CompositeLine "stale_phase2_found" $stalePhase2Found
Write-CompositeLine "current_serial" $currentSerial
Write-CompositeLine "current_profile" $currentProfile
Write-CompositeLine "current_status" $currentStatus
Write-CompositeLine "current_hid_child_found" $currentHidChildFound
Write-CompositeLine "current_audio_child_found" $currentAudioChildFound
Write-CompositeLine "suggested_next_action" $suggestedNextAction

foreach ($device in $matching | Select-Object -First 20) {
    $present = $presentIds.ContainsKey([string]$device.InstanceId)
    $isStale = (!$present) -or ($device.Status -ne "OK")
    Write-Output ("[V5_5_USB_COMPOSITE] device status={0} class={1} present={2} stale_or_error={3} name={4} instance_id={5}" -f
        $device.Status,
        $device.Class,
        $present.ToString().ToLowerInvariant(),
        $isStale.ToString().ToLowerInvariant(),
        (($device.FriendlyName -replace "[`r`n]+", " ").Trim()),
        $device.InstanceId)
}

exit 0
