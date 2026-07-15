using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Pro2CalibrationProbe;

internal static class Program
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorNoMoreItems = 259;
    private const int UsbdPipeTypeBulk = 2;
    private const uint PipeTransferTimeout = 3;
    private static readonly uint[] CalibrationAddresses =
    [
        0x13000, 0x13040, 0x13060, 0x13080,
        0x130C0, 0x13100, 0x1FC040, 0x1FC080
    ];

    private static int Main(string[] args)
    {
        try
        {
            string? instanceFilter = ReadArgument(args, "--instance-id");
            string? outputPath = ReadArgument(args, "--output");
            bool listOnly = args.Contains("--list", StringComparer.OrdinalIgnoreCase);
            if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("-h", StringComparer.OrdinalIgnoreCase))
            {
                PrintHelp();
                return 0;
            }

            IReadOnlyList<DeviceCandidate> candidates = DiscoverCandidates();
            foreach (DeviceCandidate candidate in candidates)
            {
                Console.WriteLine($"CANDIDATE instance={candidate.InstanceId} path={candidate.Path}");
            }
            if (listOnly)
            {
                return 0;
            }

            DeviceCandidate[] selected = candidates
                .Where(candidate => instanceFilter is null ||
                    candidate.InstanceId.Contains(instanceFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (selected.Length != 1)
            {
                Console.Error.WriteLine(
                    selected.Length == 0
                        ? "没有唯一的 Pro2 MI_01 WinUSB 候选。先运行 --list，连接真实有线 Pro2 后再试。"
                        : "发现多个候选。为避免读取错误实例，请使用 --instance-id 指定上面打印的实例。" );
                return 2;
            }

            ProbeResult result = ReadCalibration(selected[0]);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(result, jsonOptions);
            Console.WriteLine(json);
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                string fullPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, json);
                Console.Error.WriteLine("OUTPUT " + fullPath);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR " + ex);
            return 1;
        }
    }

    private static ProbeResult ReadCalibration(DeviceCandidate candidate)
    {
        using SafeFileHandle file = Native.CreateFile(
            candidate.Path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);
        if (file.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile MI_01 失败");
        }

        if (!Native.WinUsb_Initialize(file, out IntPtr usbHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize 失败");
        }
        try
        {
            (byte bulkIn, byte bulkOut) = FindBulkPipes(usbHandle);
            uint timeoutMs = 1500;
            Native.WinUsb_SetPipePolicy(usbHandle, bulkIn, PipeTransferTimeout, sizeof(uint), ref timeoutMs);
            Native.WinUsb_SetPipePolicy(usbHandle, bulkOut, PipeTransferTimeout, sizeof(uint), ref timeoutMs);

            var blocks = new List<CalibrationBlock>();
            byte sequence = 1;
            foreach (uint address in CalibrationAddresses)
            {
                byte[] command = BuildFlashRead(address, sequence++);
                if (!Native.WinUsb_WritePipe(usbHandle, bulkOut, command, command.Length, out uint written, IntPtr.Zero) ||
                    written != command.Length)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"写入 flash-read {address:X8} 失败");
                }

                byte[] response = ReadMatchingResponse(usbHandle, bulkIn, command[2], address);
                int declaredLength = response.Length > 8 ? response[8] : 0;
                int availableLength = Math.Max(0, response.Length - 16);
                int dataLength = Math.Min(declaredLength, availableLength);
                blocks.Add(new CalibrationBlock(
                    "0x" + address.ToString("X8"),
                    declaredLength,
                    Convert.ToHexString(response.AsSpan(0, Math.Min(16, response.Length))),
                    Convert.ToHexString(response.AsSpan(16, dataLength))));
            }

            return new ProbeResult(
                DateTimeOffset.Now,
                candidate.InstanceId,
                candidate.Path,
                "read_only_flash_command_0x02_0x01",
                bulkIn,
                bulkOut,
                blocks);
        }
        finally
        {
            Native.WinUsb_Free(usbHandle);
        }
    }

    private static byte[] ReadMatchingResponse(IntPtr usbHandle, byte bulkIn, byte sequence, uint address)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var buffer = new byte[512];
            if (!Native.WinUsb_ReadPipe(usbHandle, bulkIn, buffer, buffer.Length, out uint read, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 121)
                {
                    continue;
                }
                throw new Win32Exception(error, "读取 Bulk IN 响应失败");
            }
            if (read < 16)
            {
                continue;
            }
            Array.Resize(ref buffer, checked((int)read));
            uint responseAddress = BitConverter.ToUInt32(buffer, 12);
            if (buffer[0] == 0x02 && buffer[2] == sequence && buffer[3] == 0x01 && responseAddress == address)
            {
                return buffer;
            }
        }
        throw new TimeoutException($"未收到匹配的 flash-read 响应 address=0x{address:X8}");
    }

    private static (byte BulkIn, byte BulkOut) FindBulkPipes(IntPtr usbHandle)
    {
        if (!Native.WinUsb_QueryInterfaceSettings(usbHandle, 0, out UsbInterfaceDescriptor descriptor))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings 失败");
        }

        byte bulkIn = 0;
        byte bulkOut = 0;
        for (byte index = 0; index < descriptor.NumEndpoints; index++)
        {
            if (!Native.WinUsb_QueryPipe(usbHandle, 0, index, out WinUsbPipeInformation pipe) ||
                pipe.PipeType != UsbdPipeTypeBulk)
            {
                continue;
            }
            if ((pipe.PipeId & 0x80) != 0)
            {
                bulkIn = pipe.PipeId;
            }
            else
            {
                bulkOut = pipe.PipeId;
            }
        }
        if (bulkIn == 0 || bulkOut == 0)
        {
            throw new InvalidOperationException($"没有找到完整 Bulk 管道 in=0x{bulkIn:X2} out=0x{bulkOut:X2}");
        }
        return (bulkIn, bulkOut);
    }

    private static byte[] BuildFlashRead(uint address, byte sequence)
    {
        byte[] command =
        [
            0x02, 0x91, sequence, 0x01,
            0x00, 0x08, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        ];
        BitConverter.GetBytes(address).CopyTo(command, 12);
        return command;
    }

    private static IReadOnlyList<DeviceCandidate> DiscoverCandidates()
    {
        var results = new Dictionary<string, DeviceCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (Guid discoveredGuid in DiscoverInterfaceGuids())
        {
            Guid interfaceGuid = discoveredGuid;
            IntPtr info = Native.SetupDiGetClassDevs(
                ref interfaceGuid,
                null,
                IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);
            if (info == new IntPtr(-1))
            {
                continue;
            }
            try
            {
                for (uint index = 0; ; index++)
                {
                    var interfaceData = new SpDeviceInterfaceData
                    {
                        CbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
                    };
                    if (!Native.SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                    {
                        if (Marshal.GetLastWin32Error() == ErrorNoMoreItems)
                        {
                            break;
                        }
                        continue;
                    }

                    var deviceInfo = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };
                    Native.SetupDiGetDeviceInterfaceDetail(
                        info, ref interfaceData, IntPtr.Zero, 0, out uint required, ref deviceInfo);
                    IntPtr detail = Marshal.AllocHGlobal(checked((int)required));
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(
                                info, ref interfaceData, detail, required, out _, ref deviceInfo))
                        {
                            continue;
                        }
                        string? path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (string.IsNullOrWhiteSpace(path) ||
                            !path.Contains("vid_057e&pid_2069&mi_01", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        var instanceBuffer = new char[512];
                        Native.SetupDiGetDeviceInstanceId(info, ref deviceInfo, instanceBuffer, instanceBuffer.Length, out _);
                        string instanceId = new(instanceBuffer, 0, Array.IndexOf(instanceBuffer, '\0') is int end && end >= 0 ? end : instanceBuffer.Length);
                        results[path] = new DeviceCandidate(path, instanceId, interfaceGuid);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(info);
            }
        }
        return results.Values.OrderBy(candidate => candidate.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<Guid> DiscoverInterfaceGuids()
    {
        const string registryPath = @"SYSTEM\CurrentControlSet\Enum\USB\VID_057E&PID_2069&MI_01";
        var guids = new HashSet<Guid>();
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(registryPath);
        if (root != null)
        {
            foreach (string instanceName in root.GetSubKeyNames())
            {
                using RegistryKey? parameters = root.OpenSubKey(instanceName + @"\Device Parameters");
                object? raw = parameters?.GetValue("DeviceInterfaceGUIDs") ??
                              parameters?.GetValue("DeviceInterfaceGUID");
                IEnumerable<string> values = raw switch
                {
                    string value => [value],
                    string[] array => array,
                    _ => []
                };
                foreach (string value in values)
                {
                    if (Guid.TryParse(value, out Guid guid))
                    {
                        guids.Add(guid);
                    }
                }
            }
        }
        return guids;
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Pro2 Calibration Probe（只读）");
        Console.WriteLine("  --list                       列出 VID_057E/PID_2069 MI_01 WinUSB 候选");
        Console.WriteLine("  --instance-id <substring>    多候选时指定真实设备实例");
        Console.WriteLine("  --output <result.json>       保存校准区原始字节");
    }
}

internal sealed record DeviceCandidate(string Path, string InstanceId, Guid InterfaceGuid);
internal sealed record CalibrationBlock(string Address, int DeclaredLength, string HeaderHex, string DataHex);
internal sealed record ProbeResult(
    DateTimeOffset Timestamp,
    string InstanceId,
    string DevicePath,
    string Operation,
    byte BulkIn,
    byte BulkOut,
    IReadOnlyList<CalibrationBlock> Blocks);

[StructLayout(LayoutKind.Sequential)]
internal struct SpDeviceInterfaceData
{
    public int CbSize;
    public Guid InterfaceClassGuid;
    public int Flags;
    public IntPtr Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpDevinfoData
{
    public int CbSize;
    public Guid ClassGuid;
    public uint DevInst;
    public IntPtr Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UsbInterfaceDescriptor
{
    public byte Length;
    public byte DescriptorType;
    public byte InterfaceNumber;
    public byte AlternateSetting;
    public byte NumEndpoints;
    public byte InterfaceClass;
    public byte InterfaceSubClass;
    public byte InterfaceProtocol;
    public byte Interface;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WinUsbPipeInformation
{
    public int PipeType;
    public byte PipeId;
    public ushort MaximumPacketSize;
    public byte Interval;
}

internal static partial class Native
{
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr detailData, uint detailDataSize, out uint requiredSize,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        [Out] char[] deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_QueryInterfaceSettings(
        IntPtr interfaceHandle, byte alternateInterfaceNumber, out UsbInterfaceDescriptor descriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_QueryPipe(
        IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex,
        out WinUsbPipeInformation pipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_SetPipePolicy(
        IntPtr interfaceHandle, byte pipeId, uint policyType, int valueLength, ref uint value);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_WritePipe(
        IntPtr interfaceHandle, byte pipeId, byte[] buffer, int bufferLength,
        out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle, byte pipeId, byte[] buffer, int bufferLength,
        out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Free(IntPtr interfaceHandle);
}
