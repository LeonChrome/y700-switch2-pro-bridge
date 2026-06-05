param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Find-Executable {
    param(
        [string]$Name,
        [string[]]$Candidates = @()
    )

    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }
    return $null
}

function Test-Admin {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-EnvLine {
    param([string]$Key, [object]$Value)
    if ($Value -is [bool]) {
        $Value = $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value -or $Value -eq "") {
        $Value = "not_found"
    }
    Write-Output "[VIIPER_ENV] $Key=$Value"
}

$dotnet = Find-Executable "dotnet" @(
    (Join-Path $env:USERPROFILE ".dotnet-codex\dotnet.exe"),
    (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"),
    "C:\Program Files\dotnet\dotnet.exe",
    "C:\Program Files (x86)\dotnet\dotnet.exe"
)
$git = Find-Executable "git" @("C:\Program Files\Git\bin\git.exe")
$go = Find-Executable "go" @(
    (Join-Path $ProjectRoot "work\deps\go\bin\go.exe"),
    "C:\Program Files\Go\bin\go.exe"
)
$cmake = Find-Executable "cmake" @("C:\Program Files\CMake\bin\cmake.exe")
$usbip = Find-Executable "usbip" @(
    "C:\Program Files\usbip-win2\usbip.exe",
    "C:\Program Files\USBIP\usbip.exe"
)
$viiper = Find-Executable "viiper" @(
    (Join-Path $ProjectRoot "work\tools\viiper\viiper.exe"),
    (Join-Path $ProjectRoot "tools\viiper\viiper.exe"),
    (Join-Path $env:LOCALAPPDATA "VIIPER\viiper.exe")
)

$os = Get-CimInstance Win32_OperatingSystem
$usbipServices = @(Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "usbip|vhci" -or $_.DisplayName -match "usbip|USB/IP|VHCI" })
$usbipPnp = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -match "usbip|USB/IP|VHCI" -or $_.InstanceId -match "USBIP|VHCI" })
$usbipInstalled = [bool]$usbip -or $usbipServices.Count -gt 0 -or $usbipPnp.Count -gt 0
$steam = Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 1
$githubOk = $false
try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/Alia5/VIIPER/releases/latest" -MaximumRedirection 5 -TimeoutSec 20
    $githubOk = $resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400
} catch {
    $githubOk = $false
}

Write-EnvLine "windows" ("{0} {1} build={2} arch={3}" -f $os.Caption, $os.Version, $os.BuildNumber, $os.OSArchitecture)
Write-EnvLine "dotnet" ($(if ($dotnet) { & $dotnet --version } else { "not_found" }))
Write-EnvLine "git" ($(if ($git) { & $git --version } else { "not_found" }))
Write-EnvLine "go" ($(if ($go) { & $go version } else { "not_found" }))
Write-EnvLine "cmake" ($(if ($cmake) { (& $cmake --version | Select-Object -First 1) } else { "not_found" }))
Write-EnvLine "usbip_win2" ($(if ($usbipInstalled) { "installed" } else { "not_found" }))
Write-EnvLine "usbip_exe" $usbip
Write-EnvLine "viiper" ($(if ($viiper) { $viiper } else { "not_found" }))
Write-EnvLine "admin" (Test-Admin)
Write-EnvLine "steam" ($(if ($steam) { "running pid=$($steam.Id)" } else { "not_running" }))
Write-EnvLine "project" $ProjectRoot
Write-EnvLine "github_access" $githubOk
Write-EnvLine "viiper_source" ($(if (Test-Path (Join-Path $ProjectRoot "work\upstream-research\VIIPER\go.mod")) { "present" } else { "not_found" }))
Write-EnvLine "viiper_build_possible" ($(if ($go -and (Test-Path (Join-Path $ProjectRoot "work\upstream-research\VIIPER\go.mod"))) { "true" } else { "false" }))
