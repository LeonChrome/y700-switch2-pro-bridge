using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ImuFidelityLab;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            if (options.ShowHelp ||
                (options.Fd2Inputs.Count == 0 &&
                 options.HidCsvInputs.Count == 0 &&
                 string.IsNullOrWhiteSpace(options.SixFaceDirectory) &&
                 options.EllipsoidInputs.Count == 0 &&
                 !options.SelfTest))
            {
                PrintHelp();
                return options.ShowHelp ? 0 : 2;
            }

            if (options.SelfTest)
            {
                EllipsoidAnalyzer.RunSelfTest();
                Console.WriteLine("SELF_TEST PASS ellipsoid calibration");
                if (options.Fd2Inputs.Count == 0 &&
                    options.HidCsvInputs.Count == 0 &&
                    string.IsNullOrWhiteSpace(options.SixFaceDirectory) &&
                    options.EllipsoidInputs.Count == 0)
                {
                    return 0;
                }
            }

            var report = new StringBuilder();
            report.AppendLine("# IMU Fidelity Lab 基线报告");
            report.AppendLine();
            report.AppendLine("生成时间：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            report.AppendLine();

            foreach (string input in options.Fd2Inputs)
            {
                Fd2Analysis analysis = Fd2Analyzer.Analyze(input);
                ReportWriter.AppendFd2(report, analysis);
            }

            foreach (string input in options.HidCsvInputs)
            {
                HidAnalysis analysis = HidCsvAnalyzer.Analyze(input);
                ReportWriter.AppendHid(report, analysis);
            }

            if (!string.IsNullOrWhiteSpace(options.SixFaceDirectory))
            {
                SixFaceAnalysis analysis = SixFaceAnalyzer.Analyze(options.SixFaceDirectory);
                ReportWriter.AppendSixFace(report, analysis);
            }

            if (options.EllipsoidInputs.Count > 0)
            {
                EllipsoidAnalysis analysis = EllipsoidAnalyzer.Analyze(options.EllipsoidInputs);
                ReportWriter.AppendEllipsoid(report, analysis);
            }

            ReportWriter.AppendInterpretation(report);
            string text = report.ToString();
            Console.Write(text);
            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                string outputPath = Path.GetFullPath(options.OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, text, new UTF8Encoding(false));
                Console.Error.WriteLine("REPORT " + outputPath);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR " + ex.Message);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("IMU Fidelity Lab");
        Console.WriteLine("  --fd2-jsonl <file-or-directory>  分析完整 FD2 原始包，可重复指定");
        Console.WriteLine("  --hid-csv <file-or-directory>    分析 HidRateProbe CSV，可重复指定");
        Console.WriteLine("  --six-face-dir <directory>       求解六面加速度 3x3 标定矩阵");
        Console.WriteLine("  --ellipsoid-csv <file>           从任意静置姿态片段求解椭球标定");
        Console.WriteLine("  --self-test                      运行椭球求解合成数据自检");
        Console.WriteLine("  --output <report.md>             保存 Markdown 报告");
        Console.WriteLine("  --help                           显示帮助");
    }
}

internal sealed record Options(
    List<string> Fd2Inputs,
    List<string> HidCsvInputs,
    string? SixFaceDirectory,
    List<string> EllipsoidInputs,
    string? OutputPath,
    bool ShowHelp,
    bool SelfTest)
{
    public static Options Parse(string[] args)
    {
        var fd2 = new List<string>();
        var hid = new List<string>();
        string? output = null;
        string? sixFaceDirectory = null;
        var ellipsoidInputs = new List<string>();
        bool help = false;
        bool selfTest = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--fd2-jsonl":
                    fd2.Add(RequireValue(args, ref i));
                    break;
                case "--hid-csv":
                    hid.Add(RequireValue(args, ref i));
                    break;
                case "--output":
                    output = RequireValue(args, ref i);
                    break;
                case "--six-face-dir":
                    sixFaceDirectory = RequireValue(args, ref i);
                    break;
                case "--ellipsoid-csv":
                    ellipsoidInputs.Add(RequireValue(args, ref i));
                    break;
                case "--self-test":
                    selfTest = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                default:
                    throw new ArgumentException("未知参数：" + args[i]);
            }
        }
        return new Options(fd2, hid, sixFaceDirectory, ellipsoidInputs, output, help, selfTest);
    }

    private static string RequireValue(string[] args, ref int index)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException("参数缺少值：" + args[index - 1]);
        }
        return args[index];
    }
}

internal sealed record Fd2Sample(
    DateTimeOffset Arrival,
    double ArrivalDeltaMs,
    uint Counter,
    uint MotionTimestampUs,
    short MagnetX,
    short MagnetY,
    short MagnetZ,
    double TemperatureC,
    short AccelX,
    short AccelY,
    short AccelZ,
    short GyroX,
    short GyroY,
    short GyroZ,
    string SourceFile);

internal sealed record AxisStats(double Mean, double StdDev, double Min, double Max);

internal sealed record Fd2Analysis(
    string Input,
    int FileCount,
    int SampleCount,
    int DuplicateCount,
    double DurationSeconds,
    double ArrivalHz,
    double SourceTimestampHz,
    double ImuChangeHz,
    double ArrivalGapP50Ms,
    double ArrivalGapP95Ms,
    double ArrivalGapP99Ms,
    double ArrivalGapMaxMs,
    double SourceGapP50Ms,
    double SourceGapP95Ms,
    double SourceGapP99Ms,
    double SourceGapMaxMs,
    double ArrivalSourceJitterP95Ms,
    double ArrivalSourceJitterMaxAbsMs,
    int ArrivalGapOver33,
    int ArrivalGapOver50,
    int SourceTimestampNonMonotonic,
    AxisStats Temperature,
    AxisStats AccelNorm,
    AxisStats GyroX,
    AxisStats GyroY,
    AxisStats GyroZ);

