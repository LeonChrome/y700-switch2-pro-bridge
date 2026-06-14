using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private static string targetVidPid = "vid_054c&pid_0ce6";
    private static readonly object LogLock = new();
    private static StreamWriter? log;
    private static CancellationTokenSource? cancellation;

    public static async Task<int> Main(string[] args)
    {
        Options options = Options.Parse(args);
        targetVidPid = options.TargetVidPid;
        string outputDirectory = options.OutputDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                         "DualSenseHostTrace_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(outputDirectory);
        string logPath = Path.Combine(outputDirectory, "host_trace.log");
        log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        if (!Console.IsInputRedirected)
        {
            _ = Task.Run(() =>
            {
                Console.WriteLine("Press ENTER after the failure has been captured.");
                Console.ReadLine();
                cancellation.Cancel();
            });
        }

        Log("trace_start",
            ("version", "5.9.2"),
            ("pid", Environment.ProcessId),
            ("duration_seconds", options.DurationSeconds),
            ("rumble_test", options.RumbleTest),
            ("target", targetVidPid),
            ("output", outputDirectory));
        WriteEnvironment(outputDirectory);

        using var deadline = options.DurationSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds))
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellation.Token,
            deadline.Token);

        Task pnpTask = MonitorPnpAsync(linked.Token);
        Task hidTask = MonitorHidAsync(options, linked.Token);
        Task foregroundTask = MonitorForegroundProcessAsync(linked.Token);
        Task serialTask = MonitorSerialAsync(options, linked.Token);

        try
        {
            await Task.WhenAll(pnpTask, hidTask, serialTask, foregroundTask);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Log("trace_stop");
            log.Dispose();
        }

        Console.WriteLine("Trace saved to: " + outputDirectory);
        return 0;
    }

    private static async Task MonitorPnpAsync(CancellationToken token)
    {
        string previous = "";
        DateTime nextHeartbeat = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            string snapshot;
            try
            {
                snapshot = QueryPnpSnapshot();
            }
            catch (Exception ex)
            {
                snapshot = "query_error=" + ex.Message;
            }

            if (!string.Equals(previous, snapshot, StringComparison.Ordinal) ||
                DateTime.UtcNow >= nextHeartbeat)
            {
                Log("pnp_snapshot", ("devices", snapshot));
                previous = snapshot;
                nextHeartbeat = DateTime.UtcNow.AddSeconds(5);
            }
            await Task.Delay(500, token);
        }
    }

    private static async Task MonitorForegroundProcessAsync(CancellationToken token)
    {
        string previousKey = "";
        DateTime nextHeartbeatUtc = DateTime.MinValue;

        while (!token.IsCancellationRequested)
        {
            IntPtr window = GetForegroundWindow();
            uint processId = 0;
            string processName = "";

            if (window != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(window, out processId);

                try
                {
                    using Process process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch
                {
                }
            }

            string key = $"{processId}|{processName}";
            if (!string.Equals(previousKey, key, StringComparison.Ordinal) ||
                DateTime.UtcNow >= nextHeartbeatUtc)
            {
                Log("foreground_process",
                    ("pid", processId),
                    ("process", processName));
                previousKey = key;
                nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(30);
            }

            await Task.Delay(500, token);
        }
    }

    private static async Task MonitorHidAsync(Options options, CancellationToken token)
    {
        long totalReports = 0;
        long sessionReports = 0;
        int openGeneration = 0;
        DateTime lastReportUtc = DateTime.MinValue;
        DateTime lastHeartbeatUtc = DateTime.MinValue;

        while (!token.IsCancellationRequested)
        {
            string? path = HidApi.FindTargetPath();
            if (path == null)
            {
                Log("hid_absent", ("total_reports", totalReports));
                await Task.Delay(500, token);
                continue;
            }

            openGeneration++;
            sessionReports = 0;
            try
            {
                bool writeAccess = options.RumbleTest;
                SafeFileHandle handle = HidApi.Open(path, writeAccess);
                if (handle.IsInvalid && options.RumbleTest)
                {
                    handle.Dispose();
                    writeAccess = false;
                    handle = HidApi.Open(path, writeAccess: false);
                }
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    Log("hid_open_failed", ("path", path), ("win32", Marshal.GetLastWin32Error()));
                    await Task.Delay(500, token);
                    continue;
                }

                using (handle)
                using (var stream = new FileStream(
                           handle,
                           writeAccess ? FileAccess.ReadWrite : FileAccess.Read,
                           bufferSize: 64,
                           isAsync: true))
                {
                    Log("hid_open",
                        ("generation", openGeneration),
                        ("path", path),
                        ("write_access", writeAccess));

                    if (options.RumbleTest && writeAccess)
                    {
                        await SendRumbleProbeAsync(stream, token);
                        options.RumbleTest = false;
                    }

                    byte[] buffer = new byte[64];
                    while (!token.IsCancellationRequested)
                    {
                        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        readTimeout.CancelAfter(1000);
                        int read;
                        try
                        {
                            read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), readTimeout.Token);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            long ageMs = lastReportUtc == DateTime.MinValue
                                ? -1
                                : (long)(DateTime.UtcNow - lastReportUtc).TotalMilliseconds;
                            Log("hid_read_timeout",
                                ("generation", openGeneration),
                                ("session_reports", sessionReports),
                                ("total_reports", totalReports),
                                ("last_report_age_ms", ageMs),
                                ("pnp_present", HidApi.FindTargetPath() != null));
                            continue;
                        }

                        if (read <= 0)
                        {
                            Log("hid_read_eof", ("generation", openGeneration), ("read", read));
                            break;
                        }

                        totalReports++;
                        sessionReports++;
                        lastReportUtc = DateTime.UtcNow;
                        if (sessionReports == 1 || DateTime.UtcNow >= lastHeartbeatUtc)
                        {
                            Log("hid_input",
                                ("generation", openGeneration),
                                ("bytes", read),
                                ("report_id", buffer[0]),
                                ("session_reports", sessionReports),
                                ("total_reports", totalReports),
                                ("preview", Convert.ToHexString(buffer.AsSpan(0, Math.Min(read, 16)))));
                            lastHeartbeatUtc = DateTime.UtcNow.AddSeconds(2);
                        }
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Log("hid_exception",
                    ("generation", openGeneration),
                    ("type", ex.GetType().Name),
                    ("message", ex.Message),
                    ("session_reports", sessionReports),
                    ("pnp_present", HidApi.FindTargetPath() != null));
                await Task.Delay(250, token);
            }
        }
    }

    private static async Task SendRumbleProbeAsync(FileStream stream, CancellationToken token)
    {
        byte[] on = new byte[48];
        on[0] = 0x02;
        on[1] = 0x03;
        on[2] = 0x15;
        on[3] = 0x70;
        on[4] = 0xb0;
        byte[] off = new byte[48];
        off[0] = 0x02;
        off[1] = 0x03;
        off[2] = 0x15;

        for (int pulse = 0; pulse < 3; pulse++)
        {
            await stream.WriteAsync(on.AsMemory(), token);
            await stream.FlushAsync(token);
            Log("hid_rumble_write",
                ("pulse", pulse),
                ("state", "on"),
                ("bytes", on.Length),
                ("preview", Convert.ToHexString(on.AsSpan(0, 8))));
            await Task.Delay(450, token);
            await stream.WriteAsync(off.AsMemory(), token);
            await stream.FlushAsync(token);
            Log("hid_rumble_write",
                ("pulse", pulse),
                ("state", "off"),
                ("bytes", off.Length),
                ("preview", Convert.ToHexString(off.AsSpan(0, 8))));
            await Task.Delay(250, token);
        }
    }

    private static async Task MonitorSerialAsync(Options options, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string? portName = options.ComPort ?? FindCh343Port();
            if (string.IsNullOrWhiteSpace(portName))
            {
                Log("serial_absent");
                await Task.Delay(1000, token);
                continue;
            }

            try
            {
                using var port = new SerialPort(portName, 115200)
                {
                    DtrEnable = false,
                    RtsEnable = false,
                    ReadTimeout = 250,
                    WriteTimeout = 1000,
                    NewLine = "\n"
                };
                port.Open();
                Log("serial_open", ("port", portName));
                port.WriteLine("status diag");
                DateTime readDeadline = DateTime.UtcNow.AddMilliseconds(650);
                while (!token.IsCancellationRequested &&
                       port.IsOpen &&
                       DateTime.UtcNow < readDeadline)
                {
                    try
                    {
                        string line = port.ReadLine().Trim();
                        if (line.Length == 0)
                        {
                            continue;
                        }
                        Log("serial_rx", ("port", portName), ("line", StripAnsi(line)));
                    }
                    catch (TimeoutException)
                    {
                    }
                }
                port.Close();
                Log("serial_close", ("port", portName));
                await Task.Delay(1350, token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Log("serial_exception",
                    ("port", portName),
                    ("type", ex.GetType().Name),
                    ("message", ex.Message));
                await Task.Delay(750, token);
            }
        }
    }

    private static string QueryPnpSnapshot()
    {
        var devices = new List<object>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name,Status,PNPClass,DeviceID,ConfigManagerErrorCode FROM Win32_PnPEntity");
        foreach (ManagementObject item in searcher.Get())
        {
            string id = Convert.ToString(item["DeviceID"]) ?? "";
            string name = Convert.ToString(item["Name"]) ?? "";
            if (!id.Contains(targetVidPid, StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            devices.Add(new
            {
                name,
                status = Convert.ToString(item["Status"]) ?? "",
                pnpClass = Convert.ToString(item["PNPClass"]) ?? "",
                id,
                error = Convert.ToUInt32(item["ConfigManagerErrorCode"] ?? 0)
            });
        }
        return JsonSerializer.Serialize(devices);
    }

    private static string? FindCh343Port()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name,Manufacturer,DeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports'");
        foreach (ManagementObject item in searcher.Get())
        {
            string name = Convert.ToString(item["Name"]) ?? "";
            string manufacturer = Convert.ToString(item["Manufacturer"]) ?? "";
            string id = Convert.ToString(item["DeviceID"]) ?? "";
            if (!name.Contains("CH343", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("CH340", StringComparison.OrdinalIgnoreCase) &&
                !manufacturer.Contains("WCH", StringComparison.OrdinalIgnoreCase) &&
                !id.Contains("VID_1A86&PID_55D3", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Match match = Regex.Match(name, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        return null;
    }

    private static void WriteEnvironment(string outputDirectory)
    {
        var data = new
        {
            machine = Environment.MachineName,
            os = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            process_arch = RuntimeInformation.ProcessArchitecture.ToString(),
            admin = IsAdministrator(),
            pnp = QueryPnpSnapshot(),
            hid_paths = HidApi.FindAllTargetPaths(),
            ports = SerialPort.GetPortNames()
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "environment.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

        string setupApi = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "INF",
            "setupapi.dev.log");
        if (File.Exists(setupApi))
        {
            try
            {
                File.Copy(setupApi, Path.Combine(outputDirectory, "setupapi.dev.start.log"), true);
            }
            catch (Exception ex)
            {
                Log("setupapi_copy_failed", ("message", ex.Message));
            }
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static string StripAnsi(string value) =>
        Regex.Replace(value, @"\x1B\[[0-?]*[ -/]*[@-~]", "");

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    private static void Log(string kind, params (string Name, object? Value)[] fields)
    {
        var record = new Dictionary<string, object?>
        {
            ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            ["utc"] = DateTime.UtcNow.ToString("O"),
            ["kind"] = kind
        };
        foreach ((string name, object? value) in fields)
        {
            record[name] = value;
        }
        string line = JsonSerializer.Serialize(record);
        lock (LogLock)
        {
            Console.WriteLine(line);
            log?.WriteLine(line);
        }
    }

    private sealed class Options
    {
        public int DurationSeconds { get; private set; } = 1800;
        public string? OutputDirectory { get; private set; }
        public string? ComPort { get; private set; }
        public bool RumbleTest { get; set; }
        public string TargetVidPid { get; private set; } = "vid_054c&pid_0ce6";

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--rumble-test", StringComparison.OrdinalIgnoreCase))
                {
                    options.RumbleTest = true;
                }
                else if (i + 1 < args.Length &&
                         arg.Equals("--duration-seconds", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(args[++i], out int duration))
                {
                    options.DurationSeconds = Math.Max(0, duration);
                }
                else if (i + 1 < args.Length &&
                         arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
                {
                    options.OutputDirectory = args[++i];
                }
                else if (i + 1 < args.Length &&
                         arg.Equals("--com", StringComparison.OrdinalIgnoreCase))
                {
                    options.ComPort = args[++i];
                }
                else if (i + 1 < args.Length &&
                         arg.Equals("--vidpid", StringComparison.OrdinalIgnoreCase))
                {
                    string value = args[++i].Trim().ToLowerInvariant()
                        .Replace("vid_", "")
                        .Replace("pid_", "")
                        .Replace("&", ":");
                    string[] parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        options.TargetVidPid = "vid_" + parts[0] + "&pid_" + parts[1];
                    }
                }
            }
            return options;
        }
    }

    private static class HidApi
    {
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        public static string? FindTargetPath() => FindAllTargetPaths().FirstOrDefault();

        public static IReadOnlyList<string> FindAllTargetPaths()
        {
            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr infoSet = SetupDiGetClassDevs(
                ref hidGuid,
                null,
                IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);
            if (infoSet == InvalidHandleValue)
            {
                return Array.Empty<string>();
            }

            var paths = new List<string>();
            try
            {
                uint index = 0;
                while (true)
                {
                    var interfaceData = new SpDeviceInterfaceData
                    {
                        Size = Marshal.SizeOf<SpDeviceInterfaceData>()
                    };
                    if (!SetupDiEnumDeviceInterfaces(
                            infoSet,
                            IntPtr.Zero,
                            ref hidGuid,
                            index,
                            ref interfaceData))
                    {
                        break;
                    }

                    SetupDiGetDeviceInterfaceDetail(
                        infoSet,
                        ref interfaceData,
                        IntPtr.Zero,
                        0,
                        out uint requiredSize,
                        IntPtr.Zero);
                    IntPtr detail = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(
                                infoSet,
                                ref interfaceData,
                                detail,
                                requiredSize,
                                out _,
                                IntPtr.Zero))
                        {
                            string? path = Marshal.PtrToStringUni(detail + 4);
                            if (!string.IsNullOrWhiteSpace(path) &&
                                path.Contains(targetVidPid, StringComparison.OrdinalIgnoreCase))
                            {
                                paths.Add(path);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                    index++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(infoSet);
            }
            return paths;
        }

        public static SafeFileHandle Open(string path, bool writeAccess)
        {
            uint access = GenericRead | (writeAccess ? GenericWrite : 0);
            return CreateFile(
                path,
                access,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOverlapped,
                IntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            string? enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);
    }
}
