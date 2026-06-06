param(
    [string]$IdfPath = "",
    [switch]$SendToRealPro2,
    [string]$Port = "",
    [int]$BasicMonitorSeconds = 8,
    [int]$HidRumbleSeconds = 45,
    [int]$SdlMonitorSeconds = 25
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LogDir = Join-Path $RepoRoot "logs\v5_2"
$DocsDir = Join-Path $RepoRoot "docs"
New-Item -ItemType Directory -Force $LogDir | Out-Null
New-Item -ItemType Directory -Force $DocsDir | Out-Null

$Stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogPath = Join-Path $LogDir "night_probe_$Stamp.log"
$SummaryPath = Join-Path $DocsDir "v5_2_night_probe_summary.md"
$Results = New-Object System.Collections.Generic.List[object]
$AllOutput = New-Object System.Text.StringBuilder

if ($SendToRealPro2 -and [string]::IsNullOrWhiteSpace($Port)) {
    throw "-SendToRealPro2 requires -Port. Example: -SendToRealPro2 -Port COM12"
}

function ConvertTo-RepoPath {
    param([string]$Path)
    if (!$Path) { return "" }
    $full = $Path
    try { $full = (Resolve-Path $Path -ErrorAction SilentlyContinue).Path } catch { $full = $Path }
    if ($full -and $full.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $trimChars = [char[]](92, 47)
        $relative = $full.Substring($RepoRoot.Length).TrimStart($trimChars)
        return "." + [IO.Path]::DirectorySeparatorChar + $relative
    }
    return $Path
}

function Write-NightLog {
    param([string]$Line)
    $Line | Tee-Object -FilePath $LogPath -Append
}

function Test-AnyPattern {
    param(
        [string]$Text,
        [string[]]$Patterns
    )
    foreach ($pattern in $Patterns) {
        if ($Text -match $pattern) { return $true }
    }
    return $false
}

function Add-Result {
    param(
        [string]$Name,
        [string]$Status,
        [object]$ExitCode,
        [string]$Note
    )
    $Results.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        ExitCode = $ExitCode
        Note = $Note
    }) | Out-Null
    Write-NightLog "[NIGHT] step=$Name status=$Status exit=$ExitCode note=$Note"
}

function Invoke-NightScript {
    param(
        [string]$Name,
        [string]$RelativeScript,
        [string[]]$Arguments = @(),
        [string[]]$PassPatterns = @(),
        [string[]]$BlockedPatterns = @("blocked", "BLOCKED", "blocked_reason", "DUALSENSE_BLOCKED"),
        [string]$Note = ""
    )

    $scriptPath = Join-Path $RepoRoot $RelativeScript
    Write-NightLog ""
    Write-NightLog "========== $Name =========="
    Write-NightLog "[NIGHT] command=powershell -NoProfile -ExecutionPolicy Bypass -File $RelativeScript $($Arguments -join ' ')"

    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { $exitCode = 0 }

    $text = ($output | Out-String)
    [void]$AllOutput.AppendLine($text)
    foreach ($line in $output) { Write-NightLog "$line" }

    $pass = ($exitCode -eq 0)
    if ($PassPatterns.Count -gt 0 -and (Test-AnyPattern -Text $text -Patterns $PassPatterns)) {
        $pass = $true
    }
    $blocked = Test-AnyPattern -Text $text -Patterns $BlockedPatterns

    if ($blocked) {
        Add-Result -Name $Name -Status "blocked" -ExitCode $exitCode -Note $Note
    } elseif ($pass) {
        Add-Result -Name $Name -Status "passed" -ExitCode $exitCode -Note $Note
    } else {
        Add-Result -Name $Name -Status "failed" -ExitCode $exitCode -Note $Note
    }
}

