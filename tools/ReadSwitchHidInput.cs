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
        string mode = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : "brief";
        bool fullMode = mode == "full";
        bool statsMode = mode == "stats";
        try {
            string path = FindHidPath();
            Console.WriteLine("Path: " + path);
            Console.WriteLine("Mode: " + mode);
            using (SafeFileHandle file = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, IntPtr.Zero)) {
                if (file.IsInvalid) {
                    ThrowLast("CreateFile");
                }

                DateTime until = DateTime.UtcNow.AddSeconds(seconds);
                byte[] last = null;
                byte[] first = null;
                int reportCount = 0;
                int[] changedCounts = new int[64];
                int[] nonZeroCounts = new int[64];
                byte[] minValues = new byte[64];
                byte[] maxValues = new byte[64];
                SdlImuStats imuStats = new SdlImuStats();
                while (DateTime.UtcNow < until) {
                    byte[] report = new byte[64];
                    uint read;
                    if (ReadOne(file, report, out read, 1000)) {
                        reportCount++;
                        bool changed = last == null || !EqualPrefix(last, report, (int)read);
                        if (first == null) {
                            first = (byte[])report.Clone();
                            for (int i = 0; i < read && i < minValues.Length; i++) {
                                minValues[i] = report[i];
                                maxValues[i] = report[i];
                            }
                        }
                        UpdateStats(last, report, (int)read, changedCounts, nonZeroCounts, minValues, maxValues);
                        imuStats.Add(report, (int)read);
                        if (fullMode) {
                            Console.WriteLine("Read {0} changed={1} diff={2} hex={3}",
                                read,
                                changed,
                                last == null ? "first" : ChangedIndexes(last, report, (int)read),
                                Hex(report, (int)read));
                        } else if (!statsMode) {
                            Console.WriteLine("Read {0} changed={1} b0..8={2} A_bit={3}",
                                read,
                                changed,
                                Hex(report, Math.Min((int)read, 9)),
                                read > 5 && (report[5] & 0x08) != 0 ? "on" : "off");
                        }
                        last = report;
                    } else {
                        Console.WriteLine("Read timeout");
                    }
                }
                if (statsMode) {
                    PrintStats(reportCount, changedCounts, nonZeroCounts, minValues, maxValues, first, last);
                    imuStats.Print();
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

    private static void UpdateStats(byte[] last, byte[] report, int count, int[] changedCounts, int[] nonZeroCounts, byte[] minValues, byte[] maxValues)
    {
        for (int i = 0; i < count && i < changedCounts.Length; i++) {
            if (last != null && last[i] != report[i]) {
                changedCounts[i]++;
            }
            if (report[i] != 0) {
                nonZeroCounts[i]++;
            }
            if (report[i] < minValues[i]) {
                minValues[i] = report[i];
            }
            if (report[i] > maxValues[i]) {
                maxValues[i] = report[i];
            }
        }
    }

    private static void PrintStats(int reportCount, int[] changedCounts, int[] nonZeroCounts, byte[] minValues, byte[] maxValues, byte[] first, byte[] last)
    {
        Console.WriteLine("Reports: " + reportCount);
        if (first != null) {
            Console.WriteLine("First: " + Hex(first, first.Length));
            Console.WriteLine("First SDL IMU: " + SdlImuSummary(first));
            Console.WriteLine("First raw IMU: " + RawImuSummary(first));
        }
        if (last != null) {
            Console.WriteLine("Last : " + Hex(last, last.Length));
            Console.WriteLine("Last SDL IMU : " + SdlImuSummary(last));
            Console.WriteLine("Last raw IMU : " + RawImuSummary(last));
        }
        Console.WriteLine("Changed byte summary:");
        for (int i = 0; i < changedCounts.Length; i++) {
            if (changedCounts[i] == 0 && nonZeroCounts[i] == 0) {
                continue;
            }
            Console.WriteLine("  [{0:00}/0x{0:x2}] changed={1} nonzero={2} min=0x{3:x2} max=0x{4:x2}",
                i,
                changedCounts[i],
                nonZeroCounts[i],
                minValues[i],
                maxValues[i]);
        }
    }

    private static string ChangedIndexes(byte[] last, byte[] report, int count)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < count; i++) {
            if (last[i] == report[i]) {
                continue;
            }
            if (sb.Length > 0) {
                sb.Append(',');
            }
            sb.Append(i.ToString());
        }
        return sb.Length == 0 ? "-" : sb.ToString();
    }

    private static string SdlImuSummary(byte[] report)
    {
        if (report.Length < 0x3d) {
            return "short";
        }

        uint timestamp = ReadU32Le(report, 0x2b);
        short accelX = ReadI16Le(report, 0x31);
        short accelZ = ReadI16Le(report, 0x33);
        short accelY = ReadI16Le(report, 0x35);
        short gyroX = ReadI16Le(report, 0x37);
        short gyroZ = ReadI16Le(report, 0x39);
        short gyroY = ReadI16Le(report, 0x3b);
        return string.Format("ts={0} accel(x,y,z)=({1},{2},{3}) gyro(x,y,z)=({4},{5},{6}) sample={7}",
            timestamp,
            accelX,
            accelY,
            accelZ,
            gyroX,
            gyroY,
            gyroZ,
            HexSlice(report, 0x31, 12));
    }

    private static string RawImuSummary(byte[] report)
    {
        if (report.Length < 0x3d) {
            return "short";
        }

        short accelX = ReadI16Le(report, 0x31);
        short accelY = ReadI16Le(report, 0x33);
        short accelZ = ReadI16Le(report, 0x35);
        short gyroX = ReadI16Le(report, 0x37);
        short gyroY = ReadI16Le(report, 0x39);
        short gyroZ = ReadI16Le(report, 0x3b);
        return string.Format("accel(x,y,z)=({0},{1},{2}) gyro(x,y,z)=({3},{4},{5}) sample={6}",
            accelX,
            accelY,
            accelZ,
            gyroX,
            gyroY,
            gyroZ,
            HexSlice(report, 0x31, 12));
    }

    private sealed class SdlImuStats
    {
        private int count;
        private long gyroXSum;
        private long gyroYSum;
        private long gyroZSum;
        private long gyroXSquareSum;
        private long gyroYSquareSum;
        private long gyroZSquareSum;
        private short gyroXMin = short.MaxValue;
        private short gyroYMin = short.MaxValue;
        private short gyroZMin = short.MaxValue;
        private short gyroXMax = short.MinValue;
        private short gyroYMax = short.MinValue;
        private short gyroZMax = short.MinValue;

        public void Add(byte[] report, int read)
        {
            if (read < 0x3d) {
                return;
            }

            short gyroX = ReadI16Le(report, 0x37);
            short gyroZ = ReadI16Le(report, 0x39);
            short gyroY = ReadI16Le(report, 0x3b);
            count++;
            gyroXSum += gyroX;
            gyroYSum += gyroY;
            gyroZSum += gyroZ;
            gyroXSquareSum += (long)gyroX * gyroX;
            gyroYSquareSum += (long)gyroY * gyroY;
            gyroZSquareSum += (long)gyroZ * gyroZ;
            if (gyroX < gyroXMin) gyroXMin = gyroX;
            if (gyroY < gyroYMin) gyroYMin = gyroY;
            if (gyroZ < gyroZMin) gyroZMin = gyroZ;
            if (gyroX > gyroXMax) gyroXMax = gyroX;
            if (gyroY > gyroYMax) gyroYMax = gyroY;
            if (gyroZ > gyroZMax) gyroZMax = gyroZ;
        }

        public void Print()
        {
            if (count == 0) {
                Console.WriteLine("SDL IMU stats: none");
                return;
            }

            Console.WriteLine("SDL gyro stats: count={0} mean=({1:F1},{2:F1},{3:F1}) rms=({4:F1},{5:F1},{6:F1}) rangeX={7}..{8} rangeY={9}..{10} rangeZ={11}..{12}",
                count,
                Mean(gyroXSum),
                Mean(gyroYSum),
                Mean(gyroZSum),
                Rms(gyroXSquareSum),
                Rms(gyroYSquareSum),
                Rms(gyroZSquareSum),
                gyroXMin,
                gyroXMax,
                gyroYMin,
                gyroYMax,
                gyroZMin,
                gyroZMax);
        }

        private double Mean(long sum)
        {
            return (double)sum / count;
        }

        private double Rms(long squareSum)
        {
            return Math.Sqrt((double)squareSum / count);
        }
    }

    private static uint ReadU32Le(byte[] data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }

    private static short ReadI16Le(byte[] data, int offset)
    {
        return unchecked((short)(data[offset] | (data[offset + 1] << 8)));
    }

    private static string HexSlice(byte[] data, int offset, int count)
    {
        StringBuilder sb = new StringBuilder();
        int end = Math.Min(data.Length, offset + count);
        for (int i = offset; i < end; i++) {
            if (sb.Length > 0) {
                sb.Append(' ');
            }
            sb.Append(data[i].ToString("x2"));
        }
        return sb.ToString();
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
