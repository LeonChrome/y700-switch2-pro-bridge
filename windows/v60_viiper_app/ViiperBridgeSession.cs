using System;
using System.Collections.Generic;
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
    private readonly ViiperGyroMode gyroMode;
    private readonly object streamSync = new();
    private ViiperDeviceStream? stream;
    private ViiperDevice? device;
    private uint busId;
    private bool createdBus;
    private Task? inputTask;
    private Task? feedbackTask;
    private int faultReported;
    private const int StreamRecoveryAttempts = 5;
    private ulong usbipAttachCount;
    private ulong UsbipDetachCount { get; set; }
    private ulong DeviceRecreateCount { get; set; }
    private ulong viiperStreamDisconnectCount;
    private ulong viiperStreamRecoveryCount;
    private ulong SteamDeviceHashChangeCount { get; set; }
    private string lastViiperError = "";
    private string lastUsbipLifecycleEvent = "none";

    public ViiperBridgeSession(
        ViiperProtocolClient client,
        ViiperDeviceProfile profile,
        IProgress<string> progress,
        IGamepadInputSource? inputSource = null,
        IGamepadOutputSink? outputSink = null,
        IProgress<Exception>? faultProgress = null,
        ViiperGyroMode gyroMode = ViiperGyroMode.Source60Hz)
    {
        this.client = client;
        this.profile = profile;
        this.progress = progress;
        this.inputSource = inputSource;
        this.outputSink = outputSink ?? inputSource as IGamepadOutputSink;
        this.faultProgress = faultProgress;
        this.gyroMode = gyroMode;
    }

    public ViiperDeviceProfile Profile => profile;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string ping = await client.PingAsync(cancellationToken);
        progress.Report("[VIIPER] ping " + ping);

        busId = await client.BusCreateAsync(cancellationToken);
        createdBus = true;
        progress.Report("[VIIPER] created dedicated bus " + busId);

        device = await client.AddDeviceAsync(busId, profile.DeviceType, cancellationToken);
        usbipAttachCount = 1;
        lastUsbipLifecycleEvent = "initial_auto_attach";
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
                lastUsbipLifecycleEvent = "session_stop_remove_device";
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
        double pushTargetHz = 1.0 / profile.SendInterval.TotalSeconds;
        ulong frames = 0;
        ulong lastRateFrames = 0;
        uint lastRateUpdates = 0;
        uint lastSeenUpdates = 0;
        long lastRateTicks = 0;
        long lastLoopTicks = 0;
        uint? lastPushedUpdate = null;
        uint lastMotionUpdate = 0;
        GamepadState? filteredMotionState = null;
        ulong statePushCount = 0;
        ulong stateReuseCount = 0;
        ulong currentSameStatePushes = 0;
        ulong sameStatePushMax = 0;
        ulong age0To20 = 0;
        ulong age20To33 = 0;
        ulong age33To50 = 0;
        ulong age50To100 = 0;
        ulong ageOver100 = 0;
        var intervalSourceAgeSamples = new List<double>((int)PerformanceLogIntervalFrames);
        var intervalLoopGapSamples = new List<double>((int)PerformanceLogIntervalFrames);
        var intervalWriteSamplesMs = new List<double>((int)PerformanceLogIntervalFrames);
        double intervalWriteMsSum = 0;
        double intervalWriteMaxMs = 0;
        ulong intervalWriteSamples = 0;
        ulong intervalWriteOver2Ms = 0;
        ulong intervalWriteOver8Ms = 0;
        double intervalLoopGapMaxMs = 0;
        ulong intervalLoopGapOver8Ms = 0;
        ulong intervalLoopGapOver16Ms = 0;
        double intervalSourceAgeMaxMs = 0;
        string lastSource = "";
        progress.Report("[VIIPER_TIMER] requested_ms=1 active=" + timerResolution.IsActive +
                        " result=" + timerResolution.Result +
                        " backend=" + timer.Backend +
                        " push_target_hz=" + pushTargetHz.ToString("F1") +
                        " gyro_mode=" + GyroModeLabel(gyroMode));
        while (timer.WaitForNextTick(cancellationToken))
        {
            long loopTicks = Stopwatch.GetTimestamp();
            if (lastLoopTicks != 0)
            {
                double loopGapMs = TicksToMilliseconds(loopTicks - lastLoopTicks);
                intervalLoopGapMaxMs = Math.Max(intervalLoopGapMaxMs, loopGapMs);
                if (loopGapMs >= 8)
                {
                    intervalLoopGapOver8Ms++;
                }
                if (loopGapMs >= 16)
                {
                    intervalLoopGapOver16Ms++;
                }
                intervalLoopGapSamples.Add(loopGapMs);
            }
            lastLoopTicks = loopTicks;

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
                lastSeenUpdates = state.Updates;
                bool reusedState = lastPushedUpdate.HasValue &&
                                   state.Updates == lastPushedUpdate.Value;
                statePushCount++;
                if (reusedState)
                {
                    stateReuseCount++;
                    currentSameStatePushes++;
                }
                else
                {
                    currentSameStatePushes = 1;
                    lastPushedUpdate = state.Updates;
                }
                sameStatePushMax = Math.Max(sameStatePushMax, currentSameStatePushes);

                if (age < TimeSpan.FromMinutes(1))
                {
                    double ageMs = age.TotalMilliseconds;
                    intervalSourceAgeMaxMs = Math.Max(intervalSourceAgeMaxMs, ageMs);
                    intervalSourceAgeSamples.Add(ageMs);
                    CountAgeBucket(
                        ageMs,
                        ref age0To20,
                        ref age20To33,
                        ref age33To50,
                        ref age50To100,
                        ref ageOver100);
                }
                GamepadState continuous =
                    InputContinuityPolicy.Resolve(state, age, out source);
                continuous = ApplyGyroMode(
                    continuous,
                    state.Updates,
                    pushTargetHz,
                    ref lastMotionUpdate,
                    ref filteredMotionState);
                packet = VirtualPadPackets.FromGamepad(profile, continuous);
            }
            else
            {
                currentSameStatePushes = 0;
                packet = VirtualPadPackets.NeutralInput(profile);
                source = "neutral";
            }

            try
            {
                long writeStartTicks = Stopwatch.GetTimestamp();
                await current.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
                double writeMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - writeStartTicks);
                intervalWriteSamples++;
                intervalWriteMsSum += writeMs;
                intervalWriteSamplesMs.Add(writeMs);
                intervalWriteMaxMs = Math.Max(intervalWriteMaxMs, writeMs);
                if (writeMs >= 2)
                {
                    intervalWriteOver2Ms++;
                }
                if (writeMs >= 8)
                {
                    intervalWriteOver8Ms++;
                }
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
            string sourceFamily = SourceFamily(source);
            bool sourceChanged = sourceFamily != lastSource;
            if (rateDue || sourceChanged)
            {
                lastSource = sourceFamily;
                string rateSummary = "target_hz=" + pushTargetHz.ToString("F1");
                if (rateDue)
                {
                    long nowTicks = rateWatch.ElapsedTicks;
                    double elapsedSeconds = (nowTicks - lastRateTicks) / (double)Stopwatch.Frequency;
                    double actualHz = elapsedSeconds > 0
                        ? (frames - lastRateFrames) / elapsedSeconds
                        : 0;
                    if (inputSource is IGamepadRuntimeTelemetrySink telemetrySink)
                    {
                        telemetrySink.ReportViiperPushRate(actualHz);
                    }
                    uint updateDelta = lastSeenUpdates >= lastRateUpdates
                        ? lastSeenUpdates - lastRateUpdates
                        : lastSeenUpdates;
                    double bleHz = elapsedSeconds > 0
                        ? updateDelta / elapsedSeconds
                        : 0;
                    double stateReuseRatio = statePushCount == 0
                        ? 0
                        : stateReuseCount / (double)statePushCount;
                    lastRateTicks = nowTicks;
                    lastRateFrames = frames;
                    lastRateUpdates = lastSeenUpdates;
                    rateSummary +=
                        " actual_hz=" + actualHz.ToString("F1") +
                        " ble_hz=" + bleHz.ToString("F1") +
                        " viiper_push_hz=" + actualHz.ToString("F1") +
                        " state_reuse_count=" + stateReuseCount +
                        " state_reuse_ratio=" + stateReuseRatio.ToString("F3") +
                        " same_state_push_max=" + sameStatePushMax +
                        " source_age_bucket=0_20:" + age0To20 +
                        ",20_33:" + age20To33 +
                        ",33_50:" + age33To50 +
                        ",50_100:" + age50To100 +
                        ",gt100:" + ageOver100 +
                        " source_age_bucket_0_20=" + age0To20 +
                        " source_age_bucket_20_33=" + age20To33 +
                        " source_age_bucket_33_50=" + age33To50 +
                        " source_age_bucket_50_100=" + age50To100 +
                        " source_age_bucket_over100=" + ageOver100;
                    double avgWriteMs = intervalWriteSamples == 0
                        ? 0
                        : intervalWriteMsSum / intervalWriteSamples;
                    rateSummary +=
                        " loop_gap_max_ms=" + intervalLoopGapMaxMs.ToString("F2") +
                        " loop_gap_p95_ms=" + Percentile(intervalLoopGapSamples, 95).ToString("F2") +
                        " loop_gap_p99_ms=" + Percentile(intervalLoopGapSamples, 99).ToString("F2") +
                        " loop_gap_over8=" + intervalLoopGapOver8Ms +
                        " loop_gap_over16=" + intervalLoopGapOver16Ms +
                        " viiper_write_avg_ms=" + avgWriteMs.ToString("F3") +
                        " viiper_write_max_ms=" + intervalWriteMaxMs.ToString("F3") +
                        " viiper_write_p95_ms=" + Percentile(intervalWriteSamplesMs, 95).ToString("F3") +
                        " viiper_write_p99_ms=" + Percentile(intervalWriteSamplesMs, 99).ToString("F3") +
                        " viiper_write_over2=" + intervalWriteOver2Ms +
                        " viiper_write_over8=" + intervalWriteOver8Ms +
                        " source_age_max_ms=" + intervalSourceAgeMaxMs.ToString("F1") +
                        " source_age_p95_ms=" + Percentile(intervalSourceAgeSamples, 95).ToString("F1") +
                        " source_age_p99_ms=" + Percentile(intervalSourceAgeSamples, 99).ToString("F1") +
                        " usbip_attach_count=" + usbipAttachCount +
                        " usbip_detach_count=" + UsbipDetachCount +
                        " device_recreate_count=" + DeviceRecreateCount +
                        " viiper_stream_disconnect_count=" + viiperStreamDisconnectCount +
                        " viiper_stream_recovery_count=" + viiperStreamRecoveryCount +
                        " steam_device_hash_change_count=" + SteamDeviceHashChangeCount +
                        " last_viiper_error=\"" + SanitizeLogValue(lastViiperError) + "\"" +
                        " last_usbip_lifecycle_event=" + lastUsbipLifecycleEvent;
                    intervalWriteMsSum = 0;
                    intervalWriteMaxMs = 0;
                    intervalWriteSamples = 0;
                    intervalWriteOver2Ms = 0;
                    intervalWriteOver8Ms = 0;
                    intervalLoopGapMaxMs = 0;
                    intervalLoopGapOver8Ms = 0;
                    intervalLoopGapOver16Ms = 0;
                    intervalLoopGapSamples.Clear();
                    intervalSourceAgeMaxMs = 0;
                    intervalSourceAgeSamples.Clear();
                    intervalWriteSamplesMs.Clear();
                    statePushCount = 0;
                    stateReuseCount = 0;
                    sameStatePushMax = currentSameStatePushes;
                    age0To20 = 0;
                    age20To33 = 0;
                    age33To50 = 0;
                    age50To100 = 0;
                    ageOver100 = 0;
                }

                string inputMetrics = inputSource is IGamepadInputMetricsSource metrics
                    ? " " + metrics.MetricsSummary
                    : "";
                progress.Report("[VIIPER] fed " + frames + " " + source + " " + profile.Label +
                                " frames " + rateSummary + inputMetrics);
            }
        }
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        double position = (sorted.Length - 1) * Math.Clamp(percentile, 0, 100) / 100.0;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        double weight = position - lower;
        return sorted[lower] * (1.0 - weight) + sorted[upper] * weight;
    }

    private static void CountAgeBucket(
        double ageMs,
        ref ulong age0To20,
        ref ulong age20To33,
        ref ulong age33To50,
        ref ulong age50To100,
        ref ulong ageOver100)
    {
        switch (ageMs)
        {
            case < 20:
                age0To20++;
                break;
            case < 33:
                age20To33++;
                break;
            case < 50:
                age33To50++;
                break;
            case < 100:
                age50To100++;
                break;
            default:
                ageOver100++;
                break;
        }
    }

    private GamepadState ApplyGyroMode(
        GamepadState state,
        uint sourceUpdate,
        double pushTargetHz,
        ref uint lastMotionUpdate,
        ref GamepadState? filteredMotionState)
    {
        switch (gyroMode)
        {
            case ViiperGyroMode.Hold250Hz:
                lastMotionUpdate = sourceUpdate;
                return state;
            case ViiperGyroMode.Source60Hz:
                if (sourceUpdate == lastMotionUpdate)
                {
                    return WithoutMotion(state);
                }
                lastMotionUpdate = sourceUpdate;
                return state;
            case ViiperGyroMode.Scaled250Hz:
                lastMotionUpdate = sourceUpdate;
                return ScaleGyro(
                    state,
                    Math.Clamp(66.7 / Math.Max(pushTargetHz, 1.0), 0.25, 1.0));
            case ViiperGyroMode.Filtered250Hz:
                lastMotionUpdate = sourceUpdate;
                filteredMotionState = FilterMotion(filteredMotionState, state);
                return CopyMotion(state, filteredMotionState);
            default:
                lastMotionUpdate = sourceUpdate;
                return state;
        }
    }

    private static GamepadState WithoutMotion(GamepadState state)
    {
        GamepadState result = state.Clone();
        result.GyroValid = false;
        result.AccelValid = false;
        result.GyroX = 0;
        result.GyroY = 0;
        result.GyroZ = 0;
        result.AccelX = 0;
        result.AccelY = 0;
        result.AccelZ = 0;
        return result;
    }

    private static GamepadState ScaleGyro(GamepadState state, double scale)
    {
        if (!state.GyroValid || Math.Abs(scale - 1.0) < 0.001)
        {
            return state;
        }

        GamepadState result = state.Clone();
        result.GyroX = ScaleInt16(result.GyroX, scale);
        result.GyroY = ScaleInt16(result.GyroY, scale);
        result.GyroZ = ScaleInt16(result.GyroZ, scale);
        return result;
    }

    private static GamepadState FilterMotion(GamepadState? previous, GamepadState current)
    {
        if (previous == null)
        {
            return current.Clone();
        }

        const double alpha = 0.35;
        GamepadState filtered = current.Clone();
        if (current.GyroValid && previous.GyroValid)
        {
            filtered.GyroX = LerpInt16(previous.GyroX, current.GyroX, alpha);
            filtered.GyroY = LerpInt16(previous.GyroY, current.GyroY, alpha);
            filtered.GyroZ = LerpInt16(previous.GyroZ, current.GyroZ, alpha);
        }
        if (current.AccelValid && previous.AccelValid)
        {
            filtered.AccelX = LerpInt16(previous.AccelX, current.AccelX, alpha);
            filtered.AccelY = LerpInt16(previous.AccelY, current.AccelY, alpha);
            filtered.AccelZ = LerpInt16(previous.AccelZ, current.AccelZ, alpha);
        }
        return filtered;
    }

    private static GamepadState CopyMotion(GamepadState current, GamepadState? motion)
    {
        if (motion == null)
        {
            return current;
        }

        GamepadState result = current.Clone();
        result.GyroValid = motion.GyroValid;
        result.GyroX = motion.GyroX;
        result.GyroY = motion.GyroY;
        result.GyroZ = motion.GyroZ;
        result.AccelValid = motion.AccelValid;
        result.AccelX = motion.AccelX;
        result.AccelY = motion.AccelY;
        result.AccelZ = motion.AccelZ;
        return result;
    }

    private static short ScaleInt16(short value, double scale)
    {
        return ClampInt16((int)Math.Round(value * scale));
    }

    private static short LerpInt16(short previous, short current, double alpha)
    {
        return ClampInt16((int)Math.Round(previous + (current - previous) * alpha));
    }

    private static short ClampInt16(int value)
    {
        if (value < short.MinValue) return short.MinValue;
        if (value > short.MaxValue) return short.MaxValue;
        return (short)value;
    }

    private static string GyroModeLabel(ViiperGyroMode mode)
    {
        return mode switch
        {
            ViiperGyroMode.Hold250Hz => "hold_250hz",
            ViiperGyroMode.Source60Hz => "source_60hz",
            ViiperGyroMode.Scaled250Hz => "scaled_250hz",
            ViiperGyroMode.Filtered250Hz => "filtered_250hz",
            _ => mode.ToString()
        };
    }

    private static string SourceFamily(string source)
    {
        if (source.StartsWith("pro2_ble_age_", StringComparison.Ordinal))
        {
            return "pro2_ble_live";
        }
        return source;
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
                            " Reopening API stream without detaching USB device. SEVERE_LINK_ANOMALY=true");
            viiperStreamDisconnectCount++;
            lastViiperError = error.Message;
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
                    viiperStreamRecoveryCount++;
                    lastUsbipLifecycleEvent = "api_stream_reopened_without_detach";
                    progress.Report("[VIIPER] API stream recovered attempt=" + attempt +
                                    " bus=" + currentDevice.BusId +
                                    " dev=" + currentDevice.DevId +
                                    " SEVERE_LINK_ANOMALY=true");
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    lastViiperError = ex.Message;
                    int delayMs = Math.Min(250 * attempt, 1000);
                    progress.Report("[VIIPER] API stream recover retry " + attempt +
                                    "/" + StreamRecoveryAttempts +
                                    " failed: " + ex.Message);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }

            progress.Report("[VIIPER] API stream recovery exhausted; session restart required. last=" +
                            (lastError?.Message ?? error.Message));
            lastViiperError = lastError?.Message ?? error.Message;
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

    private static string SanitizeLogValue(string value)
    {
        return value.Replace("\"", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        streamRecoveryGate.Dispose();
        cts.Dispose();
    }
}
