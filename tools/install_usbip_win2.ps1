param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ManualAssetUrl = "",
    [string]$InstallerPath = "",
    [switch]$Install,
    [switch]$Elevate
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$OutDir = Join-Path $ProjectRoot "work\deps\usbip-win2"
$ManualDownloadUrl = "https://github.com/vadimgrn/usbip-win2/releases"
$ManualFallbackUrl = "https://github.com/OSSign/vadimgrn--usbip-win2/releases"
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Write-UsbipLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value -or $Value -eq "") {
        $Value = "not_found"
    }
    Write-Output "[USBIP_WIN2] $Key=$Value"
}

function Test-Admin {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-Usbip {
    $cmd = Get-Command usbip -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @(
        (Join-Path $env:ProgramFiles "usbip-win2\usbip.exe"),
        (Join-Path $env:ProgramFiles "USBIP\usbip.exe")
    )) {
        if (Test-Path $p) { return (Resolve-Path $p).Path }
    }
    return $null
}

function Format-Names {
    param(
        [object[]]$Items,
        [scriptblock]$Selector
    )

    $names = @($Items | ForEach-Object { & $Selector $_ } | Where-Object { $_ } | Sort-Object -Unique)
    if ($names.Count -eq 0) {
        return "not_found"
    }
    return ($names -join "; ")
}

function Get-UsbipWin2Status {
    $exe = Find-Usbip
    $services = @(Get-Service -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -match "(?i)usbip|vhci" -or $_.DisplayName -match "(?i)usbip|USB/IP|VHCI"
    })

    $pnpDevices = @()
    try {
        $pnpDevices = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
            $_.FriendlyName -match "(?i)usbip|USB/IP|VHCI" -or
            $_.InstanceId -match "(?i)USBIP|VHCI|VID_.*PID_.*USBIP"
        })
    } catch {
        $pnpDevices = @()
    }

    $rootHubDevices = @($pnpDevices | Where-Object {
        $_.FriendlyName -match "(?i)root hub|USB/IP|VHCI" -or
        $_.InstanceId -match "(?i)ROOT|USBIP|VHCI"
    })

    $driverDevices = @($pnpDevices | Where-Object {
        $_.FriendlyName -match "(?i)usbip|USB/IP|VHCI" -or
        $_.Service -match "(?i)usbip|vhci" -or
        $_.InstanceId -match "(?i)USBIP|VHCI"
    })

    [pscustomobject]@{
        Exe = $exe
        Services = $services
        DriverDevices = $driverDevices
        RootHubDevices = $rootHubDevices
        ServicePresent = $services.Count -gt 0
        DriverPresent = $driverDevices.Count -gt 0 -or $services.Count -gt 0
        RootHubPresent = $rootHubDevices.Count -gt 0
        Installed = [bool]$exe -or $services.Count -gt 0 -or $driverDevices.Count -gt 0 -or $rootHubDevices.Count -gt 0
    }
}

function Write-UsbipVerification {
    param([object]$Status)

    Write-UsbipLine "verify_usbip_exe" $Status.Exe
    Write-UsbipLine "verify_service" (Format-Names $Status.Services { param($s) "$($s.Name):$($s.Status)" })
    Write-UsbipLine "verify_driver" (Format-Names $Status.DriverDevices { param($d) "$($d.FriendlyName):$($d.Status)" })
    Write-UsbipLine "verify_root_hub" (Format-Names $Status.RootHubDevices { param($d) "$($d.FriendlyName):$($d.Status)" })
    Write-UsbipLine "verify_installed" $Status.Installed
}

function Show-ManualInstallInstructions {
    Write-UsbipLine "manual_download" $ManualDownloadUrl
    Write-UsbipLine "manual_fallback_download" $ManualFallbackUrl
    Write-UsbipLine "manual_asset" "latest Windows x64/amd64 installer asset: usually USBip-<version>-x64-Release.exe; .msi or .zip also supported"
    Write-UsbipLine "manual_save_to" ".\work\deps\usbip-win2\<asset-file>"
    Write-UsbipLine "manual_install_command" "powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate -InstallerPath .\work\deps\usbip-win2\<asset-file>"
}

function Quote-Argument {
    param([string]$Value)
    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }
    return $Value
}

