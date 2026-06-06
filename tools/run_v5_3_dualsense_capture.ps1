param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$DurationSeconds = 90,
    [string]$SteamInputHint = "compare_on_off"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logsRoot = Join-Path $ProjectRoot "logs\v5_3_dualsense"
$logsDir = Join-Path $logsRoot $stamp
New-Item -ItemType Directory -Force $logsDir | Out-Null

function Write-CaptureLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) { $Value = $Value.ToString().ToLowerInvariant() }
    if ($null -eq $Value) { $Value = "not_found" }
    if ($Value -is [string] -and $Value -eq "") { $Value = "not_found" }
    Write-Output "[V5_3_CAPTURE] $Key=$Value"
}

function Invoke-CaptureCommand {
    param(
        [string]$Label,
        [string]$Script,
        [string[]]$Args,
        [string]$LogPath
    )

    $cmd = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $Script) + $Args
    $output = & powershell @cmd 2>&1
    $output | Tee-Object -FilePath $LogPath | Out-Null
    return @($output)
}

$envScript = Join-Path $ProjectRoot "tools\check_dualsense_env.ps1"
$hidScript = Join-Path $ProjectRoot "experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1"
$audioScript = Join-Path $ProjectRoot "experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1"

Write-CaptureLine "logs_dir" $logsDir
Write-CaptureLine "duration_seconds" $DurationSeconds
Write-CaptureLine "steam_input_hint" $SteamInputHint
Write-CaptureLine "user_action" "after_start_open_native_dualsense_game_and_compare_steam_input_on_off"

$envLog = Join-Path $logsDir "env.log"
$envOutput = Invoke-CaptureCommand -Label "env" -Script $envScript -Args @("-ProjectRoot", $ProjectRoot) -LogPath $envLog
$dualsensePresent = ($envOutput -match "\[DUALSENSE_ENV\] real_dualsense=true" -or
    $envOutput -match "\[DUALSENSE_ENV\] hid_usb=true" -or
    $envOutput -match "\[DUALSENSE_ENV\] hid_bluetooth=true")
$audioEndpoint = ($envOutput -match "\[DUALSENSE_ENV\] audio_endpoint_count=[1-9]" -or
    ($envOutput -match "\[DUALSENSE_ENV\] audio_endpoint=" -and !($envOutput -match "audio_endpoint=not_found")))

Write-CaptureLine "env" ($(if ($dualsensePresent) { "present" } else { "blocked" }))

if (!$dualsensePresent) {
    Write-CaptureLine "hid_output_reports" 0
    Write-CaptureLine "audio_activity" "false"
    Write-CaptureLine "likely_advanced_haptic_source" "false"
    Write-CaptureLine "blocked" "no_real_dualsense"
    Write-CaptureLine "next_action" "plug_dualsense_usb"
    exit 0
}

Write-CaptureLine "dualsense_present" "true"
Write-CaptureLine "audio_endpoint" $audioEndpoint
Write-CaptureLine "instruction" "start_native_dualsense_game_now"

$hidLog = Join-Path $logsDir "hid_output.log"
$hidJsonl = Join-Path $logsDir "hid_output.jsonl"
$hidRaw = Join-Path $logsDir "hid_output_raw.txt"
$audioLog = Join-Path $logsDir "haptic_audio.log"
$audioJsonl = Join-Path $logsDir "haptic_audio.jsonl"

$hidArgs = @("-ProjectRoot", $ProjectRoot, "-DurationSeconds", $DurationSeconds.ToString(), "-JsonlPath", $hidJsonl, "-RawHexLogPath", $hidRaw)
$audioArgs = @("-ProjectRoot", $ProjectRoot, "-DurationSeconds", $DurationSeconds.ToString(), "-JsonlPath", $audioJsonl)

$hidProcess = Start-Process -FilePath "powershell" -ArgumentList (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $hidScript) + $hidArgs) -WorkingDirectory $ProjectRoot -RedirectStandardOutput $hidLog -RedirectStandardError (Join-Path $logsDir "hid_output.err.log") -PassThru -WindowStyle Hidden
$audioProcess = Start-Process -FilePath "powershell" -ArgumentList (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $audioScript) + $audioArgs) -WorkingDirectory $ProjectRoot -RedirectStandardOutput $audioLog -RedirectStandardError (Join-Path $logsDir "haptic_audio.err.log") -PassThru -WindowStyle Hidden

Wait-Process -Id $hidProcess.Id, $audioProcess.Id

$hidText = if (Test-Path $hidLog) { Get-Content -Raw -Encoding UTF8 $hidLog } else { "" }
$audioText = if (Test-Path $audioLog) { Get-Content -Raw -Encoding UTF8 $audioLog } else { "" }
$hidReports = 0
$hidMatch = [regex]::Match($hidText, "\[DUALSENSE_OUTPUT\] captured_reports=(\d+)")
if ($hidMatch.Success) { $hidReports = [int]$hidMatch.Groups[1].Value }
$audioActivity = $audioText -match "\[HAPTIC_AUDIO\].*activity=true"
$likelyAdvanced = ($hidReports -gt 0 -and $audioActivity)

Write-CaptureLine "hid_output_reports" $hidReports
Write-CaptureLine "audio_activity" $audioActivity
Write-CaptureLine "likely_advanced_haptic_source" $likelyAdvanced
Write-CaptureLine "logs_dir" $logsDir
exit 0
