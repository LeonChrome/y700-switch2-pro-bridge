using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

const uint DigcfPresent = 0x00000002;
const uint DigcfDeviceInterface = 0x00000010;
const uint GenericRead = 0x80000000;
const uint FileShareRead = 0x00000001;
const uint FileShareWrite = 0x00000002;
const uint OpenExisting = 3;
const uint FileFlagOverlapped = 0x40000000;

double durationSeconds = ReadDoubleArgument(args, "--duration", 6.0);
string? csvPath = ReadStringArgument(args, "--csv");
bool guidedYaw = args.Any(value =>
    string.Equals(value, "--guided-yaw", StringComparison.OrdinalIgnoreCase));
bool listOnly = args.Any(value =>
    string.Equals(value, "--list", StringComparison.OrdinalIgnoreCase));
int? requestedPathIndex = ReadIntArgument(args, "--path-index");
string requestedVidPid =
    (ReadStringArgument(args, "--vidpid") ?? "vid_057e&pid_2069")
    .Trim()
    .ToLowerInvariant();

Guid hidGuid;
Native.HidD_GetHidGuid(out hidGuid);
IntPtr info = Native.SetupDiGetClassDevs(
    ref hidGuid,
    null,
    IntPtr.Zero,
    DigcfPresent | DigcfDeviceInterface);
if (info == new IntPtr(-1))
{
    throw new InvalidOperationException("SetupDiGetClassDevs failed");
}

