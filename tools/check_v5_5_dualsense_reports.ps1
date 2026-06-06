param(
    [ValidateRange(1, 60)]
    [int]$Seconds = 5
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Source = Join-Path $RepoRoot "tools\ReadDualSenseHidInput.cs"
$OutputRoot = Join-Path $RepoRoot "work\tools\v5_5_dualsense_input"
$Reader = Join-Path $OutputRoot "ReadDualSenseHidInput.exe"
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path -LiteralPath $Source)) {
    throw "Missing source: tools/ReadDualSenseHidInput.cs"
}
if (!(Test-Path -LiteralPath $Csc)) {
    throw "C# compiler not found: $Csc"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$needsBuild = !(Test-Path -LiteralPath $Reader)
if (!$needsBuild) {
    $needsBuild = (Get-Item $Source).LastWriteTimeUtc -gt
        (Get-Item $Reader).LastWriteTimeUtc
}

if ($needsBuild) {
    Write-Output "[V5_5_DS5_REPORT] build_reader=true"
    & $Csc /nologo /optimize+ /out:$Reader $Source
    if ($LASTEXITCODE -ne 0) {
        throw "ReadDualSenseHidInput.cs compilation failed: $LASTEXITCODE"
    }
} else {
    Write-Output "[V5_5_DS5_REPORT] build_reader=false"
}

Write-Output "[V5_5_DS5_REPORT] seconds=$Seconds"
& $Reader $Seconds
exit $LASTEXITCODE
