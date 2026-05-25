using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

public sealed class Y700Switch2Launcher
{
    private const string DefaultControllerAddress = "38:C6:CE:27:FC:2D";
    private const string RemoteBleJar = "/data/local/tmp/switch2_ble_bridge_v3.jar";
    private const string RemoteResponderJar = "/data/local/tmp/switch2_ffs_responder_v3.jar";
    private const string RemoteSetup = "/data/local/tmp/setup_y700_switch2_proto_v3.sh";

    private readonly Options options;
    private readonly string rootDir;
    private readonly string artifactDir;
    private string adbPath;
    private string serial;

    public static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            if (options.Help)
            {
                PrintUsage();
                return 0;
            }

            Y700Switch2Launcher launcher = new Y700Switch2Launcher(options);
            return launcher.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private Y700Switch2Launcher(Options options)
    {
        this.options = options;
        rootDir = FindRootDir(AppDomain.CurrentDomain.BaseDirectory);
        artifactDir = FindArtifactDir(rootDir, AppDomain.CurrentDomain.BaseDirectory);
    }

    private int Run()
    {
        adbPath = FindAdb(options.AdbPath);
        Console.WriteLine("ADB: " + adbPath);
        Console.WriteLine("Root: " + rootDir);
        Console.WriteLine("Artifacts: " + artifactDir);

        if (options.Command == "logs")
        {
            serial = SelectDevice(options.Serial, true);
            PullLogs();
            return 0;
        }

        if (options.Command == "status")
        {
            serial = SelectDevice(options.Serial, true);
            Status();
            return 0;
        }

        if (options.Command == "haptic-test")
        {
            serial = SelectDevice(options.Serial, true);
            HapticTest();
            return 0;
        }

        if (options.Command == "stop")
        {
            serial = SelectDevice(options.Serial, true);
            Stop();
            return 0;
        }

        if (options.Command != "start")
        {
            throw new InvalidOperationException("Unknown command: " + options.Command);
        }

        serial = SelectDevice(options.Serial, true);
        Console.WriteLine("Device: " + serial);
        WarnIfUsbOnly(serial);
        CheckRoot();
        PushArtifacts();

        if (!options.SkipGadget)
        {
            StartResponderAndGadget();
        }
        else
        {
            Console.WriteLine("Skipping USB gadget/responder setup.");
        }

        StartBleBridge();
        Thread.Sleep(3000);
        Status();
        Console.WriteLine();
        Console.WriteLine("Started. Keep the Switch 2 Pro Controller awake/connected.");
        Console.WriteLine("Use this for a quick vibration sanity check:");
        Console.WriteLine("  " + Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName) + " haptic-test --serial " + serial);
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Y700Switch2Launcher.exe [start|status|logs|haptic-test|stop] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  start        Find Y700, deploy stable v3 artifacts, start USB gadget and BLE bridge.");
        Console.WriteLine("  status       Print current Y700 gadget/process/log status.");
        Console.WriteLine("  logs         Pull v3 runtime logs into logs\\launcher_YYYYMMDD_HHMMSS.");
        Console.WriteLine("  haptic-test  Send play-hd to the v3 BLE bridge.");
        Console.WriteLine("  stop         Stop v3 bridge/responder processes.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --adb <path>          adb.exe path.");
        Console.WriteLine("  --serial <serial>     adb serial, e.g. 192.168.31.107:35929.");
        Console.WriteLine("  --controller <mac>    Switch 2 Pro BLE address.");
        Console.WriteLine("  --skip-gadget         Start BLE bridge without reconfiguring USB gadget.");
    }

    private void CheckRoot()
    {
        Console.WriteLine("Checking root...");
        CommandResult result = Adb("shell", "su", "-c", "id");
        if (result.ExitCode != 0 || result.StdOut.IndexOf("uid=0", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException("Y700 root/su is required. Output: " + result.AllText.Trim());
        }
        Console.WriteLine(result.StdOut.Trim());
    }

    private void PushArtifacts()
    {
        Console.WriteLine("Deploying stable v3 artifacts...");
        Push("switch2_ble_bridge_v3.jar", RemoteBleJar);
        Push("switch2_ffs_responder_v3.jar", RemoteResponderJar);
        Push("setup_y700_switch2_proto_v3.sh", RemoteSetup);
        AdbChecked("shell", "su", "-c", "chmod 755 " + RemoteSetup);
    }

    private void Push(string fileName, string remote)
    {
        string path = Path.Combine(artifactDir, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Missing artifact: " + path);
        }
        AdbChecked("push", path, remote);
    }

    private void StartResponderAndGadget()
    {
        Console.WriteLine("Starting Nintendo USB gadget/responder v3...");
        CommandResult result = Adb("shell", "su", "-c", "sh " + RemoteSetup);
        Console.Write(result.StdOut);
        if (result.ExitCode != 0)
        {
            Console.Error.Write(result.StdErr);
            throw new InvalidOperationException("USB gadget setup failed.");
        }
    }

    private void StartBleBridge()
    {
        Console.WriteLine("Starting BLE bridge v3...");
        string kill = "pids=\"$(ps -A -o PID,ARGS 2>/dev/null | grep -E 'Switch2BleBridgeV3' | grep -v grep | awk '{print $1}' || true)\"; for pid in $pids; do kill \"$pid\" 2>/dev/null || true; done";
        AdbChecked("shell", "su", "-c", kill);
        AdbChecked("shell", "su", "-c", "rm -f /data/local/tmp/switch2_ble_write_v3.txt /data/local/tmp/switch2_haptic_log_only_v3");

        string controller = options.ControllerAddress ?? DefaultControllerAddress;
        string cmdLine = "CLASSPATH=" + RemoteBleJar + " app_process64 /system/bin Switch2BleBridgeV3 --address " + controller;
        string start = "nohup sh -c '" + cmdLine + "' >/data/local/tmp/switch2_ble_bridge_v3.stdout 2>&1 &";
        AdbChecked("shell", start);
    }

    private void Status()
    {
        Console.WriteLine("Status:");
        string status =
            "echo UDC=$(cat /sys/class/udc/a600000.dwc3/state 2>/dev/null); " +
            "echo ---processes---; toybox pgrep -af 'Switch2(BleBridge|FfsResponder)V3' 2>/dev/null || true; " +
            "echo ---hashes---; md5sum /data/local/tmp/switch2_ble_bridge_v3.jar /data/local/tmp/switch2_ffs_responder_v3.jar 2>/dev/null || true; " +
            "echo ---state---; cat /data/local/tmp/switch2_state.txt 2>/dev/null || true; " +
            "echo ---ble-tail---; tail -n 25 /data/local/tmp/switch2_ble_bridge_v3.log 2>/dev/null || true; " +
            "echo ---responder-tail---; tail -n 25 /data/local/tmp/switch2_ffs_responder_v3.log 2>/dev/null || true";
        CommandResult result = Adb("shell", "su", "-c", status);
        Console.WriteLine(result.AllText.Trim());
    }

    private void PullLogs()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outDir = Path.Combine(rootDir, "logs", "launcher_" + stamp);
        Directory.CreateDirectory(outDir);
        PullMaybe("/data/local/tmp/switch2_ble_bridge_v3.log", Path.Combine(outDir, "switch2_ble_bridge_v3.log"));
        PullMaybe("/data/local/tmp/switch2_ble_input_raw_v3.log", Path.Combine(outDir, "switch2_ble_input_raw_v3.log"));
        PullMaybe("/data/local/tmp/switch2_button_changes_v3.log", Path.Combine(outDir, "switch2_button_changes_v3.log"));
        PullMaybe("/data/local/tmp/switch2_ffs_responder_v3.log", Path.Combine(outDir, "switch2_ffs_responder_v3.log"));
        PullMaybe("/data/local/tmp/switch2_hid_output_v3.log", Path.Combine(outDir, "switch2_hid_output_v3.log"));
        PullMaybe("/data/local/tmp/switch2_state.txt", Path.Combine(outDir, "switch2_state.txt"));
        Console.WriteLine("Pulled logs to " + outDir);
    }

    private void PullMaybe(string remote, string local)
    {
        CommandResult result = Adb("pull", remote, local);
        if (result.ExitCode != 0)
        {
            Console.WriteLine("Skipped " + remote + ": " + result.AllText.Trim());
        }
    }

    private void HapticTest()
    {
        AdbChecked("shell", "su", "-c", "echo play-hd > /data/local/tmp/switch2_ble_write_v3.txt");
        Console.WriteLine("Sent play-hd.");
    }

    private void Stop()
    {
        string cmd = "pids=\"$(ps -A -o PID,ARGS 2>/dev/null | grep -E 'Switch2(BleBridge|FfsResponder)V3' | grep -v grep | awk '{print $1}' || true)\"; for pid in $pids; do kill \"$pid\" 2>/dev/null || true; done; ps -A -o PID,ARGS | grep -E 'Switch2(BleBridge|FfsResponder)V3' | grep -v grep || true";
        CommandResult result = Adb("shell", "su", "-c", cmd);
        Console.WriteLine(result.AllText.Trim());
    }

    private string SelectDevice(string requestedSerial, bool allowAutoConnect)
    {
        if (!String.IsNullOrEmpty(requestedSerial))
        {
            return requestedSerial;
        }

        CommandResult devices = RawAdb("devices", "-l");
        List<DeviceInfo> online = ParseDevices(devices.StdOut);
        if (online.Count == 0 && allowAutoConnect)
        {
            TryMdnsConnect();
            devices = RawAdb("devices", "-l");
            online = ParseDevices(devices.StdOut);
        }

        if (online.Count == 0)
        {
            throw new InvalidOperationException("No online Y700 adb device found. Turn on wireless debugging or pass --serial.");
        }

        DeviceInfo best = null;
        foreach (DeviceInfo d in online)
        {
            if (d.Line.IndexOf("model:OPD2404", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.Line.IndexOf("product:OPD2404", StringComparison.OrdinalIgnoreCase) >= 0 ||
                d.Line.IndexOf("device:OP5D77L1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (best == null || IsTcpSerial(d.Serial))
                {
                    best = d;
                }
            }
        }
        if (best == null)
        {
            best = online[0];
        }
        return best.Serial;
    }

    private void TryMdnsConnect()
    {
        CommandResult mdns = RawAdb("mdns", "services");
        string[] lines = mdns.StdOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            if (line.IndexOf("_adb-tls-connect", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            string[] parts = Regex.Split(line.Trim(), "\\s+");
            foreach (string part in parts)
            {
                if (part.IndexOf(":") > 0 && Regex.IsMatch(part, @":\d+$"))
                {
                    RawAdb("connect", part);
                    return;
                }
            }
        }
    }

    private static List<DeviceInfo> ParseDevices(string text)
    {
        List<DeviceInfo> outList = new List<DeviceInfo>();
        string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            if (line.StartsWith("List of", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Match m = Regex.Match(line, @"^(\S+)\s+device\b");
            if (m.Success)
            {
                outList.Add(new DeviceInfo(m.Groups[1].Value, line));
            }
        }
        return outList;
    }

    private static bool IsTcpSerial(string value)
    {
        return value.IndexOf(":") > 0 || value.IndexOf("_adb-tls-connect", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void WarnIfUsbOnly(string value)
    {
        if (!IsTcpSerial(value))
        {
            Console.WriteLine("WARNING: selected adb serial looks USB-only. Reconfiguring the USB gadget may disconnect adb.");
            Console.WriteLine("Prefer wireless debugging for start mode.");
        }
    }

    private CommandResult Adb(params string[] args)
    {
        List<string> full = new List<string>();
        if (!String.IsNullOrEmpty(serial))
        {
            full.Add("-s");
            full.Add(serial);
        }
        full.AddRange(args);
        return RunProcess(adbPath, full.ToArray());
    }

    private void AdbChecked(params string[] args)
    {
        CommandResult result = Adb(args);
        Console.Write(result.StdOut);
        if (result.ExitCode != 0)
        {
            Console.Error.Write(result.StdErr);
            throw new InvalidOperationException("adb failed: " + JoinArgs(args));
        }
    }

    private CommandResult RawAdb(params string[] args)
    {
        return RunProcess(adbPath, args);
    }

    private static CommandResult RunProcess(string exe, string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = exe;
        psi.Arguments = JoinArgs(args);
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        Process p = Process.Start(psi);
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new CommandResult(p.ExitCode, stdout, stderr);
    }

    private static string JoinArgs(string[] args)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(QuoteArg(args[i]));
        }
        return sb.ToString();
    }

    private static string QuoteArg(string arg)
    {
        if (arg == null) return "\"\"";
        if (arg.Length == 0) return "\"\"";
        bool needQuote = arg.IndexOfAny(new char[] { ' ', '\t', '\n', '\r', '"' }) >= 0;
        if (!needQuote) return arg;
        StringBuilder sb = new StringBuilder();
        sb.Append('"');
        int slashCount = 0;
        for (int i = 0; i < arg.Length; i++)
        {
            char c = arg[i];
            if (c == '\\')
            {
                slashCount++;
            }
            else if (c == '"')
            {
                sb.Append('\\', slashCount * 2 + 1);
                sb.Append('"');
                slashCount = 0;
            }
            else
            {
                sb.Append('\\', slashCount);
                slashCount = 0;
                sb.Append(c);
            }
        }
        sb.Append('\\', slashCount * 2);
        sb.Append('"');
        return sb.ToString();
    }

    private static string FindAdb(string explicitPath)
    {
        List<string> candidates = new List<string>();
        if (!String.IsNullOrEmpty(explicitPath)) candidates.Add(explicitPath);
        string env = Environment.GetEnvironmentVariable("ADB_PATH");
        if (!String.IsNullOrEmpty(env)) candidates.Add(env);
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe"));
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "adb.exe"));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(home, "Desktop", "\u5de5\u5177", "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(home, "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"));

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] dirs = path.Split(';');
        foreach (string dir in dirs)
        {
            if (dir.Length > 0) candidates.Add(Path.Combine(dir, "adb.exe"));
        }

        foreach (string c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException("adb.exe not found. Pass --adb C:\\path\\to\\adb.exe");
    }

    private static string FindRootDir(string start)
    {
        DirectoryInfo dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "setup_y700_switch2_proto_v3.sh")) ||
                File.Exists(Path.Combine(dir.FullName, "STABLE_CHECKPOINT_20260525_V3_INPUT_RUMBLE.md")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return start.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string FindArtifactDir(string root, string exeDir)
    {
        string[] candidates = new string[]
        {
            exeDir,
            Path.Combine(root, "release", "v3-stable-20260525-input-rumble"),
            root
        };
        foreach (string c in candidates)
        {
            if (File.Exists(Path.Combine(c, "switch2_ble_bridge_v3.jar")) &&
                File.Exists(Path.Combine(c, "switch2_ffs_responder_v3.jar")) &&
                File.Exists(Path.Combine(c, "setup_y700_switch2_proto_v3.sh")))
            {
                return c;
            }
        }
        throw new DirectoryNotFoundException("Stable v3 artifacts not found near " + exeDir);
    }

    private sealed class DeviceInfo
    {
        public readonly string Serial;
        public readonly string Line;
        public DeviceInfo(string serial, string line)
        {
            Serial = serial;
            Line = line;
        }
    }

    private sealed class CommandResult
    {
        public readonly int ExitCode;
        public readonly string StdOut;
        public readonly string StdErr;
        public string AllText { get { return StdOut + StdErr; } }
        public CommandResult(int exitCode, string stdout, string stderr)
        {
            ExitCode = exitCode;
            StdOut = stdout ?? "";
            StdErr = stderr ?? "";
        }
    }

    private sealed class Options
    {
        public string Command = "start";
        public string AdbPath;
        public string Serial;
        public string ControllerAddress = DefaultControllerAddress;
        public bool SkipGadget;
        public bool Help;

        public static Options Parse(string[] args)
        {
            Options options = new Options();
            int i = 0;
            if (args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
            {
                options.Command = args[0].ToLowerInvariant();
                i = 1;
            }
            while (i < args.Length)
            {
                string arg = args[i];
                if (arg == "--help" || arg == "-h" || arg == "/?")
                {
                    options.Help = true;
                    i++;
                }
                else if (arg == "--adb" && i + 1 < args.Length)
                {
                    options.AdbPath = args[++i];
                    i++;
                }
                else if (arg == "--serial" && i + 1 < args.Length)
                {
                    options.Serial = args[++i];
                    i++;
                }
                else if (arg == "--controller" && i + 1 < args.Length)
                {
                    options.ControllerAddress = args[++i];
                    i++;
                }
                else if (arg == "--skip-gadget")
                {
                    options.SkipGadget = true;
                    i++;
                }
                else
                {
                    throw new ArgumentException("Unknown or incomplete option: " + arg);
                }
            }
            return options;
        }
    }
}
