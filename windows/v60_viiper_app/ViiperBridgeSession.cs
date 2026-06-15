using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2V60Viiper;

public sealed class ViiperBridgeSession : IAsyncDisposable
{
    private const ulong PerformanceLogIntervalFrames = 1000;
    private readonly ViiperProtocolClient client;
    private readonly ViiperDeviceProfile profile;
    private readonly IProgress<string> progress;
    private readonly IProgress<Exception>? faultProgress;
    private readonly IGamepadInputSource? inputSource;
    private readonly IGamepadOutputSink? outputSink;
    private readonly CancellationTokenSource cts = new();
    private readonly SemaphoreSlim streamRecoveryGate = new(1, 1);
    private readonly object streamSync = new();
    private ViiperDeviceStream? stream;
    private ViiperDevice? device;
    private uint busId;
    private bool createdBus;
    private Task? inputTask;
    private Task? feedbackTask;
    private int faultReported;
    private const int StreamRecoveryAttempts = 5;

    public ViiperBridgeSession(
        ViiperProtocolClient client,
        ViiperDeviceProfile profile,
        IProgress<string> progress,
        IGamepadInputSource? inputSource = null,
        IGamepadOutputSink? outputSink = null,
        IProgress<Exception>? faultProgress = null)
    {
        this.client = client;
        this.profile = profile;
        this.progress = progress;
        this.inputSource = inputSource;
        this.outputSink = outputSink ?? inputSource as IGamepadOutputSink;
        this.faultProgress = faultProgress;
    }

    public ViiperDeviceProfile Profile => profile;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string ping = await client.PingAsync(cancellationToken);
        progress.Report("[VIIPER] ping " + ping);

        var buses = await client.BusListAsync(cancellationToken);
        if (buses.Count == 0)
        {
            busId = await client.BusCreateAsync(cancellationToken);
            createdBus = true;
            progress.Report("[VIIPER] created bus " + busId);
        }
        else
        {
            busId = buses.Min();
            progress.Report("[VIIPER] using bus " + busId);
        }

        device = await client.AddDeviceAsync(busId, profile.DeviceType, cancellationToken);
        progress.Report($"[VIIPER] added {profile.Label} device bus={device.BusId} dev={device.DevId} vid={device.Vid} pid={device.Pid}");
        SetStream(await client.OpenStreamAsync(device.BusId, device.DevId, cancellationToken));
        progress.Report(inputSource is { IsRunning: true }
            ? "[VIIPER] stream connected; feeding Pro2 BLE input."
            : "[VIIPER] stream connected; feeding neutral input until Pro2 BLE source is connected.");
        progress.Report(outputSink is { IsOutputReady: true }
            ? "[VIIPER] Pro2 output writeback is enabled for rumble."
            : "[VIIPER] Pro2 output writeback is not ready; host rumble will be logged only.");

