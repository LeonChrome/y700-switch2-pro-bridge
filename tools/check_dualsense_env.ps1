param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path

function Convert-EnvValue {
    param([object]$Value)
    if ($Value -is [bool]) {
        return $Value.ToString().ToLowerInvariant()
    }
    if ($null -eq $Value) {
        return "not_found"
    }
    if ($Value -is [string] -and $Value -eq "") {
        return "not_found"
    }
    return ($Value.ToString() -replace "[`r`n]+", " ").Trim()
}

function Write-EnvLine {
    param([string]$Key, [object]$Value)
    Write-Output "[DUALSENSE_ENV] $Key=$(Convert-EnvValue $Value)"
}

function Write-BlockedLine {
    param([string]$Reason)
    Write-Output "[DUALSENSE_BLOCKED] reason=$Reason"
}

function Test-Admin {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-DualSensePnpDevice {
    param($Device)
    $name = if ($Device.FriendlyName) { $Device.FriendlyName } elseif ($Device.Name) { $Device.Name } else { "" }
    $id = if ($Device.InstanceId) { $Device.InstanceId } else { "" }
    return $name -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive" -or
        $id -match "VID_054C&PID_0CE6|VID&0002054C_PID&0CE6|VID_054C&PID_0DF2|VID&0002054C_PID&0DF2"
}

function Get-VidPid {
    param([object[]]$Devices)

    foreach ($dev in $Devices) {
        $id = if ($dev.InstanceId) { $dev.InstanceId } else { "" }
        $patterns = @(
            'VID_([0-9A-Fa-f]{4}).*PID_([0-9A-Fa-f]{4})',
            'VID&0002([0-9A-Fa-f]{4}).*PID&([0-9A-Fa-f]{4})',
            'VID&([0-9A-Fa-f]{4}).*PID&([0-9A-Fa-f]{4})'
        )
        foreach ($pattern in $patterns) {
            $match = [regex]::Match($id, $pattern)
            if ($match.Success) {
                return [pscustomobject]@{
                    Vid = $match.Groups[1].Value.ToUpperInvariant()
                    Pid = $match.Groups[2].Value.ToUpperInvariant()
                }
            }
        }
    }

    return [pscustomobject]@{
        Vid = "not_found"
        Pid = "not_found"
    }
}

function Get-HidDevicePathSummary {
    $source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class DualSenseEnvHidPaths {
  [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct SP_DEVICE_INTERFACE_DETAIL_DATA { public int cbSize; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=512)] public string DevicePath; }
  [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid HidGuid);
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

  const int DIGCF_PRESENT=0x02, DIGCF_DEVICEINTERFACE=0x10;
  const uint GENERIC_READ=0x80000000, GENERIC_WRITE=0x40000000, SHARE=0x03, OPEN_EXISTING=3;

  public static string Run() {
    List<string> found = new List<string>();
    foreach (string path in EnumerateHidPaths()) {
      using (SafeFileHandle h = CreateFile(path, GENERIC_READ|GENERIC_WRITE, SHARE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)) {
        if (h.IsInvalid) continue;
        HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES(); attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
        if (!HidD_GetAttributes(h, ref attr)) continue;
        if (attr.VendorID == 0x054c && (attr.ProductID == 0x0ce6 || attr.ProductID == 0x0df2)) {
          found.Add(path);
        }
      }
    }
    return found.Count == 0 ? "not_found" : String.Join(";", found.ToArray());
  }

  static IEnumerable<string> EnumerateHidPaths() {
    Guid guid; HidD_GetHidGuid(out guid);
    IntPtr set=SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,DIGCF_PRESENT|DIGCF_DEVICEINTERFACE);
    if (set == IntPtr.Zero || set.ToInt64() == -1) yield break;
    try {
      for(int i=0;;i++){
        SP_DEVICE_INTERFACE_DATA data=new SP_DEVICE_INTERFACE_DATA(); data.cbSize=Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
        if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref data)) break;
        int needed; SetupDiGetDeviceInterfaceDetail(set,ref data,IntPtr.Zero,0,out needed,IntPtr.Zero);
        SP_DEVICE_INTERFACE_DETAIL_DATA detail=new SP_DEVICE_INTERFACE_DETAIL_DATA(); detail.cbSize = IntPtr.Size == 8 ? 8 : 6;
        if(SetupDiGetDeviceInterfaceDetail(set,ref data,ref detail,Marshal.SizeOf(detail),out needed,IntPtr.Zero)) yield return detail.DevicePath;
      }
    } finally { SetupDiDestroyDeviceInfoList(set); }
  }
}
'@
    try {
        Add-Type $source -ErrorAction SilentlyContinue | Out-Null
        return [DualSenseEnvHidPaths]::Run()
    } catch {
        return "not_found"
    }
}