function Invoke-FirmwareBuild {
    Write-NightLog ""
    Write-NightLog "========== firmware_build =========="

    $buildScript = Join-Path $RepoRoot "tools\esp32s3\build.ps1"
    $arguments = @()
    if (![string]::IsNullOrWhiteSpace($IdfPath)) {
        $arguments += @("-IdfPath", $IdfPath)
    }

    $displayArgs = if ($arguments.Count -gt 0) { $arguments -join " " } else { "" }
    Write-NightLog "[NIGHT] command=powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build.ps1 $displayArgs"

    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript @arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { $exitCode = 0 }

    $text = ($output | Out-String)
    [void]$AllOutput.AppendLine($text)
    foreach ($line in $output) { Write-NightLog "$line" }

    if ($text -match "idf\.py not found|ESP-IDF export\.ps1 not found|ESP-IDF.*not found") {
        Add-Result -Name "firmware_build" -Status "blocked" -ExitCode $exitCode -Note "idf.py unavailable; pass -IdfPath C:\Espressif\v5.3.3\esp-idf or open ESP-IDF PowerShell"
        return
    }
    if ($exitCode -eq 0) {
        Add-Result -Name "firmware_build" -Status "passed" -ExitCode $exitCode -Note "ESP32-S3 firmware build completed"
        return
    }

    Add-Result -Name "firmware_build" -Status "failed" -ExitCode $exitCode -Note "firmware build failed"
}