internal static class Fd2Analyzer
{
    public static Fd2Analysis Analyze(string input)
    {
        string[] files = InputFiles(input, "*.jsonl");
        var samples = new List<Fd2Sample>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int duplicates = 0;
        foreach (string file in files)
        {
            foreach (string line in File.ReadLines(file))
            {
                if (!TryParseFrame(line, file, out Fd2Sample? sample) || sample is null)
                {
                    continue;
                }
                string key = sample.Arrival.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" +
                             sample.Counter.ToString(CultureInfo.InvariantCulture) + ":" +
                             sample.MotionTimestampUs.ToString(CultureInfo.InvariantCulture);
                if (!seen.Add(key))
                {
                    duplicates++;
                    continue;
                }
                samples.Add(sample);
            }
        }

        samples.Sort((a, b) => a.Arrival.CompareTo(b.Arrival));
        if (samples.Count < 2)
        {
            throw new InvalidDataException("FD2 有效样本不足：" + input);
        }

        var arrivalGaps = new List<double>();
        var sourceGaps = new List<double>();
        var jitter = new List<double>();
        int timestampNonMonotonic = 0;
        int imuChanges = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            Fd2Sample previous = samples[i - 1];
            Fd2Sample current = samples[i];
            double arrivalGap = (current.Arrival - previous.Arrival).TotalMilliseconds;
            if (arrivalGap <= 0 || arrivalGap > 5000)
            {
                continue;
            }
            arrivalGaps.Add(arrivalGap);

            uint sourceDelta = unchecked(current.MotionTimestampUs - previous.MotionTimestampUs);
            if (sourceDelta == 0 || sourceDelta > 5_000_000)
            {
                timestampNonMonotonic++;
            }
            else
            {
                double sourceGap = sourceDelta / 1000.0;
                sourceGaps.Add(sourceGap);
                jitter.Add(arrivalGap - sourceGap);
            }

            if ((current.AccelX, current.AccelY, current.AccelZ, current.GyroX, current.GyroY, current.GyroZ) !=
                (previous.AccelX, previous.AccelY, previous.AccelZ, previous.GyroX, previous.GyroY, previous.GyroZ))
            {
                imuChanges++;
            }
        }

        double duration = (samples[^1].Arrival - samples[0].Arrival).TotalSeconds;
        double sourceDuration = sourceGaps.Sum() / 1000.0;
        var temperatures = samples.Select(s => s.TemperatureC).ToArray();
        var accelNorms = samples.Select(s => Math.Sqrt(
            (double)s.AccelX * s.AccelX +
            (double)s.AccelY * s.AccelY +
            (double)s.AccelZ * s.AccelZ)).ToArray();

