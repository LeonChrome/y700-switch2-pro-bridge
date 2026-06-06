param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$Sdl3Path = "",
    [switch]$All,
    [UInt16]$Low = 65535,
    [UInt16]$High = 65535,
    [UInt32]$DurationMs = 800,
    [switch]$NoEffect,
    [string]$EffectHex = "",
    [switch]$NoDownload
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Write-SdlRuntimeLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value -or $Value -eq "") {
        $Value = "not_found"
    }
    Write-Host "[SDL_RUNTIME] $Key=$Value"
}

function Find-Dotnet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if ($dotnet) {
        return $dotnet
    }
    $candidate = Join-Path $env:USERPROFILE ".dotnet-codex\dotnet.exe"
    if (Test-Path $candidate) {
        return $candidate
    }
    return $null
}

function Find-Sdl3Dll {
    param(
        [string]$ExplicitPath,
        [string]$OutputDir
    )

    $candidates = @(
        $ExplicitPath,
        $env:SDL3_DLL,
        (Join-Path $OutputDir "SDL3.dll"),
        (Join-Path $ProjectRoot "work\deps\sdl3\SDL3.dll")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    $depRoot = Join-Path $ProjectRoot "work\deps\sdl3"
    if (Test-Path $depRoot) {
        $found = Get-ChildItem -Path $depRoot -Recurse -Filter SDL3.dll -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }

    return $null
}

function Select-SdlAsset {
    param([object[]]$Assets)

    $candidateAssets = @($Assets | Where-Object { $_.name -match "(?i)^SDL3-.*win32-x64.*\.zip$" })
    if ($candidateAssets.Count -eq 0) {
        $candidateAssets = @($Assets | Where-Object { $_.name -match "(?i)SDL3.*x64.*\.zip$" })
    }
    if ($candidateAssets.Count -eq 0) {
        return $null
    }
    return $candidateAssets | Sort-Object name -Descending | Select-Object -First 1
}

function Find-SdlReleaseAsset {
    $headers = @{ "User-Agent" = "v52-sdl-runtime" }
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/libsdl-org/SDL/releases/latest" -Headers $headers -TimeoutSec 30
        $asset = Select-SdlAsset @($release.assets)
        if ($asset) {
            Write-SdlRuntimeLine "release_source" "api tag=$($release.tag_name) asset=$($asset.name)"
            return $asset.browser_download_url
        }
    } catch {
        $statusCode = $null
        try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { $statusCode = $null }
        if ($statusCode -eq 403) {
            Write-SdlRuntimeLine "github_api_403" "libsdl-org/SDL/releases/latest"
        } else {
            Write-SdlRuntimeLine "release_probe_failed" $_.Exception.Message
        }
    }

    try {
        $latest = Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/libsdl-org/SDL/releases/latest" -MaximumRedirection 5 -TimeoutSec 30
        $tag = $null
        if ($latest.BaseResponse -and $latest.BaseResponse.ResponseUri) {
            $tag = Split-Path $latest.BaseResponse.ResponseUri.AbsolutePath -Leaf
        }
        if (!$tag) {
            throw "latest release tag not found"
        }

        $expanded = Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/libsdl-org/SDL/releases/expanded_assets/$tag" -TimeoutSec 30
        $matches = [regex]::Matches($expanded.Content, 'href="([^"]*SDL3-[^"]*win32-x64\.zip)"')
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
        $asset = Select-SdlAsset $assets
        if ($asset) {
            Write-SdlRuntimeLine "release_source" "html tag=$tag asset=$($asset.name)"
            return $asset.browser_download_url
        }
    } catch {
        Write-SdlRuntimeLine "html_release_probe_failed" $_.Exception.Message
    }

    return $null
}

function Install-Sdl3Runtime {
    $assetUrl = Find-SdlReleaseAsset
    if (!$assetUrl) {
        throw "SDL3 runtime download asset not found"
    }

    $root = Join-Path $ProjectRoot "work\deps\sdl3"
    $downloads = Join-Path $root "downloads"
    $extract = Join-Path $root "extracted"
    New-Item -ItemType Directory -Force $downloads | Out-Null
    if (Test-Path $extract) {
        Remove-Item -LiteralPath $extract -Recurse -Force
    }
    New-Item -ItemType Directory -Force $extract | Out-Null

    $zipPath = Join-Path $downloads (Split-Path $assetUrl -Leaf)
    Write-SdlRuntimeLine "downloading" $assetUrl
    Invoke-WebRequest -UseBasicParsing -Uri $assetUrl -OutFile $zipPath
    Write-SdlRuntimeLine "downloaded" ".\work\deps\sdl3\downloads\$(Split-Path $zipPath -Leaf)"

    Expand-Archive -Path $zipPath -DestinationPath $extract -Force
    $dll = Get-ChildItem -Path $extract -Recurse -Filter SDL3.dll | Select-Object -First 1
    if (!$dll) {
        throw "SDL3.dll not found after extracting $zipPath"
    }

    $canonical = Join-Path $root "SDL3.dll"
    Copy-Item -LiteralPath $dll.FullName -Destination $canonical -Force
    Write-SdlRuntimeLine "canonical_dll" ".\work\deps\sdl3\SDL3.dll"
    return (Resolve-Path $canonical).Path
}

$Dotnet = Find-Dotnet
if (!$Dotnet) { throw "dotnet SDK not found" }

$Project = Join-Path $PSScriptRoot "sdl_ns2pro_rumble_test.csproj"
& $Dotnet build $Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$OutputDir = Join-Path $PSScriptRoot "bin\Release\net8.0"
$ResolvedSdl3 = Find-Sdl3Dll -ExplicitPath $Sdl3Path -OutputDir $OutputDir
if (!$ResolvedSdl3 -and !$NoDownload) {
    $ResolvedSdl3 = Install-Sdl3Runtime
}
if (!$ResolvedSdl3) {
    Write-SdlRuntimeLine "blocked" "SDL3.dll not found. Pass -Sdl3Path, set SDL3_DLL, or allow download."
    exit 2
}

New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDll = Join-Path $OutputDir "SDL3.dll"
$resolvedFull = (Resolve-Path $ResolvedSdl3).Path
$outputFull = if (Test-Path $OutputDll) { (Resolve-Path $OutputDll).Path } else { $OutputDll }
if (![string]::Equals($resolvedFull, $outputFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $ResolvedSdl3 -Destination $OutputDll -Force
}
Write-SdlRuntimeLine "resolved_dll" $ResolvedSdl3
Write-SdlRuntimeLine "copied_to" ".\experiments\sdl_ns2pro_rumble_test\bin\Release\net8.0\SDL3.dll"

$ProbeArgs = @("--sdl3", $OutputDll, "--low", "$Low", "--high", "$High", "--duration-ms", "$DurationMs")
if ($All) { $ProbeArgs += "--all" }
if ($NoEffect) { $ProbeArgs += "--no-effect" }
if ($EffectHex) { $ProbeArgs += @("--effect-hex", $EffectHex) }

& $Dotnet run --project $Project -c Release --no-build -- @ProbeArgs
exit $LASTEXITCODE
