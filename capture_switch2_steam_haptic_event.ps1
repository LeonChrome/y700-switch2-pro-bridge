param(
    [string]$AdbPath = "",
    [string]$DeviceSerial = "adb-HA2F83JF-d8q2TM._adb-tls-connect._tcp",
    [int]$Seconds = 12,
    [switch]$RichCycle,
    [switch]$NormalMode,
    [switch]$LogOnly
)

$ErrorActionPreference = "Stop"

function Resolve-AdbPath {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) { return (Resolve-Path $Path).Path }
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($sdk in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA "Android\Sdk"))) {
        if (!$sdk) { continue }
        $candidate = Join-Path $sdk "platform-tools\adb.exe"
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path $candidate).Path }
    }
    throw "Missing adb. Add adb to PATH, set ANDROID_HOME/ANDROID_SDK_ROOT, or pass -AdbPath."
}

$AdbPath = Resolve-AdbPath $AdbPath

function Invoke-AdbShell {
    param([string]$Command, [switch]$Root)

    if ($Root) {
        & $AdbPath -s $DeviceSerial shell "su -c '$Command'"
    } else {
        & $AdbPath -s $DeviceSerial shell $Command
    }
    if ($LASTEXITCODE -ne 0) {
        throw "adb shell exited with code $LASTEXITCODE"
    }
}

function Get-AdbShellText {
    param([string]$Command, [switch]$Root)

    if ($Root) {
        $text = & $AdbPath -s $DeviceSerial shell "su -c '$Command'"
    } else {
        $text = & $AdbPath -s $DeviceSerial shell $Command
    }
    if ($LASTEXITCODE -ne 0) {
        throw "adb shell exited with code $LASTEXITCODE"
    }
    return $text
}

function Select-AfterMarker {
    param([string[]]$Lines, [string]$MarkerText)

    $start = -1
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -like "*$MarkerText*") {
            $start = $i
        }
    }
    if ($start -lt 0) {
        return $Lines
    }
    if ($start + 1 -ge $Lines.Count) {
        return @()
    }
    return $Lines[($start + 1)..($Lines.Count - 1)]
}

$modeCount = 0
if ($RichCycle) { $modeCount++ }
if ($NormalMode) { $modeCount++ }
if ($LogOnly) { $modeCount++ }
if ($modeCount -gt 1) {
    throw "Choose only one of -RichCycle, -NormalMode, or -LogOnly."
}

if ($RichCycle) {
    Invoke-AdbShell "rm -f /data/local/tmp/switch2_haptic_log_only; touch /data/local/tmp/switch2_haptic_cycle_rich" -Root
    Write-Host "Rich haptic cycle enabled: 1 -> 2 -> 5 -> 4 -> 6 -> 7"
} elseif ($NormalMode) {
    Invoke-AdbShell "rm -f /data/local/tmp/switch2_haptic_cycle_rich /data/local/tmp/switch2_haptic_log_only" -Root
    Write-Host "Rich haptic cycle disabled; normal timing mapper enabled."
} elseif ($LogOnly) {
    Invoke-AdbShell "rm -f /data/local/tmp/switch2_haptic_cycle_rich; touch /data/local/tmp/switch2_haptic_log_only; printf 'cmd 0a910102000800000000000000000000\n' > /data/local/tmp/switch2_ble_write.txt" -Root
    Write-Host "LogOnly haptic capture enabled: Steam HID OUT is logged, non-stop BLE presets are suppressed."
}

$marker = "=== haptic-capture $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="
Invoke-AdbShell "echo '$marker' >> /data/local/tmp/switch2_ffs_responder.log; echo '$marker' >> /data/local/tmp/switch2_ble_bridge.log" -Root

Write-Host ""
Write-Host "Now trigger the Steam rumble source once or twice: Steam settings, BzzzController, or a game."
Write-Host "Capturing for $Seconds seconds..."
Start-Sleep -Seconds $Seconds

Write-Host ""
Write-Host "=== Responder haptic lines ==="
$responderLines = Get-AdbShellText "tail -n 320 /data/local/tmp/switch2_ffs_responder.log 2>/dev/null" -Root
$responderNewLines = Select-AfterMarker $responderLines $marker
$responderNewLines |
    Select-String -Pattern "haptic-capture|HID OUT|HID rumble|rumble bridge|bulk OUT|bulk rumble|bulk-led" |
    Select-Object -Last 120 |
    ForEach-Object { $_.Line }

Write-Host ""
Write-Host "=== Unique decoded rumble frames ==="
$responderNewLines |
    Select-String -Pattern "decoded=" |
    ForEach-Object {
        $_.Line -replace '^.*decoded=', ''
    } |
    Sort-Object -Unique |
    Select-Object -First 40

Write-Host ""
Write-Host "=== BLE write / ACK lines ==="
$bleLines = Get-AdbShellText "tail -n 320 /data/local/tmp/switch2_ble_bridge.log 2>/dev/null" -Root
$bleNewLines = Select-AfterMarker $bleLines $marker
$bleNewLines |
    Select-String -Pattern "haptic-capture|BLE write|write-file|ack n=|cmd " |
    Select-Object -Last 120 |
    ForEach-Object { $_.Line }