        return new Fd2Analysis(
            Path.GetFullPath(input),
            files.Length,
            samples.Count,
            duplicates,
            duration,
            (samples.Count - 1) / duration,
            sourceDuration > 0 ? sourceGaps.Count / sourceDuration : 0,
            imuChanges / duration,
            Stats.Percentile(arrivalGaps, 0.50),
            Stats.Percentile(arrivalGaps, 0.95),
            Stats.Percentile(arrivalGaps, 0.99),
            arrivalGaps.DefaultIfEmpty().Max(),
            Stats.Percentile(sourceGaps, 0.50),
            Stats.Percentile(sourceGaps, 0.95),
            Stats.Percentile(sourceGaps, 0.99),
            sourceGaps.DefaultIfEmpty().Max(),
            Stats.Percentile(jitter.Select(Math.Abs), 0.95),
            jitter.Select(Math.Abs).DefaultIfEmpty().Max(),
            arrivalGaps.Count(v => v > 33),
            arrivalGaps.Count(v => v > 50),
            timestampNonMonotonic,
            Stats.Axis(temperatures),
            Stats.Axis(accelNorms),
            Stats.Axis(samples.Select(s => (double)s.GyroX)),
            Stats.Axis(samples.Select(s => (double)s.GyroY)),
            Stats.Axis(samples.Select(s => (double)s.GyroZ)));
    }

    private static bool TryParseFrame(string line, string sourceFile, out Fd2Sample? sample)
    {
        sample = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("type", out JsonElement type) || type.GetString() != "frame" ||
                !root.TryGetProperty("frame", out JsonElement frame) ||
                !frame.TryGetProperty("RawFd2Hex", out JsonElement rawElement) ||
                !frame.TryGetProperty("Timestamp", out JsonElement timestampElement))
            {
                return false;
            }

            byte[] raw = Convert.FromHexString(rawElement.GetString() ?? "");
            if (raw.Length < 60)
            {
                return false;
            }

            DateTimeOffset arrival = DateTimeOffset.Parse(
                timestampElement.GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            double arrivalDelta = frame.TryGetProperty("DeltaMsFromPreviousFrame", out JsonElement deltaElement)
                ? deltaElement.GetDouble()
                : 0;
            ushort temperatureRaw = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(46, 2));
            sample = new Fd2Sample(
                arrival,
                arrivalDelta,
                BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(42, 4)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(25, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(27, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(29, 2)),
                25.0 + temperatureRaw / 127.0,
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(48, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(50, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(52, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(54, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(56, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(58, 2)),
                sourceFile);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string[] InputFiles(string input, string pattern)
    {
        string fullPath = Path.GetFullPath(input);
        if (File.Exists(fullPath))
        {
            return [fullPath];
        }
        if (Directory.Exists(fullPath))
        {
            return Directory.GetFiles(fullPath, pattern, SearchOption.AllDirectories);
        }
        throw new FileNotFoundException("输入不存在", fullPath);
    }
}

internal sealed record HidAnalysis(
    string File,
    int Samples,
    double DurationSeconds,
    double ReportHz,
    double ImuChangeHz,
    double MotionTimestampChangeHz,
    double MotionTimestampStepP50Us,
    double MotionTimestampStepP99Us,
    double StateReuseRatio,
    double GapP50Ms,
    double GapP95Ms,
    double GapP99Ms,
    double GapMaxMs,
    uint CounterStepMode,
    double CounterStepP50,
    int CounterStepOutliers,
    AxisStats AccelNorm,
    AxisStats GyroX,
    AxisStats GyroY,
    AxisStats GyroZ,
    bool HasExtendedFd2Fields,
    AxisStats MagnetX,
    AxisStats MagnetY,
    AxisStats MagnetZ,
    AxisStats TemperatureC);

internal static class HidCsvAnalyzer
{
    public static HidAnalysis Analyze(string input)
    {
        string[] files = Fd2Analyzer.InputFiles(input, "*.csv");
        if (files.Length != 1)
        {
            throw new InvalidDataException("--hid-csv 每次应指向一个 CSV 文件：" + input);
        }

        using var reader = new StreamReader(files[0]);
        string[] header = (reader.ReadLine() ?? throw new InvalidDataException("CSV 缺少表头")).Split(',');
        var columns = header.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                rows.Add(line.Split(','));
            }
        }
        if (rows.Count < 2)
        {
            throw new InvalidDataException("HID CSV 有效样本不足：" + files[0]);
        }

        double Value(string[] row, string name) =>
            double.Parse(row[columns[name]], CultureInfo.InvariantCulture);
        long Integer(string[] row, string name) =>
            long.Parse(row[columns[name]], CultureInfo.InvariantCulture);

        var gaps = rows.Skip(1).Select(row => Value(row, "gap_ms")).Where(v => v > 0).ToArray();
        double duration = (Value(rows[^1], "time_ms") - Value(rows[0], "time_ms")) / 1000.0;
        int imuChanges = 0;
        int timestampChanges = 0;
        var motionTimestampSteps = new List<uint>();
        var counterSteps = new List<uint>();
        for (int i = 1; i < rows.Count; i++)
        {
            string[] a = rows[i - 1];
            string[] b = rows[i];
            if ((Integer(a, "accel_x"), Integer(a, "accel_y"), Integer(a, "accel_z"), Integer(a, "gyro_x"), Integer(a, "gyro_y"), Integer(a, "gyro_z")) !=
                (Integer(b, "accel_x"), Integer(b, "accel_y"), Integer(b, "accel_z"), Integer(b, "gyro_x"), Integer(b, "gyro_y"), Integer(b, "gyro_z")))
            {
                imuChanges++;
            }
            if (Integer(a, "motion_timestamp") != Integer(b, "motion_timestamp"))
            {
                timestampChanges++;
            }
            uint motionTimestampStep = unchecked(
                (uint)Integer(b, "motion_timestamp") -
                (uint)Integer(a, "motion_timestamp"));
            if (motionTimestampStep > 0 && motionTimestampStep < 5_000_000)
            {
                motionTimestampSteps.Add(motionTimestampStep);
            }
            uint counterStep = unchecked(
                (uint)Integer(b, "report_counter") -
                (uint)Integer(a, "report_counter"));
            if (counterStep > 0 && counterStep < 10_000)
            {
                counterSteps.Add(counterStep);
            }
        }

        double reportHz = (rows.Count - 1) / duration;
        double changeHz = imuChanges / duration;
        bool hasExtendedFields =
            columns.ContainsKey("magnet_x") &&
            columns.ContainsKey("magnet_y") &&
            columns.ContainsKey("magnet_z") &&
            columns.ContainsKey("temp_c");
        AxisStats EmptyStats() => new(0, 0, 0, 0);
        uint counterStepMode = counterSteps
            .GroupBy(v => v)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault();
        int counterStepOutliers = counterSteps.Count(step =>
            counterStepMode > 0 && Math.Abs((long)step - counterStepMode) > 1);
        return new HidAnalysis(
            files[0],
            rows.Count,
            duration,
            reportHz,
            changeHz,
            timestampChanges / duration,
            Stats.Percentile(motionTimestampSteps.Select(value => (double)value), 0.50),
            Stats.Percentile(motionTimestampSteps.Select(value => (double)value), 0.99),
            1.0 - Math.Min(1.0, changeHz / reportHz),
            Stats.Percentile(gaps, 0.50),
            Stats.Percentile(gaps, 0.95),
            Stats.Percentile(gaps, 0.99),
            gaps.Max(),
            counterStepMode,
            Stats.Percentile(counterSteps.Select(v => (double)v), 0.50),
            counterStepOutliers,
            Stats.Axis(rows.Select(row => Math.Sqrt(
                Math.Pow(Value(row, "accel_x"), 2) +
                Math.Pow(Value(row, "accel_y"), 2) +
                Math.Pow(Value(row, "accel_z"), 2)))),
            Stats.Axis(rows.Select(row => Value(row, "gyro_x"))),
            Stats.Axis(rows.Select(row => Value(row, "gyro_y"))),
            Stats.Axis(rows.Select(row => Value(row, "gyro_z"))),
            hasExtendedFields,
            hasExtendedFields ? Stats.Axis(rows.Select(row => Value(row, "magnet_x"))) : EmptyStats(),
            hasExtendedFields ? Stats.Axis(rows.Select(row => Value(row, "magnet_y"))) : EmptyStats(),
            hasExtendedFields ? Stats.Axis(rows.Select(row => Value(row, "magnet_z"))) : EmptyStats(),
            hasExtendedFields ? Stats.Axis(rows.Select(row => Value(row, "temp_c"))) : EmptyStats());
    }
}

internal static class Stats
{
    public static AxisStats Axis(IEnumerable<double> source)
    {
        double[] values = source.ToArray();
        if (values.Length == 0)
        {
            return new AxisStats(0, 0, 0, 0);
        }
        double mean = values.Average();
        double variance = values.Select(v => (v - mean) * (v - mean)).Average();
        return new AxisStats(mean, Math.Sqrt(variance), values.Min(), values.Max());
    }

    public static double Percentile(IEnumerable<double> source, double percentile)
    {
        double[] values = source.OrderBy(v => v).ToArray();
        if (values.Length == 0)
        {
            return 0;
        }
        double position = percentile * (values.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return values[lower];
        }
        return values[lower] + (values[upper] - values[lower]) * (position - lower);
    }
}

internal sealed record SixFacePose(string Name, double TargetX, double TargetY, double TargetZ, double RawX, double RawY, double RawZ);

internal sealed record SixFaceAnalysis(
    string Directory,
    IReadOnlyList<SixFacePose> Poses,
    double[,] Matrix,
    double[] Offset,
    double[] RawBias,
    double ResidualRmsG,
    double Determinant);

internal static class SixFaceAnalyzer
{
    private static readonly (string File, double X, double Y, double Z)[] Definitions =
    [
        ("accel_pos_x.csv", 1, 0, 0),
        ("accel_neg_x.csv", -1, 0, 0),
        ("accel_pos_y.csv", 0, 1, 0),
        ("accel_neg_y.csv", 0, -1, 0),
        ("accel_pos_z.csv", 0, 0, 1),
        ("accel_neg_z.csv", 0, 0, -1)
    ];

    public static SixFaceAnalysis Analyze(string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        var poses = new List<SixFacePose>();
        foreach ((string file, double x, double y, double z) in Definitions)
        {
            string path = Path.Combine(fullDirectory, file);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("六面数据缺少 " + file, path);
            }
            (double rawX, double rawY, double rawZ) = ReadAccelMean(path);
            poses.Add(new SixFacePose(file, x, y, z, rawX, rawY, rawZ));
        }

        var design = new double[poses.Count, 4];
        for (int row = 0; row < poses.Count; row++)
        {
            design[row, 0] = poses[row].RawX;
            design[row, 1] = poses[row].RawY;
            design[row, 2] = poses[row].RawZ;
            design[row, 3] = 1.0;
        }

        var matrix = new double[3, 3];
        var offset = new double[3];
        for (int outputAxis = 0; outputAxis < 3; outputAxis++)
        {
            double[] target = poses.Select(pose => outputAxis switch
            {
                0 => pose.TargetX,
                1 => pose.TargetY,
                _ => pose.TargetZ
            }).ToArray();
            double[] solution = SolveLeastSquares(design, target);
            for (int inputAxis = 0; inputAxis < 3; inputAxis++)
            {
                matrix[outputAxis, inputAxis] = solution[inputAxis];
            }
            offset[outputAxis] = solution[3];
        }

        double[] rawBias = Solve3x3(matrix, offset.Select(value => -value).ToArray());
        double errorSquared = 0;
        foreach (SixFacePose pose in poses)
        {
            double[] predicted = Apply(matrix, offset, [pose.RawX, pose.RawY, pose.RawZ]);
            errorSquared += Math.Pow(predicted[0] - pose.TargetX, 2) +
                            Math.Pow(predicted[1] - pose.TargetY, 2) +
                            Math.Pow(predicted[2] - pose.TargetZ, 2);
        }
        double residual = Math.Sqrt(errorSquared / poses.Count);
        return new SixFaceAnalysis(
            fullDirectory,
            poses,
            matrix,
            offset,
            rawBias,
            residual,
            Determinant3x3(matrix));
    }

    private static (double X, double Y, double Z) ReadAccelMean(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 3)
        {
            throw new InvalidDataException("六面 CSV 样本不足：" + path);
        }
        string[] header = lines[0].Split(',');
        var columns = header.Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        double Parse(string[] row, string name) =>
            double.Parse(row[columns[name]], CultureInfo.InvariantCulture);
        string[][] rows = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Split(',')).ToArray();
        return (
            rows.Average(row => Parse(row, "accel_x")),
            rows.Average(row => Parse(row, "accel_y")),
            rows.Average(row => Parse(row, "accel_z")));
    }

    private static double[] SolveLeastSquares(double[,] design, double[] target)
    {
        int rows = design.GetLength(0);
        int columns = design.GetLength(1);
        var normal = new double[columns, columns];
        var rhs = new double[columns];
        for (int row = 0; row < rows; row++)
        {
            for (int i = 0; i < columns; i++)
            {
                rhs[i] += design[row, i] * target[row];
                for (int j = 0; j < columns; j++)
                {
                    normal[i, j] += design[row, i] * design[row, j];
                }
            }
        }
        return SolveLinear(normal, rhs);
    }

    private static double[] Solve3x3(double[,] matrix, double[] rhs)
    {
        var copy = new double[3, 3];
        Array.Copy(matrix, copy, matrix.Length);
        return SolveLinear(copy, rhs);
    }

    private static double[] SolveLinear(double[,] matrix, double[] rhs)
    {
        int size = rhs.Length;
        var augmented = new double[size, size + 1];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                augmented[row, column] = matrix[row, column];
            }
            augmented[row, size] = rhs[row];
        }

        for (int pivot = 0; pivot < size; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < size; row++)
            {
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot]))
                {
                    best = row;
                }
            }
            if (Math.Abs(augmented[best, pivot]) < 1e-12)
            {
                throw new InvalidDataException("六面数据无法求解，姿态可能重复或主轴标注错误。");
            }
            if (best != pivot)
            {
                for (int column = pivot; column <= size; column++)
                {
                    (augmented[pivot, column], augmented[best, column]) =
                        (augmented[best, column], augmented[pivot, column]);
                }
            }
            double divisor = augmented[pivot, pivot];
            for (int column = pivot; column <= size; column++)
            {
                augmented[pivot, column] /= divisor;
            }
            for (int row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                double factor = augmented[row, pivot];
                for (int column = pivot; column <= size; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }
        return Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
    }

    private static double[] Apply(double[,] matrix, double[] offset, double[] raw) =>
    [
        matrix[0, 0] * raw[0] + matrix[0, 1] * raw[1] + matrix[0, 2] * raw[2] + offset[0],
        matrix[1, 0] * raw[0] + matrix[1, 1] * raw[1] + matrix[1, 2] * raw[2] + offset[1],
        matrix[2, 0] * raw[0] + matrix[2, 1] * raw[1] + matrix[2, 2] * raw[2] + offset[2]
    ];

    private static double Determinant3x3(double[,] m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
        m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
        m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
}