function Format-RepoPath {
    param([string]$Path)

    if (!$Path) {
        return ""
    }

    $fullPath = $Path
    if (Test-Path $Path) {
        $fullPath = (Resolve-Path $Path).Path
    }

    if ($fullPath.StartsWith($ProjectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return ".\" + $fullPath.Substring($ProjectRoot.Length).TrimStart("\", "/")
    }
    return $fullPath
}

function Select-UsbipAsset {
    param([object[]]$Assets)

    $candidateAssets = @($Assets | Where-Object { $_.name -match "\.(exe|zip|msi)$" })
    if ($candidateAssets.Count -eq 0) {
        return $null
    }

    return $candidateAssets |
        Sort-Object `
            @{ Expression = { if ($_.name -match "(?i)(x64|amd64|win64)") { 0 } else { 1 } } }, `
            @{ Expression = { if ($_.name -match "(?i)(release|installer|setup|vhci|usbip|signed)") { 0 } else { 1 } } }, `
            @{ Expression = { if ($_.name -match "(?i)\.exe$") { 0 } elseif ($_.name -match "(?i)\.msi$") { 1 } else { 2 } } }, `
            name |
        Select-Object -First 1
}

function Find-UsbipReleaseAssetFromHtml {
    param([string]$Repo)

    try {
        $latestUri = "https://github.com/$Repo/releases/latest"
        $latest = Invoke-WebRequest -UseBasicParsing -Uri $latestUri -MaximumRedirection 5 -TimeoutSec 30
        $tag = $null
        if ($latest.BaseResponse -and $latest.BaseResponse.ResponseUri) {
            $tag = Split-Path $latest.BaseResponse.ResponseUri.AbsolutePath -Leaf
        }
        if (!$tag) {
            $tagMatch = [regex]::Match($latest.Content, "/$([regex]::Escape($Repo))/releases/tag/([^`"?#]+)")
            if ($tagMatch.Success) {
                $tag = $tagMatch.Groups[1].Value
            }
        }
        if (!$tag) {
            Write-UsbipLine "html_release_probe_failed" "$Repo tag not found"
            return $null
        }

        $expandedUri = "https://github.com/$Repo/releases/expanded_assets/$tag"
        $expanded = Invoke-WebRequest -UseBasicParsing -Uri $expandedUri -TimeoutSec 30
        $matches = [regex]::Matches($expanded.Content, 'href="([^"]+/releases/download/[^"]+\.(?:exe|zip|msi))"')
        $assets = @()
        foreach ($match in $matches) {
            $href = $match.Groups[1].Value
            if ($href.StartsWith("/")) {
                $href = "https://github.com$href"
            }
            $assets += [pscustomobject]@{
                name = Split-Path $href -Leaf
                browser_download_url = $href
            }
        }
        $asset = Select-UsbipAsset $assets
        if ($asset) {
            Write-UsbipLine "release_source" "$Repo tag=$tag asset=$($asset.name) source=html"
            return $asset.browser_download_url
        }
    } catch {
        Write-UsbipLine "html_release_probe_failed" "$Repo error=$($_.Exception.Message)"
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
                    $asset = Select-UsbipAsset @($release.assets)
                    if (!$asset) {
                        continue
                    }
                    Write-UsbipLine "release_source" "$repo tag=$($release.tag_name) asset=$($asset.name)"
                    return $asset.browser_download_url
                }
            } catch {
                $statusCode = $null
                try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { $statusCode = $null }
                if ($statusCode -eq 403) {
                    Write-UsbipLine "github_api_403" "$repo/$endpoint"
                } else {
                    Write-UsbipLine "release_probe_failed" "$repo/$endpoint error=$($_.Exception.Message)"
                }
            }
        }

        $htmlAsset = Find-UsbipReleaseAssetFromHtml $repo
        if ($htmlAsset) {
            return $htmlAsset
        }
    }
    return $null
}

function Resolve-InstallerPath {
    param([string]$Path)

    if (!$Path) {
        return ""
    }
    if (!(Test-Path $Path)) {
        throw "InstallerPath not found: $Path"
    }
    return (Resolve-Path $Path).Path
}

function Save-RemoteAsset {
    param([string]$AssetUrl)

    $fileName = Split-Path $AssetUrl -Leaf
    if (!$fileName -or $fileName -eq "") {
        $fileName = "usbip-win2-installer"
    }
    $download = Join-Path $OutDir $fileName
    Write-UsbipLine "downloading" $AssetUrl
    Invoke-WebRequest -UseBasicParsing -Uri $AssetUrl -OutFile $download
    return $download
}

