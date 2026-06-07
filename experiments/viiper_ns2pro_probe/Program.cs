using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const int InputSize = 24;
const int OutputSize = 34;
var options = Args.Parse(args);
using var log = new ProbeLog(options.LogPath);

log.Write("[VIIPER] starting");
log.Write($"[VIIPER] mode={(options.StartServer ? "subprocess" : "server")} api={options.ApiHost}:{options.ApiPort}");
if (options.MonitorOnly)
{
    log.Write($"[NS2PRO_MONITOR] enabled=true seconds={options.DurationSeconds}");
    if (options.ExitOnNonZero)
    {
        log.Write("[NS2PRO_MONITOR] exit_on_nonzero=true");
    }
}

Process? server = null;
try
{
    if (options.StartServer)
    {
        if (string.IsNullOrWhiteSpace(options.ViiperPath) || !File.Exists(options.ViiperPath))
        {
            log.Write($"[VIIPER] blocked: viiper executable not found path={options.ViiperPath}");
            return 2;
        }

        var serverArgs = new List<string>
        {
            "server",
            $"--api.addr={options.ApiHost}:{options.ApiPort}",
            $"--usb.addr={options.ApiHost}:{options.UsbPort}",
            "--api.device-handler-connect-timeout=30s",
            "--log.level=debug"
        };
        if (options.AutoAttach)
        {
            serverArgs.Add("--api.auto-attach-local-client");
        }
        else
        {
            serverArgs.Add("--api.auto-attach-local-client=false");
        }

        server = StartViiper(options.ViiperPath, serverArgs, log);
    }

    if (!await WaitForPort(options.ApiHost, options.ApiPort, TimeSpan.FromSeconds(12)))
    {
        log.Write("[VIIPER] blocked: API port did not open");
        return 3;
    }

    log.Write($"[VIIPER] backend=server auto_attach={options.AutoAttach}");
    string createJson = await ApiRequest(options, "bus/create");
    uint busId = JsonDocument.Parse(createJson).RootElement.GetProperty("busId").GetUInt32();
    log.Write($"[VIIPER] bus={busId}");

    string addJson = await ApiRequest(options, $"bus/{busId}/add {{\"type\":\"ns2pro\"}}");
    using var addDoc = JsonDocument.Parse(addJson);
    string devId = addDoc.RootElement.GetProperty("devId").GetString() ?? "1";
    string vid = addDoc.RootElement.TryGetProperty("vid", out var vidJson) ? vidJson.GetString() ?? "" : "";
    string pid = addDoc.RootElement.TryGetProperty("pid", out var pidJson) ? pidJson.GetString() ?? "" : "";
    log.Write($"[NS2PRO] virtual device created bus={busId} dev={devId} vid={vid} pid={pid}");

    using var streamClient = new TcpClient();
    await streamClient.ConnectAsync(options.ApiHost, options.ApiPort);
    NetworkStream ns = streamClient.GetStream();
    byte[] handshake = Encoding.UTF8.GetBytes($"bus/{busId}/{devId}\0");
    await ns.WriteAsync(handshake);
    log.Write("[NS2PRO] virtual device connected");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
    var readTask = ReadFeedbackLoop(ns, log, cts, options.ExitOnNonZero);
    int frame = 0;
    while (!cts.IsCancellationRequested)
    {
        try
        {
            byte[] input = BuildInput(frame);
            await ns.WriteAsync(input, cts.Token);
            if (frame % 20 == 0)
            {
                ushort lx = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(4, 2));
                ushort ly = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(6, 2));
                ushort rx = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(8, 2));
                ushort ry = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(10, 2));
                short ax = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(12, 2));
                short ay = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(14, 2));
                short az = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(16, 2));
                short gx = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(18, 2));
                short gy = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(20, 2));
                short gz = BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(22, 2));
                uint buttons = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(0, 4));
                log.Write($"[NS2PRO_INPUT] buttons=0x{buttons:x8} lx={lx} ly={ly} rx={rx} ry={ry} gyro=({gx},{gy},{gz}) accel=({ax},{ay},{az})");
            }
            frame++;
            await Task.Delay(options.FrameDelayMs, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            break;
        }
    }

    cts.Cancel();
    await readTask.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { });
    bool nonzero = log.LeftNonZero || log.RightNonZero;
    log.Write($"[NS2PRO_OUTPUT] feedback_count={log.OutputFeedbackCount} nonzero_count={log.NonZeroOutputCount}");
    log.Write($"[NS2PRO_OUTPUT] first_nonzero_flags={log.FirstNonZeroFlags ?? "not_found"} first_nonzero_led={log.FirstNonZeroLed ?? "not_found"} first_nonzero_left={log.FirstNonZeroLeftHex ?? "not_found"} first_nonzero_right={log.FirstNonZeroRightHex ?? "not_found"}");
    log.Write($"[NS2PRO_OUTPUT] left_nonzero={log.LeftNonZero.ToString().ToLowerInvariant()} right_nonzero={log.RightNonZero.ToString().ToLowerInvariant()}");
    log.Write($"[NS2PRO] result output_feedback={log.OutputFeedbackSeen.ToString().ToLowerInvariant()} nonzero={nonzero.ToString().ToLowerInvariant()}");
    if (options.MonitorOnly)
    {
        log.Write("[NS2PRO_MONITOR] completed");
        return 0;
    }
    return nonzero ? 0 : 10;
}
catch (OperationCanceledException)
{
    log.Write("[VIIPER] canceled");
    return 11;
}
catch (Exception ex)
{
    log.Write($"[VIIPER] error: {ex.GetType().Name}: {ex.Message}");
    return 12;
}
finally
{
    if (server is { HasExited: false })
    {
        try
        {
            server.Kill(entireProcessTree: true);
            server.WaitForExit(2000);
        }
        catch
        {
            // best effort cleanup
        }
    }
}

