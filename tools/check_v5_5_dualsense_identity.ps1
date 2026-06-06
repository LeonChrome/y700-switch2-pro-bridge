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
        IsUsbDevice = $instanceId -match "^USB\\VID_054C&PID_0CE6"
    }
}

$hidCandidates = @($candidates | Where-Object { $_.IsHidInterface })
$usbCandidates = @($candidates | Where-Object { $_.IsUsbDevice })

$hidBest = $hidCandidates |
    Sort-Object @{ Expression = { if ($_.Status -eq "OK") { 0 } else { 1 } } },
                @{ Expression = { $_.InstanceId } } |
    Select-Object -First 1

$usbBest = $usbCandidates |
    Sort-Object @{ Expression = { if ($_.InstanceId -match "V55PHASE3") { 0 } else { 1 } } },
                @{ Expression = { if ($_.Status -eq "OK") { 0 } else { 1 } } },
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
$audioEndpointFound = $false

try {
    $audioDevices = @(Get-CimInstance Win32_SoundDevice -ErrorAction Stop |
        Where-Object {
            ($_.Name -match "DualSense|Wireless Controller|054C|0CE6|Sony") -or
            ($_.PNPDeviceID -match "VID_054C&PID_0CE6")
        })
    $audioPnpDevices = @(Get-PnpDevice -PresentOnly -ErrorAction Stop |
        Where-Object {
            (($_.Class -match "AudioEndpoint|Media") -or
             ($_.FriendlyName -match "Speaker|Headphones|Wireless Controller|DualSense")) -and
            (($_.FriendlyName -match "DualSense|Wireless Controller|054C|0CE6|Sony") -or
             ($_.InstanceId -match "VID_054C&PID_0CE6"))
        })
    $audioEndpointFound = ($audioDevices.Count -gt 0) -or ($audioPnpDevices.Count -gt 0)
} catch {
    $audioEndpointFound = $false
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
Write-IdentityLine "steam_running" ($null -ne $steam)
Write-IdentityLine "likely_dualsense" $likelyDualSense
Write-IdentityLine "audio_endpoint_found" $audioEndpointFound
Write-IdentityLine "output_0x02_seen" "use_tools/send_v5_5_dualsense_rumble_test.ps1"
Write-IdentityLine "haptic_audio_activity" "firmware_log_required"
Write-IdentityLine "ordinary_rumble_to_pro2_status" "phase2_1_supported"
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
