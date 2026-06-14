using HidSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2V60Viiper;

public sealed class Pro2HidInputSource : IGamepadInputSource, IGamepadOutputSink
{
    private static readonly (int Vid, int Pid)[] KnownIds =
    [
        (0x057E, 0x2009),
        (0x057E, 0x2069)
    ];

    private readonly object gate = new();
    private readonly object writeGate = new();
    private readonly Pro2HidReportParser parser = new();
    private HidStream? stream;
    private int outputReportLength;
    private CancellationTokenSource? cts;
    private Task? readTask;
    private string currentDeviceDescription = "";
    private GamepadState latest = GamepadState.Neutral();
    private DateTimeOffset latestAt;
    private uint updates;
    private uint ignoredReports;
    private string status = "未连接真实 Pro2 输入。";

    public bool IsRunning { get; private set; }
    public bool IsOutputReady => IsRunning && stream != null;
    public string Status
    {
        get { lock (gate) return status; }
        private set { lock (gate) status = value; }
    }

    public IReadOnlyList<string> DescribeCandidates()
    {
        return FindCandidates(includeLikelyVirtual: true)
            .Select(DescribeCandidate)
            .ToList();
    }

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            progress.Report("[PRO2_INPUT] already running: " + Status);
            return;
        }

        List<HidDevice> candidates = FindCandidates(includeLikelyVirtual: false);
        if (candidates.Count == 0)
        {
            Status = "没有找到真实 Windows HID Pro2/Switch Pro 输入。请先在 Windows 蓝牙里配对手柄；VIIPER 虚拟设备不会当作真实输入。";
            progress.Report("[PRO2_INPUT] no candidate hid devices.");
            return;
        }

        foreach (HidDevice device in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!device.TryOpen(out HidStream? opened) || opened == null)
                {
                    progress.Report("[PRO2_INPUT] open skipped: " + DescribeCandidate(device));
                    continue;
                }

                ResetLiveState();
                stream = opened;
                stream.ReadTimeout = 250;
                stream.WriteTimeout = 80;
                outputReportLength = device.GetMaxOutputReportLength();
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                currentDeviceDescription = DescribeCandidate(device);
                IsRunning = true;
                Status = "已打开候选 HID，正在等待真实 Pro2 输入报告：" + currentDeviceDescription;
                progress.Report("[PRO2_INPUT] opened candidate " + currentDeviceDescription +
                                " input_len=" + device.GetMaxInputReportLength() +
                                " output_len=" + (outputReportLength > 0 ? outputReportLength.ToString() : "unknown"));
                readTask = Task.Run(() => ReadLoopAsync(device, opened, progress, cts.Token), CancellationToken.None);

                if (await WaitForLiveInputAsync(TimeSpan.FromSeconds(2.5), cancellationToken))
                {
                    Status = "真实 Pro2 输入已连接并 live：" + currentDeviceDescription;
                    progress.Report("[PRO2_INPUT] confirmed live " + currentDeviceDescription);
                    return;
                }

                progress.Report("[PRO2_INPUT] candidate rejected: opened but no parseable Switch Pro live input within 2.5s. " +
                                currentDeviceDescription);
                await CloseCurrentAsync("候选 HID 没有真实输入，继续尝试下一项。");
            }
            catch (Exception ex)
            {
                progress.Report("[PRO2_INPUT] open failed: " + DescribeCandidate(device) + " / " + ex.Message);
                await CloseCurrentAsync("打开候选失败，继续尝试下一项。");
            }
        }

        Status = "没有确认到真实 Pro2 live 输入。请先在 Windows 蓝牙里配对并唤醒手柄，再点“连接 Pro2 输入”。";
        await CloseCurrentAsync(Status);
    }

    public bool TryGetLatest(out GamepadState state, out TimeSpan age)
    {
        lock (gate)
        {
            if (!IsRunning || latestAt == default)
            {
                state = GamepadState.Neutral();
                age = TimeSpan.MaxValue;
                return false;
            }

            state = latest.Clone();
            age = DateTimeOffset.UtcNow - latestAt;
            return true;
        }
    }

    public bool TryWriteOutputReport(ReadOnlySpan<byte> report, out string error)
    {
        error = "";
        HidStream? target = stream;
        if (!IsRunning || target == null)
        {
            error = "真实 Pro2 HID 未连接";
            return false;
        }

        int targetLength = outputReportLength > 0 ? outputReportLength : report.Length;
        if (targetLength < 33)
        {
            error = "hid output report len " + targetLength + " is too small";
            return false;
        }

        if (report.Length > targetLength)
        {
            for (int i = targetLength; i < report.Length; i++)
            {
                if (report[i] != 0)
                {
                    error = "output report len " + report.Length + " > hid max " + targetLength;
                    return false;
                }
            }
        }

        byte[] buffer = new byte[targetLength];
        report[..Math.Min(report.Length, targetLength)].CopyTo(buffer);

        try
        {
            lock (writeGate)
            {
                target.Write(buffer, 0, buffer.Length);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task StopAsync()
    {
        await CloseCurrentAsync("真实 Pro2 输入已停止。");
    }

    private async Task CloseCurrentAsync(string nextStatus)
    {
        IsRunning = false;
        cts?.Cancel();
        stream?.Dispose();
        stream = null;
        outputReportLength = 0;
        currentDeviceDescription = "";
        if (readTask != null)
        {
            try
            {
                await readTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
            readTask = null;
        }
        cts?.Dispose();
        cts = null;
        Status = nextStatus;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task ReadLoopAsync(
        HidDevice device,
        HidStream opened,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[Math.Max(64, device.GetMaxInputReportLength())];
        DateTimeOffset lastLog = DateTimeOffset.MinValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = opened.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException ex)
            {
                progress.Report("[PRO2_INPUT] read stopped: " + ex.Message);
                break;
            }

            if (read <= 0)
            {
                await Task.Delay(5, cancellationToken);
                continue;
            }

            if (parser.TryParseHidInputReport(buffer.AsSpan(0, read), out GamepadState state, out string source))
            {
                lock (gate)
                {
                    updates++;
                    state.Updates = updates;
                    latest = state;
                    latestAt = DateTimeOffset.UtcNow;
                    status = "真实 Pro2 输入 live，updates=" + updates + " source=" + source + " device=" + currentDeviceDescription;
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now - lastLog > TimeSpan.FromSeconds(5))
                {
                    lastLog = now;
                    progress.Report("[PRO2_INPUT] live source=" + source + " updates=" + updates);
                }
            }
            else
            {
                ignoredReports++;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now - lastLog > TimeSpan.FromSeconds(5))
                {
                    lastLog = now;
                    int preview = Math.Min(read, 8);
                    progress.Report("[PRO2_INPUT] ignored non-Pro2-standard report len=" + read +
                                    " first=" + Convert.ToHexString(buffer.AsSpan(0, preview)).ToLowerInvariant() +
                                    " ignored=" + ignoredReports);
                }
            }
        }

        IsRunning = false;
        Status = "真实 Pro2 输入读取已结束。";
    }

    private async Task<bool> WaitForLiveInputAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRunning)
            {
                return false;
            }

            if (TryGetLatest(out _, out TimeSpan age) && age <= TimeSpan.FromMilliseconds(500))
            {
                return true;
            }

            await Task.Delay(50, cancellationToken);
        }

        return false;
    }

    private void ResetLiveState()
    {
        lock (gate)
        {
            latest = GamepadState.Neutral();
            latestAt = default;
            updates = 0;
            ignoredReports = 0;
        }
    }

    private static List<HidDevice> FindCandidates(bool includeLikelyVirtual)
    {
        return DeviceList.Local.GetHidDevices()
            .Where(IsCandidate)
            .Where(device => includeLikelyVirtual || !IsLikelyVirtualOrBridge(device))
            .OrderByDescending(ScoreDevice)
            .ThenBy(DescribeCandidate, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCandidate(HidDevice device)
    {
        return KnownIds.Any(id => id.Vid == device.VendorID && id.Pid == device.ProductID);
    }

    private static int ScoreDevice(HidDevice device)
    {
        string text = (SafeProduct(device) + " " + SafeManufacturer(device) + " " + device.DevicePath).ToLowerInvariant();
        int score = 0;
        if (device.ProductID == 0x2009) score += 30;
        if (LooksBluetoothBacked(text)) score += 35;
        if (text.Contains("pro controller")) score += 15;
        if (text.Contains("switch")) score += 10;
        if (text.Contains("usbip")) score -= 50;
        if (text.Contains("viiper")) score -= 50;
        return score;
    }

    private static bool IsLikelyVirtualOrBridge(HidDevice device)
    {
        string text = (SafeProduct(device) + " " + SafeManufacturer(device) + " " + device.DevicePath).ToLowerInvariant();
        return text.Contains("viiper") ||
               text.Contains("usbip") ||
               text.Contains("virtual usb") ||
               text.Contains("root#usbip");
    }

    private static bool LooksBluetoothBacked(string text)
    {
        return text.Contains("bluetooth") ||
               text.Contains("bth") ||
               text.Contains("bthenum") ||
               text.Contains("bthle");
    }

    private static string DescribeCandidate(HidDevice device)
    {
        string path = device.DevicePath ?? "";
        string transport = LooksBluetoothBacked(path.ToLowerInvariant()) ? "bluetooth" :
            IsLikelyVirtualOrBridge(device) ? "virtual/usbip" : "hid";
        return $"VID_{device.VendorID:X4}&PID_{device.ProductID:X4} {SafeManufacturer(device)} / {SafeProduct(device)} transport={transport} path={ShortPath(path)}";
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return path.Length <= 96 ? path : path[..96] + "...";
    }

    private static string SafeProduct(HidDevice device)
    {
        try
        {
            return device.GetProductName() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string SafeManufacturer(HidDevice device)
    {
        try
        {
            return device.GetManufacturer() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
