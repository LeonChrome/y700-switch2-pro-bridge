param()

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$IdentityScript = Join-Path $RepoRoot "tools\check_v5_5_dualsense_identity.ps1"

function Write-InputLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    Write-Output "[V5_5_DS5_INPUT] $Key=$Value"
}

if (!(Test-Path -LiteralPath $IdentityScript)) {
    throw "Identity checker not found: tools/check_v5_5_dualsense_identity.ps1"
}

$identityOutput = @(
    & powershell -NoProfile -ExecutionPolicy Bypass -File $IdentityScript
)
$identityExitCode = $LASTEXITCODE
$identityOutput | Write-Output

$identityText = $identityOutput -join "`n"
$identityFound = $identityText -match '\[V5_5_DS5_IDENTITY\] hid_found=true'
$likelyDualSense = $identityText -match '\[V5_5_DS5_IDENTITY\] likely_dualsense=true'

Write-InputLine "identity_found" ($identityFound -and $likelyDualSense)
Write-InputLine "test_method" "joy.cpl/steam/gamepad_tester"
Write-InputLine "expected" "buttons/sticks move when Pro2 moves"
Write-InputLine "realtime_capture" "manual"

if ($identityExitCode -ne 0) {
    Write-InputLine "result" "identity_check_failed"
    exit $identityExitCode
}

if (!$identityFound -or !$likelyDualSense) {
    Write-InputLine "result" "blocked_no_dualsense_identity"
    exit 0
}

Write-InputLine "result" "ready_for_manual_input_test"
exit 0
