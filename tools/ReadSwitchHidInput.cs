using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class ReadSwitchHidInput
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const int ERROR_IO_PENDING = 997;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid HidGuid);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle hFile, IntPtr lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static int Main(string[] args)
    {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 6;
        try {
            string path = FindHidPath();
            Console.WriteLine("Path: " + path);
            using (SafeFileHandle file = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, IntPtr.Zero)) {
                if (file.IsInvalid) {
                    ThrowLast("CreateFile");
                }

                DateTime until = DateTime.UtcNow.AddSeconds(seconds);
                byte[] last = null;
                while (DateTime.UtcNow < until) {
                    byte[] report = new byte[64];
                    uint read;
                    if (ReadOne(file, report, out read, 1000)) {
                        bool changed = last == null || !EqualPrefix(last, report, (int)read);
                        last = report;
                        Console.WriteLine("Read {0} changed={1} b0..8={2} A_bit={3}",
                            read,
                            changed,
                            Hex(report, Math.Min((int)read, 9)),
                            read > 5 && (report[5] & 0x08) != 0 ? "on" : "off");
                    } else {
                        Console.WriteLine("Read timeout");
                    }
                }
            }
            return 0;
        } catch (Exception ex) {
            Console.WriteLine("ERROR: " + ex.GetType().FullName);
            Console.WriteLine("Message: " + ex.Message);
            if (ex is Win32Exception) {
                Console.WriteLine("NativeErrorCode: " + ((Win32Exception)ex).NativeErrorCode);
            }
            return 1;
        }
    }

    private static bool ReadOne(SafeFileHandle file, byte[] buffer, out uint transferred, uint timeoutMs)
    {
        transferred = 0;
        IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
        if (evt == IntPtr.Zero) {
            ThrowLast("CreateEvent");
        }

        IntPtr ovPtr = IntPtr.Zero;
        try {
            OVERLAPPED ov = new OVERLAPPED();
            ov.hEvent = evt;
            ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OVERLAPPED)));
            Marshal.StructureToPtr(ov, ovPtr, false);

            uint immediate;
            if (ReadFile(file, buffer, (uint)buffer.Length, out immediate, ovPtr)) {
                transferred = immediate;
                return true;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_IO_PENDING) {
                throw new Win32Exception(err, "ReadFile");
            }

            uint wait = WaitForSingleObject(evt, timeoutMs);
            if (wait == WAIT_TIMEOUT) {
                CancelIoEx(file, ovPtr);
                return false;
            }
            if (wait != WAIT_OBJECT_0) {
                ThrowLast("WaitForSingleObject");
            }

            if (!GetOverlappedResult(file, ovPtr, out transferred, false)) {
                ThrowLast("GetOverlappedResult");
            }
            return true;
        } finally {
            if (ovPtr != IntPtr.Zero) {
                Marshal.FreeHGlobal(ovPtr);
            }
            CloseHandle(evt);
        }
    }

    private static string FindHidPath()
    {
        const uint DIGCF_PRESENT = 0x00000002;
        const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) {
            ThrowLast("SetupDiGetClassDevs");
        }

        try {
            for (uint index = 0; ; index++) {
                SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 259) {
                        break;
                    }
                    throw new Win32Exception(err, "SetupDiEnumDeviceInterfaces");
                }

                SP_DEVICE_INTERFACE_DETAIL_DATA detail = new SP_DEVICE_INTERFACE_DETAIL_DATA();
                detail.cbSize = IntPtr.Size == 8 ? 8 : 5;
                int required;
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, ref detail, Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DETAIL_DATA)), out required, IntPtr.Zero)) {
                    ThrowLast("SetupDiGetDeviceInterfaceDetail");
                }

                string path = detail.DevicePath;
                if (path.IndexOf("vid_057e&pid_2069&mi_00", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return path;
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(set);
        }

        throw new InvalidOperationException("Switch HID interface not found");
    }

    private static bool EqualPrefix(byte[] a, byte[] b, int count)
    {
        for (int i = 0; i < count; i++) {
            if (a[i] != b[i]) {
                return false;
            }
        }
        return true;
    }

    private static void ThrowLast(string api)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), api);
    }

    private static string Hex(byte[] data, int count)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < count; i++) {
            if (i > 0) {
                sb.Append(' ');
            }
            sb.Append(data[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