static Process StartViiper(string viiperPath, List<string> args, ProbeLog log)
{
    var psi = new ProcessStartInfo
    {
        FileName = viiperPath,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    foreach (var arg in args)
    {
        psi.ArgumentList.Add(arg);
    }

    var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start VIIPER");
    _ = Task.Run(async () =>
    {
        while (!process.StandardOutput.EndOfStream)
        {
            string? line = await process.StandardOutput.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line)) log.Write("[VIIPER_LOG] " + line);
        }
    });
    _ = Task.Run(async () =>
    {
        while (!process.StandardError.EndOfStream)
        {
            string? line = await process.StandardError.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line)) log.Write("[VIIPER_LOG] " + line);
        }
    });
    return process;
}

static async Task<bool> WaitForPort(string host, int port, TimeSpan timeout)
{
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromMilliseconds(500));
            return true;
        }
        catch
        {
            await Task.Delay(200);
        }
    }
    return false;
}

static async Task<string> ApiRequest(Args options, string command)
{
    using var client = new TcpClient();
    await client.ConnectAsync(options.ApiHost, options.ApiPort);
    NetworkStream stream = client.GetStream();
    byte[] request = Encoding.UTF8.GetBytes(command + "\0");
    await stream.WriteAsync(request);
    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms);
    string response = Encoding.UTF8.GetString(ms.ToArray()).Trim();
    if (response.Contains("\"status\":", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"VIIPER API error for '{command}': {response}");
    }
    return response;
}

static byte[] BuildInput(int frame)
{
    byte[] data = new byte[InputSize];
    uint buttons = 0;
    if ((frame / 30) % 2 == 0) buttons |= 0x00000002; // A
    if ((frame / 45) % 2 == 0) buttons |= 0x00000010; // R
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), buttons);

    static ushort Stick(double value) => (ushort)Math.Clamp((int)Math.Round(0x0800 + value * 0x07ff), 0, 0x0fff);
    double t = frame / 30.0;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), Stick(Math.Sin(t)));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), Stick(Math.Cos(t)));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), Stick(Math.Sin(t * 0.7)));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10, 2), Stick(Math.Cos(t * 0.7)));
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12, 2), (short)(Math.Sin(t) * 300));
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14, 2), (short)(Math.Cos(t) * 300));
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(16, 2), 4096);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(18, 2), (short)(Math.Sin(t * 1.3) * 900));
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(20, 2), (short)(Math.Cos(t * 1.1) * 900));
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22, 2), (short)(Math.Sin(t * 0.9) * 500));
    return data;
}