function Get-AudioEndpointRegistry {
    $results = @()
    $root = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio"
    foreach ($type in @("Render", "Capture")) {
        $typePath = Join-Path $root $type
        foreach ($endpoint in @(Get-ChildItem $typePath -ErrorAction SilentlyContinue)) {
            $propsPath = Join-Path $endpoint.PSPath "Properties"
            $props = Get-ItemProperty $propsPath -ErrorAction SilentlyContinue
            if ($null -eq $props) { continue }

            $propList = @($props.PSObject.Properties)
            $friendly = ($propList | Where-Object { $_.Name -eq "{a45c254e-df1c-4efd-8020-67d146a850e0},2" } | Select-Object -First 1).Value
            $allText = ($propList | ForEach-Object {
                if ($_.Value -is [string]) { $_.Value }
            }) -join " "

            if ($friendly -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive" -or
                $allText -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive") {
                $results += [pscustomobject]@{
                    Type = $type.ToLowerInvariant()
                    Name = if ($friendly) { $friendly } else { "unknown" }
                    Id = $endpoint.PSChildName
                }
            }
        }
    }
    return $results
}

function Test-PnpName {
    param([string]$Pattern)
    $matches = @($script:pnp | Where-Object {
        $_.FriendlyName -match $Pattern -or $_.Name -match $Pattern -or $_.InstanceId -match $Pattern
    })
    return $matches.Count -gt 0
}

$pnp = @(Get-PnpDevice -ErrorAction SilentlyContinue)
$script:pnp = $pnp
$dualSenseDevices = @($pnp | Where-Object { Test-DualSensePnpDevice $_ })
$hidUsb = @($dualSenseDevices | Where-Object {
    $_.Class -match "HIDClass|USB" -or $_.InstanceId -match "USB\\|HID\\VID_054C"
})
$hidBluetooth = @($dualSenseDevices | Where-Object {
    $_.Class -match "Bluetooth" -or $_.InstanceId -match "BTHENUM|BTHLE|Bluetooth|VID&0002054C"
})

