using HidSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2V60Viiper;

public sealed class Pro2HidInputSource : IGamepadInputSource
{
    private static readonly (int Vid, int Pid)[] KnownIds =
    [
        (0x057E, 0x2009),
        (0x057E, 0x2069)
    ];

    private readonly object gate = new();
    private readonly Pro2HidReportParser parser = new();
    private HidStream? stream;
    private CancellationTokenSource? cts;
    private Task? readTask;
    private GamepadState latest = GamepadState.Neutral();
    private DateTimeOffset latestAt;
    private uint updates;
    private string status = "未连接真实 Pro2 输入。";

    public bool IsRunning { get; private set; }
    public string Status
    {
        get { lock (gate) return status; }
        private set { lock (gate) status = value; }
    }

    public IReadOnlyList<string> DescribeCandidates()
    {
        return FindCandidates()
            .Select(DescribeDevice)
            .ToList();
    }

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            progress.Report("[PRO2_INPUT] already running: " + Status);
            return;
        }

        List<HidDevice> candidates = FindCandidates();
        if (candidates.Count == 0)
        {
            Status = "没有找到 Windows HID Pro2/Switch Pro 输入。请先在 Windows 蓝牙里配对手柄。";
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
                    progress.Report("[PRO2_INPUT] open skipped: " + DescribeDevice(device));
                    continue;
                }

                stream = opened;
                stream.ReadTimeout = 250;
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                IsRunning = true;
                Status = "真实 Pro2 输入已连接：" + DescribeDevice(device);
                progress.Report("[PRO2_INPUT] opened " + DescribeDevice(device));
                readTask = Task.Run(() => ReadLoopAsync(device, opened, progress, cts.Token), CancellationToken.None);
                return;
            }
            catch (Exception ex)
            {
                progress.Report("[PRO2_INPUT] open failed: " + DescribeDevice(device) + " / " + ex.Message);
            }
        }

        Status = "找到 HID 候选设备，但全部无法打开。请确认没有 Steam/Input 工具独占真实手柄。";
        await StopAsync();
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

    public async Task StopAsync()
    {
        IsRunning = false;
        cts?.Cancel();
        stream?.Dispose();
        stream = null;
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
        Status = "真实 Pro2 输入已停止。";
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

            if (parser.TryParse(buffer.AsSpan(0, read), out GamepadState state, out string source))
            {
                lock (gate)
                {
                    updates++;
                    state.Updates = updates;
                    latest = state;
                    latestAt = DateTimeOffset.UtcNow;
                    status = "真实 Pro2 输入 live，updates=" + updates + " source=" + source;
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now - lastLog > TimeSpan.FromSeconds(5))
                {
                    lastLog = now;
                    progress.Report("[PRO2_INPUT] live source=" + source + " updates=" + updates);
                }
            }
        }

        IsRunning = false;
        Status = "真实 Pro2 输入读取已结束。";
    }

    private static List<HidDevice> FindCandidates()
    {
        return DeviceList.Local.GetHidDevices()
            .Where(IsCandidate)
            .OrderByDescending(ScoreDevice)
            .ThenBy(DescribeDevice, StringComparer.OrdinalIgnoreCase)
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
        if (text.Contains("bluetooth")) score += 20;
        if (text.Contains("pro controller")) score += 15;
        if (text.Contains("switch")) score += 10;
        if (text.Contains("usbip")) score -= 50;
        if (text.Contains("viiper")) score -= 50;
        return score;
    }

    private static string DescribeDevice(HidDevice device)
    {
        return $"VID_{device.VendorID:X4}&PID_{device.ProductID:X4} {SafeManufacturer(device)} / {SafeProduct(device)}";
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