internal sealed record EllipsoidAnalysis(
    string File,
    int TotalSamples,
    int StationarySamples,
    int StationarySegments,
    double[] GyroBiasRaw,
    double[] RawBias,
    double[,] MatrixRawToG,
    double ResidualRmsG,
    double ResidualP95G,
    double LeaveOneOutRmsG,
    double LeaveOneOutP95G,
    double CoverageMinimumEigenvalue);

internal static class EllipsoidAnalyzer
{
    private const double RawReference = 4096.0;
    private const double StationaryGyroThresholdRaw = 8.0;
    private const int MinimumStationarySegmentSamples = 75;

    private sealed record Row(double Ax, double Ay, double Az, double Gx, double Gy, double Gz);
    private sealed record FitResult(double[] RawBias, double[,] MatrixRawToG, double Rms, double P95, double Coverage);

    public static EllipsoidAnalysis Analyze(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0) throw new ArgumentException("至少需要一个任意姿态 CSV。", nameof(inputs));
        string[] paths = inputs.Select(Path.GetFullPath).ToArray();
        var combined = new List<Row>();
        int totalSamples = 0;
        foreach (string path in paths)
        {
            Row[] fileRows = ReadRows(path);
            totalSamples += fileRows.Length;
            combined.AddRange(fileRows);
            combined.Add(new Row(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN));
        }
        Row[] rows = combined.ToArray();
        if (totalSamples < 500)
        {
            throw new InvalidDataException("任意姿态 CSV 总样本不足 500：" + string.Join("; ", paths));
        }

