param()

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

$allDevices = @(Get-PnpDevice -ErrorAction SilentlyContinue)
$presentIds = @{}
@(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue) | ForEach-Object {
    if ($_.InstanceId) {
        $presentIds[[string]$_.InstanceId] = $true
    }
}

$matching = @($allDevices | Where-Object {
    ($_.InstanceId -match "VID_054C&PID_0CE6") -or
    ($_.FriendlyName -match "DualSense|Wireless Controller|Sony")
})

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
$phase3Driver = Convert-Value (Get-DevicePropertyValue -InstanceId $phase3InstanceId -KeyName "DEVPKEY_Device_Driver")
$phase3Service = Convert-Value (Get-DevicePropertyValue -InstanceId $phase3InstanceId -KeyName "DEVPKEY_Device_Service")
$phase3Parent = Convert-Value (Get-DevicePropertyValue -InstanceId $phase3InstanceId -KeyName "DEVPKEY_Device_Parent")
$phase3Children = Convert-Value (Get-DevicePropertyValue -InstanceId $phase3InstanceId -KeyName "DEVPKEY_Device_Children")
$phase3ConfigError = "not_found"

try {
    $pnpEntity = Get-CimInstance Win32_PnPEntity -ErrorAction Stop |
        Where-Object { $_.PNPDeviceID -eq $phase3InstanceId } |
        Select-Object -First 1
    if ($pnpEntity) {
        $phase3ConfigError = $pnpEntity.ConfigManagerErrorCode
    }
} catch {
    $phase3ConfigError = "not_found"
}

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

$phase1Devices = @($matching | Where-Object { $_.InstanceId -match "V55PHASE1" })
$phase2Devices = @($matching | Where-Object { $_.InstanceId -match "V55PHASE2" })
$stalePhase1Found = @($phase1Devices | Where-Object {
    $_.Status -ne "OK" -or !$presentIds.ContainsKey([string]$_.InstanceId)
}).Count -gt 0
$stalePhase2Found = @($phase2Devices | Where-Object {
    $_.Status -ne "OK" -or !$presentIds.ContainsKey([string]$_.InstanceId)
}).Count -gt 0

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
Write-CompositeLine "stale_phase1_found" $stalePhase1Found
Write-CompositeLine "stale_phase2_found" $stalePhase2Found

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
