param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$DurationSeconds = 20
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$EnvScript = Join-Path $ProjectRoot "tools\check_dualsense_env.ps1"

Write-Output "[DUALSENSE_HID] starting duration_seconds=$DurationSeconds"
& $EnvScript -ProjectRoot $ProjectRoot

$pnp = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
    $_.FriendlyName -match "DualSense|Wireless Controller" -or
    $_.InstanceId -match "VID_054C&PID_0CE6|VID&0002054C_PID&0CE6|VID_054C&PID_0DF2|VID&0002054C_PID&0DF2"
})

if ($pnp.Count -eq 0) {
    Write-Output "[DUALSENSE_HID] blocked: no real DualSense HID device found"
    Write-Output "[DUALSENSE_OUTPUT] captured=false"
    exit 2
}

foreach ($dev in $pnp) {
    Write-Output "[DUALSENSE_HID] device=$($dev.FriendlyName) class=$($dev.Class) id=$($dev.InstanceId)"
}

Write-Output "[DUALSENSE_HID] blocked: output report sniffing requires a proxy/filter path or a library-owned device handle"
Write-Output "[DUALSENSE_OUTPUT] captured=false"
exit 3