static async Task ReadFeedbackLoop(NetworkStream stream, ProbeLog log, CancellationTokenSource cts, bool exitOnNonZero)
{
    byte[] buf = new byte[OutputSize];
    CancellationToken token = cts.Token;
    while (!token.IsCancellationRequested)
    {
        int offset = 0;
        try
        {
            while (offset < OutputSize)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                int read = await stream.ReadAsync(buf.AsMemory(offset, OutputSize - offset), timeoutCts.Token);
                if (read == 0) return;
                offset += read;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            continue;
        }

        byte[] left = buf[..16];
        byte[] right = buf[16..32];
        byte flags = buf[32];
        byte led = buf[33];
        bool leftNonZero = left.Any(b => b != 0);
        bool rightNonZero = right.Any(b => b != 0);
        string leftHex = Convert.ToHexString(left);
        string rightHex = Convert.ToHexString(right);
        log.OutputFeedbackSeen = true;
        log.OutputFeedbackCount++;
        log.LeftNonZero |= leftNonZero;
        log.RightNonZero |= rightNonZero;
        if (leftNonZero || rightNonZero)
        {
            log.NonZeroOutputCount++;
            if (log.FirstNonZeroLeftHex is null && log.FirstNonZeroRightHex is null)
            {
                log.FirstNonZeroFlags = $"0x{flags:x2}";
                log.FirstNonZeroLed = $"0x{led:x2}";
                log.FirstNonZeroLeftHex = leftHex;
                log.FirstNonZeroRightHex = rightHex;
                log.Write($"[NS2PRO_OUTPUT_FIRST_NONZERO] flags=0x{flags:x2} led=0x{led:x2} left_rumble_hex={leftHex} right_rumble_hex={rightHex}");
            }
        }
        log.Write($"[NS2PRO_OUTPUT] flags=0x{flags:x2} led=0x{led:x2} left_rumble_hex={leftHex} right_rumble_hex={rightHex}");
        log.Write($"[NS2PRO_OUTPUT] left_nonzero={leftNonZero.ToString().ToLowerInvariant()} right_nonzero={rightNonZero.ToString().ToLowerInvariant()}");
        if (exitOnNonZero && (leftNonZero || rightNonZero))
        {
            log.Write("[NS2PRO_OUTPUT] exit_on_nonzero=true");
            cts.Cancel();
            return;
        }
    }
}

sealed class ProbeLog : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter? writer;

    public bool OutputFeedbackSeen { get; set; }
    public bool LeftNonZero { get; set; }
    public bool RightNonZero { get; set; }
    public int OutputFeedbackCount { get; set; }
    public int NonZeroOutputCount { get; set; }
    public string? FirstNonZeroFlags { get; set; }
    public string? FirstNonZeroLed { get; set; }
    public string? FirstNonZeroLeftHex { get; set; }
    public string? FirstNonZeroRightHex { get; set; }

    public ProbeLog(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
        }
    }

    public void Write(string line)
    {
        lock (gate)
        {
            Console.WriteLine(line);
            writer?.WriteLine(line);
        }
    }

    public void Dispose() => writer?.Dispose();
}

sealed class Args
{
    public string ApiHost { get; init; } = "127.0.0.1";
    public int ApiPort { get; init; } = 3242;
    public int UsbPort { get; init; } = 3241;
    public bool StartServer { get; init; } = true;
    public bool AutoAttach { get; init; } = true;
    public string ViiperPath { get; init; } = "";
    public int DurationSeconds { get; init; } = 20;
    public int FrameDelayMs { get; init; } = 16;
    public string? LogPath { get; init; }
    public bool MonitorOnly { get; init; }
    public bool ExitOnNonZero { get; init; }

    public static Args Parse(string[] args)
    {
        string GetValue(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        bool Has(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        return new Args
        {
            ApiHost = GetValue("--api-host", "127.0.0.1"),
            ApiPort = int.Parse(GetValue("--api-port", "3242")),
            UsbPort = int.Parse(GetValue("--usb-port", "3241")),
            StartServer = !Has("--no-start-server"),
            AutoAttach = !Has("--no-auto-attach"),
            ViiperPath = GetValue("--viiper", ""),
            DurationSeconds = int.Parse(GetValue("--duration-seconds", "20")),
            FrameDelayMs = int.Parse(GetValue("--frame-delay-ms", "16")),
            LogPath = GetValue("--log", ""),
            MonitorOnly = Has("--monitor-only"),
            ExitOnNonZero = Has("--exit-on-nonzero")
        };
    }
}
