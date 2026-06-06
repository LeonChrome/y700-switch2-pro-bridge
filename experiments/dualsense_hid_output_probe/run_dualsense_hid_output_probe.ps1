param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [switch]$SendSafeTest
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path $ProjectRoot).Path
$EnvScript = Join-Path $ProjectRoot "tools\check_dualsense_env.ps1"

Write-Output "[DUALSENSE_HID] starting"
& $EnvScript -ProjectRoot $ProjectRoot

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class DualSenseHidEnumerator {
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
        Console.WriteLine("[DUALSENSE_OUTPUT] report_id=not_captured len=" + caps.OutputReportByteLength + " hex=not_captured");
        Console.WriteLine("[DUALSENSE_TRIGGER] supported=unknown reason=no_safe_output_sent");
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

Add-Type $source
$matched = [DualSenseHidEnumerator]::Run()
Write-Output "[DUALSENSE_ENV] hid_matched_count=$matched"

if ($matched -eq 0) {
    Write-Output "[DUALSENSE_ENV] hid_usb=false"
    Write-Output "[DUALSENSE_ENV] hid_bluetooth=false"
    Write-Output "[DUALSENSE_BLOCKED] reason=no_real_dualsense"
    exit 0
}

if (!$SendSafeTest) {
    Write-Output "[DUALSENSE_BLOCKED] reason=safe_output_disabled"
    Write-Output "[DUALSENSE_BLOCKED] next=pass -SendSafeTest only after a real DualSense is connected and a safe lightbar/trigger report is selected"
    exit 0
}

Write-Output "[DUALSENSE_BLOCKED] reason=safe_output_not_implemented_yet"
exit 0
