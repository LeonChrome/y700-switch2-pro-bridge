using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Y700Switch2V60Viiper;

public sealed class Pro2BleInputSource : IGamepadInputSource, IGamepadInputMetricsSource, IGamepadOutputSink
{
    private static readonly Guid NotifyFd2Uuid = Guid.Parse("ab7de9be-89fe-49ad-828f-118f09df7fd2");
    private static readonly Guid NotifyLegacyUuid = Guid.Parse("7492866c-ec3e-4619-8258-32755ffcc0f8");
    private static readonly Guid AckUuid = Guid.Parse("c765a961-d9d8-4d36-a20a-5315b111836a");
    private static readonly Guid CmdUuid = Guid.Parse("649d4ac9-8eb7-4e6c-af44-1ea54fe5f005");
    private static readonly Guid RumbleUuid = Guid.Parse("cc483f51-9258-427d-a939-630c31f72b05");
    private const ushort NintendoCompanyId = 0x0553;
    private const ushort FastestLegacyConnectionIntervalUnits = 6;
    private const ushort MinimumAcceptedConnectionIntervalUnits = 12;
    private const double MinimumTargetNotifyRateHz = 62.0;
    private const double MinimumUsableNotifyRateHz = 40.0;
    private const double FastNotifyRateHz = 115.0;

