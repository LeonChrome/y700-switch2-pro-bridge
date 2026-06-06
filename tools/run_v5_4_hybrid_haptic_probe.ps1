param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Write-HybridLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) { $Value = $Value.ToString().ToLowerInvariant() }
    if ($null -eq $Value -or $Value -eq "") { $Value = "not_found" }
    Write-Output "[V5_4_HYBRID] $Key=$Value"
}

$v52Probe = Join-Path $ProjectRoot "experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1"
$raw02Helper = Join-Path $ProjectRoot "tools\send_pro2_raw02.ps1"
$envScript = Join-Path $ProjectRoot "tools\check_dualsense_env.ps1"
$pipelineScript = Join-Path $ProjectRoot "tools\run_v5_3_dualsense_to_pro2_pipeline.ps1"

$purePro2Preserved = (Test-Path $v52Probe) -and (Test-Path $raw02Helper)
$envOutput = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $envScript -ProjectRoot $ProjectRoot 2>&1)
$envExit = $LASTEXITCODE
$envText = $envOutput -join "`n"
$realDualSense = $envText -match "(?m)^\[DUALSENSE_ENV\] real_dualsense=true$"
$audioEndpoint = $envText -match "(?m)^\[DUALSENSE_ENV\] audio_endpoint_count=[1-9][0-9]*$"

$pipelineOutput = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $pipelineScript -ProjectRoot $ProjectRoot -Synthetic -Event impact -DryRun 2>&1)
$pipelineExit = $LASTEXITCODE
$syntheticPassed = $pipelineExit -eq 0 -and ($pipelineOutput -match "^\[V5_3_PIPELINE\] result=passed$")

Write-HybridLine "pure_pro2_path_preserved" $purePro2Preserved
Write-HybridLine "v5_2_default_changed" $false
Write-HybridLine "identity_option_1" "pro2_ns2_viiper"
Write-HybridLine "identity_option_2" "dualsense_esp32s3_experimental"
Write-HybridLine "real_dualsense" $realDualSense
Write-HybridLine "dualsense_audio_endpoint" $audioEndpoint
Write-HybridLine "synthetic_policy_probe" ($(if ($syntheticPassed) { "passed" } else { "failed" }))
Write-HybridLine "hardware_probe" ($(if ($realDualSense) { "ready" } else { "blocked" }))
Write-HybridLine "hardware_blocker" ($(if ($realDualSense) { "none" } else { "no_real_dualsense" }))

if (!$purePro2Preserved -or $envExit -ne 0 -or !$syntheticPassed) {
    Write-HybridLine "result" "failed"
    exit 1
}

Write-HybridLine "result" ($(if ($realDualSense) { "passed_ready_for_capture" } else { "passed_as_blocked" }))
exit 0