$sound = @(Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue)
$audioPnp = @($pnp | Where-Object {
    $_.Class -match "AudioEndpoint|MEDIA" -and
    ($_.FriendlyName -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive")
})
$dualSenseSound = @($sound | Where-Object {
    $_.Name -match "DualSense|Wireless Controller|Controller Speaker|Sony Interactive"
})
$audioReg = @(Get-AudioEndpointRegistry)
$audioEndpointCount = $audioPnp.Count + $dualSenseSound.Count + $audioReg.Count
$audioEndpointNames = @(
    $audioPnp | ForEach-Object { $_.FriendlyName }
    $dualSenseSound | ForEach-Object { $_.Name }
    $audioReg | ForEach-Object { "$($_.Name)[$($_.Type)]" }
) | Where-Object { $_ } | Select-Object -Unique
$audioEndpointTypes = @($audioReg | ForEach-Object { $_.Type } | Select-Object -Unique)
if ($audioEndpointTypes.Count -eq 0 -and $audioEndpointCount -gt 0) { $audioEndpointTypes = @("unknown") }

$steam = Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 1
$isWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$wasapiLoopback = $isWindows -and $audioEndpointCount -gt 0
$vidPid = Get-VidPid $dualSenseDevices
$firstDevice = $dualSenseDevices | Select-Object -First 1
$product = if ($firstDevice) {
    if ($firstDevice.FriendlyName) { $firstDevice.FriendlyName } elseif ($firstDevice.Name) { $firstDevice.Name } else { "not_found" }
} else {
    "not_found"
}
$instanceId = if ($firstDevice -and $firstDevice.InstanceId) { $firstDevice.InstanceId } else { "not_found" }
$devicePath = Get-HidDevicePathSummary
$defaultOutput = "unknown"

$viiperPath = Join-Path $ProjectRoot "work\tools\viiper\viiper.exe"
$esp32Raw02Tools = (Test-Path (Join-Path $ProjectRoot "tools\send_pro2_raw02.ps1")) -and
    (Test-Path (Join-Path $ProjectRoot "experiments\viiper_ns2pro_to_real_pro2_rumble_probe\run_ns2pro_to_real_pro2_rumble_probe.ps1"))
$usbipCommand = Get-Command usbip.exe -ErrorAction SilentlyContinue
$usbipPnp = Test-PnpName "USBip|USBip 3.X|vhci|usbip"
$vigem = Test-PnpName "ViGEm|Nefarius Virtual Gamepad|Virtual Gamepad Emulation Bus"

Write-EnvLine "project" $ProjectRoot
Write-EnvLine "admin" (Test-Admin)
Write-EnvLine "hid_usb" ($hidUsb.Count -gt 0)
Write-EnvLine "hid_bluetooth" ($hidBluetooth.Count -gt 0)
Write-EnvLine "vid" $vidPid.Vid
Write-EnvLine "pid" $vidPid.Pid
Write-EnvLine "product" $product
Write-EnvLine "product_name" $product
Write-EnvLine "instance_id" $instanceId
Write-EnvLine "device_path" $devicePath
Write-EnvLine "real_dualsense" ($dualSenseDevices.Count -gt 0)
Write-EnvLine "audio_endpoint_count" $audioEndpointCount
Write-EnvLine "audio_endpoint" (($audioEndpointNames -join ";"))
Write-EnvLine "audio_endpoint_name" (($audioEndpointNames -join ";"))
Write-EnvLine "audio_endpoint_type" (($audioEndpointTypes -join ";"))
Write-EnvLine "default_audio_output" $defaultOutput
Write-EnvLine "wasapi_loopback_api" $isWindows
Write-EnvLine "wasapi_loopback" $wasapiLoopback
Write-EnvLine "steam_running" ($null -ne $steam)
Write-EnvLine "steam_input_hint" ($(if ($steam) { "compare_on_off_for_native_dualsense" } else { "steam_not_running" }))
Write-EnvLine "steam" ($(if ($steam) { "running pid=$($steam.Id)" } else { "not_running" }))
Write-EnvLine "vigembus" $vigem
Write-EnvLine "usbip_win2" (($null -ne $usbipCommand) -or $usbipPnp)
Write-EnvLine "usbip_exe" ($null -ne $usbipCommand)
Write-EnvLine "viiper" (Test-Path $viiperPath)
Write-EnvLine "viiper_path" ($(if (Test-Path $viiperPath) { $viiperPath } else { "not_found" }))
Write-EnvLine "esp32_raw02_tools" $esp32Raw02Tools
Write-EnvLine "blocked_by_missing_real_dualsense" ($dualSenseDevices.Count -eq 0)

foreach ($dev in $dualSenseDevices | Select-Object -First 8) {
    $devName = if ($dev.FriendlyName) { $dev.FriendlyName } elseif ($dev.Name) { $dev.Name } else { "unknown" }
    Write-Output "[DUALSENSE_ENV] device product=$(Convert-EnvValue $devName) class=$(Convert-EnvValue $dev.Class) status=$(Convert-EnvValue $dev.Status) instance_id=$(Convert-EnvValue $dev.InstanceId)"
}

foreach ($ep in $audioReg | Select-Object -First 8) {
    Write-Output "[DUALSENSE_ENV] audio_endpoint_detail name=$(Convert-EnvValue $ep.Name) type=$(Convert-EnvValue $ep.Type) id=$(Convert-EnvValue $ep.Id)"
}

if ($dualSenseDevices.Count -eq 0) {
    Write-BlockedLine "no_real_dualsense"
}
if ($audioEndpointCount -eq 0) {
    Write-BlockedLine "no_dualsense_audio_endpoint"
}

exit 0
