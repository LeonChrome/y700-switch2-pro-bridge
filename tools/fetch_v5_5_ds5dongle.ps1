param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$RepositoryUrl = "https://github.com/awalol/DS5Dongle.git",
    [switch]$Refresh
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$target = Join-Path $ProjectRoot "research\upstream\DS5Dongle"

function Write-FetchLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) { $Value = $Value.ToString().ToLowerInvariant() }
    if ($null -eq $Value) { $Value = "not_found" }
    if ($Value -is [string] -and $Value -eq "") { $Value = "not_found" }
    Write-Output "[V5_5_DS5_FETCH] $Key=$Value"
}

Write-FetchLine "repository" $RepositoryUrl
Write-FetchLine "target" $target

try {
    if (Test-Path (Join-Path $target ".git")) {
        Write-FetchLine "existing" $true
        if ($Refresh) {
            $oldPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            & git -C $target fetch --all --prune 2>&1 | ForEach-Object { Write-Output "[V5_5_DS5_FETCH] git=$_" }
            $ErrorActionPreference = $oldPreference
            if ($LASTEXITCODE -ne 0) {
                Write-FetchLine "refresh" "blocked"
                Write-FetchLine "reason" "git_fetch_failed"
            } else {
                Write-FetchLine "refresh" "passed"
            }
        }
    } else {
        New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
        $oldPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & git clone --depth 1 $RepositoryUrl $target 2>&1 | ForEach-Object { Write-Output "[V5_5_DS5_FETCH] git=$_" }
        $ErrorActionPreference = $oldPreference
        if ($LASTEXITCODE -ne 0) {
            Write-FetchLine "cloned" $false
            Write-FetchLine "blocked" $true
            Write-FetchLine "reason" "network_or_git_clone_failed"
            exit 0
        }
        Write-FetchLine "cloned" $true
    }

    $commit = (& git -C $target rev-parse HEAD 2>$null)
    $branch = (& git -C $target branch --show-current 2>$null)
    if (!$branch) {
        $branch = (& git -C $target rev-parse --abbrev-ref HEAD 2>$null)
    }
    $licenseFiles = @(Get-ChildItem $target -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -match "^(LICENSE|COPYING|NOTICE)"
    } | Select-Object -ExpandProperty Name)

    Write-FetchLine "available" $true
    Write-FetchLine "commit" $commit
    Write-FetchLine "branch" $branch
    Write-FetchLine "license" ($(if ($licenseFiles.Count -gt 0) { $licenseFiles -join "," } else { "not_found" }))
    exit 0
} catch {
    Write-FetchLine "available" $false
    Write-FetchLine "blocked" $true
    Write-FetchLine "reason" "exception"
    Write-FetchLine "error" $_.Exception.Message
    exit 0
}
