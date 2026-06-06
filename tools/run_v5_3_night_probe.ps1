param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$DurationSeconds = 5
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Write-NightLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) { $Value = $Value.ToString().ToLowerInvariant() }
    if ($null -eq $Value) { $Value = "not_found" }
    if ($Value -is [string] -and $Value -eq "") { $Value = "not_found" }
    Write-Output "[V5_3_NIGHT] $Key=$Value"
}

function Invoke-Probe {
    param([string]$Script, [string[]]$Args)
    $cmd = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $Script) + $Args
    $output = & powershell @cmd 2>&1
    $exitCode = $LASTEXITCODE
    return [pscustomobject]@{ Output = @($output); ExitCode = $exitCode }
}

function Get-ProbeStatus {
    param($Probe)
    $text = ($Probe.Output -join "`n")
    if ($Probe.ExitCode -ne 0) { return "failed" }
    if ($text -match "\[DUALSENSE_BLOCKED\]|\[V5_3_CAPTURE\] blocked=") { return "blocked" }
    return "passed"
}

$envScript = Join-Path $ProjectRoot "tools\check_dualsense_env.ps1"
$hidScript = Join-Path $ProjectRoot "experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1"
$audioScript = Join-Path $ProjectRoot "experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1"
$captureScript = Join-Path $ProjectRoot "tools\run_v5_3_dualsense_capture.ps1"

$envProbe = Invoke-Probe -Script $envScript -Args @("-ProjectRoot", $ProjectRoot)
$envText = $envProbe.Output -join "`n"
$dualsensePresent = $envText -match "\[DUALSENSE_ENV\] real_dualsense=true"
$audioEndpoint = $envText -match "\[DUALSENSE_ENV\] audio_endpoint_count=[1-9]"

$hidProbe = Invoke-Probe -Script $hidScript -Args @("-ProjectRoot", $ProjectRoot, "-DurationSeconds", $DurationSeconds.ToString())
$audioProbe = Invoke-Probe -Script $audioScript -Args @("-ProjectRoot", $ProjectRoot, "-DurationSeconds", $DurationSeconds.ToString())
$captureProbe = Invoke-Probe -Script $captureScript -Args @("-ProjectRoot", $ProjectRoot, "-DurationSeconds", $DurationSeconds.ToString())

Write-NightLine "dualsense_present" $dualsensePresent
Write-NightLine "audio_endpoint" $audioEndpoint
Write-NightLine "hid_probe" (Get-ProbeStatus $hidProbe)
Write-NightLine "audio_probe" (Get-ProbeStatus $audioProbe)
Write-NightLine "capture_runner" (Get-ProbeStatus $captureProbe)
Write-NightLine "next_action" ($(if ($dualsensePresent) { "run_native_game_capture" } else { "plug_dualsense_usb" }))

if ($envProbe.ExitCode -ne 0 -or $hidProbe.ExitCode -ne 0 -or $audioProbe.ExitCode -ne 0 -or $captureProbe.ExitCode -ne 0) {
    Write-NightLine "result" "failed"
    exit 1
}

Write-NightLine "result" "passed_as_blocked_or_ready"
exit 0
