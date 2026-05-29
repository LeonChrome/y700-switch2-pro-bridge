using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Y700Switch2Manager;

public sealed class BulkControlClient : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint TransferTimeoutMs = 1000;
    private const string ControlMagic = "Y7CTL1";
    private const string ReplyMagic = "Y7RSP1";
    private static readonly Guid InterfaceGuid = new("6F13725E-EF0E-4FD3-AE5F-B2DE989EC825");

    private SafeFileHandle? file;
    private IntPtr winusb;
    private byte inPipe;
    private byte outPipe;

    public bool IsConnected => file is { IsInvalid: false, IsClosed: false } && winusb != IntPtr.Zero;
    public string? DevicePath { get; private set; }

    public void Connect()
    {
        Disconnect();
        string path = FindDevicePath();
        SafeFileHandle opened = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, FileAttributeNormal | FileFlagOverlapped, IntPtr.Zero);
        if (opened.IsInvalid)
        {
            ThrowLast("CreateFile");
        }

        if (!WinUsb_Initialize(opened, out IntPtr handle))
        {
            opened.Dispose();
            ThrowLast("WinUsb_Initialize");
        }

        try
        {
            if (!WinUsb_QueryInterfaceSettings(handle, 0, out UsbInterfaceDescriptor iface))
            {
                ThrowLast("WinUsb_QueryInterfaceSettings");
            }

            byte foundIn = 0;
            byte foundOut = 0;
            for (byte i = 0; i < iface.EndpointCount; i++)
            {
                if (!WinUsb_QueryPipe(handle, 0, i, out WinUsbPipeInformation pipe))
                {
                    ThrowLast("WinUsb_QueryPipe");
                }

                if ((pipe.PipeId & 0x80) != 0)
                {
                    foundIn = pipe.PipeId;
                }
                else
                {
                    foundOut = pipe.PipeId;
                }
            }

            if (foundIn == 0 || foundOut == 0)
            {
                throw new InvalidOperationException("Native USB bulk interface is missing IN or OUT pipe.");
            }

            file = opened;
            winusb = handle;
            inPipe = foundIn;
            outPipe = foundOut;
            DevicePath = path;
        }
        catch
        {
            WinUsb_Free(handle);
            opened.Dispose();
            throw;
        }
    }

    public void Disconnect()
    {
        if (winusb != IntPtr.Zero)
        {
            WinUsb_Free(winusb);
            winusb = IntPtr.Zero;
        }
        file?.Dispose();
        file = null;
        DevicePath = null;
        inPipe = 0;
        outPipe = 0;
    }

    public string SendCommand(string command)
    {
        if (!IsConnected || file == null)
        {
            throw new InvalidOperationException("Native USB bulk control is not connected.");
        }

        byte[] commandBytes = Encoding.ASCII.GetBytes(command.TrimEnd());
        byte[] request = new byte[ControlMagic.Length + commandBytes.Length];
        byte[] magicBytes = Encoding.ASCII.GetBytes(ControlMagic);
        Buffer.BlockCopy(magicBytes, 0, request, 0, magicBytes.Length);
        Buffer.BlockCopy(commandBytes, 0, request, ControlMagic.Length, commandBytes.Length);
        WritePipeAsync(file, winusb, outPipe, request, (uint)request.Length);

        byte[] reply = ReadReply(file, winusb, inPipe);
        return Encoding.UTF8.GetString(reply);
    }

    private static byte[] ReadReply(SafeFileHandle file, IntPtr winusb, byte pipeId)
    {
        byte[] received = new byte[4096];
        int used = 0;
        int expected = -1;

        while (used < received.Length)
        {
            byte[] chunk = new byte[512];
            uint n = ReadPipeAsync(file, winusb, pipeId, chunk, (uint)chunk.Length);
            if (n == 0)
            {
                continue;
            }

            int copy = Math.Min((int)n, received.Length - used);
            Buffer.BlockCopy(chunk, 0, received, used, copy);
            used += copy;

            if (expected < 0 && used >= 8)
            {
                string magic = Encoding.ASCII.GetString(received, 0, ReplyMagic.Length);
                if (!string.Equals(magic, ReplyMagic, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Unexpected native USB bulk reply magic: " + magic);
                }
                expected = 8 + received[6] + (received[7] << 8);
            }

            if (expected >= 0 && used >= expected)
            {
                byte[] json = new byte[expected - 8];
                Buffer.BlockCopy(received, 8, json, 0, json.Length);
                return json;
            }
        }

        throw new InvalidOperationException("Native USB bulk reply exceeded local receive buffer.");
    }

    private static uint WritePipeAsync(SafeFileHandle file, IntPtr handle, byte pipeId, byte[] buffer, uint length) =>
        TransferAsync(file, "WinUsb_WritePipe", (IntPtr overlapped, out uint transferred) =>
            WinUsb_WritePipe(handle, pipeId, buffer, length, out transferred, overlapped));

    private static uint ReadPipeAsync(SafeFileHandle file, IntPtr handle, byte pipeId, byte[] buffer, uint length) =>
        TransferAsync(file, "WinUsb_ReadPipe", (IntPtr overlapped, out uint transferred) =>
            WinUsb_ReadPipe(handle, pipeId, buffer, length, out transferred, overlapped));

    private delegate bool PipeTransfer(IntPtr overlapped, out uint transferred);

    private static uint TransferAsync(SafeFileHandle file, string api, PipeTransfer transfer)
    {
        IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
        if (evt == IntPtr.Zero)
        {
            ThrowLast("CreateEvent");
        }

        IntPtr ovPtr = IntPtr.Zero;
        try
        {
            OverlappedNative ov = new() { EventHandle = evt };
            ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf<OverlappedNative>());
            Marshal.StructureToPtr(ov, ovPtr, false);

            if (transfer(ovPtr, out uint immediate))
            {
                return immediate;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ErrorIoPending)
            {
                throw new Win32Exception(err, api);
            }

            uint wait = WaitForSingleObject(evt, TransferTimeoutMs);
            if (wait == WaitTimeout)
            {
                CancelIoEx(file, ovPtr);
                throw new TimeoutException(api + " timed out");
            }
            if (wait != WaitObject0)
            {
                ThrowLast("WaitForSingleObject");
            }

            if (!GetOverlappedResult(file, ovPtr, out uint transferred, false))
            {
                ThrowLast("GetOverlappedResult");
            }
            return transferred;
        }
        finally
        {
            if (ovPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ovPtr);
            }
            CloseHandle(evt);
        }
    }

    private static string FindDevicePath()
    {
        const uint present = 0x00000002;
        const uint deviceInterface = 0x00000010;

        Guid guid = InterfaceGuid;
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

                if (detail.DevicePath.Contains("vid_057e&pid_2069&mi_01", StringComparison.OrdinalIgnoreCase))
                {
                    return detail.DevicePath;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        throw new InvalidOperationException("Native USB bulk control interface was not found.");
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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct UsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte EndpointCount;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte InterfaceStringIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinUsbPipeInformation
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlappedNative
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr EventHandle;
    }

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

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WinUsbPipeInformation pipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle fileHandle, IntPtr overlapped, out uint numberOfBytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle fileHandle, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
