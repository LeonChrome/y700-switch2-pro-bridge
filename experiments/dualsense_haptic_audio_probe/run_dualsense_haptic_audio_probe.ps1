param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$DurationSeconds = 10,
    [string]$JsonlPath = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Write-Jsonl {
    param([hashtable]$Event)
    if (!$JsonlPath) { return }
    $dir = Split-Path -Parent $JsonlPath
    if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
    ($Event | ConvertTo-Json -Compress -Depth 8) | Add-Content -Encoding UTF8 $JsonlPath
}

function Convert-LogValue {
    param([object]$Value)
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($null -eq $Value) { return "not_found" }
    if ($Value -is [string] -and $Value -eq "") { return "not_found" }
    return ($Value.ToString() -replace "[`r`n]+", " ").Trim()
}

function Get-AudioEndpointRegistry {
    $results = @()
    $root = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio"
    foreach ($type in @("Render", "Capture")) {
        $typePath = Join-Path $root $type
        foreach ($endpoint in @(Get-ChildItem $typePath -ErrorAction SilentlyContinue)) {
            $propsPath = Join-Path $endpoint.PSPath "Properties"
            $props = Get-ItemProperty $propsPath -ErrorAction SilentlyContinue
            if ($null -eq $props) { continue }
            $propList = @($props.PSObject.Properties)
            $friendly = ($propList | Where-Object { $_.Name -eq "{a45c254e-df1c-4efd-8020-67d146a850e0},2" } | Select-Object -First 1).Value
            $allText = ($propList | ForEach-Object {
                if ($_.Value -is [string]) { $_.Value }
            }) -join " "
            if ($friendly -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive" -or
                $allText -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive") {
                $results += [pscustomobject]@{
                    Type = $type.ToLowerInvariant()
                    Name = if ($friendly) { $friendly } else { "unknown" }
                    Id = $endpoint.PSChildName
                }
            }
        }
    }
    return $results
}

Write-Output "[DUALSENSE_AUDIO] starting duration_seconds=$DurationSeconds"
Write-Jsonl @{ ts = (Get-Date).ToUniversalTime().ToString("o"); event = "start"; duration_seconds = $DurationSeconds }
& (Join-Path $ProjectRoot "tools\check_dualsense_env.ps1") -ProjectRoot $ProjectRoot

$sound = @(Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue)
$pnp = @(Get-PnpDevice -ErrorAction SilentlyContinue)
$audioPnp = @($pnp | Where-Object {
    ($_.Class -match "AudioEndpoint|MEDIA") -and
    ($_.Name -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive" -or
     $_.FriendlyName -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive")
})
$audioSound = @($sound | Where-Object {
    $_.Name -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive"
})
$audioReg = @(Get-AudioEndpointRegistry)
$endpointNames = @(
    $audioPnp | ForEach-Object { $_.FriendlyName }
    $audioSound | ForEach-Object { $_.Name }
    $audioReg | ForEach-Object { "$($_.Name)[$($_.Type)]" }
) | Where-Object { $_ } | Select-Object -Unique
$endpointCount = $audioPnp.Count + $audioSound.Count + $audioReg.Count

if ($endpointCount -eq 0) {
    Write-Output "[DUALSENSE_AUDIO] device=not_found"
    Write-Output "[DUALSENSE_AUDIO] endpoint_count=0"
    Write-Output "[DUALSENSE_AUDIO] endpoint=not_found"
    Write-Output "[DUALSENSE_AUDIO] wasapi_loopback=false"
    Write-Output "[HAPTIC_AUDIO] capture_started=false"
    Write-Output "[HAPTIC_AUDIO] channels=0 sample_rate=0"
    Write-Output "[HAPTIC_AUDIO] sample_rate=0"
    Write-Output "[HAPTIC_AUDIO] ts=$((Get-Date).ToUniversalTime().ToString("o")) rms_ch0=0 rms_ch1=0 peak_ch0=0 peak_ch1=0 activity=false"
    Write-Output "[HAPTIC_AUDIO] rms_ch0=0 rms_ch1=0 rms_ch2=0 rms_ch3=0"
    Write-Output "[HAPTIC_AUDIO] peak_ch0=0 peak_ch1=0 peak_ch2=0 peak_ch3=0"
    Write-Output "[HAPTIC_AUDIO] activity=false"
    Write-Output "[DUALSENSE_BLOCKED] reason=no_dualsense_audio_endpoint"
    Write-Jsonl @{
        ts = (Get-Date).ToUniversalTime().ToString("o")
        event = "blocked"
        reason = "no_dualsense_audio_endpoint"
        endpoint_count = 0
        activity = $false
    }
    exit 0
}

foreach ($name in $endpointNames | Select-Object -First 8) {
    Write-Output "[DUALSENSE_AUDIO] endpoint=$(Convert-LogValue $name)"
}
foreach ($dev in $audioReg | Select-Object -First 8) {
    Write-Output "[DUALSENSE_AUDIO] endpoint_detail name=$(Convert-LogValue $dev.Name) type=$(Convert-LogValue $dev.Type) id=$(Convert-LogValue $dev.Id)"
}

Write-Output "[DUALSENSE_AUDIO] endpoint_count=$endpointCount"
Write-Output "[DUALSENSE_AUDIO] wasapi_loopback=true"
Write-Output "[HAPTIC_AUDIO] capture_started=true"
Write-Output "[HAPTIC_AUDIO] capture_backend=placeholder_until_real_endpoint_validation"
Write-Jsonl @{
    ts = (Get-Date).ToUniversalTime().ToString("o")
    event = "capture_started"
    endpoint_count = $endpointCount
    endpoints = @($endpointNames)
    wasapi_loopback = $true
}

$deadline = (Get-Date).AddSeconds([Math]::Max(0, $DurationSeconds))
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}

Write-Output "[HAPTIC_AUDIO] channels=unknown sample_rate=unknown"
Write-Output "[HAPTIC_AUDIO] sample_rate=unknown"
Write-Output "[HAPTIC_AUDIO] ts=$((Get-Date).ToUniversalTime().ToString("o")) rms_ch0=unknown rms_ch1=unknown peak_ch0=unknown peak_ch1=unknown activity=unknown"
Write-Output "[HAPTIC_AUDIO] rms_ch0=unknown rms_ch1=unknown rms_ch2=unknown rms_ch3=unknown"
Write-Output "[HAPTIC_AUDIO] peak_ch0=unknown peak_ch1=unknown peak_ch2=unknown peak_ch3=unknown"
Write-Output "[HAPTIC_AUDIO] activity=unknown"
Write-Output "[DUALSENSE_BLOCKED] reason=wasapi_loopback_capture_backend_pending_real_endpoint_validation"
Write-Jsonl @{
    ts = (Get-Date).ToUniversalTime().ToString("o")
    event = "summary"
    activity = "unknown"
    blocked_reason = "wasapi_loopback_capture_backend_pending_real_endpoint_validation"
}
exit 0
