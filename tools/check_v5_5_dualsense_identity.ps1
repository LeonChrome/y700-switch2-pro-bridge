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
    $instanceId = if ($device.InstanceId) { $device.InstanceId } else { "" }
    $friendlyName = if ($device.FriendlyName) { $device.FriendlyName } elseif ($device.Name) { $device.Name } else { "" }
    if ($instanceId -notmatch "VID_054C&PID_0CE6" -and
        $friendlyName -notmatch "DualSense|Wireless Controller|Sony Interactive Entertainment") {
        continue
    }

    $text = "$friendlyName $instanceId"

    if ($instanceId -match "VID_054C&PID_0CE6" -or
        $text -match "DualSense|Wireless Controller|Sony Interactive Entertainment") {
        $candidates += [pscustomobject]@{
            Device = $device
            InstanceId = $instanceId
            FriendlyName = $friendlyName
            Product = if ($friendlyName) { $friendlyName } else { $device.Name }
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

Write-IdentityLine "hid_found" $hidFound
Write-IdentityLine "identity_found" $likelyDualSense
Write-IdentityLine "vid" ($(if ($best) { $best.Vid } else { "not_found" }))
Write-IdentityLine "pid" ($(if ($best) { $best.Pid } else { "not_found" }))
Write-IdentityLine "product" ($(if ($best) { $best.Product } else { "not_found" }))
Write-IdentityLine "instance_id" ($(if ($best) { $best.InstanceId } else { "not_found" }))
Write-IdentityLine "steam_running" ($null -ne $steam)
Write-IdentityLine "likely_dualsense" $likelyDualSense
Write-IdentityLine "audio_endpoint_found" $audioEndpointFound
Write-IdentityLine "output_0x02_seen" "use_tools/send_v5_5_dualsense_rumble_test.ps1"
Write-IdentityLine "haptic_audio_activity" "firmware_log_required"
Write-IdentityLine "ordinary_rumble_to_pro2_status" "phase2_1_supported"
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
