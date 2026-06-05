param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ManualAssetUrl = "",
    [switch]$Install,
    [switch]$Elevate
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$OutDir = Join-Path $ProjectRoot "work\deps\usbip-win2"
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Test-Admin {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-Usbip {
    $cmd = Get-Command usbip -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @("C:\Program Files\usbip-win2\usbip.exe", "C:\Program Files\USBIP\usbip.exe")) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

function Find-UsbipReleaseAsset {
    param([string[]]$Repos)

    $headers = @{ "User-Agent" = "v52-usbip-installer" }
    foreach ($repo in $Repos) {
        foreach ($endpoint in @("releases/latest", "releases?per_page=20")) {
            $uri = "https://api.github.com/repos/$repo/$endpoint"
            try {
                $releaseResult = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 30
                $releases = @($releaseResult)
                foreach ($release in $releases) {
                    $assets = @($release.assets | Where-Object { $_.name -match "\.(zip|msi)$" })
                    if ($assets.Count -eq 0) {
                        continue
                    }
                    $asset = $assets |
                        Sort-Object @{ Expression = { if ($_.name -match "(?i)(x64|amd64|win64|installer|setup|vhci|usbip)") { 0 } else { 1 } } }, name |
                        Select-Object -First 1
                    Write-Host "[USBIP_WIN2] release_source=$repo tag=$($release.tag_name) asset=$($asset.name)"
                    return $asset.browser_download_url
                }
            } catch {
                Write-Host "[USBIP_WIN2] release_probe_failed repo=$repo endpoint=$endpoint error=$($_.Exception.Message)"
            }
        }
    }
    return $null
}

$existing = Find-Usbip
if ($existing) {
    Write-Output "[USBIP_WIN2] already_installed exe=$existing"
    exit 0
}

if ($Install -and !(Test-Admin)) {
    if ($Elevate) {
        $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-ProjectRoot", "`"$ProjectRoot`"", "-Install")
        if ($ManualAssetUrl) { $args += @("-ManualAssetUrl", "`"$ManualAssetUrl`"") }
        Write-Output "[USBIP_WIN2] requesting elevation"
        Start-Process powershell -ArgumentList $args -Verb RunAs
        exit 0
    }
    Write-Output "[USBIP_WIN2] blocked: administrator rights are required for driver installation. Re-run with -Install -Elevate."
    exit 2
}

$assetUrl = $ManualAssetUrl
if (!$assetUrl) {
    $assetUrl = Find-UsbipReleaseAsset @(
        "vadimgrn/usbip-win2",
        "OSSign/vadimgrn--usbip-win2"
    )
}

if (!$assetUrl) {
    Write-Output "[USBIP_WIN2] blocked: GitHub API did not expose a downloadable zip/msi asset in this environment."
    Write-Output "[USBIP_WIN2] download manually from https://github.com/vadimgrn/usbip-win2/releases or https://github.com/OSSign/vadimgrn--usbip-win2/releases, then rerun with -ManualAssetUrl <url>."
    exit 3
}

$fileName = Split-Path $assetUrl -Leaf
$download = Join-Path $OutDir $fileName
Write-Output "[USBIP_WIN2] downloading $assetUrl"
Invoke-WebRequest -UseBasicParsing -Uri $assetUrl -OutFile $download

if ($download -match "\.msi$") {
    if (!$Install) {
        Write-Output "[USBIP_WIN2] downloaded=$download"
        Write-Output "[USBIP_WIN2] not installing because -Install was not specified"
        exit 0
    }
    Start-Process msiexec.exe -ArgumentList @("/i", "`"$download`"", "/passive") -Wait
} elseif ($download -match "\.zip$") {
    $extract = Join-Path $OutDir "extracted"
    if (Test-Path $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
    Expand-Archive -Path $download -DestinationPath $extract -Force
    $infFiles = @(Get-ChildItem -Path $extract -Recurse -Filter *.inf)
    if (!$Install) {
        Write-Output "[USBIP_WIN2] downloaded=$download"
        Write-Output "[USBIP_WIN2] extracted=$extract"
        Write-Output "[USBIP_WIN2] not installing because -Install was not specified"
        exit 0
    }
    if ($infFiles.Count -eq 0) {
        Write-Output "[USBIP_WIN2] blocked: no INF files found after extraction"
        exit 4
    }
    foreach ($inf in $infFiles) {
        Write-Output "[USBIP_WIN2] installing driver $($inf.FullName)"
        pnputil /add-driver $inf.FullName /install
    }
}

$installed = Find-Usbip
if ($installed) {
    Write-Output "[USBIP_WIN2] installed exe=$installed"
} else {
    Write-Output "[USBIP_WIN2] install attempted; usbip.exe not found on PATH. A reboot may be required."
}
