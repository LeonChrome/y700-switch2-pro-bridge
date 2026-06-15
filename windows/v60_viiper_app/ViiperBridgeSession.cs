using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2V60Viiper;

public sealed class ViiperBridgeSession : IAsyncDisposable
{
    private readonly ViiperProtocolClient client;
    private readonly ViiperDeviceProfile profile;
    private readonly IProgress<string> progress;
    private readonly IGamepadInputSource? inputSource;
    private readonly IGamepadOutputSink? outputSink;
    private readonly CancellationTokenSource cts = new();
    private ViiperDeviceStream? stream;
    private ViiperDevice? device;
    private uint busId;
    private bool createdBus;
    private Task? inputTask;
    private Task? feedbackTask;

    public ViiperBridgeSession(
        ViiperProtocolClient client,
        ViiperDeviceProfile profile,
        IProgress<string> progress,
        IGamepadInputSource? inputSource = null,
        IGamepadOutputSink? outputSink = null)
    {
        this.client = client;
        this.profile = profile;
        this.progress = progress;
        this.inputSource = inputSource;
        this.outputSink = outputSink ?? inputSource as IGamepadOutputSink;
    }

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
        stream = await client.OpenStreamAsync(device.BusId, device.DevId, cancellationToken);
        progress.Report(inputSource is { IsRunning: true }
            ? "[VIIPER] stream connected; feeding Pro2 BLE input."
            : "[VIIPER] stream connected; feeding neutral input until Pro2 BLE source is connected.");
        progress.Report(outputSink is { IsOutputReady: true }
            ? "[VIIPER] Pro2 output writeback is enabled for rumble."
            : "[VIIPER] Pro2 output writeback is not ready; host rumble will be logged only.");

        inputTask = Task.Run(() => InputLoopAsync(cts.Token));
        feedbackTask = Task.Run(() => FeedbackLoopAsync(cts.Token));
    }

    public async Task StopAsync()
    {
        cts.Cancel();
        if (stream != null)
        {
            await stream.DisposeAsync();
            stream = null;
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
        using var timer = new PeriodicTimer(profile.SendInterval);
        var rateWatch = Stopwatch.StartNew();
        ulong frames = 0;
        ulong lastRateFrames = 0;
        long lastRateTicks = 0;
        string lastSource = "";
        progress.Report("[VIIPER_TIMER] requested_ms=1 active=" + timerResolution.IsActive +
                        " result=" + timerResolution.Result);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            ViiperDeviceStream? current = stream;
            if (current == null)
            {
                return;
            }

            byte[] packet;
            string source;
            if (inputSource != null &&
                inputSource.TryGetLatest(out GamepadState state, out TimeSpan age) &&
                age <= TimeSpan.FromMilliseconds(250))
            {
                packet = VirtualPadPackets.FromGamepad(profile, state);
                source = "pro2_ble";
            }
            else
            {
                packet = VirtualPadPackets.NeutralInput(profile);
                source = "neutral";
            }

            await current.WriteAsync(packet, cancellationToken);
            frames++;
            bool rateDue = frames % 250 == 0;
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
        ulong frames = 0;
        ulong writes = 0;
        ulong failures = 0;
        string lastFeedbackSummary = "";
        string lastOutputState = "";
        while (!cancellationToken.IsCancellationRequested)
        {
            ViiperDeviceStream? current = stream;
            if (current == null)
            {
                return;
            }

            byte[] feedback = await current.ReadExactAsync(profile.FeedbackSize, cancellationToken);
            frames++;
            string feedbackSummary = VirtualPadPackets.FeedbackSummary(profile, feedback);
            if (feedbackSummary != lastFeedbackSummary || frames <= 4 || frames % 100 == 0)
            {
                lastFeedbackSummary = feedbackSummary;
                progress.Report("[HOST_OUTPUT] " + feedbackSummary);
            }

            if (!Pro2OutputPacketMapper.TryMapFeedback(profile, feedback, out Pro2OutputPacket packet, out string reason))
            {
                if (!string.IsNullOrWhiteSpace(reason) && reason != lastOutputState)
                {
                    lastOutputState = reason;
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

            if (outputSink.TryWriteOutputReport(packet.Report, out string error))
            {
                writes++;
                string state = packet.Source + "/" + (packet.Active ? "active" : "neutral");
                if (state != lastOutputState || writes <= 4 || writes % 100 == 0)
                {
                    lastOutputState = state;
                    progress.Report("[PRO2_OUTPUT] wrote " + state + " count=" + writes);
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
        cts.Dispose();
    }
}