        inputTask = Task.Run(() => RunLoopAsync("input", InputLoopAsync, cts.Token));
        feedbackTask = Task.Run(() => RunLoopAsync("feedback", FeedbackLoopAsync, cts.Token));
    }

    public async Task StopAsync()
    {
        cts.Cancel();
        ViiperDeviceStream? activeStream = ExchangeStream(null);
        if (activeStream != null)
        {
            await activeStream.DisposeAsync();
        }

        await DrainTaskAsync(inputTask);
        await DrainTaskAsync(feedbackTask);

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        if (device != null)
        {
            try
            {
                await client.RemoveDeviceAsync(device.BusId, device.DevId, cleanup.Token);
                progress.Report("[VIIPER] removed device " + device.DevId);
            }
            catch (Exception ex)
            {
                progress.Report("[VIIPER] device cleanup warning: " + ex.Message);
            }
            device = null;
        }

        if (createdBus)
        {
            try
            {
                await client.RemoveBusAsync(busId, cleanup.Token);
                progress.Report("[VIIPER] removed bus " + busId);
            }
            catch (Exception ex)
            {
                progress.Report("[VIIPER] bus cleanup warning: " + ex.Message);
            }
        }
    }

    private async Task InputLoopAsync(CancellationToken cancellationToken)
    {
        using WindowsTimerResolutionScope timerResolution = WindowsTimerResolutionScope.Begin();
        using var timer = new HighResolutionPeriodicTimer(profile.SendInterval);
        var rateWatch = Stopwatch.StartNew();
        ulong frames = 0;
        ulong lastRateFrames = 0;
        long lastRateTicks = 0;
        string lastSource = "";
        progress.Report("[VIIPER_TIMER] requested_ms=1 active=" + timerResolution.IsActive +
                        " result=" + timerResolution.Result +
                        " backend=" + timer.Backend);
        while (timer.WaitForNextTick(cancellationToken))
        {
            ViiperDeviceStream? current = GetStream();
            if (current == null)
            {
                return;
            }

            byte[] packet;
            string source;
            if (inputSource != null &&
                inputSource.TryGetLatest(out GamepadState state, out TimeSpan age))
            {
                GamepadState continuous =
                    InputContinuityPolicy.Resolve(state, age, out source);
                packet = VirtualPadPackets.FromGamepad(profile, continuous);
            }
            else
            {
                packet = VirtualPadPackets.NeutralInput(profile);
                source = "neutral";
            }

            try
            {
                await current.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (await RecoverStreamAsync("input", current, ex, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                throw;
            }
            frames++;
            bool rateDue = frames % PerformanceLogIntervalFrames == 0;
            bool sourceChanged = source != lastSource;
            if (rateDue || sourceChanged)
            {
                lastSource = source;
                double targetHz = 1.0 / profile.SendInterval.TotalSeconds;
                string rateSummary = "target_hz=" + targetHz.ToString("F1");
                if (rateDue)
                {
                    long nowTicks = rateWatch.ElapsedTicks;
                    double elapsedSeconds = (nowTicks - lastRateTicks) / (double)Stopwatch.Frequency;
                    double actualHz = elapsedSeconds > 0
                        ? (frames - lastRateFrames) / elapsedSeconds
                        : 0;
                    lastRateTicks = nowTicks;
                    lastRateFrames = frames;
                    rateSummary += " actual_hz=" + actualHz.ToString("F1");
                }

                string inputMetrics = inputSource is IGamepadInputMetricsSource metrics
                    ? " " + metrics.MetricsSummary
                    : "";
                progress.Report("[VIIPER] fed " + frames + " " + source + " " + profile.Label +
                                " frames " + rateSummary + inputMetrics);
            }
        }
    }

    private async Task FeedbackLoopAsync(CancellationToken cancellationToken)
    {
        DualSenseHapticRumbleScheduler? dualSenseScheduler =
            profile.Mode == ViiperVirtualMode.DualSenseLike
                ? new DualSenseHapticRumbleScheduler()
                : null;
        ulong frames = 0;
        ulong writes = 0;
        ulong failures = 0;
        Dictionary<byte, string> lastFeedbackSummaryByKind = new();
        Dictionary<byte, ulong> feedbackFramesByKind = new();
        string lastOutputState = "";
        HashSet<string> reportedSkipReasons = new(StringComparer.Ordinal);
        while (!cancellationToken.IsCancellationRequested)
        {
            ViiperDeviceStream? current = GetStream();
            if (current == null)
            {
                return;
            }

            byte[] feedback;
            try
            {
                feedback = await current.ReadExactAsync(profile.FeedbackSize, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (await RecoverStreamAsync("feedback", current, ex, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                throw;
            }
            frames++;
            Pro2OutputPacket packet;
            string reason;
            bool mapped;
            string feedbackSummary;
            if (dualSenseScheduler != null)
            {
                mapped = dualSenseScheduler.TryProcess(
                    feedback,
                    out packet,
                    out feedbackSummary,
                    out reason);
            }
            else
            {
                feedbackSummary = VirtualPadPackets.FeedbackSummary(profile, feedback);
                mapped = Pro2OutputPacketMapper.TryMapFeedback(
                    profile,
                    feedback,
                    out packet,
                    out reason);
            }
            byte feedbackKind = dualSenseScheduler != null && feedback.Length > 0
                ? feedback[0]
                : (byte)0;
            ulong kindFrames = feedbackFramesByKind.TryGetValue(feedbackKind, out ulong previousKindFrames)
                ? previousKindFrames + 1
                : 1;
            feedbackFramesByKind[feedbackKind] = kindFrames;
            bool summaryChanged =
                !lastFeedbackSummaryByKind.TryGetValue(feedbackKind, out string? lastFeedbackSummary) ||
                feedbackSummary != lastFeedbackSummary;
            ulong periodicLogFrames = feedbackKind == (byte)DualSenseHapticFrameKind.AudioPcm
                ? 1000UL
                : 100UL;
            if (summaryChanged || kindFrames <= 4 || kindFrames % periodicLogFrames == 0)
            {
                lastFeedbackSummaryByKind[feedbackKind] = feedbackSummary;
                progress.Report("[HOST_OUTPUT] " + feedbackSummary);
            }

            if (!mapped)
            {
                if (!string.IsNullOrWhiteSpace(reason) && reportedSkipReasons.Add(reason))
                {
                    progress.Report("[PRO2_OUTPUT] skipped: " + reason);
                }
                continue;
            }

            if (outputSink == null)
            {
                if (lastOutputState != "no_sink")
                {
                    lastOutputState = "no_sink";
                    progress.Report("[PRO2_OUTPUT] no real Pro2 output sink; source=" + packet.Source);
                }
                continue;
            }

            if (!outputSink.IsOutputReady)
            {
                string state = "not_ready:" + packet.Source;
                if (lastOutputState != state)
                {
                    lastOutputState = state;
                    progress.Report("[PRO2_OUTPUT] real Pro2 output is not ready; source=" +
                                    packet.Source + "; host feedback remains log-only.");
                }
                continue;
            }

            if (outputSink.TryWriteOutputReport(packet.Report, out string error))
            {
                writes++;
                string state = packet.Source + "/" + (packet.Active ? "active" : "neutral");
                if (state != lastOutputState || writes <= 4 || writes % 100 == 0)
                {
                    lastOutputState = state;
                    progress.Report("[PRO2_OUTPUT] queued " + state + " count=" + writes);
                }
            }
            else
            {
                failures++;
                string state = "write_failed:" + error;
                if (state != lastOutputState || failures <= 4 || failures % 20 == 0)
                {
                    lastOutputState = state;
                    progress.Report("[PRO2_OUTPUT] write failed count=" + failures + " error=" + error);
                }
            }
        }
    }

    private async Task RunLoopAsync(
        string name,
        Func<CancellationToken, Task> loop,
        CancellationToken cancellationToken)
    {
        try
        {
            await loop(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            progress.Report("[VIIPER] " + name + " loop failed: " + ex.Message);
            cts.Cancel();
            if (Interlocked.Exchange(ref faultReported, 1) == 0)
            {
                faultProgress?.Report(ex);
            }
        }
    }

    private async Task<bool> RecoverStreamAsync(
        string loopName,
        ViiperDeviceStream failedStream,
        Exception error,
        CancellationToken cancellationToken)
    {
        if (cts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        await streamRecoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            ViiperDevice? currentDevice = device;
            if (currentDevice == null)
            {
                return false;
            }

            ViiperDeviceStream? currentStream = GetStream();
            if (currentStream != null && !ReferenceEquals(currentStream, failedStream))
            {
                progress.Report("[VIIPER] " + loopName + " stream resumed on recovered API stream.");
                return true;
            }

            if (ReferenceEquals(currentStream, failedStream))
            {
                _ = ExchangeStream(null);
            }

            progress.Report("[VIIPER] " + loopName +
                            " stream interrupted: " + error.Message +
                            " Reopening API stream without detaching USB device.");
            try
            {
                await failedStream.DisposeAsync();
            }
            catch
            {
            }

            Exception? lastError = error;
            for (int attempt = 1; attempt <= StreamRecoveryAttempts; attempt++)
            {
                try
                {
                    ViiperDeviceStream replacement = await client.OpenStreamAsync(
                        currentDevice.BusId,
                        currentDevice.DevId,
                        cancellationToken).ConfigureAwait(false);
                    SetStream(replacement);
                    progress.Report("[VIIPER] API stream recovered attempt=" + attempt +
                                    " bus=" + currentDevice.BusId +
                                    " dev=" + currentDevice.DevId);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    int delayMs = Math.Min(250 * attempt, 1000);
                    progress.Report("[VIIPER] API stream recover retry " + attempt +
                                    "/" + StreamRecoveryAttempts +
                                    " failed: " + ex.Message);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }

            progress.Report("[VIIPER] API stream recovery exhausted; session restart required. last=" +
                            (lastError?.Message ?? error.Message));
            return false;
        }
        finally
        {
            streamRecoveryGate.Release();
        }
    }

    private ViiperDeviceStream? GetStream()
    {
        lock (streamSync)
        {
            return stream;
        }
    }

    private void SetStream(ViiperDeviceStream? value)
    {
        lock (streamSync)
        {
            stream = value;
        }
    }

    private ViiperDeviceStream? ExchangeStream(ViiperDeviceStream? value)
    {
        lock (streamSync)
        {
            ViiperDeviceStream? previous = stream;
            stream = value;
            return previous;
        }
    }

    private static async Task DrainTaskAsync(Task? task)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
            // The user-facing log already records stream state; cleanup should not throw.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        streamRecoveryGate.Dispose();
        cts.Dispose();
    }
}
