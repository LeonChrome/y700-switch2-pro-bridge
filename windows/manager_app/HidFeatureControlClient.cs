using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Y700Switch2Manager;

public sealed class HidFeatureControlClient : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const byte ReportId = 0x7f;
    private const int ReportSize = 64;
    private const string SetMagic = "Y7HID1";
    private const string ReplyMagic = "Y7HRS1";

    private SafeFileHandle? file;

    public bool IsConnected => file is { IsInvalid: false, IsClosed: false };
    public string? DevicePath { get; private set; }

    public void Connect()
    {
        Disconnect();
        string path = FindHidPath();
        SafeFileHandle opened = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (opened.IsInvalid)
        {
            ThrowLast("CreateFile");
        }

        file = opened;
        DevicePath = path;
    }

    public void Disconnect()
    {
        file?.Dispose();
        file = null;
        DevicePath = null;
    }

    public string SendCommand(string command)
    {
        if (!IsConnected || file == null)
        {
            throw new InvalidOperationException("HID feature control is not connected.");
        }

        byte[] report = new byte[ReportSize];
        report[0] = ReportId;
        byte[] payload = Encoding.ASCII.GetBytes(SetMagic + command.TrimEnd());
        Buffer.BlockCopy(payload, 0, report, 1, Math.Min(payload.Length, ReportSize - 1));

        if (!HidD_SetFeature(file, report, report.Length))
        {
            ThrowLast("HidD_SetFeature");
        }

        byte[] json = ReadReply();
        return Encoding.UTF8.GetString(json);
    }

    private byte[] ReadReply()
    {
        if (file == null)
        {
            throw new InvalidOperationException("HID feature control is not connected.");
        }

        byte[] output = new byte[3072];
        int total = -1;
        int received = 0;
        int guard = 0;

        while ((total < 0 || received < total) && guard++ < 128)
        {
            byte[] report = new byte[ReportSize];
            report[0] = ReportId;
            if (!HidD_GetFeature(file, report, report.Length))
            {
                ThrowLast("HidD_GetFeature");
            }

            int baseIndex = report[0] == ReportId ? 1 : 0;
            string magic = Encoding.ASCII.GetString(report, baseIndex, ReplyMagic.Length);
            if (!string.Equals(magic, ReplyMagic, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected HID feature reply magic: " + magic);
            }

            int replyTotal = report[baseIndex + 6] | (report[baseIndex + 7] << 8);
            int offset = report[baseIndex + 8] | (report[baseIndex + 9] << 8);
            int chunk = report[baseIndex + 10];
            if (replyTotal > output.Length)
            {
                throw new InvalidOperationException("HID feature reply is too large.");
            }
            if (offset + chunk > output.Length || baseIndex + 11 + chunk > report.Length)
            {
                throw new InvalidOperationException("Invalid HID feature reply chunk.");
            }

            total = replyTotal;
            if (chunk > 0)
            {
                Buffer.BlockCopy(report, baseIndex + 11, output, offset, chunk);
                received = Math.Max(received, offset + chunk);
            }
            else if (total == 0)
            {
                break;
            }
        }

        if (total < 0 || received < total)
        {
            throw new TimeoutException("HID feature reply did not complete.");
        }

        byte[] json = new byte[total];
        Buffer.BlockCopy(output, 0, json, 0, total);
        return json;
    }

    private static string FindHidPath()
    {
        const uint present = 0x00000002;
        const uint deviceInterface = 0x00000010;

        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, present | deviceInterface);
        if (set == new IntPtr(-1))
        {
            ThrowLast("SetupDiGetClassDevs");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                DeviceInterfaceData data = new() { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 259)
                    {
                        break;
                    }
                    throw new Win32Exception(err, "SetupDiEnumDeviceInterfaces");
                }

                DeviceInterfaceDetailData detail = new() { Size = IntPtr.Size == 8 ? 8 : 5 };
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, ref detail,
                        Marshal.SizeOf<DeviceInterfaceDetailData>(), out _, IntPtr.Zero))
                {
                    ThrowLast("SetupDiGetDeviceInterfaceDetail");
                }

                if (detail.DevicePath.Contains("vid_057e&pid_2069&mi_00", StringComparison.OrdinalIgnoreCase))
                {
                    return detail.DevicePath;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        throw new InvalidOperationException("Nintendo HID control interface was not found.");
    }

    private static void ThrowLast(string api) => throw new Win32Exception(Marshal.GetLastWin32Error(), api);

    public void Dispose() => Disconnect();

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DeviceInterfaceDetailData
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string DevicePath;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData, ref DeviceInterfaceDetailData deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
