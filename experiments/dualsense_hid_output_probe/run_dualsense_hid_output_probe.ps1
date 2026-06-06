param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$DurationSeconds = 10,
    [string]$JsonlPath = "",
    [string]$RawHexLogPath = "",
    [switch]$SendSafeTest
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$EnvScript = Join-Path $ProjectRoot "tools\check_dualsense_env.ps1"

function Convert-LogValue {
    param([object]$Value)
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($null -eq $Value) { return "not_found" }
    if ($Value -is [string] -and $Value -eq "") { return "not_found" }
    return ($Value.ToString() -replace "[`r`n]+", " ").Trim()
}

function Write-Jsonl {
    param([hashtable]$Event)
    if (!$JsonlPath) { return }
    $dir = Split-Path -Parent $JsonlPath
    if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
    ($Event | ConvertTo-Json -Compress -Depth 8) | Add-Content -Encoding UTF8 $JsonlPath
}

function Write-RawHex {
    param([string]$Line)
    if (!$RawHexLogPath) { return }
    $dir = Split-Path -Parent $RawHexLogPath
    if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $Line | Add-Content -Encoding ASCII $RawHexLogPath
}

function Get-OutputCategory {
    param([byte[]]$Data)
    if ($null -eq $Data -or $Data.Length -eq 0) { return "unknown" }
    $reportId = $Data[0]
    if ($reportId -eq 0x02 -or $reportId -eq 0x31) {
        if ($Data.Length -gt 20) {
            return "possible_haptic_control"
        }
        return "ordinary_rumble"
    }
    if ($reportId -eq 0x05 -or $reportId -eq 0x10) { return "lightbar_or_led" }
    return "unknown"
}

function Convert-HexToBytes {
    param([string]$Hex)
    if (!$Hex) { return @() }
    $clean = $Hex -replace "[^0-9A-Fa-f]", ""
    $bytes = New-Object byte[] ($clean.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($clean.Substring($i * 2, 2), 16)
    }
    return $bytes
}

Write-Output "[DUALSENSE_HID] starting duration_seconds=$DurationSeconds"
Write-Jsonl @{ ts = (Get-Date).ToUniversalTime().ToString("o"); event = "start"; duration_seconds = $DurationSeconds }
& $EnvScript -ProjectRoot $ProjectRoot

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class DualSenseHidProbeEnumerator {
  [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct SP_DEVICE_INTERFACE_DETAIL_DATA { public int cbSize; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=512)] public string DevicePath; }
  [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
  [StructLayout(LayoutKind.Sequential)] struct HIDP_CAPS {
    public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
    public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
  }

  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid HidGuid);
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

  const int DIGCF_PRESENT=0x02, DIGCF_DEVICEINTERFACE=0x10;
  const uint GENERIC_READ=0x80000000, GENERIC_WRITE=0x40000000, SHARE=0x03, OPEN_EXISTING=3;

  public static int Run() {
    int matched = 0;
    foreach (string path in EnumerateHidPaths()) {
      using (SafeFileHandle h = CreateFile(path, GENERIC_READ|GENERIC_WRITE, SHARE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)) {
        if (h.IsInvalid) continue;
        HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES(); attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
        if (!HidD_GetAttributes(h, ref attr)) continue;
        if (attr.VendorID != 0x054c) continue;
        if (attr.ProductID != 0x0ce6 && attr.ProductID != 0x0df2) continue;
        matched++;
        HIDP_CAPS caps = GetCaps(h);
        string transport = path.IndexOf("BTH", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("BTHENUM", StringComparison.OrdinalIgnoreCase) >= 0 ? "bluetooth" : "usb_or_hid";
        Console.WriteLine("[DUALSENSE_HID] device=" + path);
        Console.WriteLine("[DUALSENSE_HID] transport=" + transport + " vid=" + attr.VendorID.ToString("x4") + " pid=" + attr.ProductID.ToString("x4") + " ver=" + attr.VersionNumber.ToString("x4"));
        Console.WriteLine("[DUALSENSE_HID] usage_page=0x" + caps.UsagePage.ToString("x4") + " usage=0x" + caps.Usage.ToString("x4") + " input_len=" + caps.InputReportByteLength + " output_len=" + caps.OutputReportByteLength + " feature_len=" + caps.FeatureReportByteLength);
      }
    }
    return matched;
  }

  static HIDP_CAPS GetCaps(SafeFileHandle h) {
    HIDP_CAPS caps = new HIDP_CAPS();
    IntPtr pp;
    if (HidD_GetPreparsedData(h, out pp)) {
      try { HidP_GetCaps(pp, out caps); } finally { HidD_FreePreparsedData(pp); }
    }
    return caps;
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

Add-Type $source -ErrorAction SilentlyContinue | Out-Null
$matched = [DualSenseHidProbeEnumerator]::Run()
Write-Output "[DUALSENSE_HID] matched_devices=$matched"
Write-Jsonl @{ ts = (Get-Date).ToUniversalTime().ToString("o"); event = "enumeration"; matched_devices = $matched }

if ($matched -eq 0) {
    Write-Output "[DUALSENSE_ENV] hid_usb=false"
    Write-Output "[DUALSENSE_ENV] hid_bluetooth=false"
    Write-Output "[DUALSENSE_HID] capture_started=false"
    Write-Output "[DUALSENSE_OUTPUT] captured_reports=0"
    Write-Output "[DUALSENSE_BLOCKED] reason=no_real_dualsense"
    Write-Jsonl @{ ts = (Get-Date).ToUniversalTime().ToString("o"); event = "blocked"; reason = "no_real_dualsense" }
    exit 0
}

Write-Output "[DUALSENSE_HID] capture_started=true"
Write-Output "[DUALSENSE_HID] duration_seconds=$DurationSeconds"
Write-Output "[DUALSENSE_HID] passive_output_capture_supported=false"
Write-Output "[DUALSENSE_HID] note=windows_user_mode_cannot_passively_intercept_other_process_hid_output_without_filter_or_instrumented_sender"
Write-Jsonl @{
    ts = (Get-Date).ToUniversalTime().ToString("o")
    event = "capture_started"
    matched_devices = $matched
    passive_output_capture_supported = $false
}

$deadline = (Get-Date).AddSeconds([Math]::Max(0, $DurationSeconds))
$captured = 0
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}

Write-Output "[DUALSENSE_OUTPUT] captured_reports=$captured"
Write-Output "[DUALSENSE_TRIGGER] left=unknown right=unknown"
Write-Output "[DUALSENSE_RUMBLE] small=unknown large=unknown"
Write-RawHex "# no passive output reports captured"
Write-Jsonl @{
    ts = (Get-Date).ToUniversalTime().ToString("o")
    event = "summary"
    captured_reports = $captured
    blocked_reason = "passive_hid_output_capture_requires_filter_or_instrumented_sender"
}

if (!$SendSafeTest) {
    Write-Output "[DUALSENSE_BLOCKED] reason=passive_hid_output_capture_requires_filter_or_instrumented_sender"
    Write-Output "[DUALSENSE_BLOCKED] next=use_native_game_plus_instrumented_sender_or_hid_filter_for_real_output_capture"
    exit 0
}

Write-Output "[DUALSENSE_BLOCKED] reason=safe_output_not_implemented_yet"
exit 0