    private static readonly byte[][] InitCommands =
    [
        [0x03, 0x91, 0x01, 0x0d, 0x00, 0x08, 0x00, 0x00, 0x01, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff],
        [0x07, 0x91, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00],
        [0x16, 0x91, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00],
        [0x15, 0x91, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00],
        [0x0c, 0x91, 0x01, 0x02, 0x00, 0x04, 0x00, 0x00, 0xff, 0x00, 0x00, 0x00],
        [0x11, 0x91, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00],
        [0x0a, 0x91, 0x01, 0x08, 0x00, 0x14, 0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x35, 0x00, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        [0x0c, 0x91, 0x01, 0x04, 0x00, 0x04, 0x00, 0x00, 0xff, 0x00, 0x00, 0x00],
        [0x03, 0x91, 0x01, 0x0a, 0x00, 0x04, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00],
        [0x10, 0x91, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00],
        [0x01, 0x91, 0x01, 0x0c, 0x00, 0x00, 0x00, 0x00],
        [0x01, 0x91, 0x01, 0x01, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        [0x09, 0x91, 0x01, 0x07, 0x00, 0x08, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        [0x02, 0x91, 0x01, 0x04, 0x00, 0x08, 0x00, 0x00, 0x09, 0x7e, 0x00, 0x00, 0xa8, 0x30, 0x01, 0x00],
        [0x02, 0x91, 0x01, 0x04, 0x00, 0x08, 0x00, 0x00, 0x09, 0x7e, 0x00, 0x00, 0xe8, 0x30, 0x01, 0x00],
    ];

    private readonly object gate = new();
    private readonly object writeGate = new();
    private readonly Pro2HidReportParser parser = new();
    private readonly Pro2InputStabilityFilter inputStability = new();
    private readonly List<BleCandidate> lastCandidates = [];
    private BluetoothLEAdvertisementWatcher? watcher;
    private BluetoothLEDevice? device;
    private BluetoothLEPreferredConnectionParametersRequest? connectionParametersRequest;
    private GattCharacteristic? commandCharacteristic;
    private GattCharacteristic? fd2Characteristic;
    private GattCharacteristic? legacyCharacteristic;
    private GattCharacteristic? ackCharacteristic;
    private GattCharacteristic? rumbleCharacteristic;
    private TaskCompletionSource<byte[]>? ackTcs;
    private GamepadState latest = GamepadState.Neutral();
    private DateTimeOffset latestAt;
    private uint updates;
    private uint rawNotifyCount;
    private uint parseFailCount;
    private uint axisSpikeRejectCount;
    private long firstRawNotifyTicks;
    private long lastRawNotifyTicks;
    private long lastRawNotifyGapTicks;
    private long maxRawNotifyGapTicks;
    private long firstParsedNotifyTicks;
    private long lastParsedNotifyTicks;
    private long lastParsedNotifyGapTicks;
    private long maxParsedNotifyGapTicks;
    private byte rumblePacketId;
    private CancellationTokenSource? rumbleWriterCts;
    private SemaphoreSlim? rumbleSignal;
    private Task? rumbleWriterTask;
    private byte[]? pendingRumblePacket;
    private uint rumbleQueuedCount;
    private uint rumbleWrittenCount;
    private uint rumbleCoalescedCount;
    private uint rumbleFailureCount;
    private uint inputGap45Count;
    private uint inputGap250Count;
    private uint inputGap750Count;
    private double rumbleGain = 1.0;
    private string status = "未连接真实 Pro2 BLE。";
    private string connectedLabel = "";
    private string lastNotifySummary = "";
    private string connectionPreferenceStatus = "not_requested";
    private ushort connectionIntervalUnits;
    private ushort connectionLatency;
    private ushort connectionLinkTimeout;
    private string lastConnectionParametersSummary = "";
    private IProgress<string>? connectionProgress;
    private string linkRateClass = "unknown";
    private string lastPerformanceFailure = "";
    private string lastPerformanceWarning = "";

    public bool IsRunning { get; private set; }
    public bool IsOutputReady =>
        IsRunning &&
        rumbleCharacteristic != null &&
        rumbleWriterTask is { IsCompleted: false };
    public bool IsPerformanceDegraded
    {
        get { lock (gate) return !string.IsNullOrWhiteSpace(lastPerformanceWarning); }
    }
    public string LinkRateClass
    {
        get { lock (gate) return linkRateClass; }
    }

    public string Status
    {
        get { lock (gate) return status; }
        private set { lock (gate) status = value; }
    }

    public string MetricsSummary
    {
        get
        {
            lock (gate)
            {
                return BuildMetricsSummaryNoLock();
            }
        }
    }

    public double RumbleGain
    {
        get
        {
            lock (writeGate)
            {
                return rumbleGain;
            }
        }
    }

    public void SetRumbleGain(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = 1.0;
        }
        lock (writeGate)
        {
            rumbleGain = Math.Clamp(value, 0.0, 3.0);
        }
    }

    public IReadOnlyList<string> DescribeCandidates()
    {
        lock (lastCandidates)
        {
            return lastCandidates.Select(DescribeCandidate).ToList();
        }
    }

    public async Task<IReadOnlyList<string>> ScanAsync(
        IProgress<string> progress,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        List<BleCandidate> found = await ScanCandidatesAsync(progress, duration, cancellationToken);
        lock (lastCandidates)
        {
            lastCandidates.Clear();
            lastCandidates.AddRange(found);
        }

        return found.Select(DescribeCandidate).ToList();
    }

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            progress.Report("[PRO2_BLE] already running: " + Status);
            return;
        }

        await StopAsync();
        lock (gate)
        {
            lastPerformanceFailure = "";
            lastPerformanceWarning = "";
        }
        progress.Report("[PRO2_BLE] scanning without Windows HID pairing. Wake the Pro2 and keep it near the PC.");
        List<BleCandidate> candidates = await ScanCandidatesAsync(progress, TimeSpan.FromSeconds(8), cancellationToken);
        if (candidates.Count == 0)
        {
            Status = "没有扫描到 Pro2 BLE 候选。请长按配对/唤醒手柄，确认未连接到主机、手机或 ESP32。";
            progress.Report("[PRO2_BLE] scan none");
            return;
        }

        lock (lastCandidates)
        {
            lastCandidates.Clear();
            lastCandidates.AddRange(candidates);
        }

        foreach (BleCandidate candidate in candidates.OrderByDescending(c => c.Score).ThenByDescending(c => c.Rssi))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report("[PRO2_BLE] trying " + DescribeCandidate(candidate));
            try
            {
                if (await ConnectCandidateAsync(candidate, progress, cancellationToken))
                {
                    connectedLabel = DescribeCandidate(candidate);
                    Status = "真实 Pro2 BLE 已连接并 live：" + connectedLabel +
                             " / " + MetricsSummary +
                             (string.IsNullOrWhiteSpace(lastPerformanceWarning)
                                 ? ""
                                 : " / 警告：" + lastPerformanceWarning);
                    IsRunning = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                progress.Report("[PRO2_BLE] connect failed " + DescribeCandidate(candidate) + " / " + ex.Message);
            }

            await CloseCurrentAsync("候选 BLE 未确认 live，继续尝试下一项。");
        }

        Status = string.IsNullOrWhiteSpace(lastPerformanceFailure)
            ? "未能连接到真实 Pro2 BLE。请确保手柄处于可连接状态，并且没有被 ESP32、Switch、手机或旧进程占用。"
            : lastPerformanceFailure;
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
        if (!IsOutputReady || rumbleCharacteristic == null)
        {
            error = "真实 Pro2 BLE rumble 特征未就绪";
            return false;
        }

        if (report.Length < 33 || report[0] != 0x02)
        {
            error = "不是 Pro2/Switch2 raw02 HID 输出报告";
            return false;
        }

        try
        {
            lock (writeGate)
            {
                byte[] packet = new byte[Pro2BleRumblePacketEncoder.BlePacketSize];
                byte packetId = (byte)(rumblePacketId++ & 0x0f);
                if (!Pro2BleRumblePacketEncoder.TryEncodeRaw02(
                        report,
                        packetId,
                        packet,
                        out _,
                        out error,
                        rumbleGain))
                {
                    return false;
                }

                if (rumbleWriterTask is not { IsCompleted: false } ||
                    rumbleSignal == null)
                {
                    error = "BLE rumble writer is not running";
                    return false;
                }

                bool coalesced = pendingRumblePacket != null;
                pendingRumblePacket = packet;
                rumbleQueuedCount++;
                if (coalesced)
                {
                    rumbleCoalescedCount++;
                }
                else
                {
                    try
                    {
                        rumbleSignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                    }
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task StopAsync()
    {
        await CloseCurrentAsync("真实 Pro2 BLE 已停止。");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task<bool> ConnectCandidateAsync(
        BleCandidate candidate,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ResetLiveState();
        BluetoothLEDevice? opened = await BluetoothLEDevice.FromBluetoothAddressAsync(
            candidate.Address,
            candidate.AddressType);
        if (opened == null)
        {
            progress.Report("[PRO2_BLE] FromBluetoothAddressAsync returned null.");
            return false;
        }

        device = opened;
        connectionProgress = progress;
        progress.Report("[PRO2_BLE] opened address=" + FormatAddress(candidate.Address) +
                        " name=" + (opened.Name ?? candidate.Name ?? "<unnamed>"));

        GattDeviceServicesResult services =
            await opened.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (services.Status != GattCommunicationStatus.Success)
        {
            progress.Report("[PRO2_BLE] service discovery failed status=" + services.Status);
            return false;
        }

        ObserveWindowsConnectionParameters(opened, progress);

        foreach (GattDeviceService service in services.Services)
        {
            GattCharacteristicsResult chars =
                await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success)
            {
                continue;
            }

            foreach (GattCharacteristic characteristic in chars.Characteristics)
            {
                if (characteristic.Uuid == CmdUuid)
                {
                    commandCharacteristic = characteristic;
                }
                else if (characteristic.Uuid == AckUuid)
                {
                    ackCharacteristic = characteristic;
                }
                else if (characteristic.Uuid == NotifyFd2Uuid)
                {
                    fd2Characteristic = characteristic;
                }
                else if (characteristic.Uuid == NotifyLegacyUuid)
                {
                    legacyCharacteristic = characteristic;
                }
                else if (characteristic.Uuid == RumbleUuid)
                {
                    rumbleCharacteristic = characteristic;
                }
            }
        }

        progress.Report("[PRO2_BLE] gatt chars cmd=" + DescribeCharacteristic(commandCharacteristic) +
                        " ack=" + DescribeCharacteristic(ackCharacteristic) +
                        " fd2=" + DescribeCharacteristic(fd2Characteristic) +
                        " legacy=" + DescribeCharacteristic(legacyCharacteristic) +
                        " rumble=" + DescribeCharacteristic(rumbleCharacteristic));

        if (commandCharacteristic == null || ackCharacteristic == null ||
            (fd2Characteristic == null && legacyCharacteristic == null))
        {
            progress.Report("[PRO2_BLE] required GATT chars missing cmd=" + (commandCharacteristic != null) +
                            " ack=" + (ackCharacteristic != null) +
                            " fd2=" + (fd2Characteristic != null) +
                            " legacy=" + (legacyCharacteristic != null) +
                            " rumble=" + (rumbleCharacteristic != null));
            return false;
        }

        ackCharacteristic.ValueChanged += OnAckValueChanged;

        if (!await SubscribeAsync(ackCharacteristic, "ack", progress))
        {
            return false;
        }

        for (int i = 0; i < InitCommands.Length; i++)
        {
            ackTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            GattCommunicationStatus write = await WriteCharacteristicAsync(commandCharacteristic, InitCommands[i]);
            if (write != GattCommunicationStatus.Success)
            {
                progress.Report("[PRO2_BLE] init write failed index=" + i + " status=" + write);
                return false;
            }

            Task completed = await Task.WhenAny(ackTcs.Task, Task.Delay(1500, cancellationToken));
            if (completed != ackTcs.Task)
            {
                progress.Report("[PRO2_BLE] init ACK timeout index=" + i + "; continuing cautiously.");
            }
            else if (i == 0 || i == InitCommands.Length - 1)
            {
                progress.Report("[PRO2_BLE] init ACK index=" + i + " len=" + ackTcs.Task.Result.Length);
            }
        }

        ackTcs = null;
        if (fd2Characteristic != null)
        {
            fd2Characteristic.ValueChanged += OnNotifyValueChanged;
            if (!await SubscribeAsync(fd2Characteristic, "fd2", progress))
            {
                return false;
            }

            progress.Report("[PRO2_BLE] waiting for FD2 live input...");
            if (await WaitForLiveInputAsync(TimeSpan.FromSeconds(3), cancellationToken))
            {
                if (!await EnsureUsableBlePerformanceAsync(progress, cancellationToken))
                {
                    return false;
                }

                IsRunning = true;
                StartRumbleWriter(progress);
                progress.Report("[PRO2_BLE] live input confirmed updates=" + updates +
                                " raw_notify=" + rawNotifyCount +
                                " rumble=" + (rumbleCharacteristic != null) +
                                " " + MetricsSummary);
                return true;
            }

            progress.Report("[PRO2_BLE] no live FD2 yet; raw_notify=" + rawNotifyCount +
                            " parse_fail=" + parseFailCount +
                            " last=" + lastNotifySummary);
        }

        if (legacyCharacteristic != null)
        {
            legacyCharacteristic.ValueChanged += OnNotifyValueChanged;
            if (!await SubscribeAsync(legacyCharacteristic, "legacy-c0f8", progress))
            {
                return false;
            }

            progress.Report("[PRO2_BLE] waiting for legacy C0F8 live input fallback...");
            if (await WaitForLiveInputAsync(TimeSpan.FromSeconds(5), cancellationToken))
            {
                if (!await EnsureUsableBlePerformanceAsync(progress, cancellationToken))
                {
                    return false;
                }

                IsRunning = true;
                StartRumbleWriter(progress);
                progress.Report("[PRO2_BLE] live input confirmed updates=" + updates +
                                " raw_notify=" + rawNotifyCount +
                                " fallback=legacy rumble=" + (rumbleCharacteristic != null) +
                                " " + MetricsSummary);
                return true;
            }
        }

        progress.Report("[PRO2_BLE] no live input after notify subscribe; raw_notify=" + rawNotifyCount +
                        " parse_fail=" + parseFailCount +
                        " last=" + lastNotifySummary);
        if (rawNotifyCount == 0)
        {
            progress.Report("[PRO2_BLE] notify subscription succeeded but Windows did not deliver any input notification. Try waking the controller again or power-cycling Bluetooth.");
        }
        else if (parseFailCount > 0)
        {
            progress.Report("[PRO2_BLE] notifications arrived but parser rejected them; keep this log for protocol mapping.");
            return false;
        }

        return false;
    }

    private async Task<List<BleCandidate>> ScanCandidatesAsync(
        IProgress<string> progress,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        Dictionary<ulong, BleCandidate> found = new();
        TaskCompletionSource scanDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BluetoothLEAdvertisementWatcher localWatcher = new()
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        watcher = localWatcher;

        localWatcher.Received += (_, args) =>
        {
            BleCandidate? candidate = CandidateFromAdvertisement(args);
            if (candidate == null)
            {
                return;
            }

            lock (found)
            {
                if (!found.TryGetValue(candidate.Address, out BleCandidate? previous) ||
                    candidate.Score > previous.Score ||
                    candidate.Rssi > previous.Rssi)
                {
                    found[candidate.Address] = candidate;
                    progress.Report("[PRO2_BLE] candidate " + DescribeCandidate(candidate));
                }
            }
        };
        localWatcher.Stopped += (_, _) => scanDone.TrySetResult();

        localWatcher.Start();
        try
        {
            await Task.WhenAny(Task.Delay(duration, cancellationToken), scanDone.Task);
        }
        finally
        {
            if (localWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started ||
                localWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Created)
            {
                localWatcher.Stop();
            }
            watcher = null;
        }

        await Task.Delay(150, CancellationToken.None);
        lock (found)
        {
            return found.Values
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Rssi)
                .ToList();
        }
    }

    private static BleCandidate? CandidateFromAdvertisement(BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string name = args.Advertisement.LocalName ?? "";
        bool nintendo = args.Advertisement.ManufacturerData.Any(m => m.CompanyId == NintendoCompanyId);
        bool nameMatch = NameLooksLikeSwitchController(name);
        if (!nintendo && !nameMatch)
        {
            return null;
        }

        int score = 0;
        if (nameMatch) score += 40;
        if (nintendo) score += 30;
        if (name.Contains("Pro", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (name.Contains("Controller", StringComparison.OrdinalIgnoreCase)) score += 10;

        return new BleCandidate(
            args.BluetoothAddress,
            args.BluetoothAddressType,
            name,
            args.RawSignalStrengthInDBm,
            nintendo,
            score);
    }

    private static bool NameLooksLikeSwitchController(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string lower = name.ToLowerInvariant();
        return lower.Contains("pro controller") ||
               lower.Contains("switch") ||
               lower.Contains("nintendo") ||
               lower.Contains("pro2");
    }

    private async Task<bool> SubscribeAsync(
        GattCharacteristic characteristic,
        string label,
        IProgress<string> progress)
    {
        GattCommunicationStatus status =
            await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
        progress.Report("[PRO2_BLE] subscribe " + label + " status=" + status);
        return status == GattCommunicationStatus.Success;
    }

    private void OnAckValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] data = ReadBuffer(args.CharacteristicValue);
        ackTcs?.TrySetResult(data);
    }

    private void OnNotifyValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] data = ReadBuffer(args.CharacteristicValue);
        long nowTicks = Stopwatch.GetTimestamp();
        lock (gate)
        {
            rawNotifyCount++;
            if (lastRawNotifyTicks != 0)
            {
                double gapMilliseconds =
                    (nowTicks - lastRawNotifyTicks) * 1000.0 / Stopwatch.Frequency;
                if (gapMilliseconds >= 45)
                {
                    inputGap45Count++;
                }
                if (gapMilliseconds >= 250)
                {
                    inputGap250Count++;
                }
                if (gapMilliseconds >= 750)
                {
                    inputGap750Count++;
                }
            }
            NoteSample(
                nowTicks,
                ref firstRawNotifyTicks,
                ref lastRawNotifyTicks,
                ref lastRawNotifyGapTicks,
                ref maxRawNotifyGapTicks);
            lastNotifySummary = sender.Uuid + " len=" + data.Length + " head=" + ShortHex(data, 24);
        }

        if (!parser.TryParse(data, out GamepadState state, out string source))
        {
            lock (gate)
            {
                parseFailCount++;
            }
            return;
        }

        lock (gate)
        {
            if (!inputStability.TryAccept(state, out GamepadState stableState, out string filterReason))
            {
                axisSpikeRejectCount++;
                lastNotifySummary += " filtered=" + filterReason;
                return;
            }

            updates++;
            NoteSample(
                nowTicks,
                ref firstParsedNotifyTicks,
                ref lastParsedNotifyTicks,
                ref lastParsedNotifyGapTicks,
                ref maxParsedNotifyGapTicks);
            stableState.Updates = updates;
            latest = stableState;
            latestAt = DateTimeOffset.UtcNow;
            status = "真实 Pro2 BLE live，updates=" + updates + " source=" + source +
                     " raw_notify=" + rawNotifyCount + " " + BuildMetricsSummaryNoLock();
        }
    }

    private void ObserveWindowsConnectionParameters(
        BluetoothLEDevice opened,
        IProgress<string> progress)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            lock (gate)
            {
                connectionPreferenceStatus = "unsupported_pre_win11";
            }
            progress.Report("[PRO2_BLE_LINK] Windows 10 cannot read or request exact BLE connection parameters; the minimum rate will be verified from live notifications.");
            return;
        }

