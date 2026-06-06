param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$Seconds = 45,
    [int]$AttachTimeoutSeconds = 25,
    [int]$TriggerDelaySeconds = 1,
    [int]$PulseMs = 120,
    [ValidateRange(0, 65535)]
    [int]$LowSpeed = 65535,
    [ValidateRange(0, 65535)]
    [int]$HighSpeed = 65535,
    [ValidateSet("single", "double", "long")]
    [string]$Pattern = "single",
    [string]$PathContains = "vid_057e&pid_2069&mi_00"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$LogDir = Join-Path $ProjectRoot "logs\v5_2"
New-Item -ItemType Directory -Force $LogDir | Out-Null

function Write-ProbeLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value -or $Value -eq "") {
        $Value = "not_found"
    }
    Write-Output "[NS2PRO_HID_RUMBLE_PROBE] $Key=$Value"
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
            $hit = Select-String -Path $Path -Pattern $Pattern -SimpleMatch -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) {
                return $true
            }
        }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Read-FirstMatch {
    param(
        [string]$Path,
        [string]$Pattern
    )

    if (!(Test-Path $Path)) {
        return $null
    }
    $hit = Select-String -Path $Path -Pattern $Pattern -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) {
        return $hit.Line
    }
    return $null
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$monitorOut = Join-Path $LogDir "viiper_ns2pro_hid_rumble_monitor_$stamp.out.log"
$monitorErr = Join-Path $LogDir "viiper_ns2pro_hid_rumble_monitor_$stamp.err.log"
$hidOut = Join-Path $LogDir "viiper_ns2pro_hid_rumble_trigger_$stamp.out.log"
$hidErr = Join-Path $LogDir "viiper_ns2pro_hid_rumble_trigger_$stamp.err.log"

$monitorScript = Join-Path $ProjectRoot "experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1"
$hidScript = Join-Path $ProjectRoot "tools\Send-HidHapticProbe.ps1"

Write-ProbeLine "monitor_seconds" $Seconds
Write-ProbeLine "path_contains" $PathContains
Write-ProbeLine "pattern" $Pattern
Write-ProbeLine "pulse_ms" $PulseMs
Write-ProbeLine "monitor_out" ".\logs\v5_2\$(Split-Path $monitorOut -Leaf)"
Write-ProbeLine "hid_out" ".\logs\v5_2\$(Split-Path $hidOut -Leaf)"

$monitorArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $monitorScript,
    "-MonitorOnly",
    "-Seconds", "$Seconds",
    "-ExitOnNonZero"
)
$monitor = Start-Process powershell.exe -ArgumentList $monitorArgs -RedirectStandardOutput $monitorOut -RedirectStandardError $monitorErr -PassThru -WindowStyle Hidden

try {
    $attached = Wait-ForLogPattern -Path $monitorOut -Pattern "virtual device connected" -TimeoutSeconds $AttachTimeoutSeconds
    if (!$attached) {
        $attached = Wait-ForLogPattern -Path $monitorOut -Pattern "Successfully attached device via IOCTL" -TimeoutSeconds 2
    }

    Write-ProbeLine "monitor_attached" $attached
    if (!$attached) {
        if (!$monitor.HasExited) {
            Stop-Process -Id $monitor.Id -Force -ErrorAction SilentlyContinue
        }
        Write-ProbeLine "result" "failed"
        Write-ProbeLine "blocked" "monitor did not attach virtual ns2pro before timeout"
        exit 3
    }

    Start-Sleep -Seconds $TriggerDelaySeconds

    $hidArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $hidScript,
        "-Vid", "057e",
        "-Pids", "2069",
        "-PathContains", $PathContains,
        "-PulseMs", "$PulseMs",
        "-LowSpeed", "$LowSpeed",
        "-HighSpeed", "$HighSpeed",
        "-Pattern", $Pattern
    )
    $hid = Start-Process powershell.exe -ArgumentList $hidArgs -RedirectStandardOutput $hidOut -RedirectStandardError $hidErr -PassThru -WindowStyle Hidden
    $hid.WaitForExit()
    $hid.Refresh()
    $hidExitCode = $hid.ExitCode

    $waitMs = [Math]::Max(5000, ($Seconds + 10) * 1000)
    if (!$monitor.WaitForExit($waitMs)) {
        Write-ProbeLine "monitor_timeout" "true"
        Stop-Process -Id $monitor.Id -Force -ErrorAction SilentlyContinue
    }
    $monitor.Refresh()
    $monitorExitCode = $monitor.ExitCode

    $resultLine = Read-FirstMatch -Path $monitorOut -Pattern "\[NS2PRO\] result output_feedback=.*"
    $firstNonZeroLine = Read-FirstMatch -Path $monitorOut -Pattern "\[NS2PRO_OUTPUT_FIRST_NONZERO\].*"
    $summaryLine = Read-FirstMatch -Path $monitorOut -Pattern "\[NS2PRO_OUTPUT\] feedback_count=.*"
    $matchedLine = Read-FirstMatch -Path $hidOut -Pattern "\[HID_HAPTIC\] matched_devices=.*"

    $outputFeedback = $false
    $nonzero = $false
    if ($resultLine) {
        $outputFeedback = $resultLine -match "output_feedback=true"
        $nonzero = $resultLine -match "nonzero=true"
    }
    if ($firstNonZeroLine) {
        $nonzero = $true
    }
    $hidMatched = $false
    if ($matchedLine -and $matchedLine -match "matched_devices=([1-9][0-9]*)") {
        $hidMatched = $true
    }

    Write-ProbeLine "hid_exit" ($(if ($null -eq $hidExitCode) { "unknown" } else { $hidExitCode }))
    Write-ProbeLine "monitor_exit" ($(if ($null -eq $monitorExitCode) { "unknown" } else { $monitorExitCode }))
    Write-ProbeLine "hid_matched" ($(if ($matchedLine) { $matchedLine } else { "not_found" }))
    Write-ProbeLine "output_feedback" $outputFeedback
    Write-ProbeLine "nonzero" $nonzero
    Write-ProbeLine "summary" ($(if ($summaryLine) { $summaryLine } else { "not_found" }))
    Write-ProbeLine "first_nonzero" ($(if ($firstNonZeroLine) { $firstNonZeroLine } else { "not_found" }))

    if (!$hidMatched) {
        Write-ProbeLine "result" "failed"
        Write-ProbeLine "blocked" "HID haptic trigger did not match the virtual HID interface"
        exit 4
    }

    if (!$nonzero) {
        Write-ProbeLine "result" "failed"
        Write-ProbeLine "blocked" "VIIPER output callback did not report non-zero rumble"
        exit 5
    }

    Write-ProbeLine "result" "passed"
    exit 0
}
finally {
    if ($monitor -and !$monitor.HasExited) {
        Stop-Process -Id $monitor.Id -Force -ErrorAction SilentlyContinue
    }
}
