param(
    [string]$AdbPath = "adb",
    [string]$DeviceSerial = "",
    [string]$Targets = "cmd cc48 3dac 4147 649d fdf"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Script = Join-Path $Root "test_switch2_ble_haptics.sh"

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
    if ($DeviceSerial) {
        & $AdbPath -s $DeviceSerial @Args
    } else {
        & $AdbPath @Args
    }
    if ($LASTEXITCODE -ne 0) {
        throw "adb exited with code $LASTEXITCODE"
    }
}

Invoke-Adb push $Script /data/local/tmp/test_switch2_ble_haptics.sh
Invoke-Adb shell su -c "chmod 755 /data/local/tmp/test_switch2_ble_haptics.sh"
Invoke-Adb shell su -c "sh /data/local/tmp/test_switch2_ble_haptics.sh $Targets"