        try
        {
            ObserveWindows11ConnectionParameters(opened, progress);
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                connectionPreferenceStatus = "error_0x" + ex.HResult.ToString("X8");
            }
            progress.Report("[PRO2_BLE_LINK] connection parameter observation unavailable; live notification rate will be used. " +
                            ex.GetType().Name + ": " + ex.Message);
        }
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    private void ObserveWindows11ConnectionParameters(
        BluetoothLEDevice opened,
        IProgress<string> progress)
    {
        opened.ConnectionParametersChanged += OnConnectionParametersChanged;
        lock (gate)
        {
            connectionPreferenceStatus = "observing_native";
        }

        CaptureConnectionParameters(opened, "native_initial", progress);
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    private void OnConnectionParametersChanged(BluetoothLEDevice sender, object args)
    {
        try
        {
            CaptureConnectionParameters(sender, "changed", connectionProgress);
        }
        catch (Exception ex)
        {
            connectionProgress?.Report("[PRO2_BLE_LINK] connection parameter read failed after change: " +
                                       ex.Message);
        }
    }

    private async Task<bool> EnsureUsableBlePerformanceAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        progress.Report("[PRO2_BLE_LINK] allowing native Pro2/Windows parameter negotiation before applying fallback...");
        await Task.Delay(1800, cancellationToken);

        if (device != null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TryCaptureWindows11ConnectionParameters(device, "native_settled", progress);
        }

        BlePerformanceSnapshot native = GetPerformanceSnapshot();
        if (MeetsTargetPerformance(native))
        {
            SetLinkRateClass(native, degraded: false);
            progress.Report("[PRO2_BLE_LINK] native link accepted " + FormatPerformanceSnapshot(native));
            return true;
        }

        if (device != null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            progress.Report("[PRO2_BLE_LINK] native link below 66.7 Hz class; applying Windows 15 ms throughput fallback.");
            if (TryRequestWindows11ThroughputFallback(device, progress))
            {
                ResetPerformanceCounters();
                DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(150, cancellationToken);
                    TryCaptureWindows11ConnectionParameters(device, "fallback_poll", progress: null);
                    BlePerformanceSnapshot current = GetPerformanceSnapshot();
                    if (MeetsTargetPerformance(current))
                    {
                        SetLinkRateClass(current, degraded: false);
                        progress.Report("[PRO2_BLE_LINK] throughput fallback accepted " +
                                        FormatPerformanceSnapshot(current));
                        return true;
                    }
                }
            }
        }
        else
        {
            await Task.Delay(1800, cancellationToken);
            BlePerformanceSnapshot measured = GetPerformanceSnapshot();
            if (MeetsTargetPerformance(measured))
            {
                SetLinkRateClass(measured, degraded: false);
                progress.Report("[PRO2_BLE_LINK] notification-rate fallback accepted " +
                                FormatPerformanceSnapshot(measured));
                return true;
            }
        }

        BlePerformanceSnapshot failed = GetPerformanceSnapshot();
        if (HasUsableLivePerformance(failed))
        {
            string warning = "BLE 输入保持 live，但没有达到 66.7 Hz 目标：" +
                             FormatPerformanceSnapshot(failed);
            lock (gate)
            {
                lastPerformanceWarning = warning;
            }
            SetLinkRateClass(failed, degraded: true);
            progress.Report("[PRO2_BLE_LINK] DEGRADED_ACCEPTED " + warning);
            return true;
        }

        lock (gate)
        {
            linkRateClass = "below_minimum";
            lastPerformanceFailure = "BLE 链路未达到最低 66.7 Hz 等级：" +
                                     FormatPerformanceSnapshot(failed);
            status = lastPerformanceFailure;
        }
        progress.Report("[PRO2_BLE_LINK] REJECTED " + lastPerformanceFailure);
        return false;
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    private bool TryRequestWindows11ThroughputFallback(
        BluetoothLEDevice source,
        IProgress<string> progress)
    {
        try
        {
            BluetoothLEPreferredConnectionParameters preferred =
                BluetoothLEPreferredConnectionParameters.ThroughputOptimized;
            progress.Report("[PRO2_BLE_LINK] requesting fallback " +
                            FormatPreferredConnectionParameters(preferred));
            connectionParametersRequest?.Dispose();
            connectionParametersRequest = source.RequestPreferredConnectionParameters(preferred);
            lock (gate)
            {
                connectionPreferenceStatus = "fallback_" + connectionParametersRequest.Status;
            }
            progress.Report("[PRO2_BLE_LINK] fallback request status=" +
                            connectionParametersRequest.Status);
            return connectionParametersRequest.Status ==
                   BluetoothLEPreferredConnectionParametersRequestStatus.Success;
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                connectionPreferenceStatus = "fallback_error_0x" + ex.HResult.ToString("X8");
            }
            progress.Report("[PRO2_BLE_LINK] fallback request failed: " + ex.Message);
            return false;
        }
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    private void TryCaptureWindows11ConnectionParameters(
        BluetoothLEDevice source,
        string reason,
        IProgress<string>? progress)
    {
        try
        {
            CaptureConnectionParameters(source, reason, progress);
        }
        catch (Exception ex)
        {
            progress?.Report("[PRO2_BLE_LINK] parameter read failed reason=" + reason +
                             " error=" + ex.Message);
        }
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    private void CaptureConnectionParameters(
        BluetoothLEDevice source,
        string reason,
        IProgress<string>? progress)
    {
        BluetoothLEConnectionParameters? parameters = source.GetConnectionParameters();
        if (parameters == null)
        {
            progress?.Report("[PRO2_BLE_LINK] parameters reason=" + reason +
                             " unavailable (device is not connected yet).");
            return;
        }

        string summary = FormatConnectionParameters(parameters);
        bool changed;
        lock (gate)
        {
            connectionIntervalUnits = parameters.ConnectionInterval;
            connectionLatency = parameters.ConnectionLatency;
            connectionLinkTimeout = parameters.LinkTimeout;
            changed = summary != lastConnectionParametersSummary;
            lastConnectionParametersSummary = summary;
        }

        if (changed || reason != "changed")
        {
            progress?.Report("[PRO2_BLE_LINK] parameters reason=" + reason + " " + summary);
        }
    }

    private async Task<bool> WaitForLiveInputAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasFreshInput(TimeSpan.FromMilliseconds(500)))
            {
                return true;
            }

            await Task.Delay(50, cancellationToken);
        }

        return false;
    }

    private bool HasFreshInput(TimeSpan maximumAge)
    {
        lock (gate)
        {
            return latestAt != default &&
                   DateTimeOffset.UtcNow - latestAt <= maximumAge;
        }
    }

    private void StartRumbleWriter(IProgress<string> progress)
    {
        if (rumbleWriterTask is { IsCompleted: false })
        {
            return;
        }

        rumbleWriterCts = new CancellationTokenSource();
        rumbleSignal = new SemaphoreSlim(0, 1);
        lock (writeGate)
        {
            pendingRumblePacket = null;
            rumbleQueuedCount = 0;
            rumbleWrittenCount = 0;
            rumbleCoalescedCount = 0;
            rumbleFailureCount = 0;
        }
        rumbleWriterTask = Task.Run(
            () => RumbleWriterLoopAsync(progress, rumbleWriterCts.Token));
        progress.Report("[PRO2_OUTPUT] asynchronous coalescing BLE writer started.");
    }

    private async Task RumbleWriterLoopAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        long lastWriteTicks = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            SemaphoreSlim? signal = rumbleSignal;
            if (signal == null)
            {
                return;
            }

            await signal.WaitAsync(cancellationToken);
            double minimumWriteIntervalMs;
            lock (gate)
            {
                minimumWriteIntervalMs = connectionIntervalUnits == 0
                    ? 8
                    : Math.Clamp(connectionIntervalUnits * 1.25, 7.5, 15);
            }
            if (lastWriteTicks != 0)
            {
                double elapsedMs =
                    (Stopwatch.GetTimestamp() - lastWriteTicks) *
                    1000.0 /
                    Stopwatch.Frequency;
                double remainingMs = minimumWriteIntervalMs - elapsedMs;
                if (remainingMs > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(remainingMs),
                        cancellationToken);
                }
            }

            byte[]? packet;
            lock (writeGate)
            {
                packet = pendingRumblePacket;
                pendingRumblePacket = null;
            }
            if (packet == null)
            {
                continue;
            }

            GattCharacteristic? characteristic = rumbleCharacteristic;
            if (characteristic == null)
            {
                continue;
            }

            try
            {
                GattCommunicationStatus status =
                    await WriteCharacteristicAsync(characteristic, packet);
                lastWriteTicks = Stopwatch.GetTimestamp();
                lock (writeGate)
                {
                    if (status == GattCommunicationStatus.Success)
                    {
                        rumbleWrittenCount++;
                    }
                    else
                    {
                        rumbleFailureCount++;
                    }
                }
                if (status != GattCommunicationStatus.Success)
                {
                    progress.Report("[PRO2_OUTPUT] BLE writer status=" + status);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lock (writeGate)
                {
                    rumbleFailureCount++;
                }
                progress.Report("[PRO2_OUTPUT] BLE writer exception=" + ex.Message);
            }
        }
    }

    private async Task StopRumbleWriterAsync()
    {
        CancellationTokenSource? writerCts = rumbleWriterCts;
        Task? writerTask = rumbleWriterTask;
        SemaphoreSlim? signal = rumbleSignal;
        rumbleWriterCts = null;
        rumbleWriterTask = null;
        rumbleSignal = null;
        lock (writeGate)
        {
            pendingRumblePacket = null;
        }

        if (writerCts != null)
        {
            writerCts.Cancel();
        }
        if (signal != null)
        {
            try
            {
                signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
        if (writerTask != null)
        {
            try
            {
                await writerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        signal?.Dispose();
        writerCts?.Dispose();
    }

    private static async Task<GattCommunicationStatus> WriteCharacteristicAsync(
        GattCharacteristic characteristic,
        byte[] data)
    {
        GattWriteOption option =
            (characteristic.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse) != 0
                ? GattWriteOption.WriteWithoutResponse
                : GattWriteOption.WriteWithResponse;
        return await characteristic.WriteValueAsync(ToBuffer(data), option);
    }

    private async Task CloseCurrentAsync(string nextStatus)
    {
        IsRunning = false;
        await StopRumbleWriterAsync();
        watcher?.Stop();
        watcher = null;

        if (ackCharacteristic != null)
        {
            ackCharacteristic.ValueChanged -= OnAckValueChanged;
        }
        if (fd2Characteristic != null)
        {
            fd2Characteristic.ValueChanged -= OnNotifyValueChanged;
        }
        if (legacyCharacteristic != null)
        {
            legacyCharacteristic.ValueChanged -= OnNotifyValueChanged;
        }

        ackCharacteristic = null;
        fd2Characteristic = null;
        legacyCharacteristic = null;
        commandCharacteristic = null;
        rumbleCharacteristic = null;
        ackTcs = null;
        connectedLabel = "";
        if (device != null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            UnsubscribeWindows11ConnectionEvents(device);
        }
        connectionParametersRequest?.Dispose();
        connectionParametersRequest = null;
        connectionProgress = null;
        device?.Dispose();
        device = null;
        ResetLiveState();
        Status = nextStatus;
        await Task.CompletedTask;
    }

    private void ResetLiveState()
    {
        lock (gate)
        {
            latest = GamepadState.Neutral();
            latestAt = default;
            updates = 0;
            rawNotifyCount = 0;
            parseFailCount = 0;
            axisSpikeRejectCount = 0;
            firstRawNotifyTicks = 0;
            lastRawNotifyTicks = 0;
            lastRawNotifyGapTicks = 0;
            maxRawNotifyGapTicks = 0;
            firstParsedNotifyTicks = 0;
            lastParsedNotifyTicks = 0;
            lastParsedNotifyGapTicks = 0;
            maxParsedNotifyGapTicks = 0;
            inputGap45Count = 0;
            inputGap250Count = 0;
            inputGap750Count = 0;
            lastNotifySummary = "";
            connectionPreferenceStatus = "not_requested";
            connectionIntervalUnits = 0;
            connectionLatency = 0;
            connectionLinkTimeout = 0;
            lastConnectionParametersSummary = "";
            linkRateClass = "unknown";
            lastPerformanceWarning = "";
            inputStability.Reset();
        }
    }

    private void ResetPerformanceCounters()
    {
        lock (gate)
        {
            updates = 0;
            rawNotifyCount = 0;
            parseFailCount = 0;
            axisSpikeRejectCount = 0;
            firstRawNotifyTicks = 0;
            lastRawNotifyTicks = 0;
            lastRawNotifyGapTicks = 0;
            maxRawNotifyGapTicks = 0;
            firstParsedNotifyTicks = 0;
            lastParsedNotifyTicks = 0;
            lastParsedNotifyGapTicks = 0;
            maxParsedNotifyGapTicks = 0;
            inputGap45Count = 0;
            inputGap250Count = 0;
            inputGap750Count = 0;
        }
    }

    private string BuildMetricsSummaryNoLock()
    {
        double connectionIntervalMs = connectionIntervalUnits * 1.25;
        double connectionEventHz = connectionIntervalUnits == 0
            ? 0
            : 800.0 / connectionIntervalUnits;
        return "ble_raw_hz=" + SampleRate(rawNotifyCount, firstRawNotifyTicks, lastRawNotifyTicks).ToString("F1") +
               " ble_parsed_hz=" + SampleRate(updates, firstParsedNotifyTicks, lastParsedNotifyTicks).ToString("F1") +
               " ble_last_gap_ms=" + TicksToMilliseconds(lastParsedNotifyGapTicks).ToString("F1") +
               " ble_max_gap_ms=" + TicksToMilliseconds(maxParsedNotifyGapTicks).ToString("F1") +
               " ble_conn_ms=" + connectionIntervalMs.ToString("F2") +
               " ble_conn_event_hz=" + connectionEventHz.ToString("F1") +
               " ble_latency=" + connectionLatency +
               " ble_timeout_ms=" + (connectionLinkTimeout * 10) +
               " ble_pref=" + connectionPreferenceStatus +
               " ble_rate_class=" + linkRateClass +
               " ble_gap45=" + inputGap45Count +
               " ble_gap250=" + inputGap250Count +
               " ble_gap750=" + inputGap750Count +
               " axis_spike=" + axisSpikeRejectCount +
               " rumble_q=" + rumbleQueuedCount +
               " rumble_w=" + rumbleWrittenCount +
               " rumble_merge=" + rumbleCoalescedCount +
               " rumble_fail=" + rumbleFailureCount +
               " rumble_gain=" + RumbleGain.ToString("F1") +
               " parse_fail=" + parseFailCount;
    }

    private BlePerformanceSnapshot GetPerformanceSnapshot()
    {
        lock (gate)
        {
            return new BlePerformanceSnapshot(
                connectionIntervalUnits,
                SampleRate(rawNotifyCount, firstRawNotifyTicks, lastRawNotifyTicks),
                SampleRate(updates, firstParsedNotifyTicks, lastParsedNotifyTicks),
                rawNotifyCount,
                updates);
        }
    }

    private static bool MeetsTargetPerformance(BlePerformanceSnapshot snapshot)
    {
        bool connectionFastEnough =
            snapshot.ConnectionIntervalUnits == 0 ||
            snapshot.ConnectionIntervalUnits <= MinimumAcceptedConnectionIntervalUnits;
        return connectionFastEnough &&
               snapshot.RawNotifications >= 20 &&
               snapshot.NotifyRateHz >= MinimumTargetNotifyRateHz &&
               snapshot.ParsedRateHz >= MinimumTargetNotifyRateHz * 0.9;
    }

    private static bool HasUsableLivePerformance(BlePerformanceSnapshot snapshot)
    {
        return ShouldKeepLiveInput(
            snapshot.NotifyRateHz,
            snapshot.ParsedRateHz,
            snapshot.RawNotifications,
            snapshot.ParsedNotifications);
    }

    public static bool ShouldKeepLiveInput(
        double rawNotifyRateHz,
        double parsedNotifyRateHz,
        uint rawNotifications,
        uint parsedNotifications)
    {
        return rawNotifications >= 40 &&
               parsedNotifications >= 40 &&
               rawNotifyRateHz >= MinimumUsableNotifyRateHz &&
               parsedNotifyRateHz >= MinimumUsableNotifyRateHz &&
               parsedNotifications >= rawNotifications * 0.9;
    }

    private void SetLinkRateClass(BlePerformanceSnapshot snapshot, bool degraded)
    {
        string rateClass;
        if ((snapshot.ConnectionIntervalUnits == 0 ||
             snapshot.ConnectionIntervalUnits <= FastestLegacyConnectionIntervalUnits) &&
            snapshot.NotifyRateHz >= FastNotifyRateHz)
        {
            rateClass = "133hz";
        }
        else if (degraded)
        {
            rateClass = snapshot.ConnectionIntervalUnits <= MinimumAcceptedConnectionIntervalUnits
                ? "66.7hz_link_degraded"
                : "live_degraded";
        }
        else
        {
            rateClass = "66.7hz";
        }

        lock (gate)
        {
            linkRateClass = rateClass;
            if (connectionPreferenceStatus == "observing_native")
            {
                connectionPreferenceStatus = "native";
            }
        }
    }

    private static string FormatPerformanceSnapshot(BlePerformanceSnapshot snapshot)
    {
        double intervalMs = snapshot.ConnectionIntervalUnits * 1.25;
        double eventHz = snapshot.ConnectionIntervalUnits == 0
            ? 0
            : 800.0 / snapshot.ConnectionIntervalUnits;
        return "interval_ms=" + intervalMs.ToString("F2") +
               " event_hz=" + eventHz.ToString("F1") +
               " raw_hz=" + snapshot.NotifyRateHz.ToString("F1") +
               " parsed_hz=" + snapshot.ParsedRateHz.ToString("F1") +
               " raw_count=" + snapshot.RawNotifications +
               " parsed_count=" + snapshot.ParsedNotifications;
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    private void UnsubscribeWindows11ConnectionEvents(BluetoothLEDevice source)
    {
        source.ConnectionParametersChanged -= OnConnectionParametersChanged;
    }

    private static string FormatPreferredConnectionParameters(
        BluetoothLEPreferredConnectionParameters parameters)
    {
        double minMs = parameters.MinConnectionInterval * 1.25;
        double maxMs = parameters.MaxConnectionInterval * 1.25;
        return "interval_units=" + parameters.MinConnectionInterval + ".." +
               parameters.MaxConnectionInterval +
               " interval_ms=" + minMs.ToString("F2") + ".." + maxMs.ToString("F2") +
               " event_hz=" + (800.0 / parameters.MaxConnectionInterval).ToString("F1") + ".." +
               (800.0 / parameters.MinConnectionInterval).ToString("F1") +
               " latency=" + parameters.ConnectionLatency +
               " link_timeout_ms=" + (parameters.LinkTimeout * 10);
    }

    private static string FormatConnectionParameters(BluetoothLEConnectionParameters parameters)
    {
        double intervalMs = parameters.ConnectionInterval * 1.25;
        double eventHz = parameters.ConnectionInterval == 0
            ? 0
            : 800.0 / parameters.ConnectionInterval;
        return "interval_units=" + parameters.ConnectionInterval +
               " interval_ms=" + intervalMs.ToString("F2") +
               " event_hz=" + eventHz.ToString("F1") +
               " latency=" + parameters.ConnectionLatency +
               " link_timeout_ms=" + (parameters.LinkTimeout * 10);
    }

    private static void NoteSample(
        long nowTicks,
        ref long firstTicks,
        ref long lastTicks,
        ref long lastGapTicks,
        ref long maxGapTicks)
    {
        if (firstTicks == 0)
        {
            firstTicks = nowTicks;
        }

        if (lastTicks != 0)
        {
            lastGapTicks = Math.Max(0, nowTicks - lastTicks);
            maxGapTicks = Math.Max(maxGapTicks, lastGapTicks);
        }

        lastTicks = nowTicks;
    }

    private static double SampleRate(uint count, long firstTicks, long lastTicks)
    {
        if (count < 2 || firstTicks == 0 || lastTicks <= firstTicks)
        {
            return 0;
        }

        return (count - 1) * (double)Stopwatch.Frequency / (lastTicks - firstTicks);
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks <= 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static IBuffer ToBuffer(byte[] data)
    {
        DataWriter writer = new();
        writer.WriteBytes(data);
        return writer.DetachBuffer();
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        byte[] data = new byte[buffer.Length];
        DataReader reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(data);
        return data;
    }

    private static string DescribeCharacteristic(GattCharacteristic? characteristic)
    {
        return characteristic == null
            ? "missing"
            : "ok/" + characteristic.CharacteristicProperties;
    }

    private static string ShortHex(byte[] data, int maxBytes)
    {
        if (data.Length == 0)
        {
            return "<empty>";
        }

        int count = Math.Min(data.Length, maxBytes);
        string hex = Convert.ToHexString(data.AsSpan(0, count));
        return data.Length > count ? hex + "..." : hex;
    }

    private static string DescribeCandidate(BleCandidate candidate)
    {
        return FormatAddress(candidate.Address) +
               "/" + candidate.AddressType +
               " rssi=" + candidate.Rssi +
               " name=\"" + (string.IsNullOrWhiteSpace(candidate.Name) ? "<none>" : candidate.Name) + "\"" +
               " nintendo_mfg=" + candidate.NintendoManufacturer +
               " score=" + candidate.Score;
    }

    private static string FormatAddress(ulong address)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, address);
        return Convert.ToHexString(bytes[2..]).Insert(10, ":").Insert(8, ":").Insert(6, ":").Insert(4, ":").Insert(2, ":");
    }

    private sealed record BleCandidate(
        ulong Address,
        BluetoothAddressType AddressType,
        string Name,
        short Rssi,
        bool NintendoManufacturer,
        int Score);

    private sealed record BlePerformanceSnapshot(
        ushort ConnectionIntervalUnits,
        double NotifyRateHz,
        double ParsedRateHz,
        uint RawNotifications,
        uint ParsedNotifications);
}