        double[] gyroBias =
        [
            Median(rows.Where(IsFinite).Select(row => row.Gx)),
            Median(rows.Where(IsFinite).Select(row => row.Gy)),
            Median(rows.Where(IsFinite).Select(row => row.Gz))
        ];
        bool IsStationary(Row row)
        {
            double gx = row.Gx - gyroBias[0];
            double gy = row.Gy - gyroBias[1];
            double gz = row.Gz - gyroBias[2];
            double accelNorm = Math.Sqrt(row.Ax * row.Ax + row.Ay * row.Ay + row.Az * row.Az);
            return Math.Sqrt(gx * gx + gy * gy + gz * gz) <= StationaryGyroThresholdRaw &&
                   accelNorm is >= 3000 and <= 5000;
        }

        var poseMeans = new List<double[]>();
        int stationarySamples = 0;
        int start = -1;
        for (int index = 0; index <= rows.Length; index++)
        {
            bool stationary = index < rows.Length && IsStationary(rows[index]);
            if (stationary)
            {
                stationarySamples++;
                if (start < 0) start = index;
                continue;
            }
            if (start >= 0)
            {
                int count = index - start;
                if (count >= MinimumStationarySegmentSamples)
                {
                    Row[] segment = rows[start..index];
                    poseMeans.Add(
                    [
                        segment.Average(row => row.Ax),
                        segment.Average(row => row.Ay),
                        segment.Average(row => row.Az)
                    ]);
                }
                start = -1;
            }
        }

        if (poseMeans.Count < 12)
        {
            throw new InvalidDataException(
                $"只识别到 {poseMeans.Count} 个稳定姿态，至少需要 12 个。每个姿态请保持约 1 秒，姿态间明显转动。");
        }

