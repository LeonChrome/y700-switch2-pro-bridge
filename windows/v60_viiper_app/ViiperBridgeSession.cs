using System;
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
        IGamepadInputSource? inputSource = null)
    {
        this.client = client;
        this.profile = profile;
        this.progress = progress;
        this.inputSource = inputSource;
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
            ? "[VIIPER] stream connected; feeding Windows HID Pro2 input."
            : "[VIIPER] stream connected; feeding neutral input until Pro2 HID source is connected.");

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
        using var timer = new PeriodicTimer(profile.SendInterval);
        ulong frames = 0;
        string lastSource = "";
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
                source = "pro2_hid";
            }
            else
            {
                packet = VirtualPadPackets.NeutralInput(profile);
                source = "neutral";
            }

            await current.WriteAsync(packet, cancellationToken);
            frames++;
            if (frames % 250 == 0 || source != lastSource)
            {
                lastSource = source;
                progress.Report("[VIIPER] fed " + frames + " " + source + " " + profile.Label + " frames.");
            }
        }
    }

    private async Task FeedbackLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ViiperDeviceStream? current = stream;
            if (current == null)
            {
                return;
            }

            byte[] feedback = await current.ReadExactAsync(profile.FeedbackSize, cancellationToken);
            progress.Report("[HOST_OUTPUT] " + VirtualPadPackets.FeedbackSummary(profile, feedback));
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
