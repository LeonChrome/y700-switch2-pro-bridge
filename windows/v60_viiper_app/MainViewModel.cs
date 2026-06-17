using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Y700Switch2V60Viiper;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly StringBuilder log = new();
    private readonly Pro2BleInputSource inputSource = new();
    private readonly SessionLogWriter sessionLog = new();
    private readonly V60UserSettings userSettings = V60UserSettings.Load();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly SemaphoreSlim bleOperationGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCts = new();
    private CancellationTokenSource? autoReconnectCts;
    private Task? autoReconnectTask;
    private Process? viiperProcess;
    private int? localViiperApiPort;
    private ViiperBridgeSession? session;
    private string host = "127.0.0.1";
    private string port = "3242";
    private string status = "V6.2.0 VIIPER Windows-only 新和联胜版本已就绪。如未安装 usbip-win2，请先点击安装/修复。";
    private string inputStatus = "真实 Pro2 BLE 输入未连接。";
    private string selectedPushRateLabel = ViiperPushRateOption.Default.Label;
    private string selectedGyroModeLabel = ViiperGyroModeOption.Default.Label;
    private string selectedBackendLabel = VirtualBackendOption.Default.Label;
    private bool running;
    private bool busy;
    private bool shuttingDown;
    private bool autoReconnectEnabled;
    private string runtimeReadinessText = "";
    private double rumbleMultiplier = 1.0;
    private ViiperVirtualMode selectedMode = ViiperVirtualMode.DualSenseLike;
    private ViiperVirtualMode? activeMode;
    private const int LostInputTimeoutMilliseconds = 2000;

    public MainViewModel()
    {
        PingCommand = new RelayCommand(_ => RunExclusiveAsync("Ping VIIPER", PingAsync));
        InstallUsbipCommand = new RelayCommand(_ => RunExclusiveAsync("安装/修复 usbip-win2", InstallUsbipAsync));
        StartViiperServerCommand = new RelayCommand(_ => RunExclusiveAsync("启动本地 VIIPER", StartLocalViiperServerAsync));
        ScanPro2InputCommand = new RelayCommand(_ => RunExclusiveAsync("扫描 Pro2 BLE", ScanPro2InputAsync));
        ConnectPro2InputCommand = new RelayCommand(_ => RunExclusiveAsync("进入游戏", EnterGameAsync));
        DisconnectPro2InputCommand = new RelayCommand(_ => RunExclusiveAsync("断开 Pro2 BLE", DisconnectPro2InputAsync));
        StartDualSenseCommand = new RelayCommand(_ => RunExclusiveAsync(
            "切换 新和联胜 / PS5",
            token => SwitchModeAsync(ViiperDeviceProfile.DualSenseLike, token)));
        StartPro2Command = new RelayCommand(_ => RunExclusiveAsync(
            "切换 Pro2 / Nintendo",
            token => SwitchModeAsync(ViiperDeviceProfile.Pro2, token)));
        StartXboxCommand = new RelayCommand(_ => RunExclusiveAsync(
            "切换 Xbox / XInput",
            token => SwitchModeAsync(ViiperDeviceProfile.Xbox, token)));
        StopCommand = new RelayCommand(_ => RunExclusiveAsync("停止虚拟设备", _ => StopAsync()));
        ClearLogCommand = new RelayCommand(_ => ClearLog());
        rumbleMultiplier =
            V60UserSettings.NormalizeRumbleMultiplier(
                userSettings.RumbleMultiplier);
        selectedPushRateLabel =
            ViiperPushRateOption.FromLabel(userSettings.PushRateLabel).Label;
        selectedGyroModeLabel =
            ViiperGyroModeOption.FromLabel(userSettings.GyroModeLabel).Label;
        selectedBackendLabel =
            VirtualBackendOption.FromLabel(userSettings.BackendLabel).Label;
        inputSource.SetRumbleGain(rumbleMultiplier);
        RefreshRuntimeReadiness();
        AppendLog("[SESSION_LOG] " + sessionLog.FilePath);
        AppendLog("V6.2.0 说明：吸收 TommyWabg 路线的 BLE throughput / dynamic GATT 思路，并加入短链路后端档位。");
        AppendLog("[RUNTIME] " + RuntimeReadinessText);
        AppendLog("[RUMBLE_GAIN] multiplier=" + rumbleMultiplier.ToString("F1"));
        AppendLog("[LINK_TUNING] push_hz=" + SelectedPushRateOption.Hz.ToString("F1") +
                  " gyro_mode=" + SelectedGyroModeOption.Label +
                  " backend=" + SelectedBackendOption.Label);
    }

    public string Host
    {
        get => host;
        set { host = value; OnPropertyChanged(); }
    }

    public string Port
    {
        get => port;
        set { port = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => status;
        set { status = value; OnPropertyChanged(); }
    }

    public string InputStatus
    {
        get => inputStatus;
        set { inputStatus = value; OnPropertyChanged(); }
    }

    public bool Running
    {
        get => running;
        set
        {
            running = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanManageRuntime));
            OnPropertyChanged(nameof(ModeHeadline));
        }
    }

    public bool Busy
    {
        get => busy;
        private set
        {
            busy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanOperate));
            OnPropertyChanged(nameof(CanManageRuntime));
            OnPropertyChanged(nameof(CanManualBleControl));
        }
    }

    public bool CanStart => !Busy;
    public bool CanStop => Running && !Busy;
    public bool CanOperate => !Busy;
    public bool CanManageRuntime => !Running && !Busy;
    public bool CanManualBleControl => !Busy && !AutoReconnectEnabled;

    public bool AutoReconnectEnabled
    {
        get => autoReconnectEnabled;
        private set
        {
            if (autoReconnectEnabled == value)
            {
                return;
            }

            autoReconnectEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanManualBleControl));
            RaiseConnectionStateChanged();
        }
    }

    public string RuntimeReadinessText
    {
        get => runtimeReadinessText;
        private set
        {
            runtimeReadinessText = value;
            OnPropertyChanged();
        }
    }

    public double RumbleMultiplier
    {
        get => rumbleMultiplier;
        set
        {
            double normalized =
                V60UserSettings.NormalizeRumbleMultiplier(value);
            if (Math.Abs(rumbleMultiplier - normalized) < 0.001)
            {
                return;
            }

            rumbleMultiplier = normalized;
            inputSource.SetRumbleGain(normalized);
            userSettings.RumbleMultiplier = normalized;
            try
            {
                userSettings.Save();
            }
            catch (Exception ex)
            {
                AppendLog("[RUMBLE_GAIN] settings save warning: " + ex.Message);
            }
            AppendLog("[RUMBLE_GAIN] multiplier=" + normalized.ToString("F1"));
            OnPropertyChanged();
            OnPropertyChanged(nameof(RumbleMultiplierText));
        }
    }

    public string RumbleMultiplierText =>
        rumbleMultiplier.ToString("F1") + "x";

    public IReadOnlyList<string> PushRateChoices =>
        ViiperPushRateOption.All.Select(o => o.Label).ToArray();

    public IReadOnlyList<string> GyroModeChoices =>
        ViiperGyroModeOption.All.Select(o => o.Label).ToArray();

    public IReadOnlyList<string> BackendChoices =>
        VirtualBackendOption.All.Select(o => o.Label).ToArray();

    public string SelectedPushRate
    {
        get => selectedPushRateLabel;
        set
        {
            string normalized = ViiperPushRateOption.FromLabel(value).Label;
            if (selectedPushRateLabel == normalized)
            {
                return;
            }

            selectedPushRateLabel = normalized;
            userSettings.PushRateLabel = normalized;
            SaveUserSettings("[LINK_TUNING]");
            AppendLog("[LINK_TUNING] push_hz=" + SelectedPushRateOption.Hz.ToString("F1") +
                      " interval_ms=" + SelectedPushRateOption.Interval.TotalMilliseconds.ToString("F1") +
                      (Running ? " apply=next_session" : " apply=next_start"));
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public string SelectedGyroMode
    {
        get => selectedGyroModeLabel;
        set
        {
            string normalized = ViiperGyroModeOption.FromLabel(value).Label;
            if (selectedGyroModeLabel == normalized)
            {
                return;
            }

            selectedGyroModeLabel = normalized;
            userSettings.GyroModeLabel = normalized;
            SaveUserSettings("[LINK_TUNING]");
            AppendLog("[LINK_TUNING] gyro_mode=" + SelectedGyroModeOption.Label +
                      (Running ? " apply=next_session" : " apply=next_start"));
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public string SelectedBackend
    {
        get => selectedBackendLabel;
        set
        {
            string normalized = VirtualBackendOption.FromLabel(value).Label;
            if (selectedBackendLabel == normalized)
            {
                return;
            }

            selectedBackendLabel = normalized;
            userSettings.BackendLabel = normalized;
            SaveUserSettings("[BACKEND]");
            AppendLog("[BACKEND] selected=" + SelectedBackendOption.Label +
                      " mode=" + SelectedBackendOption.Mode +
                      (Running ? " apply=next_session" : " apply=next_start"));
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public string LinkTuningSummary =>
        "push=" + SelectedPushRateOption.Hz.ToString("F1") +
        "Hz · gyro=" + SelectedGyroModeOption.Label +
        " · backend=" + SelectedBackendOption.Label;

    public bool IsDualSenseSelected => selectedMode == ViiperVirtualMode.DualSenseLike;
    public bool IsPro2Selected => selectedMode == ViiperVirtualMode.Pro2;
    public bool IsXboxSelected => selectedMode == ViiperVirtualMode.Xbox;
    public bool IsDualSenseActive => Running && activeMode == ViiperVirtualMode.DualSenseLike;
    public bool IsPro2Active => Running && activeMode == ViiperVirtualMode.Pro2;
    public bool IsXboxActive => Running && activeMode == ViiperVirtualMode.Xbox;
    public bool IsInputConnected =>
        inputSource.IsRunning &&
        inputSource.TryGetLatest(out _, out TimeSpan age) &&
        age <= TimeSpan.FromMilliseconds(LostInputTimeoutMilliseconds);
    public string BleButtonText => IsInputConnected
        ? "PRO2 已连接 · 自动守护中"
        : AutoReconnectEnabled
            ? "正在寻找 PRO2 · 自动重连中"
            : "连接 PRO2 · 进入游戏";
    public string BleStateText => IsInputConnected
        ? inputSource.IsPerformanceDegraded
            ? "BLE LIVE · DEGRADED · " + inputSource.LinkRateClass.ToUpperInvariant()
            : "BLE LIVE · INPUT ONLINE · " + inputSource.LinkRateClass.ToUpperInvariant()
        : AutoReconnectEnabled
            ? "BLE SEARCHING · AUTO RECONNECT"
            : "BLE STANDBY · WAITING FOR PLAYER";
    public string SelectedModeLabel => SelectedProfile.Label;
    public string SelectedHeroName => selectedMode switch
    {
        ViiperVirtualMode.DualSenseLike => "KRATOS",
        ViiperVirtualMode.Pro2 => "MARIO",
        ViiperVirtualMode.Xbox => "MASTER CHIEF",
        _ => ""
    };
    public string SelectedModeSubtitle => selectedMode switch
    {
        ViiperVirtualMode.DualSenseLike => "新和联胜 · DUALSENSE IDENTITY · 054C:0CE6",
        ViiperVirtualMode.Pro2 => "NINTENDO PROTOCOL · HD RUMBLE · 057E:2069",
        ViiperVirtualMode.Xbox => "XINPUT PROTOCOL · 045E:028E",
        _ => ""
    };
    public string ModeHeadline => Running && activeMode.HasValue
        ? "ACTIVE LOADOUT · " + ActiveProfile.Label
        : "SELECTED LOADOUT · " + SelectedModeLabel;

    public string LogText => log.ToString();

    public ICommand PingCommand { get; }
    public ICommand InstallUsbipCommand { get; }
    public ICommand StartViiperServerCommand { get; }
    public ICommand ScanPro2InputCommand { get; }
    public ICommand ConnectPro2InputCommand { get; }
    public ICommand DisconnectPro2InputCommand { get; }
    public ICommand StartDualSenseCommand { get; }
    public ICommand StartPro2Command { get; }
    public ICommand StartXboxCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearLogCommand { get; }

    private async Task PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            string response = await PingCoreAsync(cancellationToken);
            Status = "VIIPER server 已响应。";
            AppendLog("[PING] " + response);
        }
        catch (Exception ex)
        {
            Status = "VIIPER server 未就绪：" + FirstLine(ex.Message);
            AppendLog("ERROR ping: " + ex);
        }
    }

    private async Task<string> PingCoreAsync(CancellationToken cancellationToken)
    {
        var client = new ViiperProtocolClient(Host, ParsePort());
        return await client.PingAsync(cancellationToken);
    }

    private async Task StartLocalViiperServerAsync(CancellationToken cancellationToken)
    {
        int apiPort = ParsePort();
        if (!IsLoopbackHost(Host))
        {
            throw new InvalidOperationException("启动本地 VIIPER 时 Host 必须是 localhost、127.0.0.1 或 ::1。");
        }

        DisposeExitedViiperProcess();
        if (viiperProcess is { HasExited: false })
        {
            if (localViiperApiPort.HasValue && localViiperApiPort.Value != apiPort)
            {
                Port = localViiperApiPort.Value.ToString();
                apiPort = localViiperApiPort.Value;
                AppendLog("[VIIPER_SERVER] restored active local API port " + apiPort + ".");
            }
            Status = "本地 VIIPER server 已在运行，pid=" + viiperProcess.Id;
            AppendLog("[VIIPER_SERVER] already_running pid=" + viiperProcess.Id);
            string response = await PingCoreAsync(cancellationToken);
            AppendLog("[PING] " + response);
            return;
        }

        string? existingResponse = null;
        try
        {
            existingResponse = await PingCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        if (existingResponse != null)
        {
            if (!LooksLikeViiperPing(existingResponse))
            {
                throw new InvalidOperationException("端口 " + apiPort + " 已被非 VIIPER 服务占用。");
            }
            Status = "检测到已有 VIIPER server，直接复用 " + Host.Trim() + ":" + apiPort + "。";
            AppendLog("[VIIPER_SERVER] using existing server " + existingResponse);
            AppendLog("[PING] " + existingResponse);
            return;
        }

        string? exe = FindLocalViiperExe();
        if (exe == null)
        {
            Status = "没有找到本地 VIIPER server，且内置 runtime 释放失败。请确认 EXE 完整。";
            AppendLog("ERROR viiper server: local runtime not found and embedded extraction failed.");
            return;
        }

        UsbipRuntime? usbip = UsbipRuntimeLocator.Find();
        if (usbip == null)
        {
            UsbipInstaller? installer = UsbipRuntimeLocator.FindBundledInstaller();
            Status = installer == null
                ? "未找到 usbip-win2 的 usbip.exe，也没有找到随包安装器。请确认发布包完整。"
                : "未安装 usbip-win2。请点击“安装/修复 usbip-win2”，完成后再启动本地 VIIPER。";
            AppendLog("[USBIP] usbip.exe not found. VIIPER can answer ping without it, but all three modes will fail during auto-attach.");
            if (installer != null)
            {
                AppendLog("[USBIP] bundled installer available: " + installer.InstallerPath);
            }
            else
            {
                AppendLog("[USBIP] bundled installer missing. Keep usbip-win2 next to the EXE under usbip-win2\\" + UsbipRuntimeLocator.BundledVersion + ".");
            }
            RefreshRuntimeReadiness();
            throw new InvalidOperationException(Status);
        }
        UsbipProbeResult usbipProbe = await UsbipRuntimeLocator.ProbeAsync(
            usbip,
            cancellationToken);
        if (!usbipProbe.Ready)
        {
            Status = "已找到 usbip.exe，但内核驱动尚未就绪。请点击“安装/修复 USBIP”，完成后重试。";
            AppendLog("[USBIP] driver probe failed: " + usbipProbe.Detail);
            throw new InvalidOperationException(Status);
        }

        string logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRO2WirelessReceiverControlBoard",
            "v6_logs");
        Directory.CreateDirectory(logRoot);
        string logPath = Path.Combine(logRoot, "viiper_server_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
        int usbPort = apiPort == 3242
            ? 3241
            : apiPort == 1
                ? 2
                : apiPort - 1;
        string args = "server --api.addr=127.0.0.1:" + apiPort +
                      " --usb.addr=127.0.0.1:" + usbPort +
                      " --api.device-handler-connect-timeout=60s" +
                      " --usb.write-batch-flush-interval=0ms" +
                      " --update-notify=none" +
                      " --log.file=\"" + logPath + "\"";
        ProcessStartInfo startInfo = new()
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.Environment["PATH"] = UsbipRuntimeLocator.BuildPathWithUsbipDirectory(
            startInfo.Environment.TryGetValue("PATH", out string? path) ? path ?? "" : Environment.GetEnvironmentVariable("PATH") ?? "",
            usbip);
        startInfo.WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;

        viiperProcess = Process.Start(startInfo);
        if (viiperProcess == null)
        {
            throw new InvalidOperationException("无法启动 VIIPER server。");
        }
        localViiperApiPort = apiPort;

        Status = "正在启动本地 VIIPER server，pid=" + viiperProcess.Id;
        AppendLog("[USBIP] using " + usbip.ExePath);
        AppendLog("[VIIPER_SERVER] started pid=" + viiperProcess.Id +
                  " api=127.0.0.1:" + apiPort +
                  " usb=127.0.0.1:" + usbPort +
                  " exe=" + exe +
                  " args=" + args +
                  " log=" + logPath);
        await Task.Delay(1000, cancellationToken);

        if (viiperProcess.HasExited)
        {
            int exitCode = viiperProcess.ExitCode;
            viiperProcess.Dispose();
            viiperProcess = null;
            localViiperApiPort = null;
            throw new InvalidOperationException(
                "VIIPER server 启动后立即退出，exit=" + exitCode + "。日志：" + logPath);
        }

        string ping = await PingCoreAsync(cancellationToken);
        Status = "本地 VIIPER server 已就绪，pid=" + viiperProcess.Id + "。";
        AppendLog("[PING] " + ping);
    }

    private async Task InstallUsbipAsync(CancellationToken cancellationToken)
    {
        UsbipInstaller? installer = UsbipRuntimeLocator.FindBundledInstaller();
        if (installer == null)
        {
            Status = "没有找到随包 usbip-win2 安装器。请确认发布包完整。";
            AppendLog("[USBIP] bundled installer not found: " + UsbipRuntimeLocator.InstallerFileName);
            return;
        }

        UsbipRuntime? existing = UsbipRuntimeLocator.Find();
        if (existing != null)
        {
            AppendLog("[USBIP] existing usbip.exe found: " + existing.ExePath);
            AppendLog("[USBIP] launching bundled installer anyway for repair/update.");
        }

        Status = "正在启动 usbip-win2 安装器，请在 UAC/安装向导中确认。安装期间 USB 设备可能会短暂重启。";
        AppendLog("[USBIP] installer=" + installer.InstallerPath);
        if (!string.IsNullOrWhiteSpace(installer.LicensePath))
        {
            AppendLog("[USBIP] license=" + installer.LicensePath);
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = installer.InstallerPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(installer.InstallerPath) ?? AppContext.BaseDirectory
            });

            if (process == null)
            {
                Status = "未能启动 usbip-win2 安装器。";
                AppendLog("[USBIP] installer process returned null.");
                return;
            }

            await process.WaitForExitAsync(cancellationToken);
            AppendLog("[USBIP] installer exited code=" + process.ExitCode);
            await Task.Delay(1000, cancellationToken);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status = "已取消 usbip-win2 安装。";
            AppendLog("[USBIP] installer cancelled by user.");
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Status = "启动 usbip-win2 安装器失败：" + FirstLine(ex.Message);
            AppendLog("ERROR usbip installer: " + ex);
            return;
        }

        UsbipRuntime? runtime = UsbipRuntimeLocator.Find();
        if (runtime != null)
        {
            UsbipProbeResult probe = await UsbipRuntimeLocator.ProbeAsync(
                runtime,
                cancellationToken);
            if (probe.Ready)
            {
                Status = "usbip-win2 已就绪：" + runtime.ExePath;
                AppendLog("[USBIP] ready " + runtime.ExePath + " / " + probe.Detail);
            }
            else
            {
                Status = "安装器已结束，但 USBIP 内核驱动检测未通过。若安装器要求重启，请重启 Windows 后再试。";
                AppendLog("[USBIP] driver probe failed after installer: " + probe.Detail);
            }
        }
        else
        {
            Status = "usbip-win2 安装器已结束，但还没找到 usbip.exe。若安装器提示重启，请重启后再打开本 EXE。";
            AppendLog("[USBIP] usbip.exe still not found after installer exit; reboot may be required.");
        }
        RefreshRuntimeReadiness();
    }

    private async Task ScanPro2InputAsync(CancellationToken cancellationToken)
    {
        await bleOperationGate.WaitAsync(cancellationToken);
        try
        {
            Status = "正在扫描 Pro2 BLE，窗口仍可正常响应，请等待约 8 秒。";
            InputStatus = "正在扫描未配对的 Pro2 BLE 广播...";
            var progress = new Progress<string>(AppendLog);
            var candidates = await inputSource.ScanAsync(progress, TimeSpan.FromSeconds(8), cancellationToken);
            if (candidates.Count == 0)
            {
                InputStatus = "没有扫描到真实 Pro2 BLE。唤醒手柄并确保没有被 ESP32、Switch、手机或旧进程占用。";
                AppendLog("[PRO2_BLE] scan none");
                return;
            }

            InputStatus = "发现 " + candidates.Count + " 个 Pro2 BLE 候选。";
            foreach (string candidate in candidates)
            {
                AppendLog("[PRO2_BLE] candidate " + candidate);
            }
        }
        finally
        {
            bleOperationGate.Release();
        }
    }

    private async Task TryConnectPro2InputOnceAsync(CancellationToken cancellationToken)
    {
        Status = "正在扫描并连接 Pro2 BLE；将依次验证 GATT、初始化命令和 live 输入。";
        InputStatus = "正在连接 Pro2 BLE...";
        var progress = new Progress<string>(AppendLog);
        await bleOperationGate.WaitAsync(cancellationToken);
        try
        {
            await inputSource.StartAsync(progress, cancellationToken);
        }
        finally
        {
            bleOperationGate.Release();
        }
        InputStatus = inputSource.Status;
        if (inputSource.IsRunning)
        {
            Status = inputSource.IsPerformanceDegraded
                ? "真实 Pro2 BLE 已接入并保持可用，但当前通知速率低于 66.7 Hz 目标；不会再强制断开。"
                : Running
                    ? "真实 Pro2 BLE 已接入，当前虚拟模式将自动切换为 live 输入和 rumble 写回。"
                    : "真实 Pro2 BLE 已连接，可以启动任一虚拟模式。";
        }
        else
        {
            Status = "真实 Pro2 BLE 未连接。请唤醒手柄，并确认没有被其他设备占用。";
        }
        RaiseConnectionStateChanged();
    }

    private async Task DisconnectPro2InputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopAutoReconnectAsync();
        await bleOperationGate.WaitAsync(cancellationToken);
        try
        {
            await inputSource.StopAsync();
        }
        finally
        {
            bleOperationGate.Release();
        }
        InputStatus = inputSource.Status;
        RaiseConnectionStateChanged();
        if (Running)
        {
            Status = "已停止自动重连并断开真实 Pro2；虚拟设备继续运行 neutral 输入。再次点击“进入游戏”即可恢复持续连接。";
        }
    }

    private async Task EnterGameAsync(CancellationToken cancellationToken)
    {
        if (!Running || activeMode != selectedMode)
        {
            await SwitchModeAsync(SelectedProfile, cancellationToken);
        }

        if (!Running)
        {
            Status = "当前虚拟模式启动失败，尚未进入自动连接。请根据上方提示修复 USBIP/VIIPER 后重试。";
            return;
        }

        StartAutoReconnect();
    }

    private async Task SwitchModeAsync(
        ViiperDeviceProfile profile,
        CancellationToken cancellationToken)
    {
        SelectMode(profile.Mode);
        if (Running && activeMode == profile.Mode)
        {
            Status = profile.Label + " 已经是当前角色。点击“连接 PRO2 · 进入游戏”接入真实手柄。";
            return;
        }

        await EnsureViiperReadyAsync(cancellationToken);
        if (Running)
        {
            AppendLog("[MODE_SWITCH] from=" + ActiveProfile.Label + " to=" + profile.Label);
            await StopSessionAsync(updateStatus: false);
            await Task.Delay(650, cancellationToken);
        }

        await StartAsync(profile, cancellationToken);
    }

    private async Task EnsureViiperReadyAsync(CancellationToken cancellationToken)
    {
        if (IsLoopbackHost(Host))
        {
            UsbipRuntime? runtime = UsbipRuntimeLocator.Find();
            UsbipProbeResult? probe = runtime == null
                ? null
                : await UsbipRuntimeLocator.ProbeAsync(runtime, cancellationToken);
            if (runtime == null || probe is not { Ready: true })
            {
                Status = runtime == null
                    ? "首次运行需要安装 USBIP 内核驱动，正在打开内置安装向导。完成后将继续启动所选角色。"
                    : "USBIP 程序存在但内核驱动未就绪，正在打开内置修复向导。";
                AppendLog("[FIRST_RUN] usbip preflight failed: " +
                          (probe?.Detail ?? "usbip.exe not found") +
                          "; launching embedded installer.");
                await InstallUsbipAsync(cancellationToken);
                runtime = UsbipRuntimeLocator.Find();
                if (runtime == null)
                {
                    throw new InvalidOperationException(
                        "USBIP 驱动尚未就绪。请完成安装向导；若安装器要求重启，请重启 Windows 后再次选择角色。");
                }

                probe = await UsbipRuntimeLocator.ProbeAsync(runtime, cancellationToken);
                if (!probe.Ready)
                {
                    throw new InvalidOperationException(
                        "USBIP 内核驱动检测未通过：" + probe.Detail +
                        "。若刚完成安装，请重启 Windows 后再次选择角色。");
                }
            }
        }

        try
        {
            await PingCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch when (IsLoopbackHost(Host))
        {
            await StartLocalViiperServerAsync(cancellationToken);
            await PingCoreAsync(cancellationToken);
        }
    }

    private void StartAutoReconnect()
    {
        if (autoReconnectTask is { IsCompleted: false })
        {
            Status = IsInputConnected
                ? "Pro2 已连接，自动重连守护正在运行。"
                : "自动连接已在持续寻找 Pro2，请保持手柄唤醒。";
            return;
        }

        autoReconnectCts?.Dispose();
        autoReconnectCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        AutoReconnectEnabled = true;
        InputStatus = "自动连接已启动：将持续扫描最匹配的 Pro2，并在断联后自动恢复。";
        Status = "已进入游戏连接流程。无需重复点击；唤醒 Pro2 后程序会持续尝试，断联也会自动重连。";
        AppendLog("[PRO2_AUTO] enabled stale_timeout_ms=" + LostInputTimeoutMilliseconds);
        autoReconnectTask = AutoReconnectLoopAsync(autoReconnectCts.Token);
    }

    private async Task AutoReconnectLoopAsync(CancellationToken cancellationToken)
    {
        int attempt = 0;
        bool previouslyLive = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool live = inputSource.IsRunning &&
                            inputSource.TryGetLatest(out _, out TimeSpan age) &&
                            age <= TimeSpan.FromMilliseconds(LostInputTimeoutMilliseconds);
                if (live)
                {
                    if (!previouslyLive)
                    {
                        previouslyLive = true;
                        InputStatus = inputSource.Status;
                        Status = "真实 Pro2 BLE 已连接。自动守护会在意外断联后继续寻找并恢复连接。";
                        AppendLog("[PRO2_AUTO] live; reconnect guard active.");
                        RaiseConnectionStateChanged();
                    }

                    await Task.Delay(750, cancellationToken);
                    continue;
                }

                if (inputSource.IsRunning)
                {
                    previouslyLive = false;
                    TimeSpan staleAge = inputSource.TryGetLatest(out _, out TimeSpan measuredAge)
                        ? measuredAge
                        : TimeSpan.MaxValue;
                    AppendLog("[PRO2_AUTO] input stale age_ms=" +
                              (staleAge == TimeSpan.MaxValue ? "unknown" : staleAge.TotalMilliseconds.ToString("F0")) +
                              "; recycling BLE session.");
                    InputStatus = "检测到 Pro2 输入中断，正在清理旧连接并自动重连...";
                    await bleOperationGate.WaitAsync(cancellationToken);
                    try
                    {
                        await inputSource.StopAsync();
                    }
                    finally
                    {
                        bleOperationGate.Release();
                    }
                    RaiseConnectionStateChanged();
                }

                attempt++;
                previouslyLive = false;
                InputStatus = "自动连接第 " + attempt + " 次：正在扫描最匹配的 Pro2，请保持手柄唤醒...";
                Status = "自动重连持续运行中。没有找到手柄时会等待片刻后继续，无需手动重试。";
                AppendLog("[PRO2_AUTO] attempt=" + attempt + " begin.");
                try
                {
                    await TryConnectPro2InputOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppendLog("[PRO2_AUTO] attempt=" + attempt + " failed: " + ex);
                    InputStatus = "本轮连接异常：" + FirstLine(ex.Message) + "。3 秒后自动继续。";
                    Status = "Pro2 自动重连仍在运行，瞬时 BLE/GATT 错误不会停止守护。";
                    await Task.Delay(3000, cancellationToken);
                    continue;
                }
                RaiseConnectionStateChanged();

                if (!inputSource.IsRunning)
                {
                    InputStatus = "本轮未连接到 Pro2，2.5 秒后自动继续扫描。可在手动控制区停止自动重连。";
                    Status = "自动重连持续运行中。本轮没有找到可用 Pro2，将自动开始下一轮。";
                    AppendLog("[PRO2_AUTO] attempt=" + attempt + " no_live; retry_delay_ms=2500.");
                    await Task.Delay(2500, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppendLog("[PRO2_AUTO] cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR Pro2 auto reconnect: " + ex);
            InputStatus = "自动重连遇到异常，将在 3 秒后由用户重新进入游戏恢复：" + FirstLine(ex.Message);
            Status = "Pro2 自动重连异常停止：" + FirstLine(ex.Message);
        }
        finally
        {
            if (!shuttingDown && !cancellationToken.IsCancellationRequested)
            {
                AutoReconnectEnabled = false;
            }
            RaiseConnectionStateChanged();
        }
    }

    private async Task StopAutoReconnectAsync()
    {
        CancellationTokenSource? reconnectCts = autoReconnectCts;
        Task? reconnectTask = autoReconnectTask;
        autoReconnectCts = null;
        autoReconnectTask = null;
        AutoReconnectEnabled = false;
        if (reconnectCts != null)
        {
            reconnectCts.Cancel();
        }
        if (reconnectTask != null)
        {
            try
            {
                await reconnectTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        reconnectCts?.Dispose();
    }

    private async Task StartAsync(ViiperDeviceProfile profile, CancellationToken cancellationToken)
    {
        if (Running)
        {
            return;
        }

        try
        {
            bool inputLive = inputSource.IsRunning &&
                             inputSource.TryGetLatest(out _, out TimeSpan inputAge) &&
                             inputAge <= TimeSpan.FromMilliseconds(500);
            Running = true;
            Status = "正在启动 " + profile.Label + " 虚拟手柄...";
            ViiperPushRateOption pushRate = SelectedPushRateOption;
            ViiperGyroModeOption gyro = SelectedGyroModeOption;
            ViiperDeviceProfile runtimeProfile = profile with { SendInterval = pushRate.Interval };
            AppendLog("[START] mode=" + runtimeProfile.Label +
                      " type=" + runtimeProfile.DeviceType +
                      " push_hz=" + pushRate.Hz.ToString("F1") +
                      " interval_ms=" + pushRate.Interval.TotalMilliseconds.ToString("F1") +
                      " gyro_mode=" + gyro.Label +
                      " backend=" + SelectedBackendOption.Label +
                      " flush=immediate");
            ResolveExperimentalBackendSelection(runtimeProfile);
            var progress = new Progress<string>(AppendLog);
            ViiperBridgeSession? createdSession = null;
            var faultProgress = new Progress<Exception>(
                ex =>
                {
                    if (createdSession != null)
                    {
                        _ = RecoverFaultedSessionAsync(createdSession, ex);
                    }
                });
            createdSession = new ViiperBridgeSession(
                new ViiperProtocolClient(Host, ParsePort()),
                runtimeProfile,
                progress,
                inputSource,
                inputSource,
                faultProgress,
                gyro.Mode);
            session = createdSession;
            await createdSession.StartAsync(cancellationToken);
            SetActiveMode(runtimeProfile.Mode);
            Status = runtimeProfile.Label + " 虚拟设备已连接。当前输入源：" +
                (inputLive
                    ? "Pro2 BLE live，rumble 写回已启用"
                    : "neutral；稍后点击“连接 Pro2 BLE”即可在不中断虚拟设备的情况下切换为 live 输入") + "。";
        }
        catch (Exception ex)
        {
            string failure = "启动失败：" + ExplainStartFailure(ex);
            AppendLog("ERROR start: " + ex);
            await StopSessionAsync(updateStatus: false);
            Status = failure;
        }
    }

    private void ResolveExperimentalBackendSelection(ViiperDeviceProfile profile)
    {
        VirtualBackendOption backend = SelectedBackendOption;
        if (backend.Mode == VirtualBackendMode.ViiperServer)
        {
            AppendLog("[BACKEND] active=viiper_server reason=stable_three_mode_haptic_path");
            return;
        }

        string libPath = FindLibViiperCandidate() ?? "";
        string reason = backend.Mode switch
        {
            VirtualBackendMode.LibViiperExperimental when string.IsNullOrWhiteSpace(libPath) =>
                "libVIIPER.dll not found in app/runtime folders",
            VirtualBackendMode.LibViiperExperimental =>
                "libVIIPER.dll found at " + libPath + " but V6.2 has not bound DualSense/NS2Pro three-mode entrypoints yet",
            VirtualBackendMode.EmbeddedUsbipExperimental =>
                "C# embedded USBIP server is scaffolded as the V6.2 research target, but the release build keeps VIIPER for PS5 HD haptic safety",
            _ => "unknown experimental backend"
        };
        AppendLog("[BACKEND] requested=" + backend.Label +
                  " mode=" + backend.Mode +
                  " profile=" + profile.DeviceType +
                  " active=viiper_server fallback_reason=\"" + reason + "\"");
    }

    private static string? FindLibViiperCandidate()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "libVIIPER.dll"),
            Path.Combine(baseDir, "runtime", "libVIIPER.dll"),
            Path.Combine(baseDir, "tools", "viiper", "libVIIPER.dll"),
            Path.Combine(Environment.CurrentDirectory, "tools", "viiper", "haptic-src", "dist", "libVIIPER", "libVIIPER.dll")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task StopAsync()
    {
        await StopSessionAsync(updateStatus: true);
    }

    private async Task StopSessionAsync(bool updateStatus)
    {
        ViiperBridgeSession? active = session;
        session = null;
        if (active != null)
        {
            await active.DisposeAsync();
        }
        Running = false;
        SetActiveMode(null);
        if (updateStatus)
        {
            Status = "已停止虚拟设备。当前角色仍保持选中，可随时重新部署。";
        }
    }

    private async Task RecoverFaultedSessionAsync(ViiperBridgeSession failedSession, Exception error)
    {
        if (shuttingDown)
        {
            return;
        }

        await operationGate.WaitAsync();
        Busy = true;
        try
        {
            if (!ReferenceEquals(session, failedSession))
            {
                return;
            }

            ViiperDeviceProfile restartProfile = failedSession.Profile;
            bool keepAutoReconnect = AutoReconnectEnabled;
            session = null;
            await failedSession.DisposeAsync();
            Running = false;
            SetActiveMode(null);
            Status = "VIIPER 数据流异常，正在自动恢复 " + restartProfile.Label + "...";
            AppendLog("ERROR session stream: " + error);

            try
            {
                await EnsureViiperReadyAsync(lifetimeCts.Token);
                await StartAsync(restartProfile, lifetimeCts.Token);
                if (Running && activeMode == restartProfile.Mode)
                {
                    Status = restartProfile.Label + " 已从 VIIPER 数据流异常中自动恢复。";
                    AppendLog("[VIIPER_RECOVERY] restarted mode=" + restartProfile.Label);
                    if (keepAutoReconnect)
                    {
                        StartAutoReconnect();
                    }
                }
                else
                {
                    Status = "VIIPER 数据流异常后自动恢复未完成，请查看日志并重试当前角色。";
                    AppendLog("[VIIPER_RECOVERY] restart did not reach running state.");
                }
            }
            catch (Exception restartError)
            {
                Status = "VIIPER 数据流异常，自动恢复失败：" + FirstLine(restartError.Message);
                AppendLog("ERROR session recovery: " + restartError);
            }
        }
        catch (Exception cleanupError)
        {
            Running = false;
            SetActiveMode(null);
            Status = "VIIPER 数据流异常，清理失败：" + FirstLine(cleanupError.Message);
            AppendLog("ERROR session cleanup: " + cleanupError);
        }
        finally
        {
            Busy = false;
            operationGate.Release();
        }
    }

    public async Task ShutdownAsync()
    {
        if (shuttingDown)
        {
            return;
        }

        shuttingDown = true;
        lifetimeCts.Cancel();
        await operationGate.WaitAsync();
        Busy = true;
        try
        {
            try
            {
                await StopAutoReconnectAsync();
            }
            catch (Exception ex)
            {
                AppendLog("[SHUTDOWN] auto reconnect cleanup warning: " + ex.Message);
            }

            try
            {
                await StopAsync();
            }
            catch (Exception ex)
            {
                AppendLog("[SHUTDOWN] virtual device cleanup warning: " + ex.Message);
            }

            try
            {
                await inputSource.DisposeAsync();
            }
            catch (Exception ex)
            {
                AppendLog("[SHUTDOWN] Pro2 BLE cleanup warning: " + ex.Message);
            }

            if (viiperProcess != null)
            {
                try
                {
                    if (!viiperProcess.HasExited)
                    {
                        viiperProcess.Kill(entireProcessTree: true);
                        await viiperProcess.WaitForExitAsync();
                        AppendLog("[VIIPER_SERVER] stopped local server.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("[VIIPER_SERVER] stop warning: " + ex.Message);
                }
                finally
                {
                    viiperProcess.Dispose();
                    viiperProcess = null;
                    localViiperApiPort = null;
                }
            }
            AppendLog("[SHUTDOWN] complete.");
        }
        finally
        {
            Busy = false;
            operationGate.Release();
            bleOperationGate.Dispose();
            lifetimeCts.Dispose();
            await sessionLog.DisposeAsync();
        }
    }

    private static string? FindLocalViiperExe()
    {
        string relative = Path.Combine(
            "tools",
            "viiper",
            "haptic-v0.8.0",
            "viiper-haptic.exe");
        string? cursor = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(cursor); i++)
        {
            string candidate = Path.GetFullPath(Path.Combine(cursor, relative));
            if (File.Exists(candidate))
            {
                return candidate;
            }
            cursor = Directory.GetParent(cursor)?.FullName;
        }

        string cwdCandidate = Path.GetFullPath(relative);
        if (File.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        return ExtractEmbeddedViiperRuntime();
    }

    private static string? ExtractEmbeddedViiperRuntime()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRO2WirelessReceiverControlBoard",
            "embedded",
            "v6.2.0",
            "viiper",
            "haptic-v0.8.0");
        Directory.CreateDirectory(root);
        string exe = Path.Combine(root, "viiper-haptic.exe");
        string licenses = Path.Combine(root, "LICENSE.txt");

        if (!ExtractResourceIfAvailable(
                assembly,
                "Embedded.viiper.haptic.exe",
                exe))
        {
            return null;
        }
        ExtractResourceIfAvailable(
            assembly,
            "Embedded.viiper.haptic.license",
            licenses);
        return exe;
    }

    private static bool ExtractResourceIfAvailable(Assembly assembly, string resourceName, string destination)
    {
        using Stream? source = assembly.GetManifestResourceStream(resourceName);
        if (source == null)
        {
            return false;
        }

        byte[] embeddedHash = SHA256.HashData(source);
        source.Position = 0;
        if (File.Exists(destination))
        {
            using FileStream existing = File.OpenRead(destination);
            byte[] existingHash = SHA256.HashData(existing);
            if (CryptographicOperations.FixedTimeEquals(embeddedHash, existingHash))
            {
                return true;
            }
        }

        string temp = destination + ".tmp";
        using (FileStream output = File.Create(temp))
        {
            source.CopyTo(output);
        }
        File.Move(temp, destination, overwrite: true);
        return true;
    }

    private int ParsePort()
    {
        if (!int.TryParse(Port, out int parsed) || parsed is < 1 or > 65535)
        {
            throw new InvalidOperationException("VIIPER Port 必须是 1 到 65535 之间的整数。");
        }

        return parsed;
    }

    private async Task RunExclusiveAsync(
        string operation,
        Func<CancellationToken, Task> action)
    {
        if (shuttingDown)
        {
            return;
        }

        if (!await operationGate.WaitAsync(0))
        {
            Status = "已有操作正在进行，请等待当前步骤完成。";
            AppendLog("[BUSY] rejected operation=" + operation);
            return;
        }

        Busy = true;
        try
        {
            await action(lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            AppendLog("[CANCELLED] operation=" + operation);
        }
        catch (Exception ex)
        {
            Status = operation + "失败：" + FirstLine(ex.Message);
            AppendLog("ERROR operation " + operation + ": " + ex);
        }
        finally
        {
            Busy = false;
            operationGate.Release();
        }
    }

    private void DisposeExitedViiperProcess()
    {
        if (viiperProcess is not { HasExited: true })
        {
            return;
        }

        AppendLog("[VIIPER_SERVER] previous process exited code=" + viiperProcess.ExitCode);
        viiperProcess.Dispose();
        viiperProcess = null;
        localViiperApiPort = null;
    }

    private static bool IsLoopbackHost(string value)
    {
        string candidate = (value ?? "").Trim();
        if (candidate.Length == 0 ||
            candidate.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(candidate, out IPAddress? address) &&
               IPAddress.IsLoopback(address);
    }

    private static bool LooksLikeViiperPing(string response)
    {
        return response.Contains("\"server\":\"VIIPER\"", StringComparison.OrdinalIgnoreCase);
    }

    private ViiperDeviceProfile SelectedProfile => ProfileFor(selectedMode);
    private ViiperDeviceProfile ActiveProfile => ProfileFor(activeMode ?? selectedMode);
    private ViiperPushRateOption SelectedPushRateOption =>
        ViiperPushRateOption.FromLabel(selectedPushRateLabel);
    private ViiperGyroModeOption SelectedGyroModeOption =>
        ViiperGyroModeOption.FromLabel(selectedGyroModeLabel);
    private VirtualBackendOption SelectedBackendOption =>
        VirtualBackendOption.FromLabel(selectedBackendLabel);

    private static ViiperDeviceProfile ProfileFor(ViiperVirtualMode mode)
    {
        return mode switch
        {
            ViiperVirtualMode.DualSenseLike => ViiperDeviceProfile.DualSenseLike,
            ViiperVirtualMode.Pro2 => ViiperDeviceProfile.Pro2,
            ViiperVirtualMode.Xbox => ViiperDeviceProfile.Xbox,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private void SelectMode(ViiperVirtualMode mode)
    {
        if (selectedMode == mode)
        {
            return;
        }

        selectedMode = mode;
        OnPropertyChanged(nameof(IsDualSenseSelected));
        OnPropertyChanged(nameof(IsPro2Selected));
        OnPropertyChanged(nameof(IsXboxSelected));
        OnPropertyChanged(nameof(SelectedModeLabel));
        OnPropertyChanged(nameof(SelectedHeroName));
        OnPropertyChanged(nameof(SelectedModeSubtitle));
        OnPropertyChanged(nameof(ModeHeadline));
    }

    private void SetActiveMode(ViiperVirtualMode? mode)
    {
        activeMode = mode;
        OnPropertyChanged(nameof(IsDualSenseActive));
        OnPropertyChanged(nameof(IsPro2Active));
        OnPropertyChanged(nameof(IsXboxActive));
        OnPropertyChanged(nameof(ModeHeadline));
    }

    private void RaiseConnectionStateChanged()
    {
        OnPropertyChanged(nameof(IsInputConnected));
        OnPropertyChanged(nameof(BleButtonText));
        OnPropertyChanged(nameof(BleStateText));
    }

    private void ClearLog()
    {
        log.Clear();
        OnPropertyChanged(nameof(LogText));
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var persisted = new StringBuilder();
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            string prefix = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] ";
            log.Append(prefix).AppendLine(line);
            persisted.Append(prefix).AppendLine(line);
        }
        if (log.Length > 160000)
        {
            log.Remove(0, log.Length - 90000);
            log.Insert(0, "[UI LOG TRIMMED]\r\n");
        }
        sessionLog.Write(persisted.ToString());
        OnPropertyChanged(nameof(LogText));
    }

    private static string FirstLine(string text)
    {
        return (text ?? "").Replace("\r", "").Split('\n')[0];
    }

    private void SaveUserSettings(string logPrefix)
    {
        try
        {
            userSettings.Save();
        }
        catch (Exception ex)
        {
            AppendLog(logPrefix + " settings save warning: " + ex.Message);
        }
    }

    private static string ExplainStartFailure(Exception ex)
    {
        string first = FirstLine(ex.Message);
        if (first.Contains("usbip", StringComparison.OrdinalIgnoreCase) &&
            first.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "VIIPER 找不到 usbip.exe。请点击“安装/修复 usbip-win2”，完成后重新启动本地 VIIPER。";
        }

        if (first.Contains("attach", StringComparison.OrdinalIgnoreCase) ||
            first.Contains("device handler", StringComparison.OrdinalIgnoreCase))
        {
            return "USBIP 虚拟设备挂载失败。请点击“安装/修复 USBIP”，完成后重启本地 VIIPER；若驱动刚安装，可能需要重启 Windows。原始错误：" + first;
        }

        if (first.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
            first.Contains("连接被拒绝", StringComparison.OrdinalIgnoreCase))
        {
            return "VIIPER 服务未响应。程序会自动启动内置 VIIPER；若仍失败，请展开系统控制台查看 VIIPER 日志。原始错误：" + first;
        }

        return first;
    }

    private void RefreshRuntimeReadiness()
    {
        UsbipRuntime? usbip = UsbipRuntimeLocator.Find();
        if (usbip != null)
        {
            RuntimeReadinessText = "USBIP 程序已发现，启动角色时会自动检查内核驱动；VIIPER 已内置，无需另行安装。";
            return;
        }

        UsbipInstaller? installer = UsbipRuntimeLocator.FindBundledInstaller();
        RuntimeReadinessText = installer != null
            ? "首次使用：需要安装 USBIP 内核驱动。直接选择角色即可自动打开内置安装向导，VIIPER 无需另行安装。"
            : "发布包不完整：未安装 USBIP，且没有找到内置安装器。请重新获取完整 EXE。";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
