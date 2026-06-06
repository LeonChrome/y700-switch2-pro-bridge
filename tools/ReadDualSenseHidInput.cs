using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class ReadDualSenseHidInput
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 0x102;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeOverlappedData
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref DeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref DeviceInterfaceData interfaceData,
        ref DeviceInterfaceDetailData detailData, int detailDataSize,
        out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle file, byte[] buffer, uint bytesToRead,
        out uint bytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(
        IntPtr eventAttributes, bool manualReset, bool initialState, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle file, IntPtr overlapped, out uint bytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static int Main(string[] args)
    {
        int seconds = args.Length > 0 ? Math.Max(1, int.Parse(args[0])) : 5;
        try {
            string path = FindHidPath();
            Write("path", path);

            using (SafeFileHandle file = CreateFile(
                path, GenericRead, ShareRead | ShareWrite, IntPtr.Zero,
                OpenExisting, FileFlagOverlapped, IntPtr.Zero)) {
                if (file.IsInvalid) {
                    ThrowLast("CreateFile");
                }

                byte[] first = null;
                byte[] last = null;
                int firstLength = 0;
                int lastLength = 0;
                int reports = 0;
                int timeouts = 0;
                bool axesChanged = false;
                bool buttonsChanged = false;
                bool motionChanged = false;
                bool sequenceChanged = false;
                bool counterChanged = false;
                long firstTicks = 0;
                long lastTicks = 0;
                Stopwatch stopwatch = Stopwatch.StartNew();

                while (stopwatch.Elapsed.TotalSeconds < seconds) {
                    byte[] report = new byte[64];
                    uint read;
                    if (!ReadOne(file, report, out read, 1000)) {
                        timeouts++;
                        continue;
                    }

                    long now = stopwatch.ElapsedTicks;
                    if (first == null) {
                        first = (byte[])report.Clone();
                        firstLength = (int)read;
                        firstTicks = now;
                    } else {
                        axesChanged |= Changed(last, report, 1, 6);
                        buttonsChanged |= Changed(last, report, 8, 3);
                        sequenceChanged |= Changed(last, report, 7, 1);
                        counterChanged |= Changed(last, report, 12, 4);
                        motionChanged |= Changed(last, report, 16, 12);
                    }

                    last = report;
                    lastLength = (int)read;
                    lastTicks = now;
                    reports++;
                }

                double elapsed = reports > 1
                    ? (double)(lastTicks - firstTicks) / Stopwatch.Frequency
                    : stopwatch.Elapsed.TotalSeconds;
                double rate = reports > 1 && elapsed > 0
                    ? (reports - 1) / elapsed
                    : 0;
                bool validShape = reports > 0 &&
                    firstLength == 64 && lastLength == 64 && first[0] == 0x01;
                bool controlsNonNeutral = last != null && (
                    last[1] != 0x80 || last[2] != 0x80 ||
                    last[3] != 0x80 || last[4] != 0x80 ||
                    last[5] != 0x00 || last[6] != 0x00 ||
                    (last[8] & 0x0f) != 0x08 ||
                    (last[8] & 0xf0) != 0 || last[9] != 0 || last[10] != 0);
                bool motionNonZero = last != null && AnyNonZero(last, 16, 12);

                Write("report_count", reports);
                Write("timeouts", timeouts);
                Write("report_id", reports > 0 ? "0x" + first[0].ToString("X2") : "not_found");
                Write("wire_length", reports > 0 ? firstLength.ToString() : "not_found");
                Write("payload_length", reports > 0 ? (firstLength - 1).ToString() : "not_found");
                Write("measured_hz", rate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                Write("sequence_changed", sequenceChanged);
                Write("counter_changed", counterChanged);
                Write("axes_changed", axesChanged);
                Write("buttons_changed", buttonsChanged);
                Write("motion_changed", motionChanged);
                Write("controls_non_neutral", controlsNonNeutral);
                Write("motion_nonzero", motionNonZero);
                Write("mapped_input_activity", axesChanged || buttonsChanged || motionChanged);
                if (first != null) {
                    Write("first_head", Hex(first, 28));
                    Write("last_head", Hex(last, 28));
                }
                Write("result", validShape ? "passed" : "failed_report_shape");
                return validShape ? 0 : 2;
            }
        } catch (Exception ex) {
            Write("error", ex.Message);
            Write("result", "failed");
            return 1;
        }
    }

    private static bool ReadOne(
        SafeFileHandle file, byte[] buffer, out uint transferred, uint timeoutMs)
    {
        transferred = 0;
        IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
        if (evt == IntPtr.Zero) {
            ThrowLast("CreateEvent");
        }

        IntPtr overlapped = IntPtr.Zero;
        try {
            NativeOverlappedData data = new NativeOverlappedData { Event = evt };
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlappedData)));
            Marshal.StructureToPtr(data, overlapped, false);

            uint immediate;
            if (ReadFile(file, buffer, (uint)buffer.Length, out immediate, overlapped)) {
                transferred = immediate;
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending) {
                throw new Win32Exception(error, "ReadFile");
            }

            uint wait = WaitForSingleObject(evt, timeoutMs);
            if (wait == WaitTimeout) {
                CancelIoEx(file, overlapped);
                return false;
            }
            if (wait != WaitObject0) {
                ThrowLast("WaitForSingleObject");
            }
            if (!GetOverlappedResult(file, overlapped, out transferred, false)) {
                ThrowLast("GetOverlappedResult");
            }
            return true;
        } finally {
            if (overlapped != IntPtr.Zero) {
                Marshal.FreeHGlobal(overlapped);
            }
            CloseHandle(evt);
        }
    }

    private static string FindHidPath()
    {
        const uint Present = 0x00000002;
        const uint DeviceInterface = 0x00000010;
        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(
            ref guid, IntPtr.Zero, IntPtr.Zero, Present | DeviceInterface);
        if (set == new IntPtr(-1)) {
            ThrowLast("SetupDiGetClassDevs");
        }

        try {
            for (uint index = 0; ; index++) {
                DeviceInterfaceData data = new DeviceInterfaceData {
                    Size = Marshal.SizeOf(typeof(DeviceInterfaceData))
                };
                if (!SetupDiEnumDeviceInterfaces(
                    set, IntPtr.Zero, ref guid, index, ref data)) {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) {
                        break;
                    }
                    throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces");
                }

                DeviceInterfaceDetailData detail = new DeviceInterfaceDetailData {
                    Size = IntPtr.Size == 8 ? 8 : 5
                };
                int required;
                if (!SetupDiGetDeviceInterfaceDetail(
                    set, ref data, ref detail, Marshal.SizeOf(typeof(DeviceInterfaceDetailData)),
                    out required, IntPtr.Zero)) {
                    ThrowLast("SetupDiGetDeviceInterfaceDetail");
                }

                if (detail.DevicePath.IndexOf(
                    "vid_054c&pid_0ce6", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return detail.DevicePath;
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(set);
        }

        throw new InvalidOperationException("DualSense HID interface 054C:0CE6 not found");
    }

    private static bool Changed(byte[] a, byte[] b, int offset, int count)
    {
        if (a == null) {
            return false;
        }
        for (int i = offset; i < offset + count; i++) {
            if (a[i] != b[i]) {
                return true;
            }
        }
        return false;
    }

    private static bool AnyNonZero(byte[] data, int offset, int count)
    {
        for (int i = offset; i < offset + count; i++) {
            if (data[i] != 0) {
                return true;
            }
        }
        return false;
    }

    private static string Hex(byte[] data, int count)
    {
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < count; i++) {
            if (i > 0) {
                result.Append(' ');
            }
            result.Append(data[i].ToString("x2"));
        }
        return result.ToString();
    }

    private static void Write(string key, object value)
    {
        if (value is bool) {
            value = value.ToString().ToLowerInvariant();
        }
        Console.WriteLine("[V5_5_DS5_REPORT] {0}={1}", key, value);
    }

    private static void ThrowLast(string api)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), api);
    }
}