var paths = new List<string>();
try
{
    for (uint index = 0; ; index++)
    {
        var data = new SpDeviceInterfaceData
        {
            CbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
        };
        if (!Native.SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, index, ref data))
        {
            if (Marshal.GetLastWin32Error() == 259)
            {
                break;
            }
            continue;
        }

        Native.SetupDiGetDeviceInterfaceDetail(
            info,
            ref data,
            IntPtr.Zero,
            0,
            out uint required,
            IntPtr.Zero);
        IntPtr detail = Marshal.AllocHGlobal((int)required);
        try
        {
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (Native.SetupDiGetDeviceInterfaceDetail(
                    info,
                    ref data,
                    detail,
                    required,
                    out _,
                    IntPtr.Zero))
            {
                string? path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                if (!string.IsNullOrWhiteSpace(path) &&
                    path.Contains(requestedVidPid, StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(path);
                }
            }
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

if (paths.Count == 0)
{
    Console.Error.WriteLine("No HID " + requestedVidPid.ToUpperInvariant() + " interface found.");
    return 2;
}

for (int index = 0; index < paths.Count; index++)
{
    Console.WriteLine($"PATH_INDEX {index + 1} {paths[index]}");
}
if (listOnly)
{
    return 0;
}
if (requestedPathIndex is < 1 || requestedPathIndex > paths.Count)
{
    Console.Error.WriteLine("--path-index 超出候选范围。");
    return 2;
}

IEnumerable<int> selectedIndices = requestedPathIndex.HasValue
    ? [requestedPathIndex.Value - 1]
    : Enumerable.Range(0, paths.Count);
int selectedCount = requestedPathIndex.HasValue ? 1 : paths.Count;
foreach (int pathIndex in selectedIndices)
{
    string path = paths[pathIndex];
    Console.WriteLine("PATH " + path);
    using SafeFileHandle handle = Native.CreateFile(
        path,
        GenericRead,
        FileShareRead | FileShareWrite,
        IntPtr.Zero,
        OpenExisting,
        FileFlagOverlapped,
        IntPtr.Zero);
    if (handle.IsInvalid)
    {
        Console.WriteLine("OPEN_FAILED win32=" + Marshal.GetLastWin32Error());
        continue;
    }

    using var stream = new FileStream(handle, FileAccess.Read, 256, isAsync: true);
    var stopwatch = Stopwatch.StartNew();
    Task guideTask = guidedYaw ? RunGuidedYawSignalsAsync() : Task.CompletedTask;
    var reportCounts = new Dictionary<(byte Id, int Length), int>();
    var intervalsMs = new List<double>();
    long lastTicks = 0;
    int imuReports = 0;
    int imuSubSamplesChanged = 0;
    int imuValueChanges = 0;
    int motionTimestampChanges = 0;
    var reportCounterSteps = new List<uint>();
    var motionTimestampSteps = new List<uint>();
    uint? lastMotionTimestamp = null;
    uint? lastReportCounter = null;
    (short Ax, short Ay, short Az, short Gx, short Gy, short Gz)? lastImu = null;
    int total = 0;
    string? deviceCsvPath = csvPath == null
        ? null
        : selectedCount == 1
            ? csvPath
            : Path.Combine(
                Path.GetDirectoryName(csvPath) ?? ".",
                Path.GetFileNameWithoutExtension(csvPath) + "_device" + (pathIndex + 1) + Path.GetExtension(csvPath));
    if (deviceCsvPath != null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(deviceCsvPath))!);
    }
    using StreamWriter? csv = deviceCsvPath == null
        ? null
        : new StreamWriter(deviceCsvPath, append: false);
    csv?.WriteLine("time_ms,gap_ms,report_length,report_counter,motion_timestamp,magnet_x,magnet_y,magnet_z,temp_raw,temp_c,accel_x,accel_y,accel_z,gyro_x,gyro_y,gyro_z");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
    byte[] buffer = new byte[256];
    try
    {
        while (!timeout.IsCancellationRequested)
        {
            // HID input report length is 64 bytes. Limiting each read prevents
            // FileStream from coalescing several reports into one buffer.
            int read = await stream.ReadAsync(buffer.AsMemory(0, 64), timeout.Token);
            if (read <= 0)
            {
                continue;
            }

            long now = Stopwatch.GetTimestamp();
            if (lastTicks != 0)
            {
                intervalsMs.Add((now - lastTicks) * 1000.0 / Stopwatch.Frequency);
            }
            lastTicks = now;
            total++;
            byte id = buffer[0];
            reportCounts[(id, read)] =
                reportCounts.TryGetValue((id, read), out int count) ? count + 1 : 1;

            if ((id is 0x30 or 0x31 or 0x32 or 0x33) && read >= 49)
            {
                imuReports++;
                bool changed = false;
                for (int offset = 13; offset < 37; offset += 12)
                {
                    if (!buffer.AsSpan(offset, 12).SequenceEqual(buffer.AsSpan(offset + 12, 12)))
                    {
                        changed = true;
                        break;
                    }
                }
                if (changed)
                {
                    imuSubSamplesChanged++;
                }
            }
            else if (id == 0x05 && read >= 61)
            {
                imuReports++;
                uint reportCounter = BitConverter.ToUInt32(buffer, 1);
                uint motionTimestamp = BitConverter.ToUInt32(buffer, 0x2B);
                var magnet = (
                    BitConverter.ToInt16(buffer, 0x1A),
                    BitConverter.ToInt16(buffer, 0x1C),
                    BitConverter.ToInt16(buffer, 0x1E));
                ushort temperatureRaw = BitConverter.ToUInt16(buffer, 0x2F);
                double temperatureC = 25.0 + temperatureRaw / 127.0;
                var imu = (
                    BitConverter.ToInt16(buffer, 0x31),
                    BitConverter.ToInt16(buffer, 0x33),
                    BitConverter.ToInt16(buffer, 0x35),
                    BitConverter.ToInt16(buffer, 0x37),
                    BitConverter.ToInt16(buffer, 0x39),
                    BitConverter.ToInt16(buffer, 0x3B));

                if (lastReportCounter.HasValue)
                {
                    reportCounterSteps.Add(unchecked(reportCounter - lastReportCounter.Value));
                }
                if (lastMotionTimestamp.HasValue)
                {
                    uint step = unchecked(motionTimestamp - lastMotionTimestamp.Value);
                    motionTimestampSteps.Add(step);
                    if (step != 0)
                    {
                        motionTimestampChanges++;
                    }
                }
                if (lastImu.HasValue && imu != lastImu.Value)
                {
                    imuValueChanges++;
                }

                lastReportCounter = reportCounter;
                lastMotionTimestamp = motionTimestamp;
                lastImu = imu;
                csv?.WriteLine(string.Join(",",
                    stopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                    (intervalsMs.Count == 0 ? 0 : intervalsMs[^1]).ToString("F3", CultureInfo.InvariantCulture),
                    read,
                    reportCounter,
                    motionTimestamp,
                    magnet.Item1,
                    magnet.Item2,
                    magnet.Item3,
                    temperatureRaw,
                    temperatureC.ToString("F4", CultureInfo.InvariantCulture),
                    imu.Item1,
                    imu.Item2,
                    imu.Item3,
                    imu.Item4,
                    imu.Item5,
                    imu.Item6));
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    await guideTask;

    double elapsed = stopwatch.Elapsed.TotalSeconds;
    intervalsMs.Sort();
    uint reportCounterStep = ModalStep(reportCounterSteps);
    uint motionTimestampStep = ModalStep(motionTimestampSteps);
    int reportCounterStepMismatches = reportCounterSteps.Count(step => step != reportCounterStep);
    int motionTimestampStepMismatches = motionTimestampSteps.Count(step => step != motionTimestampStep);
    Console.WriteLine(
        $"RESULT reports={total} elapsed_s={elapsed:F3} hz={total / Math.Max(elapsed, 0.001):F1} " +
        $"gap_p50_ms={Percentile(intervalsMs, 0.50):F3} " +
        $"gap_p95_ms={Percentile(intervalsMs, 0.95):F3} " +
        $"gap_p99_ms={Percentile(intervalsMs, 0.99):F3} " +
        $"gap_max_ms={(intervalsMs.Count == 0 ? 0 : intervalsMs[^1]):F3} " +
        $"imu_reports={imuReports} imu_three_samples_changed={imuSubSamplesChanged} " +
        $"imu_value_changes={imuValueChanges} motion_timestamp_changes={motionTimestampChanges} " +
        $"report_counter_step={reportCounterStep} " +
        $"report_counter_step_mismatches={reportCounterStepMismatches} " +
        $"motion_timestamp_step_us={motionTimestampStep} " +
        $"motion_timestamp_step_mismatches={motionTimestampStepMismatches}");
    foreach (((byte id, int length), int count) in reportCounts.OrderBy(x => x.Key.Id))
    {
        Console.WriteLine($"REPORT id=0x{id:X2} len={length} count={count}");
    }
    if (deviceCsvPath != null)
    {
        Console.WriteLine("CSV " + Path.GetFullPath(deviceCsvPath));
    }
}

return 0;

static double Percentile(List<double> sorted, double fraction)
{
    if (sorted.Count == 0)
    {
        return 0;
    }
    int index = (int)Math.Round((sorted.Count - 1) * fraction);
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
}

static uint ModalStep(List<uint> steps)
{
    return steps.Count == 0
        ? 0
        : steps
            .GroupBy(step => step)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .First()
            .Key;
}

static string? ReadStringArgument(string[] arguments, string name)
{
    int index = Array.FindIndex(arguments, value =>
        string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < arguments.Length
        ? arguments[index + 1]
        : null;
}

static double ReadDoubleArgument(string[] arguments, string name, double fallback)
{
    string? value = ReadStringArgument(arguments, name);
    return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
        ? Math.Clamp(parsed, 1.0, 120.0)
        : fallback;
}

static int? ReadIntArgument(string[] arguments, string name)
{
    string? value = ReadStringArgument(arguments, name);
    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
        ? parsed
        : null;
}

static async Task RunGuidedYawSignalsAsync()
{
    Console.Beep(700, 180); // Capture started: remain still.
    await Task.Delay(3820);
    Console.Beep(1100, 220); // Rotate right.
    await Task.Delay(2780);
    Console.Beep(1100, 220); // Hold.
    await Task.Delay(2780);
    Console.Beep(700, 220); // Rotate left to the original heading.
    await Task.Delay(2780);
    Console.Beep(1100, 220); // Hold still.
    await Task.Delay(2780);
    Console.Beep(900, 120);
    Console.Beep(1200, 160); // Capture complete.
}

[StructLayout(LayoutKind.Sequential)]
struct SpDeviceInterfaceData
{
    public int CbSize;
    public Guid InterfaceClassGuid;
    public int Flags;
    public UIntPtr Reserved;
}

static class Native
{
    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll")]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
