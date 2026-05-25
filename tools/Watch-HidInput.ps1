param(
    [string]$Vid = "057e",
    [string[]]$Pids = @("2069", "2009"),
    [int]$Seconds = 8,
    [int]$MaxReports = 80
)

$ErrorActionPreference = "Stop"

$source = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class HidInputWatch {
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
  [DllImport("kernel32.dll")] static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

  const int DIGCF_PRESENT=0x02, DIGCF_DEVICEINTERFACE=0x10;
  const uint GENERIC_READ=0x80000000, GENERIC_WRITE=0x40000000, SHARE=0x03, OPEN_EXISTING=3;

  public static void Run(ushort vid, ushort[] pids, int seconds, int maxReports) {
    foreach (string path in EnumerateHidPaths()) {
      using (SafeFileHandle h = CreateFile(path, GENERIC_READ|GENERIC_WRITE, SHARE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)) {
        if (h.IsInvalid) continue;
        HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES(); attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
        if (!HidD_GetAttributes(h, ref attr)) continue;
        if (attr.VendorID != vid || Array.IndexOf(pids, attr.ProductID) < 0) continue;

        HIDP_CAPS caps = GetCaps(h);
        Console.WriteLine("path=" + path);
        Console.WriteLine("vid=" + attr.VendorID.ToString("x4") + " pid=" + attr.ProductID.ToString("x4") + " inLen=" + caps.InputReportByteLength);

        int len = caps.InputReportByteLength > 0 ? caps.InputReportByteLength : 65;
        byte[] last = null;
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        int count = 0;
        using (FileStream fs = new FileStream(h, FileAccess.ReadWrite, len, false)) {
          while (DateTime.UtcNow < end && count < maxReports) {
            byte[] buf = new byte[len];
            IAsyncResult ar = fs.BeginRead(buf, 0, len, null, null);
            int waitMs = Math.Max(1, (int)(end - DateTime.UtcNow).TotalMilliseconds);
            if (!ar.AsyncWaitHandle.WaitOne(Math.Min(250, waitMs))) {
              CancelIoEx(h, IntPtr.Zero);
              try { fs.EndRead(ar); } catch {}
              continue;
            }
            int n = fs.EndRead(ar);
            if (n <= 0) continue;
            count++;
            if (last == null || Changed(last, buf, n)) {
              Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " n=" + n + " " + Hex(buf, Math.Min(n, 32)) + " delta=" + Delta(last, buf, n));
              last = Copy(buf, n);
            }
          }
        }
      }
    }
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

  static bool Changed(byte[] a, byte[] b, int n) {
    if (a == null || a.Length != n) return true;
    for (int i = 0; i < n; i++) if (a[i] != b[i]) return true;
    return false;
  }

  static byte[] Copy(byte[] b, int n) {
    byte[] c = new byte[n]; Array.Copy(b, c, n); return c;
  }

  static string Delta(byte[] a, byte[] b, int n) {
    if (a == null) return "initial";
    List<string> d = new List<string>();
    for (int i = 0; i < n && d.Count < 12; i++) {
      byte av = i < a.Length ? a[i] : (byte)0;
      if (av != b[i]) d.Add(i.ToString() + ":" + av.ToString("X2") + ">" + b[i].ToString("X2"));
    }
    return string.Join(" ", d.ToArray());
  }

  static string Hex(byte[] data, int n) {
    char[] chars = new char[n * 2];
    const string alphabet = "0123456789abcdef";
    for (int i = 0; i < n; i++) {
      chars[i * 2] = alphabet[(data[i] >> 4) & 0xf];
      chars[i * 2 + 1] = alphabet[data[i] & 0xf];
    }
    return new string(chars);
  }
}
'@

Add-Type $source
$vidValue = [Convert]::ToUInt16($Vid, 16)
$pidValues = $Pids | ForEach-Object { [Convert]::ToUInt16($_, 16) }
[HidInputWatch]::Run($vidValue, [UInt16[]]$pidValues, $Seconds, $MaxReports)
