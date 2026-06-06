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

function Get-PropertyValue {
    param($Device, [string]$KeyName)
    try {
        return (Get-PnpDeviceProperty -InstanceId $Device.InstanceId -KeyName $KeyName -ErrorAction Stop).Data
    } catch {
        return $null
    }
}

$allDevices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue)
$candidates = @()

foreach ($device in $allDevices) {
    $instanceId = if ($device.InstanceId) { $device.InstanceId } else { "" }
    $friendlyName = if ($device.FriendlyName) { $device.FriendlyName } elseif ($device.Name) { $device.Name } else { "" }
    if ($instanceId -notmatch "VID_054C&PID_0CE6" -and
        $friendlyName -notmatch "DualSense|Wireless Controller|Sony Interactive Entertainment") {
        continue
    }

    $busProduct = Get-PropertyValue -Device $device -KeyName "DEVPKEY_Device_BusReportedDeviceDesc"
    $deviceDesc = Get-PropertyValue -Device $device -KeyName "DEVPKEY_Device_DeviceDesc"
    $text = "$friendlyName $busProduct $deviceDesc $instanceId"

    if ($instanceId -match "VID_054C&PID_0CE6" -or
        $text -match "DualSense|Wireless Controller|Sony Interactive Entertainment") {
        $candidates += [pscustomobject]@{
            Device = $device
            InstanceId = $instanceId
            FriendlyName = $friendlyName
            Product = if ($busProduct) { $busProduct } elseif ($deviceDesc) { $deviceDesc } else { $friendlyName }
            Vid = if ($instanceId -match "VID_([0-9A-Fa-f]{4})") { $Matches[1].ToUpperInvariant() } else { "not_found" }
            Pid = if ($instanceId -match "PID_([0-9A-Fa-f]{4})") { $Matches[1].ToUpperInvariant() } else { "not_found" }
            HidClass = $device.Class -eq "HIDClass" -or $instanceId -match "^HID\\"
        }
    }
}

$best = $candidates |
    Sort-Object @{ Expression = { if ($_.InstanceId -match "VID_054C&PID_0CE6") { 0 } else { 1 } } },
                @{ Expression = { if ($_.HidClass) { 0 } else { 1 } } } |
    Select-Object -First 1

$hidFound = $null -ne $best
$likelyDualSense = $hidFound -and
    (($best.Vid -eq "054C" -and $best.Pid -eq "0CE6") -or
     $best.Product -match "DualSense|Wireless Controller")
$steam = Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 1

Write-IdentityLine "hid_found" $hidFound
Write-IdentityLine "vid" ($(if ($best) { $best.Vid } else { "not_found" }))
Write-IdentityLine "pid" ($(if ($best) { $best.Pid } else { "not_found" }))
Write-IdentityLine "product" ($(if ($best) { $best.Product } else { "not_found" }))
Write-IdentityLine "instance_id" ($(if ($best) { $best.InstanceId } else { "not_found" }))
Write-IdentityLine "steam_running" ($null -ne $steam)
Write-IdentityLine "likely_dualsense" $likelyDualSense
Write-IdentityLine "candidate_count" $candidates.Count

foreach ($candidate in $candidates | Select-Object -First 8) {
    Write-Output ("[V5_5_DS5_IDENTITY] candidate vid={0} pid={1} product={2} class={3} instance_id={4}" -f
        $candidate.Vid,
        $candidate.Pid,
        ($candidate.Product -replace "[`r`n]+", " "),
        $candidate.Device.Class,
        $candidate.InstanceId)
}

if (!$hidFound) {
    Write-IdentityLine "result" "blocked_no_dualsense_identity"
    exit 0
}

Write-IdentityLine "result" ($(if ($likelyDualSense) { "passed" } else { "found_but_not_likely_dualsense" }))
exit 0