function Invoke-MsiInstall {
    param([string]$Path)

    Write-UsbipLine "install_msi" $Path
    $proc = Start-Process msiexec.exe -ArgumentList @("/i", "`"$Path`"", "/passive", "/norestart") -Wait -PassThru
    Write-UsbipLine "msi_exit_code" $proc.ExitCode
    return $proc.ExitCode
}

function Invoke-ExeInstall {
    param([string]$Path)

    Write-UsbipLine "install_exe" $Path
    $proc = Start-Process -FilePath $Path -ArgumentList @("/VERYSILENT", "/NORESTART") -Wait -PassThru
    Write-UsbipLine "exe_exit_code" $proc.ExitCode
    return $proc.ExitCode
}

function Invoke-InfInstall {
    param([string[]]$InfPaths)

    foreach ($infPath in $InfPaths) {
        Write-UsbipLine "install_inf" $infPath
        pnputil /add-driver $infPath /install
    }
}

function Invoke-UsbipPackageInstall {
    param([string]$PackagePath)

    $extension = [System.IO.Path]::GetExtension($PackagePath).ToLowerInvariant()
    if ($extension -eq ".msi") {
        Invoke-MsiInstall $PackagePath | Out-Null
        return
    }

    if ($extension -eq ".inf") {
        Invoke-InfInstall @($PackagePath)
        return
    }

    if ($extension -eq ".zip") {
        $extract = Join-Path $OutDir "extracted"
        if (Test-Path $extract) {
            Remove-Item -LiteralPath $extract -Recurse -Force
        }
        Expand-Archive -Path $PackagePath -DestinationPath $extract -Force
        Write-UsbipLine "extracted" $extract

        $msiFiles = @(Get-ChildItem -Path $extract -Recurse -Filter *.msi)
        if ($msiFiles.Count -gt 0) {
            $msi = $msiFiles | Sort-Object FullName | Select-Object -First 1
            Invoke-MsiInstall $msi.FullName | Out-Null
            return
        }

        $infFiles = @(Get-ChildItem -Path $extract -Recurse -Filter *.inf)
        if ($infFiles.Count -eq 0) {
            throw "No MSI or INF files found after extracting $PackagePath"
        }
        Invoke-InfInstall @($infFiles.FullName)
        return
    }

    if ($extension -eq ".exe") {
        Invoke-ExeInstall $PackagePath | Out-Null
        return
    }

    throw "Unsupported installer type: $PackagePath"
}

Write-UsbipLine "project" $ProjectRoot
Write-UsbipLine "download_dir" ".\work\deps\usbip-win2"
Write-UsbipLine "v51_impact" "none"

$initialStatus = Get-UsbipWin2Status
Write-UsbipVerification $initialStatus

if ($initialStatus.Installed) {
    Write-UsbipLine "already_installed" "true"
    exit 0
}

if (!$Install) {
    Write-UsbipLine "install" "not_requested"
    Show-ManualInstallInstructions
    exit 0
}

if ($InstallerPath) {
    $InstallerPath = Resolve-InstallerPath $InstallerPath
}

if (!(Test-Admin)) {
    if ($Elevate) {
        $args = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", (Quote-Argument $PSCommandPath),
            "-ProjectRoot", (Quote-Argument $ProjectRoot),
            "-Install"
        )
        if ($ManualAssetUrl) { $args += @("-ManualAssetUrl", (Quote-Argument $ManualAssetUrl)) }
        if ($InstallerPath) { $args += @("-InstallerPath", (Quote-Argument $InstallerPath)) }

        Write-UsbipLine "requesting_elevation" "true"
        Start-Process powershell.exe -ArgumentList $args -Verb RunAs -Wait
        $afterElevation = Get-UsbipWin2Status
        Write-UsbipVerification $afterElevation
        if ($afterElevation.Installed) {
            exit 0
        }
        Write-UsbipLine "blocked" "administrator install did not complete or verification still failed"
        Show-ManualInstallInstructions
        exit 5
    }

    $nextCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate"
    if ($InstallerPath) {
        $nextCommand += " -InstallerPath $(Format-RepoPath $InstallerPath)"
    }
    Write-UsbipLine "blocked" "administrator rights are required for driver installation"
    Write-UsbipLine "next" $nextCommand
    exit 2
}

$packagePath = $InstallerPath
if (!$packagePath) {
    $assetUrl = $ManualAssetUrl
    if (!$assetUrl) {
        $assetUrl = Find-UsbipReleaseAsset @(
            "vadimgrn/usbip-win2",
            "OSSign/vadimgrn--usbip-win2"
        )
    }

    if (!$assetUrl) {
        Write-UsbipLine "blocked" "GitHub API did not expose a downloadable exe/zip/msi asset in this environment"
        Show-ManualInstallInstructions
        exit 3
    }

    $packagePath = Save-RemoteAsset $assetUrl
}

if (!$Install) {
    Write-UsbipLine "downloaded" $packagePath
    exit 0
}

Invoke-UsbipPackageInstall $packagePath

$finalStatus = Get-UsbipWin2Status
Write-UsbipVerification $finalStatus
if ($finalStatus.Installed) {
    Write-UsbipLine "installed" "true"
    exit 0
}

Write-UsbipLine "blocked" "install attempted but usbip-win2 was not detected; reboot may be required"
Show-ManualInstallInstructions
exit 7
