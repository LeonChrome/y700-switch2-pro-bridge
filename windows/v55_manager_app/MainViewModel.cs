using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Y700Switch2V55Manager;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Window owner;
    private readonly FirmwareFlasher flasher = new();
    private readonly StringBuilder log = new();
    private readonly SemaphoreSlim serialLock = new(1, 1);
    private CancellationTokenSource? gameMonitorCts;

    private PortItem? selectedPort;
    private string portStatus = "刷新端口后会优先选择 CH343P / WCH USB Serial。";
    private string usbStatus = "USB 未检查。";
    private string audioStatus = "音频未检查。";
    private string bleStatus = "BLE 未查询。";
    private string hapticStatus = "默认：Live off / Dry-run on。";
    private string nextAction = "选择 CH343P COM 口并点击一键刷 V5.5。";
    private string overallStatus = "Ready";
    private string customCommand = "status";
    private string bleTarget = "";
    private string audioPattern = "both_tick";
    private string audioIntensity = "48";
    private string audioDurationMs = "600";
    private string audioDeviceName = "Wireless Controller";
    private string gameMonitorSeconds = "300";
    private string monitorStatus = "未开始。启动后会保持 Live raw02，并每秒记录游戏是否真的输出 DualSense haptic audio。";
    private bool busy;
    private bool gameMonitorRunning;

    public ObservableCollection<PortItem> Ports { get; } = new();
    public ObservableCollection<string> AudioPatterns { get; } = new(new[]
    {
        "ch2_tick", "ch3_tick", "both_tick", "ch2_punch", "ch3_punch", "both_punch", "texture", "continuous", "sweep", "silence"
    });

    public PortItem? SelectedPort { get => selectedPort; set { selectedPort = value; OnPropertyChanged(); } }
    public string PortStatus { get => portStatus; set { portStatus = value; OnPropertyChanged(); } }
    public string UsbStatus { get => usbStatus; set { usbStatus = value; OnPropertyChanged(); } }
    public string AudioStatus { get => audioStatus; set { audioStatus = value; OnPropertyChanged(); } }
    public string BleStatus { get => bleStatus; set { bleStatus = value; OnPropertyChanged(); } }
    public string HapticStatus { get => hapticStatus; set { hapticStatus = value; OnPropertyChanged(); } }
    public string NextAction { get => nextAction; set { nextAction = value; OnPropertyChanged(); } }
    public string OverallStatus { get => overallStatus; set { overallStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverallBrush)); } }
    public string CustomCommand { get => customCommand; set { customCommand = value; OnPropertyChanged(); } }
    public string BleTarget { get => bleTarget; set { bleTarget = value; OnPropertyChanged(); } }
    public string AudioPattern { get => audioPattern; set { audioPattern = value; OnPropertyChanged(); } }
    public string AudioIntensity { get => audioIntensity; set { audioIntensity = value; OnPropertyChanged(); } }
    public string AudioDurationMs { get => audioDurationMs; set { audioDurationMs = value; OnPropertyChanged(); } }
    public string AudioDeviceName { get => audioDeviceName; set { audioDeviceName = value; OnPropertyChanged(); } }
    public string GameMonitorSeconds { get => gameMonitorSeconds; set { gameMonitorSeconds = value; OnPropertyChanged(); } }
    public string MonitorStatus { get => monitorStatus; set { monitorStatus = value; OnPropertyChanged(); } }
    public bool Busy { get => busy; set { busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverallBrush)); } }
    public bool GameMonitorRunning { get => gameMonitorRunning; set { gameMonitorRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverallBrush)); } }

    public string FirmwareSummary => "DualSense 4ch 触觉 / Pro2 原生桥接 / HID-only recovery / 内嵌 esptool";
    public string SafetySummary => "Live 默认关，Dry-run 默认开；Live 开启需确认，BLE 错误会自动关闭。";
    public string LogText => log.ToString();
    public Brush OverallBrush => Busy || GameMonitorRunning ? Brushes.Goldenrod : OverallStatus.Contains("Error", StringComparison.OrdinalIgnoreCase) ? Brushes.IndianRed : Brushes.LimeGreen;

    public ICommand RefreshPortsCommand { get; }
    public ICommand FlashHapticCommand { get; }
    public ICommand FlashHidOnlyCommand { get; }
    public ICommand FlashPro2Command { get; }
    public ICommand CheckUsbCommand { get; }
    public ICommand ListAudioCommand { get; }
    public ICommand OpenJoyCommand { get; }
    public ICommand OpenDeviceManagerCommand { get; }
    public ICommand BleScanCommand { get; }
    public ICommand BleListCommand { get; }
    public ICommand BleReconnectCommand { get; }
    public ICommand BleAutoOnCommand { get; }
    public ICommand BleAutoOffCommand { get; }
    public ICommand BleDisconnectCommand { get; }
    public ICommand BleConnectCommand { get; }
    public ICommand HapticStatusCommand { get; }
    public ICommand DryRunOnCommand { get; }
    public ICommand LiveOffCommand { get; }
    public ICommand LiveOnCommand { get; }
    public ICommand HapticTickCommand { get; }
    public ICommand HapticPunchCommand { get; }
    public ICommand HapticStopCommand { get; }
    public ICommand SafeHapticTestCommand { get; }
    public ICommand SendAudioPatternCommand { get; }
    public ICommand StartGameMonitorCommand { get; }
    public ICommand StopGameMonitorCommand { get; }
    public ICommand SendCustomCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SaveLogCommand { get; }

    public MainViewModel(Window owner)
    {
        this.owner = owner;
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        FlashHapticCommand = new RelayCommand(async _ => await FlashAsync("hid_audio_uac1_4ch_ds5like", FlashMode.Upgrade));
        FlashHidOnlyCommand = new RelayCommand(async _ => await FlashAsync("hid_only", FlashMode.Repair));
        FlashPro2Command = new RelayCommand(async _ => await FlashAsync("pro2_bridge_v5_5", FlashMode.Upgrade));
        CheckUsbCommand = new RelayCommand(_ => CheckUsb());
        ListAudioCommand = new RelayCommand(async _ => await RunAudioSenderAsync("-ListDevices"));
        OpenJoyCommand = new RelayCommand(_ => StartShell("joy.cpl"));
        OpenDeviceManagerCommand = new RelayCommand(_ => StartShell("devmgmt.msc"));
        BleScanCommand = new RelayCommand(async _ => await SendSerialAsync("ble scan", 18, s => BleStatus = "扫描完成，查看日志候选项。"));
        BleListCommand = new RelayCommand(async _ => await SendSerialAsync("ble list", 6, s => BleStatus = "已读取 BLE 列表。"));
        BleReconnectCommand = new RelayCommand(async _ => await SendSerialAsync("ble reconnect", 20, s => BleStatus = "已请求连接上次地址。"));
        BleAutoOnCommand = new RelayCommand(async _ => await SendSerialAsync("ble auto on", 5, s => BleStatus = "上次地址自动重连：开。"));
        BleAutoOffCommand = new RelayCommand(async _ => await SendSerialAsync("ble auto off", 5, s => BleStatus = "上次地址自动重连：关。"));
        BleDisconnectCommand = new RelayCommand(async _ => await SendSerialAsync("ble disconnect", 5, s => BleStatus = "已请求断开 BLE。"));
        BleConnectCommand = new RelayCommand(async _ => await SendSerialAsync("ble connect " + (string.IsNullOrWhiteSpace(BleTarget) ? "last" : BleTarget), 20, s => BleStatus = "已请求连接目标。"));
        HapticStatusCommand = new RelayCommand(async _ => await SendSerialAsync("haptic status", 5, s => HapticStatus = SummarizeHaptic(s)));
        DryRunOnCommand = new RelayCommand(async _ => await SendSerialAsync("haptic dryrun on", 4, _ => HapticStatus = "Dry-run 已开启。"));
        LiveOffCommand = new RelayCommand(async _ => await TurnLiveOffAsync());
        LiveOnCommand = new RelayCommand(async _ => await TurnLiveOnAsync());
        HapticTickCommand = new RelayCommand(async _ => await SendLiveHapticPulseAsync("tick", "Tick"));
        HapticPunchCommand = new RelayCommand(async _ => await SendLiveHapticPulseAsync("punch", "Punch"));
        HapticStopCommand = new RelayCommand(async _ => await SendSerialAsync("haptic test live stop", 4, _ => HapticStatus = "已发送 live stop。"));
        SafeHapticTestCommand = new RelayCommand(async _ => await RunSafeHapticTestAsync());
        SendAudioPatternCommand = new RelayCommand(async _ => await SendAudioPatternAsync());
        StartGameMonitorCommand = new RelayCommand(async _ => await StartGameMonitorAsync());
        StopGameMonitorCommand = new RelayCommand(async _ => await StopGameMonitorAsync());
        SendCustomCommand = new RelayCommand(async _ => await SendSerialAsync(CustomCommand, 6, _ => { }));
        ClearLogCommand = new RelayCommand(_ => { log.Clear(); OnPropertyChanged(nameof(LogText)); });
        SaveLogCommand = new RelayCommand(_ => SaveLog());

        AppendLog("V5.5 Manager ready. This exe uses embedded firmware and esptool; ESP-IDF is not required for one-click flashing.");
        RefreshPorts();
        CheckUsb();
    }

    private void RefreshPorts()
    {
        Ports.Clear();
        foreach (PortItem item in DeviceInspector.GetPorts()) Ports.Add(item);
        SelectedPort = FirstLikelyPort();
        PortStatus = Ports.Count == 0
            ? "未发现 COM 口。请连接 CH343P Type-C 或安装 WCH 驱动。"
            : "发现 " + Ports.Count + " 个串口；已优先选择 CH343P/WCH 候选。";
        AppendLog(PortStatus);
    }

    private PortItem? FirstLikelyPort()
    {
        foreach (PortItem port in Ports)
        {
            if (port.LikelyCh343) return port;
        }
        return Ports.Count > 0 ? Ports[0] : null;
    }

    private async Task FlashAsync(string profile, FlashMode mode)
    {
        try
        {
            Busy = true;
            OverallStatus = "Flashing";
            if (SelectedPort == null) RefreshPorts();
            if (SelectedPort == null) throw new InvalidOperationException("没有可用 COM 口。");

            PortStatus = "正在刷入 " + profile + " 到 " + SelectedPort.PortName;
            NextAction = "等待刷入完成。";
            var progress = new Progress<string>(AppendLog);
            await flasher.FlashAsync(SelectedPort.PortName, profile, mode, progress);
            OverallStatus = "Flash OK";
            PortStatus = "刷入完成。请重插 native USB / OTG。";
            NextAction = "重插 native USB / OTG 后点击“快速检查”。";
        }
        catch (Exception ex)
        {
            OverallStatus = "Error";
            PortStatus = "刷入失败: " + FirstLine(ex.Message);
            AppendLog("ERROR flash: " + ex);
        }
        finally
        {
            Busy = false;
        }
    }

    private void CheckUsb()
    {
        UsbStatus = DeviceInspector.GetUsbSummary();
        AppendLog("[USB CHECK]");
        AppendLog(UsbStatus);
        NextAction = UsbStatus.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase) ||
                     UsbStatus.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
            ? "如果音频显示 4ch，先 Dry-run 测试音频 pattern。"
            : "若刚刷完，请重插 native USB / OTG。";
    }

    private async Task SendAudioPatternAsync()
    {
        string args = "-DeviceName \"" + AudioDeviceName.Replace("\"", "") + "\" -Pattern " +
                      AudioPattern + " -DurationMs " + AudioDurationMs + " -Intensity " + AudioIntensity;
        try
        {
            Busy = true;
            OverallStatus = "Audio haptic";
            string initial = await SendSerialCoreAsync("status", 3);
            RequireBleConnected(initial);

            await SendSerialCoreAsync("haptic mode auto", 2);
            await SendSerialCoreAsync("haptic interval 20", 2);
            await SendSerialCoreAsync("haptic max 64", 2);
            await SendSerialCoreAsync("haptic raw02 on", 2);
            await SendSerialCoreAsync("haptic dryrun off", 2);

            string output = await RunAudioSenderCoreAsync(args);
            AppendLog(output);
            await Task.Delay(300);

            string status = await SendSerialCoreAsync("status", 3);
            long active = ReadJsonCounter(status, "audio_active");
            long livePackets = ReadJsonCounter(status, "raw02_live_packets");
            long writes = ReadJsonCounter(status, "raw02_ble_writes");
            long errors = ReadJsonCounter(status, "raw02_ble_errors");
            string left = ReadJsonString(status, "raw02_left");
            string right = ReadJsonString(status, "raw02_right");

            AudioStatus = $"音频 Pattern 已发送；active={active}, raw02={livePackets}, BLE writes={writes}, errors={errors}";
            HapticStatus = $"4ch 音频 -> raw02 -> Pro2：left={ShortHex(left)}, right={ShortHex(right)}";
            OverallStatus = errors == 0 && writes > 0 ? "Haptic OK" : "Check haptic";
            NextAction = errors == 0 && writes > 0
                ? "链路计数已确认；真实游戏还需要游戏确实输出 DualSense 触觉音频。"
                : "若 BLE 已连接但 writes 没增加，请查看日志中的 raw02_error。";
        }
        catch (Exception ex)
        {
            OverallStatus = "Error";
            AudioStatus = "音频实震失败: " + FirstLine(ex.Message);
            AppendLog("ERROR audio haptic: " + ex);
        }
        finally
        {
            foreach (string command in new[] { "haptic test live stop", "haptic dryrun on", "haptic raw02 off" })
            {
                try
                {
                    await SendSerialCoreAsync(command, 2);
                }
                catch (Exception cleanupError)
                {
                    AppendLog("WARN haptic cleanup: " + cleanupError.Message);
                }
            }
            Busy = false;
        }
    }

    private async Task StartGameMonitorAsync()
    {
        if (GameMonitorRunning)
        {
            MonitorStatus = "游戏监听已经在运行。";
            return;
        }

        int seconds = ParseIntOrDefault(GameMonitorSeconds, 300, 10, 900);
        gameMonitorCts = new CancellationTokenSource();
        CancellationToken token = gameMonitorCts.Token;

        try
        {
            Busy = true;
            OverallStatus = "Game monitor";
            MonitorStatus = "正在准备游戏监听：检查 BLE，并开启 Live raw02。";

            string initial = await SendSerialCoreAsync("status", 3);
            RequireBleConnected(initial);

            await SendSerialCoreAsync("haptic mode auto", 2);
            await SendSerialCoreAsync("haptic interval 10", 2);
            await SendSerialCoreAsync("haptic max 96", 2);
            await SendSerialCoreAsync("haptic gain 2.0", 2);
            await SendSerialCoreAsync("haptic transient_gain 1.5", 2);
            await SendSerialCoreAsync("haptic activity 256", 2);
            await SendSerialCoreAsync("haptic raw02 on", 2);
            await SendSerialCoreAsync("haptic dryrun off", 2);

            Busy = false;
            GameMonitorRunning = true;
            OverallStatus = "Monitoring";
            NextAction = "现在进入游戏测试；监听期间不要点音频 Pattern / Stop。测试结束后点“停止监听并关闭 Live”。";
            AppendLog("[GAME_MONITOR_START] seconds=" + seconds +
                      " live_forwarding=true dry_run=false interval_ms=10 max=96 gain=2.0 transient_gain=1.5 activity_threshold=256");

            await RunGameMonitorLoopAsync(seconds, token);
            await DisableLiveForwardingAfterMonitorAsync();
        }
        catch (OperationCanceledException)
        {
            AppendLog("[GAME_MONITOR_CANCELLED]");
        }
        catch (Exception ex)
        {
            OverallStatus = "Error";
            MonitorStatus = "游戏监听失败: " + FirstLine(ex.Message);
            AppendLog("ERROR game monitor: " + ex);
        }
        finally
        {
            Busy = false;
            GameMonitorRunning = false;
            gameMonitorCts?.Dispose();
            gameMonitorCts = null;
        }
    }

    private async Task DisableLiveForwardingAfterMonitorAsync()
    {
        await SendSerialCoreAsync("haptic test live stop", 2);
        await SendSerialCoreAsync("haptic dryrun on", 2);
        await SendSerialCoreAsync("haptic raw02 off", 2);
        AppendLog("[GAME_MONITOR_END_SAFE_OFF] live_forwarding=false dry_run=true");
    }

    private async Task RunGameMonitorLoopAsync(int seconds, CancellationToken token)
    {
        string previous = await SendSerialCoreAsync("status", 2);
        MonitorSnapshot baseline = MonitorSnapshot.FromStatus(previous);
        MonitorSnapshot last = baseline;
        int samples = 0;
        int activeSamples = 0;
        int writeSamples = 0;

        AppendLog("[GAME_MONITOR_BASELINE] " + baseline.ToLogString());

        for (int elapsed = 1; elapsed <= seconds; elapsed++)
        {
            await Task.Delay(1000, token);

            string status = await SendSerialCoreAsync("status", 2);
            MonitorSnapshot current = MonitorSnapshot.FromStatus(status);
            MonitorDelta delta = current.DeltaFrom(last);
            MonitorDelta total = current.DeltaFrom(baseline);
            samples++;
            if (delta.AudioActive > 0) activeSamples++;
            if (delta.BleWrites > 0) writeSamples++;

            string sampleLine =
                "[GAME_MONITOR_SAMPLE] " +
                "t=" + elapsed + "s " +
                current.ToLogString() + " " +
                "delta=(" + delta.ToLogString() + ") " +
                "total=(" + total.ToLogString() + ")";
            AppendLog(sampleLine);

            MonitorStatus =
                $"监听中 {elapsed}/{seconds}s：audio +{total.AudioPackets}，active +{total.AudioActive}，raw02 +{total.Raw02Live}，BLE writes +{total.BleWrites}，errors={current.BleErrors}";
            HapticStatus = SummarizeHaptic(status);
            last = current;
        }

        MonitorDelta finalDelta = last.DeltaFrom(baseline);
        bool gameAudioDetected = finalDelta.AudioPackets > 0 && finalDelta.AudioActive > 0;
        bool liveForwarded = finalDelta.Raw02Live > 0 && finalDelta.BleWrites > 0;
        string conclusion = gameAudioDetected && liveForwarded
            ? "game_haptic_forwarded"
            : gameAudioDetected
                ? "game_haptic_seen_but_not_forwarded"
                : "no_game_haptic_audio_detected";

        AppendLog("[GAME_MONITOR_RESULT] conclusion=" + conclusion +
                  " samples=" + samples +
                  " active_samples=" + activeSamples +
                  " write_samples=" + writeSamples +
                  " " + finalDelta.ToLogString());
        MonitorStatus = "监听完成：" + conclusion + "；日志已包含 GAME_MONITOR_RESULT。";
        OverallStatus = conclusion == "game_haptic_forwarded" ? "Monitor OK" : "Needs source";
    }

    private async Task StopGameMonitorAsync()
    {
        gameMonitorCts?.Cancel();
        try
        {
            await SendSerialCoreAsync("haptic test live stop", 2);
            await SendSerialCoreAsync("haptic dryrun on", 2);
            await SendSerialCoreAsync("haptic raw02 off", 2);
            MonitorStatus = "已停止监听，并已安全关闭 Live raw02。";
            HapticStatus = "Live raw02 已关闭，Dry-run 已开启。";
            OverallStatus = "Monitor stopped";
            AppendLog("[GAME_MONITOR_STOP] live_forwarding=false dry_run=true");
        }
        catch (Exception ex)
        {
            MonitorStatus = "停止监听时串口命令失败: " + FirstLine(ex.Message);
            AppendLog("WARN game monitor stop: " + ex);
        }
    }

    private async Task RunAudioSenderAsync(string arguments)
    {
        try
        {
            Busy = true;
            string output = await RunAudioSenderCoreAsync(arguments);
            AudioStatus = output.Contains("channels=4", StringComparison.OrdinalIgnoreCase)
                ? "音频工具检测到 4ch 或发送成功。"
                : "音频工具已运行；如果只看到 channels=2，请确认已刷 V5.5 4ch 并重插 native USB。";
            AppendLog(output);
        }
        catch (Exception ex)
        {
            AudioStatus = "音频测试失败: " + FirstLine(ex.Message);
            AppendLog("ERROR audio: " + ex);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task<string> RunAudioSenderCoreAsync(string arguments)
    {
        FirmwarePackage package = EmbeddedAssets.EnsurePackage();
        return await RunProcessAsync(package.AudioSenderPath, arguments);
    }

    private async Task SendSerialAsync(string command, int readSeconds, Action<string> after)
    {
        try
        {
            Busy = true;
            string output = await SendSerialCoreAsync(command, readSeconds);
            after(output);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR serial: " + ex);
            OverallStatus = "Error";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task<string> SendSerialCoreAsync(string command, int readSeconds)
    {
        await serialLock.WaitAsync();
        try
        {
            if (SelectedPort == null) RefreshPorts();
            if (SelectedPort == null) throw new InvalidOperationException("没有可用 COM 口。");
            return await SerialCommandClient.SendAsync(
                SelectedPort.PortName,
                command,
                readSeconds,
                new Progress<string>(AppendLog));
        }
        finally
        {
            serialLock.Release();
        }
    }

    private async Task RunSafeHapticTestAsync()
    {
        MessageBoxResult result = MessageBox.Show(owner,
            "将进行一次约 350 ms、低强度的完整链路实震：4ch DualSense 音频 -> raw02 -> BLE Pro2。测试后会自动停止并恢复 Dry-run。",
            "安全实震测试",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (result != MessageBoxResult.OK) return;

        Busy = true;
        OverallStatus = "Haptic test";
        HapticStatus = "正在准备安全实震测试。";
        try
        {
            string initial = await SendSerialCoreAsync("haptic status", 3);
            if (!initial.Contains("\"ble\":\"connected\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Pro2 BLE 尚未连接，已取消真实发送。");
            }

            await SendSerialCoreAsync("haptic defaults", 3);
            await SendSerialCoreAsync("haptic max 48", 3);
            await SendSerialCoreAsync("haptic interval 50", 3);
            await SendSerialCoreAsync("haptic raw02 on", 3);
            await SendSerialCoreAsync("haptic dryrun off", 3);

            FirmwarePackage package = EmbeddedAssets.EnsurePackage();
            string audioOutput = await RunProcessAsync(
                package.AudioSenderPath,
                "-DeviceName \"Wireless Controller\" -Pattern both_tick -DurationMs 350 -Intensity 28");
            AppendLog(audioOutput);
            await Task.Delay(300);

            string status = await SendSerialCoreAsync("haptic status", 3);
            long writes = ReadJsonCounter(status, "raw02_ble_writes");
            long livePackets = ReadJsonCounter(status, "raw02_live_packets");
            long errors = ReadJsonCounter(status, "raw02_ble_errors");
            if (writes <= 0 || livePackets <= 0 || errors != 0)
            {
                throw new InvalidOperationException(
                    $"完整链路未确认：BLE writes={writes}, live packets={livePackets}, errors={errors}。");
            }

            HapticStatus = $"传输通过：BLE writes={writes}，errors={errors}；请确认手柄是否有震感。";
            OverallStatus = "Confirm vibration";
            NextAction = "传输层已通过；物理震感必须由实际手感确认。";
            AppendLog("[SAFE_HAPTIC_TEST] result=transport_passed physical_confirmation=required ble_writes=" +
                      writes + " live_packets=" + livePackets + " errors=" + errors);
        }
        catch (Exception ex)
        {
            HapticStatus = "安全实震失败：" + FirstLine(ex.Message);
            OverallStatus = "Error";
            AppendLog("ERROR safe haptic test: " + ex);
        }
        finally
        {
            foreach (string command in new[] { "haptic test stop", "haptic dryrun on", "haptic raw02 off" })
            {
                try
                {
                    await SendSerialCoreAsync(command, 2);
                }
                catch (Exception cleanupError)
                {
                    AppendLog("WARN haptic cleanup: " + cleanupError.Message);
                }
            }
            Busy = false;
        }
    }

    private static long ReadJsonCounter(string text, string name)
    {
        MatchCollection matches = Regex.Matches(
            text,
            "\"" + Regex.Escape(name) + "\"\\s*:\\s*(\\d+)",
            RegexOptions.IgnoreCase);
        return matches.Count == 0 ? -1 : long.Parse(matches[^1].Groups[1].Value);
    }

    private static string ReadJsonString(string text, string name)
    {
        MatchCollection matches = Regex.Matches(
            text,
            "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        return matches.Count == 0 ? "" : matches[^1].Groups[1].Value;
    }

    private static string ShortHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "none";
        return text.Length <= 12 ? text : text[..12] + "...";
    }

    private static void RequireBleConnected(string statusText)
    {
        string ble = ReadJsonString(statusText, "ble");
        if (!string.Equals(ble, "connected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Pro2 BLE 尚未连接；已拒绝真实震动发送。");
        }
    }

    private async Task SendLiveHapticPulseAsync(string testName, string label)
    {
        try
        {
            Busy = true;
            OverallStatus = "Haptic pulse";
            string initial = await SendSerialCoreAsync("status", 3);
            RequireBleConnected(initial);

            await SendSerialCoreAsync("haptic test live " + testName, 4);
            await Task.Delay(testName.Equals("punch", StringComparison.OrdinalIgnoreCase) ? 220 : 160);
            await SendSerialCoreAsync("haptic test live stop", 3);

            string status = await SendSerialCoreAsync("status", 3);
            long writes = ReadJsonCounter(status, "raw02_ble_writes");
            long errors = ReadJsonCounter(status, "raw02_ble_errors");
            string left = ReadJsonString(status, "raw02_left");
            string right = ReadJsonString(status, "raw02_right");
            HapticStatus = $"{label} 已实震一次；BLE writes={writes}, errors={errors}, left={ShortHex(left)}, right={ShortHex(right)}";
            OverallStatus = errors == 0 ? "Haptic OK" : "Check haptic";
        }
        catch (Exception ex)
        {
            OverallStatus = "Error";
            HapticStatus = label + " 实震失败: " + FirstLine(ex.Message);
            AppendLog("ERROR haptic pulse: " + ex);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task TurnLiveOnAsync()
    {
        MessageBoxResult result = MessageBox.Show(owner,
            "这会把 haptic audio / raw02 真实转发到 BLE Pro2。请确认手柄已连接，先做过 Dry-run，并且不要长时间高强度测试。",
            "确认开启 Live raw02",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;
        await SendSerialAsync("haptic mode auto", 3, _ => { });
        await SendSerialAsync("haptic interval 20", 3, _ => { });
        await SendSerialAsync("haptic max 64", 3, _ => { });
        await SendSerialAsync("haptic raw02 on", 4, _ => { });
        await SendSerialAsync("haptic dryrun off", 4, _ => HapticStatus = "Live raw02 已开启，Dry-run 已关闭。");
    }

    private async Task TurnLiveOffAsync()
    {
        await SendSerialAsync("haptic test stop", 3, _ => { });
        await SendSerialAsync("haptic dryrun on", 3, _ => { });
        await SendSerialAsync("haptic raw02 off", 3, _ => HapticStatus = "Live raw02 已关闭，Dry-run 已开启。");
    }

    private string SummarizeHaptic(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "已请求 haptic status，未读取到输出。";
        string ble = ReadJsonString(output, "ble");
        string haptic = ReadJsonString(output, "haptic");
        string mode = ReadJsonString(output, "haptic_mode");
        string lastMode = ReadJsonString(output, "raw02_last_mode");
        string left = ReadJsonString(output, "raw02_left");
        string right = ReadJsonString(output, "raw02_right");
        long audioActive = ReadJsonCounter(output, "audio_active");
        long livePackets = ReadJsonCounter(output, "raw02_live_packets");
        long writes = ReadJsonCounter(output, "raw02_ble_writes");
        long errors = ReadJsonCounter(output, "raw02_ble_errors");
        if (!string.IsNullOrWhiteSpace(ble) || writes >= 0)
        {
            return $"BLE={ble}, haptic={haptic}, mode={mode}, audio_active={audioActive}, raw02_live={livePackets}, BLE writes={writes}, errors={errors}, last={lastMode}, L={ShortHex(left)}, R={ShortHex(right)}";
        }
        string compact = output.Replace("\r", " ").Replace("\n", " ");
        return compact.Length > 220 ? compact[..220] + "..." : compact;
    }

    private async Task<string> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动: " + fileName);
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = stdout + stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(output);
        return output;
    }

    private void StartShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("ERROR open " + target + ": " + ex.Message);
        }
    }

    private void SaveLog()
    {
        var dialog = new SaveFileDialog
        {
            FileName = "v5_5_manager_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log",
            Filter = "Log file|*.log|Text file|*.txt"
        };
        if (dialog.ShowDialog(owner) == true)
        {
            File.WriteAllText(dialog.FileName, log.ToString(), Encoding.UTF8);
            AppendLog("Saved log: " + dialog.FileName);
        }
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            log.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").AppendLine(line);
        }
        OnPropertyChanged(nameof(LogText));
    }

    private static string FirstLine(string text)
    {
        return text.Replace("\r", "").Split('\n')[0];
    }

    private static int ParseIntOrDefault(string text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, out int value))
        {
            value = fallback;
        }
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private sealed class MonitorSnapshot
    {
        public long AudioPackets { get; init; }
        public long AudioActive { get; init; }
        public long Raw02Live { get; init; }
        public long BleWrites { get; init; }
        public long BleErrors { get; init; }
        public long DroppedRate { get; init; }
        public long DroppedSilence { get; init; }
        public string Ble { get; init; } = "";
        public string Haptic { get; init; } = "";
        public string LastMode { get; init; } = "";
        public string Left { get; init; } = "";
        public string Right { get; init; } = "";
        public string Error { get; init; } = "";

        public static MonitorSnapshot FromStatus(string status)
        {
            return new MonitorSnapshot
            {
                AudioPackets = Counter(status, "audio_packets"),
                AudioActive = Counter(status, "audio_active"),
                Raw02Live = Counter(status, "raw02_live_packets"),
                BleWrites = Counter(status, "raw02_ble_writes"),
                BleErrors = Counter(status, "raw02_ble_errors"),
                DroppedRate = Counter(status, "raw02_dropped_rate"),
                DroppedSilence = Counter(status, "raw02_dropped_silence"),
                Ble = ReadJsonString(status, "ble"),
                Haptic = ReadJsonString(status, "haptic"),
                LastMode = ReadJsonString(status, "raw02_last_mode"),
                Left = ReadJsonString(status, "raw02_left"),
                Right = ReadJsonString(status, "raw02_right"),
                Error = ReadJsonString(status, "raw02_error")
            };
        }

        public MonitorDelta DeltaFrom(MonitorSnapshot previous)
        {
            return new MonitorDelta
            {
                AudioPackets = Math.Max(0, AudioPackets - previous.AudioPackets),
                AudioActive = Math.Max(0, AudioActive - previous.AudioActive),
                Raw02Live = Math.Max(0, Raw02Live - previous.Raw02Live),
                BleWrites = Math.Max(0, BleWrites - previous.BleWrites),
                BleErrors = Math.Max(0, BleErrors - previous.BleErrors),
                DroppedRate = Math.Max(0, DroppedRate - previous.DroppedRate),
                DroppedSilence = Math.Max(0, DroppedSilence - previous.DroppedSilence)
            };
        }

        public string ToLogString()
        {
            return "ble=" + Ble +
                   " haptic=" + Haptic +
                   " audio_packets=" + AudioPackets +
                   " audio_active=" + AudioActive +
                   " raw02_live=" + Raw02Live +
                   " ble_writes=" + BleWrites +
                   " ble_errors=" + BleErrors +
                   " dropped_rate=" + DroppedRate +
                   " dropped_silence=" + DroppedSilence +
                   " last=" + LastMode +
                   " left=" + ShortHex(Left) +
                   " right=" + ShortHex(Right) +
                   " error=" + Error;
        }

        private static long Counter(string status, string name)
        {
            return Math.Max(0, ReadJsonCounter(status, name));
        }
    }

    private sealed class MonitorDelta
    {
        public long AudioPackets { get; init; }
        public long AudioActive { get; init; }
        public long Raw02Live { get; init; }
        public long BleWrites { get; init; }
        public long BleErrors { get; init; }
        public long DroppedRate { get; init; }
        public long DroppedSilence { get; init; }

        public string ToLogString()
        {
            return "audio_packets=+" + AudioPackets +
                   " audio_active=+" + AudioActive +
                   " raw02_live=+" + Raw02Live +
                   " ble_writes=+" + BleWrites +
                   " ble_errors=+" + BleErrors +
                   " dropped_rate=+" + DroppedRate +
                   " dropped_silence=+" + DroppedSilence;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
