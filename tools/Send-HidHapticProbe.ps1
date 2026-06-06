param(
    [string]$Vid = "057e",
    [string[]]$Pids = @("2069", "2009"),
    [int]$PulseMs = 600,
    [int]$GapMs = 140,
    [ValidateRange(0, 65535)]
    [int]$LowSpeed = 65535,
    [ValidateRange(0, 65535)]
    [int]$HighSpeed = 65535,
    [ValidateSet("single", "double", "long")]
    [string]$Pattern = "single",
    [switch]$LegacyVariants,
    [string]$PathContains = ""
)

$ErrorActionPreference = "Stop"

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class HidHapticProbe {
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
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_SetOutputReport(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);
  [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);
  [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);
  [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

  const int DIGCF_PRESENT=0x02, DIGCF_DEVICEINTERFACE=0x10;
  const uint GENERIC_READ=0x80000000, GENERIC_WRITE=0x40000000, SHARE=0x03, OPEN_EXISTING=3;

  public static int Run(ushort vid, ushort[] pids, int pulseMs, int gapMs, ushort lowSpeed, ushort highSpeed, string pattern, bool legacyVariants, string pathContains) {
    int matched = 0;
    foreach (string path in EnumerateHidPaths()) {
      if (!String.IsNullOrWhiteSpace(pathContains) && path.IndexOf(pathContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
      using (SafeFileHandle h = CreateFile(path, GENERIC_READ|GENERIC_WRITE, SHARE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)) {
        if (h.IsInvalid) continue;
        HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES(); attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
        if (!HidD_GetAttributes(h, ref attr)) continue;
        if (attr.VendorID != vid || Array.IndexOf(pids, attr.ProductID) < 0) continue;
        matched++;

        HIDP_CAPS caps = GetCaps(h);
        Console.WriteLine("path=" + path);
        Console.WriteLine("vid=" + attr.VendorID.ToString("x4") + " pid=" + attr.ProductID.ToString("x4") + " ver=" + attr.VersionNumber.ToString("x4") +
          " inLen=" + caps.InputReportByteLength + " outLen=" + caps.OutputReportByteLength + " featLen=" + caps.FeatureReportByteLength);

        if (legacyVariants) {
          byte[] strong = BuildSwitch2Report(new byte[] {0x50,0x93,0x35,0x36,0x1c,0x0d});
          byte[] stop = BuildSwitch2Report(new byte[] {0,0,0,0,0,0});
          SendVariants(h, strong, "legacy strong");
          System.Threading.Thread.Sleep(pulseMs);
          SendVariants(h, stop, "legacy stop");
        } else {
          byte seq = 0;
          Console.WriteLine("sending SDL Switch2 rumble pattern=" + pattern + " pulseMs=" + pulseMs + " gapMs=" + gapMs +
            " lowSpeed=" + lowSpeed + " highSpeed=" + highSpeed);
          if (pattern == "double") {
            SendSdlPulse(h, caps.OutputReportByteLength, ref seq, pulseMs, lowSpeed, highSpeed, "pulseA");
            System.Threading.Thread.Sleep(Math.Max(0, gapMs));
            SendSdlPulse(h, caps.OutputReportByteLength, ref seq, pulseMs, lowSpeed, highSpeed, "pulseB");
          } else {
            SendSdlPulse(h, caps.OutputReportByteLength, ref seq, pulseMs, lowSpeed, highSpeed, pattern);
          }
        }
      }
    }
    Console.WriteLine("[HID_HAPTIC] matched_devices=" + matched);
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

  static void SendVariants(SafeFileHandle h, byte[] report64, string label) {
    byte[] zeroPlus64 = new byte[65]; Array.Copy(report64, 0, zeroPlus64, 1, 64);
    byte[] idPlus63 = new byte[64]; idPlus63[0] = 0x02; Array.Copy(report64, 1, idPlus63, 1, 63);
    byte[] idPlus64 = new byte[65]; idPlus64[0] = 0x02; Array.Copy(report64, 1, idPlus64, 1, 63);
    TryWrite(h, report64, label + " report64");
    TryWrite(h, zeroPlus64, label + " zeroPlus64");
    TryWrite(h, idPlus63, label + " idPlus63");
    TryWrite(h, idPlus64, label + " idPlus64");
  }

  static void TryWrite(SafeFileHandle h, byte[] data, string label) {
    int written;
    bool wf = WriteFile(h, data, data.Length, out written, IntPtr.Zero);
    int wfErr = Marshal.GetLastWin32Error();
    bool so = HidD_SetOutputReport(h, data, data.Length);
    int soErr = Marshal.GetLastWin32Error();
    Console.WriteLine(label + " WriteFile=" + wf + " written=" + written + " err=" + wfErr + " SetOutput=" + so + " err=" + soErr);
  }

  static void TryWriteFile(SafeFileHandle h, byte[] report, int outputReportLength, string label) {
    byte[] data = PadForWindowsHidWrite(report, outputReportLength);
    int written;
    bool wf = WriteFile(h, data, data.Length, out written, IntPtr.Zero);
    int wfErr = Marshal.GetLastWin32Error();
    Console.WriteLine(label + " WriteFile=" + wf + " written=" + written + " err=" + wfErr + " data=" + Hex(data, 24));
  }

  static void SendSdlPulse(SafeFileHandle h, int outputReportLength, ref byte seq, int pulseMs, ushort lowSpeed, ushort highSpeed, string label) {
    int iterations = Math.Max(1, pulseMs / 12);
    for (int i = 0; i < iterations; i++) {
      byte[] report = BuildSdlSwitch2Report(seq++, lowSpeed, highSpeed);
      TryWriteFile(h, report, outputReportLength, label + " strong seq " + i);
      System.Threading.Thread.Sleep(12);
    }
    for (int i = 0; i < 3; i++) {
      byte[] report = BuildSdlSwitch2Report(seq++, 0, 0);
      TryWriteFile(h, report, outputReportLength, label + " stop seq " + i);
      System.Threading.Thread.Sleep(12);
    }
  }

  static byte[] PadForWindowsHidWrite(byte[] report, int outputReportLength) {
    if (outputReportLength <= 0 || outputReportLength == report.Length) return report;
    byte[] data = new byte[outputReportLength];
    if (outputReportLength == report.Length + 1) {
      Array.Copy(report, 0, data, 1, report.Length);
    } else {
      Array.Copy(report, 0, data, 0, Math.Min(report.Length, data.Length));
    }
    return data;
  }

  static byte[] BuildSwitch2Report(byte[] haptic6) {
    byte[] b = new byte[64]; b[0]=0x02;
    Array.Copy(haptic6,0,b,1,Math.Min(6,haptic6.Length));
    Array.Copy(haptic6,0,b,17,Math.Min(6,haptic6.Length));
    return b;
  }

  static byte[] BuildSdlSwitch2Report(byte seq, ushort low, ushort high) {
    ushort lowAmp = (ushort)(((int)low * 29000) / 65535);
    ushort highAmp = (ushort)(((int)high * 29000) / 65535);
    byte[] b = new byte[64];
    b[0] = 0x02;
    b[1] = (byte)(0x50 | (seq & 0x0f));
    EncodeHDRumble(0x0187, highAmp, 0x0112, lowAmp, b, 2);
    Array.Copy(b, 1, b, 17, 6);
    return b;
  }

  static void EncodeHDRumble(ushort highFreq, ushort highAmp, ushort lowFreq, ushort lowAmp, byte[] data, int offset) {
    data[offset + 0] = (byte)(highFreq & 0xff);
    data[offset + 1] = (byte)(((highAmp >> 4) & 0xfc) | ((highFreq >> 8) & 0x03));
    data[offset + 2] = (byte)((highAmp >> 12) | (lowFreq << 4));
    data[offset + 3] = (byte)((lowAmp & 0xc0) | ((lowFreq >> 4) & 0x3f));
    data[offset + 4] = (byte)(lowAmp >> 8);
  }

  static string Hex(byte[] data, int n) {
    int limit = Math.Min(data.Length, n);
    char[] chars = new char[limit * 2];
    const string alphabet = "0123456789abcdef";
    for (int i = 0; i < limit; i++) {
      chars[i * 2] = alphabet[(data[i] >> 4) & 0xf];
      chars[i * 2 + 1] = alphabet[data[i] & 0xf];
    }
    return new string(chars);
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
$vidValue = [Convert]::ToUInt16($Vid, 16)
$pidValues = $Pids | ForEach-Object { [Convert]::ToUInt16($_, 16) }
$matchedDevices = [HidHapticProbe]::Run($vidValue, [UInt16[]]$pidValues, $PulseMs, $GapMs, [UInt16]$LowSpeed, [UInt16]$HighSpeed, $Pattern, [bool]$LegacyVariants, $PathContains)
if ($matchedDevices -le 0) {
    Write-Output "[HID_HAPTIC] blocked=no matching HID device"
    exit 2
}