        FitResult fit = Fit(poseMeans);
        var leaveOneOutErrors = new List<double>();
        for (int heldOut = 0; heldOut < poseMeans.Count; heldOut++)
        {
            List<double[]> training = poseMeans.Where((_, index) => index != heldOut).ToList();
            FitResult fold = Fit(training);
            leaveOneOutErrors.Add(CalibratedNormError(poseMeans[heldOut], fold.RawBias, fold.MatrixRawToG));
        }
        return new EllipsoidAnalysis(
            string.Join("; ", paths),
            totalSamples,
            stationarySamples,
            poseMeans.Count,
            gyroBias,
            fit.RawBias,
            fit.MatrixRawToG,
            fit.Rms,
            fit.P95,
            Math.Sqrt(leaveOneOutErrors.Select(error => error * error).Average()),
            Stats.Percentile(leaveOneOutErrors, 0.95),
            fit.Coverage);
    }

    private static bool IsFinite(Row row) =>
        double.IsFinite(row.Ax) && double.IsFinite(row.Ay) && double.IsFinite(row.Az) &&
        double.IsFinite(row.Gx) && double.IsFinite(row.Gy) && double.IsFinite(row.Gz);

    public static void RunSelfTest()
    {
        double[] expectedBias = [120.0, -85.0, 42.0];
        double[,] shape =
        {
            { 1.05, 0.02, -0.01 },
            { 0.02, 0.97, 0.015 },
            { -0.01, 0.015, 1.02 }
        };
        double[,] inverse = Invert3x3(shape);
        var points = new List<double[]>();
        const int count = 64;
        double goldenAngle = Math.PI * (3.0 - Math.Sqrt(5.0));
        for (int i = 0; i < count; i++)
        {
            double z = 1.0 - 2.0 * (i + 0.5) / count;
            double radius = Math.Sqrt(1.0 - z * z);
            double angle = i * goldenAngle;
            double[] unit = [radius * Math.Cos(angle), radius * Math.Sin(angle), z];
            double[] normalizedRaw = Multiply(inverse, unit);
            points.Add(
            [
                expectedBias[0] + RawReference * normalizedRaw[0],
                expectedBias[1] + RawReference * normalizedRaw[1],
                expectedBias[2] + RawReference * normalizedRaw[2]
            ]);
        }

        FitResult fit = Fit(points);
        double biasError = Math.Sqrt(
            Math.Pow(fit.RawBias[0] - expectedBias[0], 2) +
            Math.Pow(fit.RawBias[1] - expectedBias[1], 2) +
            Math.Pow(fit.RawBias[2] - expectedBias[2], 2));
        if (biasError > 1e-4 || fit.Rms > 1e-8)
        {
            throw new InvalidDataException($"椭球合成自检失败：bias_error={biasError:G8} rms={fit.Rms:G8}");
        }
    }

    private static FitResult Fit(IReadOnlyList<double[]> rawPoints)
    {
        var design = new double[rawPoints.Count, 9];
        var target = Enumerable.Repeat(1.0, rawPoints.Count).ToArray();
        for (int row = 0; row < rawPoints.Count; row++)
        {
            double x = rawPoints[row][0] / RawReference;
            double y = rawPoints[row][1] / RawReference;
            double z = rawPoints[row][2] / RawReference;
            design[row, 0] = x * x;
            design[row, 1] = y * y;
            design[row, 2] = z * z;
            design[row, 3] = 2 * x * y;
            design[row, 4] = 2 * x * z;
            design[row, 5] = 2 * y * z;
            design[row, 6] = x;
            design[row, 7] = y;
            design[row, 8] = z;
        }
        double[] p = SolveLeastSquares(design, target);
        double[,] q =
        {
            { p[0], p[3], p[4] },
            { p[3], p[1], p[5] },
            { p[4], p[5], p[2] }
        };
        double[] linear = [p[6], p[7], p[8]];
        double[] normalizedBias = Multiply(Invert3x3(q), linear).Select(value => -0.5 * value).ToArray();
        double k = 1.0 + Dot(normalizedBias, Multiply(q, normalizedBias));
        if (k <= 0)
        {
            throw new InvalidDataException("椭球拟合不是有效闭合曲面，采集姿态覆盖不足。");
        }
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++) q[row, column] /= k;
        }
        (double[] eigenvalues, double[,] eigenvectors) = SymmetricEigen(q);
        if (eigenvalues.Any(value => value <= 0))
        {
            throw new InvalidDataException("椭球拟合矩阵不是正定矩阵，采集姿态覆盖不足。");
        }
        var normalizedToG = new double[3, 3];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    normalizedToG[row, column] +=
                        eigenvectors[row, axis] * Math.Sqrt(eigenvalues[axis]) * eigenvectors[column, axis];
                }
            }
        }
        var rawToG = new double[3, 3];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++) rawToG[row, column] = normalizedToG[row, column] / RawReference;
        }
        double[] rawBias = normalizedBias.Select(value => value * RawReference).ToArray();
        double[] normErrors = rawPoints
            .Select(point => CalibratedNormError(point, rawBias, rawToG))
            .ToArray();
        double rms = Math.Sqrt(normErrors.Select(error => error * error).Average());

        var coverage = new double[3, 3];
        foreach (double[] point in rawPoints)
        {
            double[] centered = [point[0] - rawBias[0], point[1] - rawBias[1], point[2] - rawBias[2]];
            double[] calibrated = Multiply(rawToG, centered);
            double norm = Norm(calibrated);
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    coverage[row, column] += calibrated[row] * calibrated[column] / (norm * norm * rawPoints.Count);
                }
            }
        }
        double coverageMinimum = SymmetricEigen(coverage).Values.Min();
        return new FitResult(rawBias, rawToG, rms, Stats.Percentile(normErrors, 0.95), coverageMinimum);
    }

    private static double CalibratedNormError(double[] point, double[] rawBias, double[,] rawToG)
    {
        double[] centered = [point[0] - rawBias[0], point[1] - rawBias[1], point[2] - rawBias[2]];
        return Math.Abs(Norm(Multiply(rawToG, centered)) - 1.0);
    }

    private static Row[] ReadRows(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2) return [];
        string[] header = lines[0].Split(',');
        var columns = header.Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        double Parse(string[] row, string name) => double.Parse(row[columns[name]], CultureInfo.InvariantCulture);
        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .Select(row => new Row(
                Parse(row, "accel_x"), Parse(row, "accel_y"), Parse(row, "accel_z"),
                Parse(row, "gyro_x"), Parse(row, "gyro_y"), Parse(row, "gyro_z")))
            .ToArray();
    }

    private static double Median(IEnumerable<double> source) => Stats.Percentile(source, 0.5);
    private static double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();
    private static double Norm(double[] value) => Math.Sqrt(Dot(value, value));
    private static double[] Multiply(double[,] matrix, double[] vector) =>
    [
        matrix[0, 0] * vector[0] + matrix[0, 1] * vector[1] + matrix[0, 2] * vector[2],
        matrix[1, 0] * vector[0] + matrix[1, 1] * vector[1] + matrix[1, 2] * vector[2],
        matrix[2, 0] * vector[0] + matrix[2, 1] * vector[1] + matrix[2, 2] * vector[2]
    ];

    private static double[] SolveLeastSquares(double[,] design, double[] target)
    {
        int rows = design.GetLength(0);
        int columns = design.GetLength(1);
        var normal = new double[columns, columns];
        var rhs = new double[columns];
        for (int row = 0; row < rows; row++)
        {
            for (int i = 0; i < columns; i++)
            {
                rhs[i] += design[row, i] * target[row];
                for (int j = 0; j < columns; j++) normal[i, j] += design[row, i] * design[row, j];
            }
        }
        return SolveLinear(normal, rhs);
    }

    private static double[] SolveLinear(double[,] matrix, double[] rhs)
    {
        int size = rhs.Length;
        var augmented = new double[size, size + 1];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++) augmented[row, column] = matrix[row, column];
            augmented[row, size] = rhs[row];
        }
        for (int pivot = 0; pivot < size; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < size; row++)
            {
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot])) best = row;
            }
            if (Math.Abs(augmented[best, pivot]) < 1e-12)
            {
                throw new InvalidDataException("椭球拟合病态，姿态方向可能不足或过于集中。");
            }
            if (best != pivot)
            {
                for (int column = pivot; column <= size; column++)
                {
                    (augmented[pivot, column], augmented[best, column]) =
                        (augmented[best, column], augmented[pivot, column]);
                }
            }
            double divisor = augmented[pivot, pivot];
            for (int column = pivot; column <= size; column++) augmented[pivot, column] /= divisor;
            for (int row = 0; row < size; row++)
            {
                if (row == pivot) continue;
                double factor = augmented[row, pivot];
                for (int column = pivot; column <= size; column++) augmented[row, column] -= factor * augmented[pivot, column];
            }
        }
        return Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
    }

    private static double[,] Invert3x3(double[,] matrix)
    {
        var inverse = new double[3, 3];
        for (int column = 0; column < 3; column++)
        {
            double[] rhs = [column == 0 ? 1 : 0, column == 1 ? 1 : 0, column == 2 ? 1 : 0];
            double[] solution = SolveLinear((double[,])matrix.Clone(), rhs);
            for (int row = 0; row < 3; row++) inverse[row, column] = solution[row];
        }
        return inverse;
    }

    private static (double[] Values, double[,] Vectors) SymmetricEigen(double[,] source)
    {
        var matrix = (double[,])source.Clone();
        var vectors = new double[3, 3];
        for (int axis = 0; axis < 3; axis++) vectors[axis, axis] = 1;
        for (int iteration = 0; iteration < 64; iteration++)
        {
            int p = 0, q = 1;
            double largest = Math.Abs(matrix[0, 1]);
            if (Math.Abs(matrix[0, 2]) > largest) { p = 0; q = 2; largest = Math.Abs(matrix[0, 2]); }
            if (Math.Abs(matrix[1, 2]) > largest) { p = 1; q = 2; largest = Math.Abs(matrix[1, 2]); }
            if (largest < 1e-14) break;
            double angle = 0.5 * Math.Atan2(2 * matrix[p, q], matrix[q, q] - matrix[p, p]);
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            double app = matrix[p, p];
            double aqq = matrix[q, q];
            double apq = matrix[p, q];
            for (int k = 0; k < 3; k++)
            {
                if (k == p || k == q) continue;
                double akp = matrix[k, p];
                double akq = matrix[k, q];
                matrix[k, p] = matrix[p, k] = c * akp - s * akq;
                matrix[k, q] = matrix[q, k] = s * akp + c * akq;
            }
            matrix[p, p] = c * c * app - 2 * s * c * apq + s * s * aqq;
            matrix[q, q] = s * s * app + 2 * s * c * apq + c * c * aqq;
            matrix[p, q] = matrix[q, p] = 0;
            for (int k = 0; k < 3; k++)
            {
                double vkp = vectors[k, p];
                double vkq = vectors[k, q];
                vectors[k, p] = c * vkp - s * vkq;
                vectors[k, q] = s * vkp + c * vkq;
            }
        }
        return ([matrix[0, 0], matrix[1, 1], matrix[2, 2]], vectors);
    }
}

