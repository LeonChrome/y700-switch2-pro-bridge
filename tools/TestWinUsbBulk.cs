using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class TestWinUsbBulk
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
    private const uint XFER_TIMEOUT_MS = 100;
    private static readonly Guid InterfaceGuid = new Guid("6F13725E-EF0E-4FD3-AE5F-B2DE989EC825");

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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
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

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle DeviceHandle, out IntPtr InterfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr InterfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryInterfaceSettings(IntPtr InterfaceHandle, byte AlternateInterfaceNumber, out USB_INTERFACE_DESCRIPTOR UsbAltInterfaceDescriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryPipe(IntPtr InterfaceHandle, byte AlternateInterfaceNumber, byte PipeIndex, out WINUSB_PIPE_INFORMATION PipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);

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
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.GetType().FullName);
            Console.WriteLine("Message: " + ex.Message);
            Console.WriteLine("HResult: 0x" + ex.HResult.ToString("x8"));
            if (ex is Win32Exception)
            {
                Console.WriteLine("NativeErrorCode: " + ((Win32Exception)ex).NativeErrorCode);
            }
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        bool single80 = args.Length > 0 && string.Equals(args[0], "single80", StringComparison.OrdinalIgnoreCase);
        string path = FindDevicePath();
        Console.WriteLine("Path: " + path);

        using (SafeFileHandle file = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, IntPtr.Zero))
        {
            if (file.IsInvalid) ThrowLast("CreateFile");

            IntPtr winusb;
            if (!WinUsb_Initialize(file, out winusb)) ThrowLast("WinUsb_Initialize");
            try
            {
                USB_INTERFACE_DESCRIPTOR iface;
                if (!WinUsb_QueryInterfaceSettings(winusb, 0, out iface)) ThrowLast("WinUsb_QueryInterfaceSettings");
                Console.WriteLine("Interface {0}, endpoints {1}, class 0x{2:x2}", iface.bInterfaceNumber, iface.bNumEndpoints, iface.bInterfaceClass);

                byte inPipe = 0, outPipe = 0;
                for (byte i = 0; i < iface.bNumEndpoints; i++)
                {
                    WINUSB_PIPE_INFORMATION pipe;
                    if (!WinUsb_QueryPipe(winusb, 0, i, out pipe)) ThrowLast("WinUsb_QueryPipe");
                    Console.WriteLine("Pipe {0}: id=0x{1:x2}, type={2}, max={3}", i, pipe.PipeId, pipe.PipeType, pipe.MaximumPacketSize);
                    if ((pipe.PipeId & 0x80) != 0) inPipe = pipe.PipeId; else outPipe = pipe.PipeId;
                }

                if (inPipe == 0 || outPipe == 0) throw new InvalidOperationException("Missing bulk IN/OUT pipes");

                byte[] cmd = new byte[16];
                cmd[0] = 0x02;
                cmd[1] = 0x91;
                cmd[12] = 0x00;
                cmd[13] = 0x30;
                cmd[14] = 0x01;
                cmd[15] = 0x00;

                uint written = WritePipeAsync(file, winusb, outPipe, cmd, (uint)cmd.Length);
                Console.WriteLine("Wrote {0} bytes to 0x{1:x2}", written, outPipe);

                if (single80)
                {
                    byte[] reply = new byte[80];
                    uint read = ReadPipeAsync(file, winusb, inPipe, reply, 80);
                    Console.WriteLine("Read single {0} bytes from 0x{1:x2}: {2}", read, inPipe, Hex(reply, (int)read));
                }
                else
                {
                    byte[] reply = new byte[80];
                    uint firstRead = ReadPipeAsync(file, winusb, inPipe, reply, 64);
                    Console.WriteLine("Read first {0} bytes from 0x{1:x2}: {2}", firstRead, inPipe, Hex(reply, (int)firstRead));

                    if (firstRead == 64)
                    {
                        byte[] tail = new byte[16];
                        uint secondRead = ReadPipeAsync(file, winusb, inPipe, tail, 16);
                        Console.WriteLine("Read second {0} bytes from 0x{1:x2}: {2}", secondRead, inPipe, Hex(tail, (int)secondRead));
                    }
                }
            }
            finally
            {
                WinUsb_Free(winusb);
            }
        }

        return 0;
    }

    private static uint WritePipeAsync(SafeFileHandle file, IntPtr winusb, byte pipeId, byte[] buffer, uint length)
    {
        return TransferAsync(file, "WinUsb_WritePipe", delegate(IntPtr overlapped, out uint transferred)
        {
            return WinUsb_WritePipe(winusb, pipeId, buffer, length, out transferred, overlapped);
        });
    }

    private static uint ReadPipeAsync(SafeFileHandle file, IntPtr winusb, byte pipeId, byte[] buffer, uint length)
    {
        return TransferAsync(file, "WinUsb_ReadPipe", delegate(IntPtr overlapped, out uint transferred)
        {
            return WinUsb_ReadPipe(winusb, pipeId, buffer, length, out transferred, overlapped);
        });
    }

    private delegate bool PipeTransfer(IntPtr overlapped, out uint transferred);

    private static uint TransferAsync(SafeFileHandle file, string api, PipeTransfer transfer)
    {
        IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
        if (evt == IntPtr.Zero) ThrowLast("CreateEvent");

        IntPtr ovPtr = IntPtr.Zero;
        try
        {
            OVERLAPPED ov = new OVERLAPPED();
            ov.hEvent = evt;
            ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OVERLAPPED)));
            Marshal.StructureToPtr(ov, ovPtr, false);

            uint immediate;
            if (transfer(ovPtr, out immediate))
            {
                return immediate;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_IO_PENDING)
            {
                throw new Win32Exception(err, api);
            }

            uint wait = WaitForSingleObject(evt, XFER_TIMEOUT_MS);
            if (wait == WAIT_TIMEOUT)
            {
                CancelIoEx(file, ovPtr);
                throw new TimeoutException(api + " timed out");
            }
            if (wait != WAIT_OBJECT_0)
            {
                ThrowLast("WaitForSingleObject");
            }

            uint transferred;
            if (!GetOverlappedResult(file, ovPtr, out transferred, false))
            {
                ThrowLast("GetOverlappedResult");
            }
            return transferred;
        }
        finally
        {
            if (ovPtr != IntPtr.Zero) Marshal.FreeHGlobal(ovPtr);
            CloseHandle(evt);
        }
    }

    private static string FindDevicePath()
    {
        const uint DIGCF_PRESENT = 0x00000002;
        const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        Guid guid = InterfaceGuid;
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

                if (detail.DevicePath.IndexOf("vid_057e&pid_2069&mi_01", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return detail.DevicePath;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        throw new InvalidOperationException("Device interface not found");
    }

    private static void ThrowLast(string api)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), api);
    }

    private static string Hex(byte[] data, int count)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
