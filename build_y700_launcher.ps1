$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Src = Join-Path $Root "tools\Y700Switch2Launcher.cs"
$Out = Join-Path $Root "Y700Switch2Launcher.exe"
$ReleaseDir = Join-Path $Root "release\v3-stable-20260525-input-rumble"
$ReleaseOut = Join-Path $ReleaseDir "Y700Switch2Launcher.exe"
$Csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path -LiteralPath $Csc)) { throw "Missing csc: $Csc" }
if (!(Test-Path -LiteralPath $Src)) { throw "Missing source: $Src" }

& $Csc /nologo /optimize+ /target:exe /platform:anycpu /out:$Out $Src
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }

if (Test-Path -LiteralPath $ReleaseDir) {
    Copy-Item -LiteralPath $Out -Destination $ReleaseOut -Force
}

Write-Host "Built $Out"
if (Test-Path -LiteralPath $ReleaseOut) {
    Write-Host "Copied $ReleaseOut"
}
