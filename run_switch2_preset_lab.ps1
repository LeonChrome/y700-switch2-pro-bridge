param(
    [int]$Port = 8787,
    [string]$AdbPath = "",
    [string]$DeviceSerial = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Server = Join-Path $Root "tools\switch2-preset-lab-server.js"

function Resolve-AdbPath {
    param([string]$RequestedPath)

    if ($RequestedPath -and (Test-Path -LiteralPath $RequestedPath)) {
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    if ($env:ADB_PATH -and (Test-Path -LiteralPath $env:ADB_PATH)) {
        return (Resolve-Path -LiteralPath $env:ADB_PATH).Path
    }

    $cmd = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $desktop = [Environment]::GetFolderPath("Desktop")
    if ($desktop -and (Test-Path -LiteralPath $desktop)) {
        $found = Get-ChildItem -LiteralPath $desktop -Filter adb.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*platform-tools*adb.exe" } |
            Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }

    if ($RequestedPath) {
        throw "ADB not found at requested path: $RequestedPath"
    }
    throw "ADB not found. Pass -AdbPath or set ADB_PATH."
}

$ResolvedAdbPath = Resolve-AdbPath $AdbPath

$argsList = @($Server, "--port", "$Port", "--adb", $ResolvedAdbPath)
if ($DeviceSerial) {
    $argsList += @("--serial", $DeviceSerial)
}

Write-Host "Starting Switch 2 preset lab..."
Write-Host "URL: http://127.0.0.1:$Port/"
Write-Host "ADB: $ResolvedAdbPath"

& node @argsList