internal static class ReportWriter
{
    public static void AppendFd2(StringBuilder report, Fd2Analysis a)
    {
        report.AppendLine("## FD2 原始源");
        report.AppendLine();
        report.AppendLine("- 输入：`" + a.Input + "`");
        report.AppendLine($"- 文件/样本：{a.FileCount} / {a.SampleCount}，去重 {a.DuplicateCount}");
        report.AppendLine($"- 时长：{a.DurationSeconds:F3}s，到达率 {a.ArrivalHz:F2}Hz，设备 Motion Timestamp 推导率 {a.SourceTimestampHz:F2}Hz");
        report.AppendLine($"- 新 IMU 值变化率：{a.ImuChangeHz:F2}Hz");
        report.AppendLine($"- Windows 到达间隔 ms：p50={a.ArrivalGapP50Ms:F3} p95={a.ArrivalGapP95Ms:F3} p99={a.ArrivalGapP99Ms:F3} max={a.ArrivalGapMaxMs:F3}");
        report.AppendLine($"- 设备采样间隔 ms：p50={a.SourceGapP50Ms:F3} p95={a.SourceGapP95Ms:F3} p99={a.SourceGapP99Ms:F3} max={a.SourceGapMaxMs:F3}");
        report.AppendLine($"- 到达时间相对设备时间抖动：abs_p95={a.ArrivalSourceJitterP95Ms:F3}ms abs_max={a.ArrivalSourceJitterMaxAbsMs:F3}ms");
        report.AppendLine($"- 到达大间隔：>33ms={a.ArrivalGapOver33} >50ms={a.ArrivalGapOver50}，设备时间异常={a.SourceTimestampNonMonotonic}");
        report.AppendLine($"- 温度：mean={a.Temperature.Mean:F3}C std={a.Temperature.StdDev:F3} min={a.Temperature.Min:F3} max={a.Temperature.Max:F3}");
        report.AppendLine($"- 加速度模长 raw：mean={a.AccelNorm.Mean:F2} std={a.AccelNorm.StdDev:F2}");
        report.AppendLine($"- Gyro raw mean/std：X={a.GyroX.Mean:F3}/{a.GyroX.StdDev:F3} Y={a.GyroY.Mean:F3}/{a.GyroY.StdDev:F3} Z={a.GyroZ.Mean:F3}/{a.GyroZ.StdDev:F3}");
        report.AppendLine();
    }

