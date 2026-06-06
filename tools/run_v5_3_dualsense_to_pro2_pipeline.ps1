param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$Synthetic,
    [ValidateSet("impact", "engine", "texture", "ui_click")]
    [string]$Event = "impact",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Write-PipelineLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) { $Value = $Value.ToString().ToLowerInvariant() }
    if ($null -eq $Value -or $Value -eq "") { $Value = "not_found" }
    Write-Output "[V5_3_PIPELINE] $Key=$Value"
}

if (!$Synthetic) {
    Write-PipelineLine "source" "not_selected"
    Write-PipelineLine "blocked" $true
    Write-PipelineLine "reason" "only synthetic input is enabled in this planning probe"
    Write-PipelineLine "result" "blocked"
    exit 0
}

if (!$DryRun) {
    $DryRun = $true
}

$features = switch ($Event) {
    "impact" {
        [pscustomobject]@{
            RmsLeft = 0.44
            RmsRight = 0.38
            PeakLeft = 0.93
            PeakRight = 0.86
            Transient = 0.82
            LowFrequency = 0.51
            DurationMs = 18
            Preset = "captured"
        }
    }
    "engine" {
        [pscustomobject]@{
            RmsLeft = 0.37
            RmsRight = 0.35
            PeakLeft = 0.58
            PeakRight = 0.55
            Transient = 0.18
            LowFrequency = 0.79
            DurationMs = 25
            Preset = "medium"
        }
    }
    "texture" {
        [pscustomobject]@{
            RmsLeft = 0.19
            RmsRight = 0.24
            PeakLeft = 0.41
            PeakRight = 0.46
            Transient = 0.33
            LowFrequency = 0.27
            DurationMs = 20
            Preset = "low"
        }
    }
    "ui_click" {
        [pscustomobject]@{
            RmsLeft = 0.11
            RmsRight = 0.11
            PeakLeft = 0.35
            PeakRight = 0.35
            Transient = 0.71
            LowFrequency = 0.12
            DurationMs = 8
            Preset = "low"
        }
    }
}

$balance = [Math]::Round($features.RmsRight - $features.RmsLeft, 3)
$helper = Join-Path $ProjectRoot "tools\send_pro2_raw02.ps1"
if (!(Test-Path $helper)) {
    throw "Missing raw02 helper: $helper"
}

$helperOutput = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $helper -Preset $features.Preset -DryRun 2>&1)
$helperExit = $LASTEXITCODE
$payloadLine = $helperOutput | Where-Object { $_ -match "^\[PRO2_RAW02\] payload=" } | Select-Object -First 1
$payload = if ($payloadLine) { ($payloadLine -replace "^\[PRO2_RAW02\] payload=", "") } else { "not_found" }

Write-PipelineLine "source" "synthetic"
Write-PipelineLine "event" $Event
Write-PipelineLine "rms_left" $features.RmsLeft
Write-PipelineLine "rms_right" $features.RmsRight
Write-PipelineLine "peak_left" $features.PeakLeft
Write-PipelineLine "peak_right" $features.PeakRight
Write-PipelineLine "transient_score" $features.Transient
Write-PipelineLine "low_frequency_energy" $features.LowFrequency
Write-PipelineLine "stereo_balance" $balance
Write-PipelineLine "window_ms" $features.DurationMs
Write-PipelineLine "classification" $Event
Write-PipelineLine "raw02_preset" $features.Preset
Write-PipelineLine "raw02_payload" $payload
Write-PipelineLine "dry_run" $true
Write-PipelineLine "sent" $false

if ($helperExit -ne 0 -or $payload -eq "not_found") {
    Write-PipelineLine "result" "failed"
    exit 1
}

Write-PipelineLine "result" "passed"
exit 0
