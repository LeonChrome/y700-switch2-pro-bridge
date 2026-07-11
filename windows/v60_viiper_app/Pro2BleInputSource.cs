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
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace Y700Switch2V60Viiper;

public sealed class Pro2BleInputSource :
    IGamepadInputSource,
    IGamepadInputMetricsSource,
    IGamepadInputRateSource,
    IGamepadRuntimeTelemetrySink,
    IGamepadOutputSink
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
    private const double MinimumUsableNotifyRateHz = 10.0;
    private const uint MinimumUsableNotifications = 8;
    private const double FastNotifyRateHz = 115.0;
    private const double LinkHealthReassertGapMs = 55.0;
    private const double LinkHealthReassertCooldownMs = 8000.0;
    private const uint LinkHealthWarmupNotifications = 80;

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
    private readonly Pro2InputStabilityOptions inputStabilityOptions =
        Pro2InputStabilityOptions.Default;
    private readonly Pro2InputStabilityFilter inputStability;
    private readonly Pro2Fd2SpikeRecorder spikeRecorder;
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
    private GamepadState rawState = GamepadState.Neutral();
    private GamepadState filteredState = GamepadState.Neutral();
    private DateTimeOffset rawStateAt;
    private DateTimeOffset filteredStateAt;
    private uint rumbleQueuedCount;
    private uint rumbleWrittenCount;
    private uint rumbleCoalescedCount;
    private uint rumbleFailureCount;
    private uint inputGap45Count;
    private uint inputGap250Count;
    private uint inputGap750Count;
    private double rumbleGain = 1.0;
    private StickProcessingMode stickProcessingMode = StickProcessingOption.Default.Mode;
    private string status = "未连接真实 Pro2 BLE。";
    private string lastScanDiagnostic = "";
    private string connectedLabel = "";
    private string connectedAddress = "";
    private string lastNotifySummary = "";
    private string connectionPreferenceStatus = "not_requested";
    private ushort connectionIntervalUnits;
    private ushort connectionLatency;
    private ushort connectionLinkTimeout;
    private string lastConnectionParametersSummary = "";
    private string gattSelectionMode = "not_scanned";
    private string gattDiscoverySummary = "";
    private IProgress<string>? connectionProgress;
    private string linkRateClass = "unknown";
    private string lastPerformanceFailure = "";
    private string lastPerformanceWarning = "";
    private double lastViiperPushHz;
    private ulong axisSpikeLogCount;
    private long asyncProgressDroppedCount;
    private long lastThroughputReassertTicks;
    private uint throughputReassertCount;
    private double lastThroughputReassertGapMs;

    public Pro2BleInputSource()
    {
        inputStability = new Pro2InputStabilityFilter(inputStabilityOptions);
        spikeRecorder = new Pro2Fd2SpikeRecorder(inputStabilityOptions);
    }

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

    public string LastScanDiagnostic
    {
        get { lock (gate) return lastScanDiagnostic; }
        private set { lock (gate) lastScanDiagnostic = value; }
    }

    public string ConnectedAddress
    {
        get { lock (gate) return connectedAddress; }
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

    public double CurrentParsedRateHz
    {
        get
        {
            lock (gate)
            {
                return SampleRate(updates, firstParsedNotifyTicks, lastParsedNotifyTicks);
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

    public void SetStickProcessingMode(StickProcessingMode mode)
    {
        lock (gate)
        {
            if (stickProcessingMode == mode)
            {
                return;
            }

            stickProcessingMode = mode;
            inputStability.Reset();
            rawState = GamepadState.Neutral();
            filteredState = GamepadState.Neutral();
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

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        return StartAsync(
            progress,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            preferredAddresses: null,
            onlyPreferredAddresses: false,
            cancellationToken);
    }

    public Task StartAsync(
        IProgress<string> progress,
        IReadOnlySet<string> excludedAddresses,
        CancellationToken cancellationToken)
    {
        return StartAsync(
            progress,
            excludedAddresses,
            preferredAddresses: null,
            onlyPreferredAddresses: false,
            cancellationToken);
    }

    public async Task StartAsync(
        IProgress<string> progress,
        IReadOnlySet<string> excludedAddresses,
        IReadOnlySet<string>? preferredAddresses,
        bool onlyPreferredAddresses,
        CancellationToken cancellationToken)
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
        progress.Report("[PRO2_STICK] mode=" + StickProcessingModeLabel(stickProcessingMode) +
                        (stickProcessingMode == StickProcessingMode.RawDirect
                            ? " raw direct, no axis hold/ramp/filter on gameplay path."
                            : " stability guard enabled. " + inputStabilityOptions.Summary));
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

        var preferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (preferredAddresses != null)
        {
            foreach (string address in preferredAddresses)
            {
                preferred.Add(address);
            }
        }
        if (preferred.Count > 0)
        {
            progress.Report("[PRO2_BLE] preferred addresses=" + string.Join(",", preferred) +
                            " only_preferred=" + onlyPreferredAddresses);
        }

        var orderedCandidates = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Address = FormatAddress(candidate.Address)
            })
            .OrderByDescending(item => preferred.Contains(item.Address))
            .ThenByDescending(item => item.Candidate.Score)
            .ThenByDescending(item => item.Candidate.Rssi)
            .ToList();

        int skippedNonPreferred = 0;
        foreach (var item in orderedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BleCandidate candidate = item.Candidate;
            string candidateAddress = item.Address;
            if (onlyPreferredAddresses && preferred.Count > 0 && !preferred.Contains(candidateAddress))
            {
                skippedNonPreferred++;
                continue;
            }
            if (excludedAddresses.Contains(candidateAddress))
            {
                progress.Report("[PRO2_BLE] skip occupied candidate " + DescribeCandidate(candidate));
                continue;
            }

            progress.Report("[PRO2_BLE] trying " + DescribeCandidate(candidate));
            try
            {
                if (await ConnectCandidateAsync(candidate, progress, cancellationToken))
                {
                    connectedLabel = DescribeCandidate(candidate);
                    lock (gate)
                    {
                        connectedAddress = candidateAddress;
                    }
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

        if (skippedNonPreferred > 0)
        {
            progress.Report("[PRO2_BLE] skipped non-preferred candidates=" + skippedNonPreferred);
        }
        Status = onlyPreferredAddresses && preferred.Count > 0
            ? "未连接到上次保存的 Pro2 BLE 地址。请唤醒上次那只手柄；如需换新手柄，请手动点击连接。"
            : string.IsNullOrWhiteSpace(lastPerformanceFailure)
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
        await spikeRecorder.DisposeAsync();
    }

    public void ReportViiperPushRate(double actualHz)
    {
        if (double.IsNaN(actualHz) || double.IsInfinity(actualHz) || actualHz < 0)
        {
            return;
        }

        lock (gate)
        {
            lastViiperPushHz = actualHz;
        }
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

        ObserveWindowsConnectionParameters(opened, progress);
        await Task.Delay(500, cancellationToken);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TryRequestWindows11ThroughputPreference(opened, "initial", progress);
        }

        GattDeviceServicesResult services =
            await opened.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (services.Status != GattCommunicationStatus.Success)
        {
            progress.Report("[PRO2_BLE] service discovery failed status=" + services.Status);
            return false;
        }

        await DiscoverGattChannelsAsync(services.Services, progress);

        progress.Report("[PRO2_BLE] gatt chars cmd=" + DescribeCharacteristic(commandCharacteristic) +
                        " ack=" + DescribeCharacteristic(ackCharacteristic) +
                        " fd2=" + DescribeCharacteristic(fd2Characteristic) +
                        " legacy=" + DescribeCharacteristic(legacyCharacteristic) +
                        " rumble=" + DescribeCharacteristic(rumbleCharacteristic) +
                        " mode=" + gattSelectionMode);

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
            GattCommunicationStatus write = await WriteCharacteristicWithRetryAsync(
                commandCharacteristic,
                InitCommands[i],
                "init-" + i,
                progress,
                cancellationToken);
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

    private async Task DiscoverGattChannelsAsync(
        IReadOnlyList<GattDeviceService> services,
        IProgress<string> progress)
    {
        var serviceSummaries = new List<string>();
        var probes = new List<GattServiceProbe>();

        foreach (GattDeviceService service in services)
        {
            GattCharacteristicsResult chars =
                await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success)
            {
                serviceSummaries.Add(service.Uuid + ":status=" + chars.Status);
                continue;
            }

            List<GattCharacteristic> characteristics = chars.Characteristics
                .OrderBy(c => c.AttributeHandle)
                .ToList();
            List<GattCharacteristic> writeCandidates = characteristics
                .Where(HasWriteProperty)
                .ToList();
            List<GattCharacteristic> notifyCandidates = characteristics
                .Where(HasNotifyProperty)
                .ToList();

            bool exactMatch = false;
            foreach (GattCharacteristic characteristic in characteristics)
            {
                if (characteristic.Uuid == CmdUuid)
                {
                    commandCharacteristic = characteristic;
                    exactMatch = true;
                }
                else if (characteristic.Uuid == AckUuid)
                {
                    ackCharacteristic = characteristic;
                    exactMatch = true;
                }
                else if (characteristic.Uuid == NotifyFd2Uuid)
                {
                    fd2Characteristic = characteristic;
                    exactMatch = true;
                }
                else if (characteristic.Uuid == NotifyLegacyUuid)
                {
                    legacyCharacteristic = characteristic;
                    exactMatch = true;
                }
                else if (characteristic.Uuid == RumbleUuid)
                {
                    rumbleCharacteristic = characteristic;
                    exactMatch = true;
                }
            }

            bool hasInput = characteristics.Any(c =>
                c.Uuid == NotifyFd2Uuid || c.Uuid == NotifyLegacyUuid);
            if (exactMatch || hasInput)
            {
                probes.Add(new GattServiceProbe(
                    service.Uuid,
                    writeCandidates,
                    notifyCandidates,
                    hasInput,
                    exactMatch));
            }

            serviceSummaries.Add(
                ShortGuid(service.Uuid) +
                ":chars=" + characteristics.Count +
                ",write=" + writeCandidates.Count +
                ",notify=" + notifyCandidates.Count +
                (exactMatch ? ",exact" : ""));
        }

        string mode = "uuid_exact";
        if ((commandCharacteristic == null || ackCharacteristic == null) &&
            probes.Count > 0)
        {
            GattServiceProbe best = probes
                .OrderByDescending(p => p.HasInputCharacteristic ? 2 : 0)
                .ThenByDescending(p => p.HasExactKnownCharacteristic ? 1 : 0)
                .ThenByDescending(p => p.NotifyCandidates.Count)
                .ThenByDescending(p => p.WriteCandidates.Count)
                .First();

            if (commandCharacteristic == null)
            {
                commandCharacteristic = SelectDynamicCommandCharacteristic(best.WriteCandidates);
            }
            if (ackCharacteristic == null)
            {
                ackCharacteristic = SelectDynamicAckCharacteristic(
                    best.NotifyCandidates,
                    fd2Characteristic,
                    legacyCharacteristic);
            }
            mode = "dynamic_service_handle";
            progress.Report("[PRO2_BLE_GATT] dynamic fallback service=" + best.ServiceUuid +
                            " cmd=" + DescribeCharacteristic(commandCharacteristic) +
                            " ack=" + DescribeCharacteristic(ackCharacteristic));
        }

        if (commandCharacteristic == null && probes.Count == 0)
        {
            mode = "missing_no_switch_service";
        }
        else if (commandCharacteristic == null || ackCharacteristic == null ||
                 (fd2Characteristic == null && legacyCharacteristic == null))
        {
            mode = "missing_required";
        }

        string summary = string.Join(" | ", serviceSummaries);
        if (summary.Length > 950)
        {
            summary = summary[..950] + "...";
        }

        lock (gate)
        {
            gattSelectionMode = mode;
            gattDiscoverySummary = summary;
        }
        progress.Report("[PRO2_BLE_GATT] services=" + services.Count +
                        " selection=" + mode +
                        " summary=" + summary);
    }

    private static GattCharacteristic? SelectDynamicCommandCharacteristic(
        IReadOnlyList<GattCharacteristic> writeCandidates)
    {
        if (writeCandidates.Count >= 2)
        {
            return writeCandidates[1];
        }

        return writeCandidates.Count == 1 ? writeCandidates[0] : null;
    }

    private static GattCharacteristic? SelectDynamicAckCharacteristic(
        IReadOnlyList<GattCharacteristic> notifyCandidates,
        GattCharacteristic? fd2,
        GattCharacteristic? legacy)
    {
        List<GattCharacteristic> nonInput = notifyCandidates
            .Where(c => !ReferenceEquals(c, fd2) && !ReferenceEquals(c, legacy))
            .ToList();
        if (nonInput.Count >= 3)
        {
            return nonInput[2];
        }
        if (notifyCandidates.Count >= 3)
        {
            return notifyCandidates[2];
        }
        if (nonInput.Count > 0)
        {
            return nonInput[^1];
        }

        return notifyCandidates.Count > 0 ? notifyCandidates[^1] : null;
    }

    private static bool HasWriteProperty(GattCharacteristic characteristic)
    {
        GattCharacteristicProperties properties = characteristic.CharacteristicProperties;
        return (properties & GattCharacteristicProperties.Write) != 0 ||
               (properties & GattCharacteristicProperties.WriteWithoutResponse) != 0;
    }

    private static bool HasNotifyProperty(GattCharacteristic characteristic)
    {
        return (characteristic.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0;
    }

    private async Task<List<BleCandidate>> ScanCandidatesAsync(
        IProgress<string> progress,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await ReportBluetoothAdapterDiagnosticsAsync(progress);
        List<BleCandidate> active = await ScanCandidatesCoreAsync(
            progress,
            duration,
            BluetoothLEScanningMode.Active,
            "active",
            cancellationToken);
        if (active.Count > 0 || cancellationToken.IsCancellationRequested)
        {
            return active;
        }

        progress.Report("[PRO2_BLE] active scan found no Pro2 candidate; retrying passive scan for USB Bluetooth adapter compatibility.");
        TimeSpan passiveDuration = TimeSpan.FromSeconds(Math.Min(5.0, Math.Max(3.0, duration.TotalSeconds * 0.65)));
        return await ScanCandidatesCoreAsync(
            progress,
            passiveDuration,
            BluetoothLEScanningMode.Passive,
            "passive",
            cancellationToken);
    }

    private async Task<List<BleCandidate>> ScanCandidatesCoreAsync(
        IProgress<string> progress,
        TimeSpan duration,
        BluetoothLEScanningMode scanningMode,
        string scanLabel,
        CancellationToken cancellationToken)
    {
        Dictionary<ulong, BleCandidate> found = new();
        HashSet<ulong> seenAdvertisements = [];
        List<string> rejectedSamples = [];
        int rawAdvertisementCount = 0;
        int emptyNameAdvertisementCount = 0;
        int nintendoAdvertisementCount = 0;
        int nameMatchAdvertisementCount = 0;
        int rejectedAdvertisementCount = 0;
        TaskCompletionSource scanDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BluetoothError stopError = BluetoothError.Success;
        BluetoothLEAdvertisementWatcher localWatcher = new()
        {
            ScanningMode = scanningMode
        };
        watcher = localWatcher;
        LastScanDiagnostic = scanLabel + " scan started.";

        localWatcher.Received += (_, args) =>
        {
            string name = args.Advertisement.LocalName ?? "";
            bool nintendo = args.Advertisement.ManufacturerData.Any(m => m.CompanyId == NintendoCompanyId);
            bool nameMatch = NameLooksLikeSwitchController(name);
            lock (found)
            {
                rawAdvertisementCount++;
                seenAdvertisements.Add(args.BluetoothAddress);
                if (string.IsNullOrWhiteSpace(name))
                {
                    emptyNameAdvertisementCount++;
                }
                if (nintendo)
                {
                    nintendoAdvertisementCount++;
                }
                if (nameMatch)
                {
                    nameMatchAdvertisementCount++;
                }
            }

            BleCandidate? candidate = CandidateFromAdvertisement(args);
            if (candidate == null)
            {
                lock (found)
                {
                    rejectedAdvertisementCount++;
                    if (rejectedSamples.Count < 6 && !string.IsNullOrWhiteSpace(name))
                    {
                        rejectedSamples.Add(DescribeRejectedAdvertisement(args, nintendo, nameMatch));
                    }
                }
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
        localWatcher.Stopped += (_, args) =>
        {
            stopError = args.Error;
            progress.Report("[PRO2_BLE] " + scanLabel + " watcher stopped status=" +
                            localWatcher.Status + " error=" + args.Error);
            scanDone.TrySetResult();
        };

        progress.Report("[PRO2_BLE] " + scanLabel + " scan start duration_ms=" +
                        duration.TotalMilliseconds.ToString("F0"));
        try
        {
            localWatcher.Start();
        }
        catch (Exception ex)
        {
            progress.Report("[PRO2_BLE] " + scanLabel + " scan start failed: " + ex.GetType().Name + ": " + ex.Message);
            watcher = null;
            return [];
        }
        progress.Report("[PRO2_BLE] " + scanLabel + " watcher status=" + localWatcher.Status);
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
            List<BleCandidate> result = found.Values
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Rssi)
                .ToList();
            progress.Report("[PRO2_BLE] " + scanLabel + " scan result count=" + result.Count +
                            " stop_error=" + stopError);
            progress.Report("[PRO2_BLE_DIAG] " + scanLabel +
                            " raw_ads=" + rawAdvertisementCount +
                            " unique_addr=" + seenAdvertisements.Count +
                            " empty_name=" + emptyNameAdvertisementCount +
                            " nintendo_mfg=" + nintendoAdvertisementCount +
                            " name_match=" + nameMatchAdvertisementCount +
                            " rejected=" + rejectedAdvertisementCount +
                            " candidates=" + result.Count);
            if (rawAdvertisementCount == 0)
            {
                LastScanDiagnostic = scanLabel +
                                     " 扫描没有收到任何 BLE 广播。优先检查蓝牙接收器/驱动/蓝牙开关/Windows 蓝牙服务。";
                progress.Report("[PRO2_BLE_DIAG] " + scanLabel +
                                " saw zero BLE advertisements. If this repeats, suspect Bluetooth adapter/driver/radio or Windows privacy/service state before suspecting the Pro2 protocol.");
            }
            else if (result.Count == 0)
            {
                LastScanDiagnostic = scanLabel +
                                     " 收到 " + rawAdvertisementCount +
                                     " 个 BLE 广播，但没有匹配到 Pro2。优先确认手柄正在配对广播且未被其他主机占用。";
                progress.Report("[PRO2_BLE_DIAG] " + scanLabel +
                                " saw BLE traffic but no Pro2 candidate. Suspect the controller is not advertising, is captured by another host, or the identifying name/manufacturer differs from our filter.");
                foreach (string sample in rejectedSamples)
                {
                    progress.Report("[PRO2_BLE_DIAG] rejected_sample " + sample);
                }
            }
            else
            {
                LastScanDiagnostic = scanLabel +
                                     " 扫描发现 " + result.Count + " 个 Pro2 候选。";
            }
            return result;
        }
    }

    private static async Task ReportBluetoothAdapterDiagnosticsAsync(IProgress<string> progress)
    {
        try
        {
            BluetoothAdapter? adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null)
            {
                progress.Report("[BLE_ADAPTER] default=none. Windows did not expose a BLE adapter. " +
                                "A USB dongle must support Bluetooth LE Central and use a working Windows driver.");
                await ReportBluetoothRadiosAsync(progress);
                return;
            }

            progress.Report("[BLE_ADAPTER] default address=" + FormatAddress(adapter.BluetoothAddress) +
                            " central=" + adapter.IsCentralRoleSupported +
                            " peripheral=" + adapter.IsPeripheralRoleSupported +
                            " low_energy=" + adapter.IsLowEnergySupported +
                            " classic=" + adapter.IsClassicSupported +
                            " device_id=\"" + SanitizeDeviceId(adapter.DeviceId) + "\"");

            if (!adapter.IsCentralRoleSupported || !adapter.IsLowEnergySupported)
            {
                progress.Report("[BLE_ADAPTER] warning: this adapter does not report BLE Central support. " +
                                "It may install successfully but still cannot scan/connect Pro2 BLE.");
            }

            try
            {
                Radio? radio = await adapter.GetRadioAsync();
                if (radio == null)
                {
                    progress.Report("[BLE_RADIO] default adapter radio unavailable.");
                }
                else
                {
                    progress.Report("[BLE_RADIO] default name=\"" + radio.Name +
                                    "\" state=" + radio.State +
                                    " kind=" + radio.Kind);
                    if (radio.State != RadioState.On)
                    {
                        progress.Report("[BLE_RADIO] warning: Bluetooth radio is not On. Turn on Bluetooth in Windows settings.");
                    }
                }
            }
            catch (Exception ex)
            {
                progress.Report("[BLE_RADIO] default query failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            await ReportBluetoothRadiosAsync(progress);
        }
        catch (Exception ex)
        {
            progress.Report("[BLE_ADAPTER] diagnostics failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task ReportBluetoothRadiosAsync(IProgress<string> progress)
    {
        try
        {
            IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync();
            IEnumerable<Radio> bluetoothRadios = radios.Where(r => r.Kind == RadioKind.Bluetooth);
            int count = 0;
            foreach (Radio radio in bluetoothRadios)
            {
                count++;
                progress.Report("[BLE_RADIO] radio[" + count + "] name=\"" + radio.Name +
                                "\" state=" + radio.State +
                                " kind=" + radio.Kind);
            }

            if (count == 0)
            {
                progress.Report("[BLE_RADIO] no Bluetooth radio listed by Windows.");
            }
        }
        catch (Exception ex)
        {
            progress.Report("[BLE_RADIO] list failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string SanitizeDeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Replace("\r", " ").Replace("\n", " ");
    }

    private static string DescribeRejectedAdvertisement(
        BluetoothLEAdvertisementReceivedEventArgs args,
        bool nintendo,
        bool nameMatch)
    {
        string name = args.Advertisement.LocalName ?? "";
        string companyIds = string.Join(
            ",",
            args.Advertisement.ManufacturerData
                .Take(4)
                .Select(m => "0x" + m.CompanyId.ToString("x4")));
        if (string.IsNullOrWhiteSpace(companyIds))
        {
            companyIds = "none";
        }

        return "addr=" + FormatAddress(args.BluetoothAddress) +
               " type=" + args.BluetoothAddressType +
               " rssi=" + args.RawSignalStrengthInDBm +
               " name=\"" + SanitizeDeviceId(name) + "\"" +
               " nintendo_mfg=" + nintendo +
               " name_match=" + nameMatch +
               " mfg=" + companyIds;
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
        GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            progress.Report("[PRO2_BLE] subscribe " + label +
                            " attempt=" + attempt +
                            " status=" + status +
                            " " + DescribeCharacteristic(characteristic));
            if (status == GattCommunicationStatus.Success)
            {
                return true;
            }

            await Task.Delay(350 * attempt);
        }

        return false;
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
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string reportType = sender.Uuid == NotifyFd2Uuid
            ? "fd2"
            : sender.Uuid == NotifyLegacyUuid
                ? "legacy"
                : "unknown";
        ulong parseSeq;
        double rawGapMs = 0;
        double sourceAgeMs = 0;
        double bleHz;
        double viiperPushHz;
        lock (gate)
        {
            sourceAgeMs = latestAt == default
                ? 0
                : Math.Max(0, (now - latestAt).TotalMilliseconds);
            rawNotifyCount++;
            parseSeq = rawNotifyCount;
            if (lastRawNotifyTicks != 0)
            {
                rawGapMs =
                    (nowTicks - lastRawNotifyTicks) * 1000.0 / Stopwatch.Frequency;
                if (rawGapMs >= 45)
                {
                    inputGap45Count++;
                }
                if (rawGapMs >= 250)
                {
                    inputGap250Count++;
                }
                if (rawGapMs >= 750)
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
            bleHz = SampleRate(rawNotifyCount, firstRawNotifyTicks, lastRawNotifyTicks);
            viiperPushHz = lastViiperPushHz;
        }

        bool parsed = sender.Uuid == NotifyFd2Uuid
            ? parser.TryParseFd2Payload(data, out GamepadState state, out string source)
            : sender.Uuid == NotifyLegacyUuid
                ? parser.TryParseLegacyPayload(data, out state, out source)
                : parser.TryParse(data, out state, out source);
        if (!parsed)
        {
            lock (gate)
            {
                parseFailCount++;
            }
            if (sender.Uuid == NotifyFd2Uuid)
            {
                spikeRecorder.AddFrame(new Pro2Fd2FrameSnapshot(
                    parseSeq,
                    now,
                    rawGapMs,
                    reportType,
                    data.Length,
                    Convert.ToHexString(data),
                    Pro2SpikeSnapshot.Axes(GamepadState.Neutral()),
                    Pro2SpikeSnapshot.Axes(GamepadState.Neutral()),
                    0,
                    Pro2SpikeSnapshot.Motion(GamepadState.Neutral()),
                    ParseOk: false,
                    ParseSource: "",
                    FilterResult: "parse_fail",
                    FilterEvents: Array.Empty<Pro2FrameFilterEventSnapshot>()));
            }
            return;
        }

        state.SourceTimestampTicks = nowTicks;
        state.RawNotificationSequence = parseSeq;
        if (state.SwitchRawImuSamples.Length > 0)
        {
            SwitchImuRawSample[] stamped = new SwitchImuRawSample[state.SwitchRawImuSamples.Length];
            long imuSubSampleSpacingTicks = Stopwatch.Frequency / 200; // Switch-style IMU sub-samples are 5 ms apart.
            for (int i = 0; i < stamped.Length; i++)
            {
                int samplesAfterThis = stamped.Length - 1 - i;
                long sampleTicks = nowTicks - samplesAfterThis * imuSubSampleSpacingTicks;
                stamped[i] = ProfessionalImuConverter.Stamp(
                    state.SwitchRawImuSamples[i],
                    sampleTicks > 0 ? sampleTicks : nowTicks,
                    parseSeq);
            }
            state.SwitchRawImuSamples = stamped;
        }

        Pro2InputFilterResult filterResult;
        GamepadState stableState;
        GamepadState rawStateSnapshot;
        GamepadState filteredStateSnapshot;
        ulong stateSeq;
        double parsedBleHz;
        lock (gate)
        {
            filterResult = stickProcessingMode == StickProcessingMode.RawDirect
                ? new Pro2InputFilterResult(state.Clone(), state.Clone(), Array.Empty<Pro2AxisFilterEvent>())
                : inputStability.Process(state, nowTicks);
            stableState = filterResult.AcceptedState;
            rawStateSnapshot = filterResult.RawState.Clone();
            filteredStateSnapshot = stableState.Clone();
            if (filterResult.HasAxisIntervention)
            {
                axisSpikeRejectCount += (uint)filterResult.InterventionCount;
                lastNotifySummary += " filtered=" + filterResult.PrimaryReason;
            }

            updates++;
            stateSeq = updates;
            NoteSample(
                nowTicks,
                ref firstParsedNotifyTicks,
                ref lastParsedNotifyTicks,
                ref lastParsedNotifyGapTicks,
                ref maxParsedNotifyGapTicks);
            stableState.Updates = updates;
            rawStateSnapshot.Updates = updates;
            filteredStateSnapshot.Updates = updates;
            rawState = rawStateSnapshot;
            filteredState = filteredStateSnapshot;
            rawStateAt = now;
            filteredStateAt = now;
            latest = stableState;
            latestAt = DateTimeOffset.UtcNow;
            parsedBleHz = SampleRate(updates, firstParsedNotifyTicks, lastParsedNotifyTicks);
            bleHz = Math.Max(bleHz, parsedBleHz);
            status = "真实 Pro2 BLE live，updates=" + updates + " source=" + source +
                     " raw_notify=" + rawNotifyCount + " " + BuildMetricsSummaryNoLock();
        }

        if (sender.Uuid == NotifyFd2Uuid)
        {
            Pro2Fd2FrameSnapshot frame = new(
                parseSeq,
                now,
                rawGapMs,
                reportType,
                    data.Length,
                    Convert.ToHexString(data),
                    Pro2SpikeSnapshot.Axes(rawStateSnapshot),
                    Pro2SpikeSnapshot.Axes(filteredStateSnapshot),
                    (ulong)state.Buttons,
                    Pro2SpikeSnapshot.Motion(state),
                ParseOk: true,
                ParseSource: source,
                FilterResult: filterResult.PrimaryReason,
                FilterEvents: Pro2SpikeSnapshot.Events(filterResult.Events));
            spikeRecorder.AddFrame(frame);
        }

        MaybeReassertThroughputForGap(rawGapMs, parsedBleHz);

        if (filterResult.HasAxisIntervention && inputStabilityOptions.AxisSpikeLogEnabled)
        {
            foreach (Pro2AxisFilterEvent axisEvent in filterResult.Events)
            {
                if (axisEvent.Decision == Pro2AxisFilterDecisionKind.Accept &&
                    axisEvent.RawToFilteredDelta == 0 &&
                    !axisEvent.InputSwallowed)
                {
                    continue;
                }

                Pro2AxisSpikeTelemetry telemetry = BuildSpikeTelemetry(
                    axisEvent,
                    now,
                    reportType,
                    data,
                    sourceAgeMs,
                    rawGapMs,
                    parseSeq,
                    stateSeq,
                    bleHz,
                    viiperPushHz);
                ReportAxisSpikeTelemetry(telemetry, parseSeq);
            }
        }
    }

    private void MaybeReassertThroughputForGap(double rawGapMs, double parsedBleHz)
    {
        if (rawGapMs < LinkHealthReassertGapMs ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        BluetoothLEDevice? currentDevice = device;
        IProgress<string>? progress = connectionProgress;
        if (currentDevice == null || progress == null)
        {
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        bool shouldReassert;
        uint reassertSeq;
        lock (gate)
        {
            double sinceLastMs = lastThroughputReassertTicks == 0
                ? double.MaxValue
                : TicksToMilliseconds(nowTicks - lastThroughputReassertTicks);
            shouldReassert =
                rawNotifyCount >= LinkHealthWarmupNotifications &&
                sinceLastMs >= LinkHealthReassertCooldownMs;
            if (!shouldReassert)
            {
                return;
            }

            lastThroughputReassertTicks = nowTicks;
            lastThroughputReassertGapMs = rawGapMs;
            throughputReassertCount++;
            reassertSeq = throughputReassertCount;
            connectionPreferenceStatus = "health_reassert_pending";
        }

        progress.Report("[PRO2_BLE_LINK] health reassert #" + reassertSeq +
                        " raw_gap_ms=" + rawGapMs.ToString("F1") +
                        " parsed_hz=" + parsedBleHz.ToString("F1"));
        TryRequestWindows11ThroughputPreference(
            currentDevice,
            "health_reassert",
            progress);
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
            string warning = "BLE 输入保持 live，但没有达到 66.7 Hz 目标；V6.2.26 将自动降低虚拟 USB 输出刷新率：" +
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
            lastPerformanceFailure = "BLE 链路未达到最低 10 Hz live 等级：" +
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
        return TryRequestWindows11ThroughputPreference(source, "fallback", progress);
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    private bool TryRequestWindows11ThroughputPreference(
        BluetoothLEDevice source,
        string reason,
        IProgress<string> progress)
    {
        try
        {
            BluetoothLEPreferredConnectionParameters preferred =
                BluetoothLEPreferredConnectionParameters.ThroughputOptimized;
            progress.Report("[PRO2_BLE_LINK] requesting " + reason + " " +
                            FormatPreferredConnectionParameters(preferred));
            connectionParametersRequest?.Dispose();
            connectionParametersRequest = source.RequestPreferredConnectionParameters(preferred);
            lock (gate)
            {
                connectionPreferenceStatus = reason + "_" + connectionParametersRequest.Status;
            }
            progress.Report("[PRO2_BLE_LINK] " + reason + " request status=" +
                            connectionParametersRequest.Status);
            return connectionParametersRequest.Status ==
                   BluetoothLEPreferredConnectionParametersRequestStatus.Success;
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                connectionPreferenceStatus = reason + "_error_0x" + ex.HResult.ToString("X8");
            }
            progress.Report("[PRO2_BLE_LINK] " + reason + " request failed: " + ex.Message);
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

    private static async Task<GattCommunicationStatus> WriteCharacteristicWithRetryAsync(
        GattCharacteristic characteristic,
        byte[] data,
        string label,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            status = await WriteCharacteristicAsync(characteristic, data);
            if (status == GattCommunicationStatus.Success)
            {
                if (attempt > 1)
                {
                    progress.Report("[PRO2_BLE] write " + label +
                                    " recovered attempt=" + attempt);
                }
                return status;
            }

            progress.Report("[PRO2_BLE] write " + label +
                            " attempt=" + attempt +
                            " status=" + status);
            await Task.Delay(120 * attempt, cancellationToken);
        }

        return status;
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
        lock (gate)
        {
            connectedAddress = "";
        }
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
            rawState = GamepadState.Neutral();
            filteredState = GamepadState.Neutral();
            rawStateAt = default;
            filteredStateAt = default;
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
            axisSpikeLogCount = 0;
            Interlocked.Exchange(ref asyncProgressDroppedCount, 0);
            lastViiperPushHz = 0;
            lastThroughputReassertTicks = 0;
            throughputReassertCount = 0;
            lastThroughputReassertGapMs = 0;
            spikeRecorder.Clear();
            connectionPreferenceStatus = "not_requested";
            connectionIntervalUnits = 0;
            connectionLatency = 0;
            connectionLinkTimeout = 0;
            lastConnectionParametersSummary = "";
            gattSelectionMode = "not_scanned";
            gattDiscoverySummary = "";
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
            axisSpikeLogCount = 0;
            Interlocked.Exchange(ref asyncProgressDroppedCount, 0);
            spikeRecorder.Clear();
        }
    }

    private string BuildMetricsSummaryNoLock()
    {
        double connectionIntervalMs = connectionIntervalUnits * 1.25;
        double connectionEventHz = connectionIntervalUnits == 0
            ? 0
            : 800.0 / connectionIntervalUnits;
        int rawFilteredMaxDelta = MaxAxisDelta(rawState, filteredState);
        int filteredLatestMaxDelta = MaxAxisDelta(filteredState, latest);
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
               " ble_reassert=" + throughputReassertCount +
               " ble_reassert_last_gap_ms=" + lastThroughputReassertGapMs.ToString("F1") +
               " gatt_mode=" + gattSelectionMode +
               " ble_gap45=" + inputGap45Count +
               " ble_gap250=" + inputGap250Count +
               " ble_gap750=" + inputGap750Count +
               " axis_spike=" + axisSpikeRejectCount +
               inputStability.MetricsSummary +
               " stick_mode=" + StickProcessingModeLabel(stickProcessingMode) +
               " raw_integrity_mode=" + (inputStabilityOptions.RawIntegrityModeEnabled ? "shadow_on" : "shadow") +
               " raw_left_x=" + rawState.Lx +
               " raw_left_y=" + rawState.Ly +
               " raw_right_x=" + rawState.Rx +
               " raw_right_y=" + rawState.Ry +
               " filtered_left_x=" + filteredState.Lx +
               " filtered_left_y=" + filteredState.Ly +
               " filtered_right_x=" + filteredState.Rx +
               " filtered_right_y=" + filteredState.Ry +
               " latest_left_x=" + latest.Lx +
               " latest_left_y=" + latest.Ly +
               " latest_right_x=" + latest.Rx +
               " latest_right_y=" + latest.Ry +
               " raw_to_filtered_difference=" + rawFilteredMaxDelta +
               " filtered_to_latest_difference=" + filteredLatestMaxDelta +
               " axis_spike_logs=" + axisSpikeLogCount +
               " axis_spike_dump_written=" + spikeRecorder.WrittenDumpCount +
               " axis_spike_dump_dropped=" + spikeRecorder.DroppedDumpCount +
               " async_progress_dropped=" + Interlocked.Read(ref asyncProgressDroppedCount) +
               " viiper_push_hz_seen=" + lastViiperPushHz.ToString("F1") +
               " rumble_q=" + rumbleQueuedCount +
               " rumble_w=" + rumbleWrittenCount +
               " rumble_merge=" + rumbleCoalescedCount +
               " rumble_fail=" + rumbleFailureCount +
               " rumble_gain=" + RumbleGain.ToString("F1") +
               " parse_fail=" + parseFailCount;
    }

    private static string StickProcessingModeLabel(StickProcessingMode mode)
    {
        return mode switch
        {
            StickProcessingMode.RawDirect => "raw_direct",
            StickProcessingMode.StabilityGuard => "stability_guard",
            _ => mode.ToString().ToLowerInvariant()
        };
    }

    private static int MaxAxisDelta(GamepadState a, GamepadState b)
    {
        return Math.Max(
            Math.Max(Math.Abs(a.Lx - b.Lx), Math.Abs(a.Ly - b.Ly)),
            Math.Max(Math.Abs(a.Rx - b.Rx), Math.Abs(a.Ry - b.Ry)));
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
        return rawNotifications >= MinimumUsableNotifications &&
               parsedNotifications >= MinimumUsableNotifications &&
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
            rateClass = snapshot.ParsedRateHz < 15.0
                ? "10hz_link_degraded"
                : snapshot.ParsedRateHz < 25.0
                    ? "20hz_link_degraded"
                    : snapshot.ConnectionIntervalUnits <= MinimumAcceptedConnectionIntervalUnits
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
            : "ok/0x" + characteristic.AttributeHandle.ToString("X4") +
              "/" + ShortGuid(characteristic.Uuid) +
              "/" + characteristic.CharacteristicProperties;
    }

    private static string ShortGuid(Guid guid)
    {
        string text = guid.ToString("N");
        return text.Length <= 8 ? text : text[..8];
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

    private Pro2AxisSpikeTelemetry BuildSpikeTelemetry(
        Pro2AxisFilterEvent axisEvent,
        DateTimeOffset timestamp,
        string reportType,
        byte[] rawReport,
        double sourceAgeMs,
        double bleGapMs,
        ulong parseSeq,
        ulong stateSeq,
        double bleHz,
        double viiperPushHz)
    {
        return new Pro2AxisSpikeTelemetry(
            timestamp,
            reportType,
            rawReport.Length,
            axisEvent.AxisName,
            axisEvent.OldValue,
            axisEvent.NewValue,
            axisEvent.OutputValue,
            axisEvent.Delta,
            axisEvent.OldLeftX,
            axisEvent.OldLeftY,
            axisEvent.OldRightX,
            axisEvent.OldRightY,
            axisEvent.NewLeftX,
            axisEvent.NewLeftY,
            axisEvent.NewRightX,
            axisEvent.NewRightY,
            axisEvent.StickVectorDelta,
            sourceAgeMs,
            bleGapMs,
            axisEvent.ConsecutiveSuspectFrames,
            axisEvent.Decision.ToString().ToLowerInvariant(),
            axisEvent.Reason,
            Convert.ToHexString(rawReport),
            parseSeq,
            stateSeq,
            bleHz,
            viiperPushHz,
            axisEvent.CandidateAgeMs,
            axisEvent.FrameDeltaMs,
            axisEvent.DirectionStable,
            axisEvent.Continuous,
            axisEvent.MotionClass,
            axisEvent.ActiveMotion,
            axisEvent.FastReversal,
            axisEvent.CenterCrossing,
            axisEvent.InputSwallowed,
            axisEvent.RawToFilteredDelta);
    }

    private void ReportAxisSpikeTelemetry(
        Pro2AxisSpikeTelemetry telemetry,
        ulong frameIndex)
    {
        axisSpikeLogCount++;
        bool queuedDump = spikeRecorder.TryQueueDump(
            telemetry,
            frameIndex,
            out string dumpPath);
        QueueProgressReport(
            "[PRO2_AXIS_SPIKE] timestamp=" + telemetry.Timestamp.ToString("O") +
            " report_type=" + telemetry.ReportType +
            " report_len=" + telemetry.ReportLen +
            " axis_name=" + telemetry.AxisName +
            " old_value=" + telemetry.OldValue +
            " new_value=" + telemetry.NewValue +
            " output_value=" + telemetry.OutputValue +
            " delta=" + telemetry.Delta +
            " old_left_x=" + telemetry.OldLeftX +
            " old_left_y=" + telemetry.OldLeftY +
            " old_right_x=" + telemetry.OldRightX +
            " old_right_y=" + telemetry.OldRightY +
            " new_left_x=" + telemetry.NewLeftX +
            " new_left_y=" + telemetry.NewLeftY +
            " new_right_x=" + telemetry.NewRightX +
            " new_right_y=" + telemetry.NewRightY +
            " stick_vector_delta=" + telemetry.StickVectorDelta.ToString("F1") +
            " source_age_ms=" + telemetry.SourceAgeMs.ToString("F1") +
            " ble_gap_ms=" + telemetry.BleGapMs.ToString("F1") +
            " consecutive_suspect_frames=" + telemetry.ConsecutiveSuspectFrames +
            " accepted_or_rejected=" + telemetry.AcceptedOrRejected +
            " reason=" + telemetry.Reason +
            " candidate_age_ms=" + telemetry.CandidateAgeMs.ToString("F1") +
            " frame_delta_ms=" + telemetry.FrameDeltaMs.ToString("F1") +
            " direction_stable=" + telemetry.DirectionStable +
            " continuous=" + telemetry.Continuous +
            " motion_class=" + telemetry.MotionClass +
            " active_motion=" + telemetry.ActiveMotion +
            " fast_reversal=" + telemetry.FastReversal +
            " center_crossing=" + telemetry.CenterCrossing +
            " input_swallowed=" + telemetry.InputSwallowed +
            " raw_to_filtered_delta=" + telemetry.RawToFilteredDelta +
            " parse_seq=" + telemetry.ParseSeq +
            " state_seq=" + telemetry.StateSeq +
            " ble_hz=" + telemetry.BleHz.ToString("F1") +
            " viiper_push_hz=" + telemetry.ViiperPushHz.ToString("F1") +
            " dump=" + (queuedDump ? dumpPath : "not_queued") +
            " raw_fd2_hex=" + telemetry.RawFd2Hex);
    }

    private void QueueProgressReport(string message)
    {
        IProgress<string>? progress = connectionProgress;
        if (progress == null)
        {
            return;
        }

        bool queued = ThreadPool.QueueUserWorkItem(
            static state =>
            {
                var tuple = ((IProgress<string> Progress, string Message))state!;
                tuple.Progress.Report(tuple.Message);
            },
            (progress, message));
        if (!queued)
        {
            Interlocked.Increment(ref asyncProgressDroppedCount);
        }
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

    private sealed record GattServiceProbe(
        Guid ServiceUuid,
        IReadOnlyList<GattCharacteristic> WriteCandidates,
        IReadOnlyList<GattCharacteristic> NotifyCandidates,
        bool HasInputCharacteristic,
        bool HasExactKnownCharacteristic);

    private sealed record BlePerformanceSnapshot(
        ushort ConnectionIntervalUnits,
        double NotifyRateHz,
        double ParsedRateHz,
        uint RawNotifications,
        uint ParsedNotifications);
}


