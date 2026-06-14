using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Y700Switch2V60Viiper;

public sealed class Pro2BleInputSource : IGamepadInputSource, IGamepadOutputSink
{
    private static readonly Guid NotifyFd2Uuid = Guid.Parse("ab7de9be-89fe-49ad-828f-118f09df7fd2");
    private static readonly Guid AckUuid = Guid.Parse("c765a961-d9d8-4d36-a20a-5315b111836a");
    private static readonly Guid CmdUuid = Guid.Parse("649d4ac9-8eb7-4e6c-af44-1ea54fe5f005");
    private static readonly Guid RumbleUuid = Guid.Parse("cc483f51-9258-427d-a939-630c31f72b05");
    private const ushort NintendoCompanyId = 0x0553;

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
    private readonly List<BleCandidate> lastCandidates = [];
    private BluetoothLEAdvertisementWatcher? watcher;
    private BluetoothLEDevice? device;
    private GattCharacteristic? commandCharacteristic;
    private GattCharacteristic? notifyCharacteristic;
    private GattCharacteristic? ackCharacteristic;
    private GattCharacteristic? rumbleCharacteristic;
    private TaskCompletionSource<byte[]>? ackTcs;
    private GamepadState latest = GamepadState.Neutral();
    private DateTimeOffset latestAt;
    private uint updates;
    private byte rumblePacketId;
    private string status = "未连接真实 Pro2 BLE。";
    private string connectedLabel = "";

    public bool IsRunning { get; private set; }
    public bool IsOutputReady => IsRunning && rumbleCharacteristic != null;

    public string Status
    {
        get { lock (gate) return status; }
        private set { lock (gate) status = value; }
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
                    Status = "真实 Pro2 BLE 已连接并 live：" + connectedLabel;
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

        Status = "未能连接到真实 Pro2 BLE。请确保手柄处于可连接状态，并且没有被 ESP32、Switch、手机或旧进程占用。";
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
                        out error))
                {
                    return false;
                }

                Task<string?> task = WriteRumblePacketAsync(packet);
                if (!task.Wait(TimeSpan.FromMilliseconds(200)))
                {
                    error = "BLE rumble write timeout";
                    return false;
                }

                error = task.Result ?? "";
                return string.IsNullOrWhiteSpace(error);
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
        progress.Report("[PRO2_BLE] opened address=" + FormatAddress(candidate.Address) +
                        " name=" + (opened.Name ?? candidate.Name ?? "<unnamed>"));

        GattDeviceServicesResult services =
            await opened.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (services.Status != GattCommunicationStatus.Success)
        {
            progress.Report("[PRO2_BLE] service discovery failed status=" + services.Status);
            return false;
        }

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
                    notifyCharacteristic = characteristic;
                }
                else if (characteristic.Uuid == RumbleUuid)
                {
                    rumbleCharacteristic = characteristic;
                }
            }
        }

        if (commandCharacteristic == null || ackCharacteristic == null || notifyCharacteristic == null)
        {
            progress.Report("[PRO2_BLE] required GATT chars missing cmd=" + (commandCharacteristic != null) +
                            " ack=" + (ackCharacteristic != null) +
                            " fd2=" + (notifyCharacteristic != null) +
                            " rumble=" + (rumbleCharacteristic != null));
            return false;
        }

        ackCharacteristic.ValueChanged += OnAckValueChanged;
        notifyCharacteristic.ValueChanged += OnNotifyValueChanged;

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
        if (!await SubscribeAsync(notifyCharacteristic, "fd2", progress))
        {
            return false;
        }

        progress.Report("[PRO2_BLE] waiting for FD2 live input...");
        if (!await WaitForLiveInputAsync(TimeSpan.FromSeconds(5), cancellationToken))
        {
            progress.Report("[PRO2_BLE] no live FD2 input after subscribe.");
            return false;
        }

        IsRunning = true;
        progress.Report("[PRO2_BLE] live input confirmed updates=" + updates +
                        " rumble=" + (rumbleCharacteristic != null));
        return true;
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
        if (!parser.TryParse(data, out GamepadState state, out string source))
        {
            return;
        }

        lock (gate)
        {
            updates++;
            state.Updates = updates;
            latest = state;
            latestAt = DateTimeOffset.UtcNow;
            status = "真实 Pro2 BLE live，updates=" + updates + " source=" + source;
        }
    }

    private async Task<bool> WaitForLiveInputAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetLatest(out _, out TimeSpan age) && age <= TimeSpan.FromMilliseconds(500))
            {
                return true;
            }

            await Task.Delay(50, cancellationToken);
        }

        return false;
    }

    private async Task<string?> WriteRumblePacketAsync(byte[] packet)
    {
        if (rumbleCharacteristic == null)
        {
            return "rumble characteristic null";
        }

        GattCommunicationStatus status = await WriteCharacteristicAsync(rumbleCharacteristic, packet);
        return status == GattCommunicationStatus.Success ? null : status.ToString();
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
        watcher?.Stop();
        watcher = null;

        if (ackCharacteristic != null)
        {
            ackCharacteristic.ValueChanged -= OnAckValueChanged;
        }
        if (notifyCharacteristic != null)
        {
            notifyCharacteristic.ValueChanged -= OnNotifyValueChanged;
        }

        ackCharacteristic = null;
        notifyCharacteristic = null;
        commandCharacteristic = null;
        rumbleCharacteristic = null;
        ackTcs = null;
        connectedLabel = "";
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
        }
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
}
