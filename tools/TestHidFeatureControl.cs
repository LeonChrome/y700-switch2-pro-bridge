using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class TestHidFeatureControl
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const byte REPORT_ID = 0x7f;
    private const int REPORT_SIZE = 64;
    private const string SET_MAGIC = "Y7HID1";
    private const string REPLY_MAGIC = "Y7HRS1";

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public int cbSize;
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
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    public static int Main(string[] args)
    {
        string command = args.Length > 0 ? string.Join(" ", args) : "status";
        try
        {
            string path = FindHidPath();
            Console.WriteLine("Path: " + path);
            using (SafeFileHandle file = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero))
            {
                if (file.IsInvalid) ThrowLast("CreateFile");
                Console.WriteLine(SendCommand(file, command));
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.GetType().FullName);
            Console.WriteLine("Message: " + ex.Message);
            if (ex is Win32Exception) Console.WriteLine("NativeErrorCode: " + ((Win32Exception)ex).NativeErrorCode);
            return 1;
        }
    }

    private static string SendCommand(SafeFileHandle file, string command)
    {
        byte[] report = new byte[REPORT_SIZE];
        report[0] = REPORT_ID;
        byte[] payload = Encoding.ASCII.GetBytes(SET_MAGIC + command.TrimEnd());
        Buffer.BlockCopy(payload, 0, report, 1, Math.Min(payload.Length, REPORT_SIZE - 1));
        if (!HidD_SetFeature(file, report, report.Length)) ThrowLast("HidD_SetFeature");

        byte[] output = new byte[3072];
        int total = -1;
        int received = 0;
        for (int guard = 0; guard < 128 && (total < 0 || received < total); guard++)
        {
            byte[] chunkReport = new byte[REPORT_SIZE];
            chunkReport[0] = REPORT_ID;
            if (!HidD_GetFeature(file, chunkReport, chunkReport.Length)) ThrowLast("HidD_GetFeature");

            int baseIndex = chunkReport[0] == REPORT_ID ? 1 : 0;
            string magic = Encoding.ASCII.GetString(chunkReport, baseIndex, REPLY_MAGIC.Length);
            if (magic != REPLY_MAGIC) throw new InvalidOperationException("Unexpected reply magic: " + magic);

            total = chunkReport[baseIndex + 6] | (chunkReport[baseIndex + 7] << 8);
            int offset = chunkReport[baseIndex + 8] | (chunkReport[baseIndex + 9] << 8);
            int chunk = chunkReport[baseIndex + 10];
            if (offset + chunk > output.Length || baseIndex + 11 + chunk > chunkReport.Length)
            {
                throw new InvalidOperationException("Invalid reply chunk");
            }
            if (chunk > 0)
            {
                Buffer.BlockCopy(chunkReport, baseIndex + 11, output, offset, chunk);
                received = Math.Max(received, offset + chunk);
            }
        }

        if (total < 0 || received < total) throw new TimeoutException("reply did not complete");
        return Encoding.UTF8.GetString(output, 0, total);
    }

    private static string FindHidPath()
    {
        const uint DIGCF_PRESENT = 0x00000002;
        const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) ThrowLast("SetupDiGetClassDevs");

        try
        {
            for (uint index = 0; ; index++)
            {
                SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 259) break;
                    throw new Win32Exception(err, "SetupDiEnumDeviceInterfaces");
                }

                SP_DEVICE_INTERFACE_DETAIL_DATA detail = new SP_DEVICE_INTERFACE_DETAIL_DATA();
                detail.cbSize = IntPtr.Size == 8 ? 8 : 5;
                int required;
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, ref detail, Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DETAIL_DATA)), out required, IntPtr.Zero))
                {
                    ThrowLast("SetupDiGetDeviceInterfaceDetail");
                }

                if (detail.DevicePath.IndexOf("vid_057e&pid_2069&mi_00", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return detail.DevicePath;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        throw new InvalidOperationException("Nintendo HID interface not found");
    }

    private static void ThrowLast(string api)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), api);
    }
}