    public static void AppendHid(StringBuilder report, HidAnalysis a)
    {
        report.AppendLine("## HID 输出");
        report.AppendLine();
        report.AppendLine("- 输入：`" + Path.GetFullPath(a.File) + "`");
        report.AppendLine($"- 样本/时长：{a.Samples} / {a.DurationSeconds:F3}s");
        report.AppendLine($"- report_hz={a.ReportHz:F2} imu_change_hz={a.ImuChangeHz:F2} motion_timestamp_change_hz={a.MotionTimestampChangeHz:F2}");
        report.AppendLine($"- motion_timestamp_step_us：p50={a.MotionTimestampStepP50Us:F1} p99={a.MotionTimestampStepP99Us:F1}");
        report.AppendLine($"- state_reuse_ratio={a.StateReuseRatio:F3} counter_step_mode={a.CounterStepMode} counter_step_p50={a.CounterStepP50:F1} counter_step_outliers={a.CounterStepOutliers}");
        report.AppendLine($"- 报告间隔 ms：p50={a.GapP50Ms:F3} p95={a.GapP95Ms:F3} p99={a.GapP99Ms:F3} max={a.GapMaxMs:F3}");
        report.AppendLine($"- 加速度模长 raw：mean={a.AccelNorm.Mean:F2} std={a.AccelNorm.StdDev:F2}");
        report.AppendLine($"- Gyro raw mean/std：X={a.GyroX.Mean:F3}/{a.GyroX.StdDev:F3} Y={a.GyroY.Mean:F3}/{a.GyroY.StdDev:F3} Z={a.GyroZ.Mean:F3}/{a.GyroZ.StdDev:F3}");
        if (a.HasExtendedFd2Fields)
        {
            report.AppendLine($"- Magnet raw mean/std：X={a.MagnetX.Mean:F3}/{a.MagnetX.StdDev:F3} Y={a.MagnetY.Mean:F3}/{a.MagnetY.StdDev:F3} Z={a.MagnetZ.Mean:F3}/{a.MagnetZ.StdDev:F3}");
            report.AppendLine($"- 温度：mean={a.TemperatureC.Mean:F3}C std={a.TemperatureC.StdDev:F3} min={a.TemperatureC.Min:F3} max={a.TemperatureC.Max:F3}");
        }
        report.AppendLine();
    }

    public static void AppendSixFace(StringBuilder report, SixFaceAnalysis a)
    {
        report.AppendLine("## 六面加速度标定");
        report.AppendLine();
        report.AppendLine("- 数据目录：`" + a.Directory + "`");
        foreach (SixFacePose pose in a.Poses)
        {
            report.AppendLine($"- {pose.Name}: raw_mean=({pose.RawX:F3},{pose.RawY:F3},{pose.RawZ:F3}) target_g=({pose.TargetX:F0},{pose.TargetY:F0},{pose.TargetZ:F0})");
        }
        report.AppendLine("- 求解模型：`accel_g = M * raw + c`");
        report.AppendLine("```text");
        for (int row = 0; row < 3; row++)
        {
            report.AppendLine($"M{row}=({a.Matrix[row, 0]:G10}, {a.Matrix[row, 1]:G10}, {a.Matrix[row, 2]:G10}) c={a.Offset[row]:G10}");
        }
        report.AppendLine($"raw_bias=({a.RawBias[0]:F4}, {a.RawBias[1]:F4}, {a.RawBias[2]:F4})");
        report.AppendLine($"det(M)={a.Determinant:G10} residual_rms_g={a.ResidualRmsG:G8}");
        report.AppendLine("```");
        report.AppendLine();
    }

    public static void AppendEllipsoid(StringBuilder report, EllipsoidAnalysis a)
    {
        report.AppendLine("## 任意姿态椭球标定");
        report.AppendLine();
        report.AppendLine("- 输入：`" + a.File + "`");
        report.AppendLine($"- 总样本/静止样本/独立静止姿态：{a.TotalSamples} / {a.StationarySamples} / {a.StationarySegments}");
        report.AppendLine($"- 静止识别 gyro bias raw=({a.GyroBiasRaw[0]:F3},{a.GyroBiasRaw[1]:F3},{a.GyroBiasRaw[2]:F3})");
        report.AppendLine("- 求解模型：`accel_g = M * (raw - raw_bias)`");
        report.AppendLine("```text");
        for (int row = 0; row < 3; row++)
        {
            report.AppendLine($"M{row}=({a.MatrixRawToG[row, 0]:G10}, {a.MatrixRawToG[row, 1]:G10}, {a.MatrixRawToG[row, 2]:G10})");
        }
        report.AppendLine($"raw_bias=({a.RawBias[0]:F4}, {a.RawBias[1]:F4}, {a.RawBias[2]:F4})");
        report.AppendLine($"norm_residual_rms_g={a.ResidualRmsG:G8} norm_residual_p95_g={a.ResidualP95G:G8}");
        report.AppendLine($"leave_one_pose_out_rms_g={a.LeaveOneOutRmsG:G8} leave_one_pose_out_p95_g={a.LeaveOneOutP95G:G8}");
        report.AppendLine($"orientation_coverage_min_eigenvalue={a.CoverageMinimumEigenvalue:G8} ideal_is_0.3333");
        report.AppendLine("```");
        report.AppendLine();
    }

    public static void AppendInterpretation(StringBuilder report)
    {
        report.AppendLine("## 判读边界");
        report.AppendLine();
        report.AppendLine("- `report_hz` 是虚拟 USB 交付率，不等于真实 IMU 新样本率。");
        report.AppendLine("- Windows 回调到达时间只用于链路诊断；运动积分应优先使用设备 Motion Timestamp。");
        report.AppendLine("- 重复 latest_state 可以维持稳定 USB 端点，但不能被标记成新的物理采样。");
        report.AppendLine("- 本报告不做平滑、死区或异常帧替换，只描述原始链路。");
    }
}
