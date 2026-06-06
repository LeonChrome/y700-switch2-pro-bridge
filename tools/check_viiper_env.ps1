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
    param([string]$UsbipExe)

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
        Exe = $UsbipExe
        Services = $services
        PnpDevices = $pnpDevices
        RootHubDevices = $rootHubDevices
        DriverDevices = $driverDevices
        ServicePresent = $services.Count -gt 0
        RootHubPresent = $rootHubDevices.Count -gt 0
        DriverPresent = $driverDevices.Count -gt 0 -or $services.Count -gt 0
        Installed = [bool]$UsbipExe -or $services.Count -gt 0 -or $driverDevices.Count -gt 0 -or $rootHubDevices.Count -gt 0
    }
}

$dotnet = Find-Executable "dotnet" @(
    (Join-Path $env:USERPROFILE ".dotnet-codex\dotnet.exe"),
    (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"),
    (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
)
$git = Find-Executable "git" @((Join-Path $env:ProgramFiles "Git\bin\git.exe"))
$go = Find-Executable "go" @(
    (Join-Path $ProjectRoot "work\deps\go\bin\go.exe"),
    (Join-Path $env:ProgramFiles "Go\bin\go.exe")
)
$cmake = Find-Executable "cmake" @((Join-Path $env:ProgramFiles "CMake\bin\cmake.exe"))
$usbip = Find-Executable "usbip" @(
    (Join-Path $env:ProgramFiles "usbip-win2\usbip.exe"),
    (Join-Path $env:ProgramFiles "USBIP\usbip.exe")
)
$viiper = Find-Executable "viiper" @(
    (Join-Path $ProjectRoot "work\tools\viiper\viiper.exe"),
    (Join-Path $ProjectRoot "tools\viiper\viiper.exe"),
    (Join-Path $env:LOCALAPPDATA "VIIPER\viiper.exe")
)

$os = Get-CimInstance Win32_OperatingSystem
$admin = Test-Admin
$usbipStatus = Get-UsbipWin2Status $usbip
$steam = Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 1
$githubOk = $false
try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/Alia5/VIIPER/releases/latest" -MaximumRedirection 5 -TimeoutSec 20
    $githubOk = $resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400
} catch {
    $githubOk = $false
}

$nextCommand = if (!$usbipStatus.Installed) {
    if ($admin) {
        "powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install"
    } else {
        "powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_usbip_win2.ps1 -Install -Elevate"
    }
} elseif ($viiper -or ($go -and (Test-Path (Join-Path $ProjectRoot "work\upstream-research\VIIPER\go.mod")))) {
    "powershell -NoProfile -ExecutionPolicy Bypass -File .\experiments\viiper_ns2pro_probe\run_viiper_ns2pro_probe.ps1"
} else {
    "powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install_viiper.ps1"
}

Write-EnvLine "windows" ("{0} {1} build={2} arch={3}" -f $os.Caption, $os.Version, $os.BuildNumber, $os.OSArchitecture)
Write-EnvLine "dotnet" ($(if ($dotnet) { & $dotnet --version } else { "not_found" }))
Write-EnvLine "git" ($(if ($git) { & $git --version } else { "not_found" }))
Write-EnvLine "go" ($(if ($go) { & $go version } else { "not_found" }))
Write-EnvLine "cmake" ($(if ($cmake) { (& $cmake --version | Select-Object -First 1) } else { "not_found" }))
Write-EnvLine "usbip_win2" ($(if ($usbipStatus.Installed) { "installed" } else { "not_found" }))
Write-EnvLine "usbip_exe" $usbipStatus.Exe
Write-EnvLine "usbip_service" (Format-Names $usbipStatus.Services { param($s) "$($s.Name):$($s.Status)" })
Write-EnvLine "usbip_driver" (Format-Names $usbipStatus.DriverDevices { param($d) "$($d.FriendlyName):$($d.Status)" })
Write-EnvLine "usbip_root_hub" (Format-Names $usbipStatus.RootHubDevices { param($d) "$($d.FriendlyName):$($d.Status)" })
Write-EnvLine "viiper" ($(if ($viiper) { $viiper } else { "not_found" }))
Write-EnvLine "admin" $admin
Write-EnvLine "steam" ($(if ($steam) { "running pid=$($steam.Id)" } else { "not_running" }))
Write-EnvLine "project" $ProjectRoot
Write-EnvLine "github_access" $githubOk
Write-EnvLine "viiper_source" ($(if (Test-Path (Join-Path $ProjectRoot "work\upstream-research\VIIPER\go.mod")) { "present" } else { "not_found" }))
Write-EnvLine "viiper_build_possible" ($(if ($go -and (Test-Path (Join-Path $ProjectRoot "work\upstream-research\VIIPER\go.mod"))) { "true" } else { "false" }))
Write-EnvLine "next" $nextCommand