function Wait-ForLogPattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [int]$TimeoutSeconds
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Path) {
            if (Select-String -Path $Path -Pattern $Pattern -Quiet -ErrorAction SilentlyContinue) {
                return $true
            }
        }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Invoke-SdlWithMonitor {
    Write-NightLog ""
    Write-NightLog "========== sdl_ns2pro_rumble_test_with_monitor =========="

    $monitorOut = Join-Path $LogDir "night_sdl_monitor_$Stamp.out.log"
    $monitorErr = Join-Path $LogDir "night_sdl_monitor_$Stamp.err.log"
    $sdlOut = Join-Path $LogDir "night_sdl_test_$Stamp.out.log"
    $sdlErr = Join-Path $LogDir "night_sdl_test_$Stamp.err.log"
    $monitorScript = Join-Path $RepoRoot "experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1"
    $sdlScript = Join-Path $RepoRoot "experiments\sdl_ns2pro_rumble_test\run_sdl_ns2pro_rumble_test.ps1"

    $monitorArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $monitorScript,
        "-MonitorOnly",
        "-Seconds", "$SdlMonitorSeconds"
    )
    $monitor = Start-Process powershell.exe -ArgumentList $monitorArgs -RedirectStandardOutput $monitorOut -RedirectStandardError $monitorErr -PassThru -WindowStyle Hidden

    try {
        $attached = Wait-ForLogPattern -Path $monitorOut -Pattern "virtual device connected|Successfully attached device via IOCTL" -TimeoutSeconds 20
        Write-NightLog "[NIGHT_SDL] monitor_attached=$($attached.ToString().ToLowerInvariant())"

        if (!$attached) {
            if (!$monitor.HasExited) {
                Stop-Process -Id $monitor.Id -Force -ErrorAction SilentlyContinue
            }
            Add-Result -Name "sdl_ns2pro_rumble_test" -Status "failed" -ExitCode 3 -Note "monitor did not attach"
            return
        }

        $sdlArgs = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", $sdlScript,
            "-All",
            "-DurationMs", "500"
        )
        $sdl = Start-Process powershell.exe -ArgumentList $sdlArgs -RedirectStandardOutput $sdlOut -RedirectStandardError $sdlErr -PassThru -WindowStyle Hidden
        $sdl.WaitForExit()
        $sdl.Refresh()

        $sdlText = ""
        if (Test-Path $sdlOut) { $sdlText += (Get-Content $sdlOut | Out-String) }
        if (Test-Path $sdlErr) { $sdlText += (Get-Content $sdlErr | Out-String) }
        [void]$AllOutput.AppendLine($sdlText)
        foreach ($line in ($sdlText -split "`r?`n")) {
            if ($line) { Write-NightLog $line }
        }

        $waitMs = [Math]::Max(30000, ($SdlMonitorSeconds + 5) * 1000)
        if (!$monitor.WaitForExit($waitMs)) {
            Stop-Process -Id $monitor.Id -Force -ErrorAction SilentlyContinue
        }
        $monitor.Refresh()

        $monitorText = ""
        if (Test-Path $monitorOut) { $monitorText += (Get-Content $monitorOut | Out-String) }
        if (Test-Path $monitorErr) { $monitorText += (Get-Content $monitorErr | Out-String) }
        [void]$AllOutput.AppendLine($monitorText)

        $status = "passed"
        $note = "SDL enumerated ns2pro monitor"
        if ($sdlText -match "any_rumble=false" -and $sdlText -match "any_hd_effect=false") {
            $status = "blocked"
            $note = "SDL ordinary rumble/raw effect unsupported for current descriptor"
        }
        if ($sdlText -notmatch "\[SDL\] version=") {
            $status = "failed"
            $note = "SDL test did not run"
        }

        $sdlExitCode = $sdl.ExitCode
        if ($null -eq $sdlExitCode) { $sdlExitCode = 0 }
        Add-Result -Name "sdl_ns2pro_rumble_test" -Status $status -ExitCode $sdlExitCode -Note $note
        Write-NightLog "[NIGHT_SDL] monitor_log=$(ConvertTo-RepoPath $monitorOut)"
        Write-NightLog "[NIGHT_SDL] sdl_log=$(ConvertTo-RepoPath $sdlOut)"
    }
    finally {
        if ($monitor -and !$monitor.HasExited) {
            Stop-Process -Id $monitor.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-Summary {
    $text = $AllOutput.ToString()
    $firmwareBuild = $Results | Where-Object { $_.Name -eq "firmware_build" } | Select-Object -Last 1
    $firmwareBuildStatus = if ($firmwareBuild) { $firmwareBuild.Status } else { "not_run" }
    $displayIdfPath = if (![string]::IsNullOrWhiteSpace($IdfPath)) { $IdfPath } else { "not_supplied" }
    $summary = New-Object System.Collections.Generic.List[string]
    $summary.Add("# V5.2 Night Probe Summary") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add("Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add("Log: $(ConvertTo-RepoPath $LogPath)") | Out-Null
    $summary.Add("idf_path: $displayIdfPath") | Out-Null
    $summary.Add("recommended_idf_path_example: C:\Espressif\v5.3.3\esp-idf") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add("## Step Results") | Out-Null
    $summary.Add("") | Out-Null
    foreach ($result in $Results) {
        $noteText = if ([string]::IsNullOrWhiteSpace($result.Note)) { "" } else { " $($result.Note)" }
        $summary.Add("- $($result.Name): $($result.Status) (exit=$($result.ExitCode))$noteText") | Out-Null
    }
    $summary.Add("") | Out-Null
    $summary.Add("## Key Signals") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add("- firmware_build: $firmwareBuildStatus") | Out-Null
    $summary.Add("- usbip-win2 installed: $((($text -match 'usbip_win2=installed') -or ($text -match 'usbip-win2 installed: true')).ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- VIIPER ns2pro attach: $(($text -match 'monitor_attached=true|Successfully attached device via IOCTL|virtual device connected').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- synthetic input: $(($text -match '\[NS2PRO_INPUT\]').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- HID 0x02 nonzero 16+16: $(($text -match 'nonzero=true' -and $text -match 'left_rumble_hex=' -and $text -match 'right_rumble_hex=').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- Pro2 dry-run payload: $(($text -match '\[PRO2_HD_RUMBLE\] payload_0x02=').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- real Pro2 send: $(($text -match '\[PRO2_HD_RUMBLE\] sent=true').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- SDL3 runtime: $(($text -match '\[SDL\] version=').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- SDL gamepad recognition: $(($text -match '\[SDL\] gamepad_count=[1-9]').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- SDL rumble/effect nonzero route: $(($text -match 'any_rumble=true|any_hd_effect=true').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- DualSense HID detected: $(($text -match '\[DUALSENSE_ENV\] hid_usb=true|\[DUALSENSE_ENV\] hid_bluetooth=true').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("- DualSense audio endpoint detected: $(($text -match '\[DUALSENSE_AUDIO\] device=' -and $text -notmatch '\[DUALSENSE_AUDIO\] device=not_found').ToString().ToLowerInvariant())") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add("## Current Blockers") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add("- Steam Controller Test still requires manual UI action while the VIIPER monitor is online.") | Out-Null
    $summary.Add('- Real Pro2 HD send requires flashed raw02 firmware, ESP32-S3 control port, and a real Pro2 BLE connection.') | Out-Null
    $summary.Add('- SDL 3.4.10 currently treats VIIPER `VID_057E&PID_2069&MI_00` as a low-level HID joystick, not a rumble-capable Switch gamepad.') | Out-Null
    $summary.Add("- DualSense route is blocked on this machine by missing real DualSense HID/audio endpoint.") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add("## Manual Commands") | Out-Null
    $summary.Add("") | Out-Null
    $summary.Add('```powershell') | Out-Null
    $summary.Add('powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\build.ps1 -IdfPath C:\Espressif\v5.3.3\esp-idf') | Out-Null
    $summary.Add('powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\esp32s3\flash.ps1 -Port COM12 -IdfPath C:\Espressif\v5.3.3\esp-idf') | Out-Null
    $summary.Add('powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1 -MonitorOnly -Seconds 300') | Out-Null
    $summary.Add('powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1 -SendToRealPro2 -Port COM12') | Out-Null
    $summary.Add('```') | Out-Null
    Set-Content -Path $SummaryPath -Value $summary -Encoding UTF8
    Write-NightLog "[NIGHT] summary=$(ConvertTo-RepoPath $SummaryPath)"
}

Write-NightLog "[NIGHT] started=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
Write-NightLog "[NIGHT] repo=."
Write-NightLog "[NIGHT] log=$(ConvertTo-RepoPath $LogPath)"
Write-NightLog "[NIGHT] idf_path=$(if (![string]::IsNullOrWhiteSpace($IdfPath)) { $IdfPath } else { 'not_supplied' })"

Invoke-FirmwareBuild
Invoke-NightScript -Name "check_viiper_env" -RelativeScript "tools\check_viiper_env.ps1" -PassPatterns @("usbip_win2=installed", "viiper=")
Invoke-NightScript -Name "viiper_ns2pro_probe" -RelativeScript "experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1" -Arguments @("-MonitorOnly", "-Seconds", "$BasicMonitorSeconds") -PassPatterns @("\[NS2PRO_INPUT\]", "virtual device connected", "Successfully attached device via IOCTL")
Invoke-NightScript -Name "viiper_ns2pro_hid_rumble_probe" -RelativeScript "experiments\viiper_ns2pro_hid_rumble_probe\run_viiper_ns2pro_hid_rumble_probe.ps1" -Arguments @("-Seconds", "$HidRumbleSeconds", "-Pattern", "single") -PassPatterns @("result=passed", "nonzero=true")

$phase3Args = @()
if ($SendToRealPro2) {
    $phase3Args += "-SendToRealPro2"
    if ($Port) { $phase3Args += @("-Port", $Port) }
}
Invoke-NightScript -Name "viiper_to_real_pro2_phase3" -RelativeScript "experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1" -Arguments $phase3Args -PassPatterns @("payload_0x02=", "left_nonzero=true")

Invoke-SdlWithMonitor

Invoke-NightScript -Name "check_dualsense_env" -RelativeScript "tools\check_dualsense_env.ps1" -PassPatterns @("DUALSENSE_ENV")
Invoke-NightScript -Name "dualsense_hid_output_probe" -RelativeScript "experiments\dualsense_hid_output_probe\run_dualsense_hid_output_probe.ps1" -PassPatterns @("DUALSENSE_HID", "DUALSENSE_ENV")
Invoke-NightScript -Name "dualsense_haptic_audio_probe" -RelativeScript "experiments\dualsense_haptic_audio_probe\run_dualsense_haptic_audio_probe.ps1" -PassPatterns @("HAPTIC_AUDIO", "DUALSENSE_AUDIO")

Write-Summary
Write-NightLog "[NIGHT] completed=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
