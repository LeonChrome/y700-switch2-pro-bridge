param(
    [ValidateSet(
        "silence",
        "ch2_tick",
        "ch3_tick",
        "both_tick",
        "ch2_punch",
        "ch3_punch",
        "both_punch",
        "continuous",
        "texture",
        "sweep"
    )]
    [string]$Pattern = "both_tick",
    [int]$DurationMs = 600,
    [ValidateRange(0, 100)]
    [int]$Intensity = 50,
    [string]$DeviceName = "Wireless Controller",
    [switch]$ListDevices,
    [switch]$CompileOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Source = Join-Path $RepoRoot "tools\SendV55HapticAudioTest.cs"
$Out = Join-Path $RepoRoot "tools\SendV55HapticAudioTest.exe"

function Find-Csc {
    $candidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    $cmd = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    throw "csc.exe not found. Install .NET Framework developer tools or use Visual Studio Build Tools."
}

if (!(Test-Path -LiteralPath $Source)) {
    throw "Missing source: $Source"
}

$csc = Find-Csc
Write-Output "[HAPTIC_AUDIO_TEST] compile_source=tools\SendV55HapticAudioTest.cs"
& $csc /nologo /unsafe /platform:x64 /out:$Out $Source
if ($LASTEXITCODE -ne 0) {
    throw "C# compile failed: $LASTEXITCODE"
}
Write-Output "[HAPTIC_AUDIO_TEST] compile=passed exe=tools\SendV55HapticAudioTest.exe"

if ($CompileOnly) {
    return
}

$args = @()
if ($ListDevices) {
    $args += "--list"
} else {
    $args += @(
        "--pattern", $Pattern,
        "--duration-ms", [string]$DurationMs,
        "--intensity", [string]$Intensity,
        "--device-name", $DeviceName
    )
}

& $Out @args
if ($LASTEXITCODE -ne 0) {
    throw "Haptic audio test failed: $LASTEXITCODE"
}
