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
using System.Windows.Threading;

namespace Y700Switch2V55Manager;

public sealed record BleScanItem(string Target, string Address, string Name, int Rssi, bool Candidate)
{
    public string DisplayName =>
        Target + "  " + Address + "  " + Name + "  RSSI=" + Rssi + (Candidate ? "  候选" : "");
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private enum DeviceUiMode
    {
        Unknown,
        DualSense,
        Pro2,
        Xbox,
        Recovery
    }

    private readonly Window owner;
    private readonly FirmwareFlasher flasher = new();
    private readonly ManagerSettings settings = ManagerSettingsStore.Load();
    private readonly StringBuilder log = new();
    private readonly SemaphoreSlim serialLock = new(1, 1);
    private readonly SemaphoreSlim flashLock = new(1, 1);
    private readonly DispatcherTimer stateTimer = new();
    private CancellationTokenSource? gameMonitorCts;
    private bool stateRefreshInProgress;
    private DateTime nextUsbAutoCheck = DateTime.MinValue;
    private DateTime nextSerialAutoProbeAt = DateTime.MinValue;

    private PortItem? selectedPort;
    private string portStatus = "尚未检测到 ESP32 串口。请连接 CH343P/WCH 控制板，或手动选择正确的 COM 口。";
    private string usbStatus = "尚未执行 USB 检查。";
    private string audioStatus = "尚未执行音频检查。";
    private string bleStatus = "尚未读取 BLE 状态。";
    private string bleCandidates = "尚未加载 BLE 候选列表。";
    private string hapticStatus = "默认状态：实时转发已关闭，试运行已开启。";
    private string nextAction = "请先从三模切换台选择目标模式。即使没有连接 ESP32，程序也会保持离线可操作。";
    private string overallStatus = "就绪";
    private string modeSwitchStatus = "尚未执行模式切换。";
    private string customCommand = "status";
    private string bleTarget = "";
    private string audioPattern = "both_tick";
    private string audioIntensity = "48";
    private string audioDurationMs = "600";
    private string audioDeviceName = "Wireless Controller";
    private string gameMonitorSeconds = "300";
    private string xInputProbeSeconds = "8";
    private string xInputProbeLow = "32000";
    private string xInputProbeHigh = "52000";
    private string pro2RumbleScale = "140";
    private string pro2RumbleHoldMs = "220";
    private string pro2RumbleTickMs = "12";
    private string pro2RumbleStopPackets = "3";
    private string monitorStatus = "监听未启动。开始后会保持 Live raw02 + HD-only 过滤，并记录游戏是否真的打开了独立的 DualSense 控制器音频流。";
    private string xboxStatus = "Xbox / XInput 模式会枚举为 045E:028E，并将普通双马达震动回传到真实 Pro2。";
    private string pro2RumbleStatus = "这里提供 Pro2 / Nintendo 固件的普通震动自检。先确认 BLE 已连接，再做轻震、重震和停止。";
    private DeviceUiMode currentMode = DeviceUiMode.Unknown;
    private OutputModeId desiredMode = OutputModeId.Pro2;
    private bool usbDetected;
    private bool bleConnected;
    private string bleTransportState = "unknown";
    private bool bleInputHealthy;
    private bool serialBoardReady;
    private bool desiredModeAutoAligned;
    private bool busy;
    private bool flashInProgress;
    private bool gameMonitorRunning;
    private BleScanItem? selectedBleDevice;

    public ObservableCollection<PortItem> Ports { get; } = new();
    public ObservableCollection<BleScanItem> BleDevices { get; } = new();
    public ObservableCollection<string> AudioPatterns { get; } = new(new[]
    {
        "ch2_tick", "ch3_tick", "both_tick", "ch2_punch", "ch3_punch", "both_punch", "texture", "continuous", "sweep", "silence"
    });

    public PortItem? SelectedPort
    {
        get => selectedPort;
        set
        {
            string oldPort = selectedPort?.PortName ?? "";
            string newPort = value?.PortName ?? "";
            if (!string.Equals(oldPort, newPort, StringComparison.OrdinalIgnoreCase))
            {
                SerialCommandClient.CloseInBackground();
                serialBoardReady = false;
                nextSerialAutoProbeAt = DateTime.UtcNow;
            }
            selectedPort = value;
            settings.LastPortName = value?.PortName ?? "";
            ManagerSettingsStore.Save(settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUsableSerialCandidate));
            OnPropertyChanged(nameof(CanUseBleButtons));
            OnPropertyChanged(nameof(CanUsePro2ToolButtons));
            OnPropertyChanged(nameof(CanUseDualSenseToolButtons));
            OnPropertyChanged(nameof(CanUseMonitorButtons));
            OnPropertyChanged(nameof(CanUseAudioPatternButton));
            OnPropertyChanged(nameof(CanSendCustomSerialCommand));
            OnPropertyChanged(nameof(DualSenseToolStateText));
            OnPropertyChanged(nameof(Pro2ToolStateText));
        }
    }
    public string PortStatus { get => portStatus; set { portStatus = value; OnPropertyChanged(); } }
    public string UsbStatus { get => usbStatus; set { usbStatus = value; OnPropertyChanged(); } }
    public string AudioStatus { get => audioStatus; set { audioStatus = value; OnPropertyChanged(); } }
    public string BleStatus { get => bleStatus; set { bleStatus = value; OnPropertyChanged(); } }
    public string BleCandidates { get => bleCandidates; set { bleCandidates = value; OnPropertyChanged(); } }
    public string HapticStatus { get => hapticStatus; set { hapticStatus = value; OnPropertyChanged(); } }
    public string NextAction { get => nextAction; set { nextAction = value; OnPropertyChanged(); } }
    public string ModeSwitchStatus { get => modeSwitchStatus; set { modeSwitchStatus = value; OnPropertyChanged(); } }
    public string OverallStatus
    {
        get => overallStatus;
        set
        {
            overallStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverallBrush));
            OnPropertyChanged(nameof(OverallStatusBackgroundBrush));
            OnPropertyChanged(nameof(OverallStatusForegroundBrush));
            NotifyModeStateChanged();
        }
    }
    public string CustomCommand { get => customCommand; set { customCommand = value; OnPropertyChanged(); } }
    public string BleTarget
    {
        get => bleTarget;
        set
        {
            bleTarget = value;
            settings.LastBleTarget = value ?? "";
            ManagerSettingsStore.Save(settings);
            OnPropertyChanged();
        }
    }
    public BleScanItem? SelectedBleDevice
    {
        get => selectedBleDevice;
        set
        {
            selectedBleDevice = value;
            OnPropertyChanged();
            if (value != null)
            {
                BleTarget = !string.IsNullOrWhiteSpace(value.Target) ? value.Target : value.Address;
            }
        }
    }
    public string AudioPattern { get => audioPattern; set { audioPattern = value; OnPropertyChanged(); } }
    public string AudioIntensity { get => audioIntensity; set { audioIntensity = value; OnPropertyChanged(); } }
    public string AudioDurationMs { get => audioDurationMs; set { audioDurationMs = value; OnPropertyChanged(); } }
    public string AudioDeviceName
    {
        get => audioDeviceName;
        set
        {
            audioDeviceName = value;
            settings.LastAudioDeviceName = value ?? "";
            ManagerSettingsStore.Save(settings);
            OnPropertyChanged();
        }
    }
    public string GameMonitorSeconds { get => gameMonitorSeconds; set { gameMonitorSeconds = value; OnPropertyChanged(); } }
    public string XInputProbeSeconds { get => xInputProbeSeconds; set { xInputProbeSeconds = value; OnPropertyChanged(); } }
    public string XInputProbeLow { get => xInputProbeLow; set { xInputProbeLow = value; OnPropertyChanged(); } }
    public string XInputProbeHigh { get => xInputProbeHigh; set { xInputProbeHigh = value; OnPropertyChanged(); } }
    public string Pro2RumbleScale { get => pro2RumbleScale; set { pro2RumbleScale = value; OnPropertyChanged(); } }
    public string Pro2RumbleHoldMs { get => pro2RumbleHoldMs; set { pro2RumbleHoldMs = value; OnPropertyChanged(); } }
    public string Pro2RumbleTickMs { get => pro2RumbleTickMs; set { pro2RumbleTickMs = value; OnPropertyChanged(); } }
    public string Pro2RumbleStopPackets { get => pro2RumbleStopPackets; set { pro2RumbleStopPackets = value; OnPropertyChanged(); } }
    public string MonitorStatus { get => monitorStatus; set { monitorStatus = value; OnPropertyChanged(); } }
    public string XboxStatus { get => xboxStatus; set { xboxStatus = value; OnPropertyChanged(); } }
    public string Pro2RumbleStatus { get => pro2RumbleStatus; set { pro2RumbleStatus = value; OnPropertyChanged(); } }
    public bool Busy
    {
        get => busy;
        set
        {
            busy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverallBrush));
            OnPropertyChanged(nameof(OverallStatusBackgroundBrush));
            OnPropertyChanged(nameof(OverallStatusForegroundBrush));
        }
    }
    public bool GameMonitorRunning
    {
        get => gameMonitorRunning;
        set
        {
            gameMonitorRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverallBrush));
            OnPropertyChanged(nameof(OverallStatusBackgroundBrush));
            OnPropertyChanged(nameof(OverallStatusForegroundBrush));
        }
    }

    public string FirmwareSummary => "V5.8 管理器内置：Pro2 / Nintendo、Xbox / XInput、DualSense-like、HID 纯恢复固件、嵌入式 esptool 和 XInput 震动探针。";
    public string SafetySummary => "Live 转发默认不自动开启。游戏监听会保持 HD-only 过滤，普通 PCM 只计入 blocked_pcm，不会被盲目推送。";
    public string LogText => log.ToString();
    public Brush OverallBrush => Busy || GameMonitorRunning
        ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
        : OverallStatus.Contains("错误", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
            : OverallStatus.Contains("离线", StringComparison.OrdinalIgnoreCase) || OverallStatus.Contains("预留", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromRgb(148, 163, 184))
                : new SolidColorBrush(Color.FromRgb(74, 222, 128));
    public Brush OverallStatusBackgroundBrush => Busy || GameMonitorRunning
        ? new SolidColorBrush(Color.FromRgb(146, 64, 14))
        : OverallStatus.Contains("错误", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(153, 27, 27))
            : OverallStatus.Contains("离线", StringComparison.OrdinalIgnoreCase) || OverallStatus.Contains("预留", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromRgb(51, 65, 85))
                : new SolidColorBrush(Color.FromRgb(22, 101, 52));
    public Brush OverallStatusForegroundBrush => Brushes.White;
    public bool HasUsableSerialCandidate => SelectedPort != null && SelectedPort.CanOpen;
    public bool IsDualSenseToolsEnabled => desiredMode == OutputModeId.DualSenseLike;
    public bool IsPro2ToolsEnabled => desiredMode == OutputModeId.Pro2;
    public bool IsXboxToolsEnabled => desiredMode == OutputModeId.Xbox;
    public bool IsUnknownMode => currentMode == DeviceUiMode.Unknown || currentMode == DeviceUiMode.Recovery;
    public bool CanSwitchModes => !Busy && !flashInProgress;
    public bool CanUseBleButtons => HasUsableSerialCandidate;
    public bool CanUsePro2ToolButtons => HasUsableSerialCandidate && desiredMode == OutputModeId.Pro2 && currentMode == DeviceUiMode.Pro2;
    public bool CanUseDualSenseToolButtons => HasUsableSerialCandidate && desiredMode == OutputModeId.DualSenseLike && currentMode == DeviceUiMode.DualSense;
    public bool CanUseMonitorButtons => HasUsableSerialCandidate && desiredMode == OutputModeId.DualSenseLike;
    public bool CanUseAudioPatternButton => HasUsableSerialCandidate && desiredMode == OutputModeId.DualSenseLike;
    public bool CanSendCustomSerialCommand => HasUsableSerialCandidate;
    public string DesiredModeLabel => GetModeLabel(desiredMode);
    public string DesiredModeDescription => GetModeDescription(desiredMode);
    public string ModeDeckHint => desiredMode switch
    {
        OutputModeId.DualSenseLike => "点击卡片可切换到 DualSense-like 模式。刷写后管理器会等待 USB 重新枚举并校验身份。",
        OutputModeId.Pro2 => "点击卡片可切换到 Pro2 / Nintendo 模式。这是当前最稳定的普通震动路线。",
        OutputModeId.Xbox => "点击卡片可切换到 Xbox / XInput 模式。刷写后 USB 应枚举为 045E:028E，普通震动会回传到 Pro2。",
        _ => "点击手柄卡片即可设置目标模式；如果校验失败，界面会保留回退提示。"
    };
    public string CurrentModeLabel => currentMode switch
    {
        DeviceUiMode.DualSense => "DualSense-like 模式",
        DeviceUiMode.Pro2 => "Pro2 / Nintendo 模式",
        DeviceUiMode.Xbox => "Xbox / XInput 模式",
        DeviceUiMode.Recovery => "HID 纯恢复模式",
        _ => "USB 模式未知"
    };
    public string CurrentModeDescription => currentMode switch
    {
        DeviceUiMode.DualSense => "USB 当前已枚举为 DualSense-like，可使用 DualSense 实验工具。",
        DeviceUiMode.Pro2 => "USB 当前已枚举为 Pro2 / Nintendo，适合稳定输入和普通震动测试。",
        DeviceUiMode.Xbox => "USB 当前已枚举为 Xbox / XInput，适合 Steam、Apex 和普通双马达震动兼容性测试。",
        DeviceUiMode.Recovery => "当前看起来是最小化 HID 恢复固件，用于救援重刷和枚举恢复。",
        _ => "请先执行 USB 检查。在确认模式前，所有真实震动发送都会保持保守策略。"
    };
    public string UsbLightText => usbDetected
        ? CurrentModeLabel
        : currentMode == DeviceUiMode.Unknown ? "USB 未检测到" : "USB 未校验：" + CurrentModeLabel;
    public string BleLightText => bleConnected ? "BLE 已连接" : "BLE 离线";
    public string DualSenseToolStateText => DescribeToolState(
        OutputModeId.DualSenseLike,
        currentMode == DeviceUiMode.DualSense,
        "当前 USB 身份支持 DualSense-like 震动 / 音频实验链路。",
        "已选 DualSense-like 面板，但 USB 还没有切到 DualSense-like。");
    public string Pro2ToolStateText => DescribeToolState(
        OutputModeId.Pro2,
        currentMode == DeviceUiMode.Pro2,
        "当前 USB 身份就是 Pro2 / Nintendo 普通震动桥接。",
        "已选 Pro2 / Nintendo 面板，但 USB 还没有切到 Pro2 / Nintendo。");
    public string DualSenseCardStateText => GetModeStateText(OutputModeId.DualSenseLike, managerReady: true);
    public string Pro2CardStateText => GetModeStateText(OutputModeId.Pro2, managerReady: true);
    public string XboxCardStateText => GetModeStateText(OutputModeId.Xbox, managerReady: true);
    public string DualSenseCardTooltip => "点击切换到 DualSense-like 模式";
    public string Pro2CardTooltip => "点击切换到 Pro2 / Nintendo 模式";
    public string XboxCardTooltip => "点击切换到 Xbox / XInput 模式";
    public Brush UsbIndicatorBrush => currentMode switch
    {
        DeviceUiMode.DualSense => new SolidColorBrush(Color.FromRgb(29, 78, 216)),
        DeviceUiMode.Pro2 => new SolidColorBrush(Color.FromRgb(190, 24, 93)),
        DeviceUiMode.Xbox => new SolidColorBrush(Color.FromRgb(22, 101, 52)),
        DeviceUiMode.Recovery => new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        _ => new SolidColorBrush(Color.FromRgb(51, 65, 85))
    };
    public Brush BleIndicatorBrush => bleConnected ? Brushes.White : Brushes.Gray;
    public string BleDisplayText => bleTransportState switch
    {
        "connected" when bleInputHealthy => "BLE 已连接",
        "connected" => "BLE 已连接 / 输入不新鲜",
        "connecting" => "BLE 连接中",
        "scanning" => "BLE 扫描中",
        _ => "BLE 离线"
    };
    public Brush BleDisplayBrush => bleTransportState switch
    {
        "connected" when bleInputHealthy => Brushes.White,
        "connected" => new SolidColorBrush(Color.FromRgb(253, 186, 116)),
        "connecting" => new SolidColorBrush(Color.FromRgb(253, 224, 71)),
        "scanning" => new SolidColorBrush(Color.FromRgb(253, 224, 71)),
        _ => new SolidColorBrush(Color.FromRgb(203, 213, 225))
    };
    public Brush BleDisplayBackgroundBrush => bleTransportState switch
    {
        "connected" when bleInputHealthy => new SolidColorBrush(Color.FromRgb(15, 118, 110)),
        "connected" => new SolidColorBrush(Color.FromRgb(146, 64, 14)),
        "connecting" => new SolidColorBrush(Color.FromRgb(113, 63, 18)),
        "scanning" => new SolidColorBrush(Color.FromRgb(120, 53, 15)),
        _ => new SolidColorBrush(Color.FromRgb(51, 65, 85))
    };
    public Brush BleDisplayBorderBrush => bleTransportState switch
    {
        "connected" when bleInputHealthy => new SolidColorBrush(Color.FromRgb(45, 212, 191)),
        "connected" => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
        "connecting" => new SolidColorBrush(Color.FromRgb(250, 204, 21)),
        "scanning" => new SolidColorBrush(Color.FromRgb(250, 204, 21)),
        _ => new SolidColorBrush(Color.FromRgb(100, 116, 139))
    };
    public Brush BleDisplayForegroundBrush => Brushes.White;
    public Brush ModeCardBrush => currentMode switch
    {
        DeviceUiMode.DualSense => new SolidColorBrush(Color.FromRgb(235, 245, 255)),
        DeviceUiMode.Pro2 => new SolidColorBrush(Color.FromRgb(255, 241, 242)),
        DeviceUiMode.Xbox => new SolidColorBrush(Color.FromRgb(236, 252, 233)),
        _ => Brushes.White
    };
    public Brush ModeCardBorderBrush => currentMode switch
    {
        DeviceUiMode.DualSense => new SolidColorBrush(Color.FromRgb(29, 78, 216)),
        DeviceUiMode.Pro2 => new SolidColorBrush(Color.FromRgb(190, 24, 93)),
        DeviceUiMode.Xbox => new SolidColorBrush(Color.FromRgb(22, 101, 52)),
        _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
    };
    public Brush DualSenseCardBackground => GetModeCardBackground(OutputModeId.DualSenseLike);
    public Brush Pro2CardBackground => GetModeCardBackground(OutputModeId.Pro2);
    public Brush XboxCardBackground => GetModeCardBackground(OutputModeId.Xbox);
    public Brush DualSenseCardBorderBrush => GetModeCardBorderBrush(OutputModeId.DualSenseLike, managerReady: true);
    public Brush Pro2CardBorderBrush => GetModeCardBorderBrush(OutputModeId.Pro2, managerReady: true);
    public Brush XboxCardBorderBrush => GetModeCardBorderBrush(OutputModeId.Xbox, managerReady: true);
    public Brush DualSenseCardBadgeBrush => GetModeBadgeBrush(OutputModeId.DualSenseLike, managerReady: true);
    public Brush Pro2CardBadgeBrush => GetModeBadgeBrush(OutputModeId.Pro2, managerReady: true);
    public Brush XboxCardBadgeBrush => GetModeBadgeBrush(OutputModeId.Xbox, managerReady: true);
    public Visibility DualSenseLabVisibility => desiredMode == OutputModeId.DualSenseLike ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Pro2LabVisibility => desiredMode == OutputModeId.Pro2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DualSenseLabDisabledVisibility => desiredMode == OutputModeId.Unknown || desiredMode == OutputModeId.Recovery ? Visibility.Visible : Visibility.Collapsed;
    public Visibility XboxLabVisibility => desiredMode == OutputModeId.Xbox ? Visibility.Visible : Visibility.Collapsed;
    public string XboxToolStateText => DescribeToolState(
        OutputModeId.Xbox,
        currentMode == DeviceUiMode.Xbox,
        "当前 USB 身份是 Xbox / XInput。BLE 输入走同一份 Pro2 state，主机普通震动会被解析并回传到 Pro2。",
        "已选 Xbox / XInput 面板，但 USB 还没有切到 045E:028E。");

    public ICommand RefreshPortsCommand { get; }
    public ICommand FlashHapticCommand { get; }
    public ICommand FlashHidOnlyCommand { get; }
    public ICommand FlashPro2Command { get; }
    public ICommand ActivateDualSenseModeCommand { get; }
    public ICommand ActivatePro2ModeCommand { get; }
    public ICommand ActivateXboxModeCommand { get; }
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
    public ICommand InputRecalibrateCommand { get; }
    public ICommand HapticStatusCommand { get; }
    public ICommand DryRunOnCommand { get; }
    public ICommand LiveOffCommand { get; }
    public ICommand LiveOnCommand { get; }
    public ICommand AudioParserRearCommand { get; }
    public ICommand AudioParserFrontCommand { get; }
    public ICommand AudioParserStrongestCommand { get; }
    public ICommand HapticTickCommand { get; }
    public ICommand HapticPunchCommand { get; }
    public ICommand HapticStopCommand { get; }
    public ICommand SafeHapticTestCommand { get; }
    public ICommand SendAudioPatternCommand { get; }
    public ICommand StartGameMonitorCommand { get; }
    public ICommand StopGameMonitorCommand { get; }
    public ICommand RunXInputProbeCommand { get; }
    public ICommand Pro2RumbleStatusCommand { get; }
    public ICommand Pro2RumbleLightCommand { get; }
    public ICommand Pro2RumbleStrongCommand { get; }
    public ICommand Pro2RumbleApplyTuneCommand { get; }
    public ICommand Pro2RumbleManualHoldCommand { get; }
    public ICommand Pro2RumbleStopCommand { get; }
    public ICommand SendCustomCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SaveLogCommand { get; }

    public MainViewModel(Window owner)
    {
        this.owner = owner;
        desiredMode = ParseDesiredMode(settings.DesiredModeId, settings.LastSuccessfulProfileId);
        if (string.IsNullOrWhiteSpace(settings.DesiredModeId))
        {
            settings.DesiredModeId = desiredMode.ToString();
            ManagerSettingsStore.Save(settings);
        }
        RefreshPortsCommand = new RelayCommand(async _ => await RefreshPortsAsync());
        FlashHapticCommand = new RelayCommand(async _ => await FlashAsync("hid_audio_uac1_4ch_ds5like", FlashMode.Upgrade));
        FlashHidOnlyCommand = new RelayCommand(async _ => await FlashAsync("hid_only", FlashMode.Repair));
        FlashPro2Command = new RelayCommand(async _ => await FlashAsync("pro2_bridge_v5_5", FlashMode.Upgrade));
        ActivateDualSenseModeCommand = new RelayCommand(async _ => await ActivateModeAsync(OutputModeCatalog.DualSenseLike));
        ActivatePro2ModeCommand = new RelayCommand(async _ => await ActivateModeAsync(OutputModeCatalog.Pro2));
        ActivateXboxModeCommand = new RelayCommand(async _ => await ActivateModeAsync(OutputModeCatalog.Xbox));
        CheckUsbCommand = new RelayCommand(async _ => await CheckUsbAsync());
        ListAudioCommand = new RelayCommand(async _ => await ListAudioAsync());
        OpenJoyCommand = new RelayCommand(_ => StartShell("joy.cpl"));
        OpenDeviceManagerCommand = new RelayCommand(_ => StartShell("devmgmt.msc"));
        BleScanCommand = new RelayCommand(async _ => await ScanBleAsync());
        BleListCommand = new RelayCommand(async _ => await ListBleAsync());
        BleReconnectCommand = new RelayCommand(async _ => await SendSerialAsync("ble reconnect", 20, s => BleStatus = "已请求重连上一次 BLE 目标。"));
        BleAutoOnCommand = new RelayCommand(async _ => await SendSerialAsync("ble auto on", 5, s => BleStatus = "BLE 自动重连：已开启。"));
        BleAutoOffCommand = new RelayCommand(async _ => await SendSerialAsync("ble auto off", 5, s => BleStatus = "BLE 自动重连：已关闭。"));
        BleDisconnectCommand = new RelayCommand(async _ => await SendSerialAsync("ble disconnect", 5, s => BleStatus = "已请求断开 BLE。"));
        BleConnectCommand = new RelayCommand(async _ => await SendSerialAsync("ble connect " + (string.IsNullOrWhiteSpace(BleTarget) ? "last" : BleTarget), 20, s => BleStatus = "已请求连接 BLE 目标。"));
        InputRecalibrateCommand = new RelayCommand(async _ => await RecalibrateInputAsync());
        HapticStatusCommand = new RelayCommand(async _ => await SendSerialAsync("haptic status", 5, s => HapticStatus = SummarizeHaptic(s)));
        DryRunOnCommand = new RelayCommand(async _ => await SendSerialAsync("haptic dryrun on", 4, _ => HapticStatus = "试运行已开启。"));
        LiveOffCommand = new RelayCommand(async _ => await TurnLiveOffAsync());
        LiveOnCommand = new RelayCommand(async _ => await TurnLiveOnAsync());
        AudioParserRearCommand = new RelayCommand(async _ => await SetAudioParserAsync("rear"));
        AudioParserFrontCommand = new RelayCommand(async _ => await SetAudioParserAsync("front"));
        AudioParserStrongestCommand = new RelayCommand(async _ => await SetAudioParserAsync("strongest"));
        HapticTickCommand = new RelayCommand(async _ => await SendLiveHapticPulseAsync("tick", "轻击"));
        HapticPunchCommand = new RelayCommand(async _ => await SendLiveHapticPulseAsync("punch", "重击"));
        HapticStopCommand = new RelayCommand(async _ => await SendSerialAsync("haptic test live stop", 4, _ => HapticStatus = "已发送实时转发停止命令。"));
        SafeHapticTestCommand = new RelayCommand(async _ => await RunSafeHapticTestAsync());
        SendAudioPatternCommand = new RelayCommand(async _ => await SendAudioPatternAsync());
        StartGameMonitorCommand = new RelayCommand(async _ => await StartGameMonitorAsync());
        StopGameMonitorCommand = new RelayCommand(async _ => await StopGameMonitorAsync());
        RunXInputProbeCommand = new RelayCommand(async _ => await RunXInputProbeAsync());
        Pro2RumbleStatusCommand = new RelayCommand(async _ => await RefreshPro2RumbleStatusAsync());
        Pro2RumbleLightCommand = new RelayCommand(async _ => await RunPro2RumblePresetAsync("轻震", 120, 160, 10, 2));
        Pro2RumbleStrongCommand = new RelayCommand(async _ => await RunPro2RumblePresetAsync("重震", 185, 280, 12, 4));
        Pro2RumbleApplyTuneCommand = new RelayCommand(async _ => await ApplyPro2RumbleTuneAsync());
        Pro2RumbleManualHoldCommand = new RelayCommand(async _ => await RunPro2ManualHoldAsync());
        Pro2RumbleStopCommand = new RelayCommand(async _ => await StopPro2RumbleAsync());
        SendCustomCommand = new RelayCommand(async _ => await SendSerialAsync(CustomCommand, 6, _ => { }));
        ClearLogCommand = new RelayCommand(_ => { log.Clear(); OnPropertyChanged(nameof(LogText)); });
        SaveLogCommand = new RelayCommand(_ => SaveLog());

        AppendLog("PRO2 手柄无线接收器控制板 V5.8 已就绪。此 EXE 内置固件与 esptool，单击刷写时无需额外安装 ESP-IDF。");
        if (!string.IsNullOrWhiteSpace(settings.LastBleTarget))
        {
            bleTarget = settings.LastBleTarget;
        }
        if (!string.IsNullOrWhiteSpace(settings.LastAudioDeviceName))
        {
            audioDeviceName = settings.LastAudioDeviceName;
        }

        _ = InitializeAsync();
        stateTimer.Interval = TimeSpan.FromSeconds(3);
        stateTimer.Tick += async (_, _) => await AutoRefreshStateAsync();
        stateTimer.Start();
    }

    private async Task InitializeAsync()
    {
        await RefreshPortsAsync(logResult: false);
        await CheckUsbAsync(logResult: false);
    }

    private async Task RefreshPortsAsync(bool logResult = true)
    {
        PortScanResult scan = await Task.Run(() => DeviceInspector.ScanPorts());
        Ports.Clear();
        foreach (PortItem item in scan.Ports) Ports.Add(item);
        SelectedPort = PreferredPort();
        serialBoardReady = false;
        nextSerialAutoProbeAt = DateTime.UtcNow;
        PortStatus = Ports.Count == 0
            ? "当前没有检测到 COM 串口。程序会保持离线状态，直到出现 ESP32 控制板串口。"
            : SelectedPort == null
                ? "检测到了串口，但还没有可自动使用的 ESP32 控制板候选项。你仍然可以手动选择。"
                : "已发现 " + Ports.Count + " 个串口，并自动选中了 CH343P/WCH 控制板候选项。为避免串口热插拔导致 ESP32 复位，刷新列表不会主动打开 COM 口。";
        if (scan.MetadataTimedOut)
        {
            PortStatus += " 设备详情查询超时，已先返回快照结果。";
        }
        else if (scan.MetadataFailed)
        {
            PortStatus += " 设备详情查询失败，已退回到基础串口列表。";
        }
        if (logResult)
        {
            AppendLog(PortStatus);
        }
    }

    private PortItem? FirstLikelyPort()
    {
        foreach (PortItem port in Ports)
        {
            if (port.LikelyCh343 && port.CanOpen) return port;
        }
        return null;
    }

    private PortItem? PreferredPort()
    {
        if (!string.IsNullOrWhiteSpace(settings.LastPortName))
        {
            foreach (PortItem port in Ports)
            {
                if (string.Equals(port.PortName, settings.LastPortName, StringComparison.OrdinalIgnoreCase) &&
                    (port.CanOpen || !port.LikelyCh343))
                {
                    return port;
                }
            }
        }

        return FirstLikelyPort();
    }

    private async Task TryCaptureFirmwareIdentityAfterFlashAsync(string requestedProfile)
    {
        try
        {
            await Task.Delay(1200);
            string status = await SendSerialCoreAsync("status lite", 2, logOutput: false);
            string identity = DescribeFirmwareIdentity(status);
            ModeSwitchStatus = "串口固件回报：" + identity + "；现在还需要确认新的 USB 枚举。";
            AppendLog("[MODE_SWITCH_FIRMWARE] requested_profile=" + requestedProfile + " " + identity);
            string modeCommand = ModeCommandForProfile(requestedProfile);
            if (!string.IsNullOrWhiteSpace(modeCommand))
            {
                string modeReply = await SendSerialCoreAsync(modeCommand, 2, logOutput: false);
                AppendLog("[MODE_SWITCH_FIRMWARE_MODE] requested_profile=" + requestedProfile +
                          " command=" + modeCommand +
                          " reply=" + OneLine(modeReply));
                await SendSerialCoreAsync("reboot", 1, logOutput: false);
                serialBoardReady = false;
                if (!await SerialCommandClient.CloseAsync(750))
                {
                    AppendLog("[SERIAL] reboot close timed out; keeping UI responsive");
                }
                ModeSwitchStatus = "已写入 " + GetModeLabel(OutputModeCatalog.FindByProfileId(requestedProfile)?.ModeId ?? desiredMode) +
                                   " 固件模式并重启，等待新的 USB 枚举。";
            }
        }
        catch (Exception ex)
        {
            AppendLog("[MODE_SWITCH_FIRMWARE] requested_profile=" + requestedProfile +
                      " probe_failed=" + FirstLine(ex.Message));
        }
    }

    private static string ModeCommandForProfile(string requestedProfile)
    {
        return requestedProfile switch
        {
            "pro2_bridge_v5_5" => "mode nintendo",
            "xinput_bridge_v5_8" => "mode xinput",
            _ => ""
        };
    }

    private async Task ActivateModeAsync(OutputModeProfile profile)
    {
        if (Busy || flashInProgress)
        {
            ModeSwitchStatus = "已有刷写、切换或设备操作正在进行。请等待当前任务结束后再切换模式。";
            NextAction = "等待当前任务结束，不要连续点击模式卡片。";
            AppendLog("[MODE_DECK_BUSY] requested=" + profile.ModeId);
            return;
        }

        SetDesiredMode(profile.ModeId);

        if (!profile.ManagerReady)
        {
            OverallStatus = "预留";
            NextAction = profile.Label + " 目前还是预留位，暂时没有接入完整后端。";
            AppendLog("[MODE_DECK] requested=" + profile.ModeId + " status=staged manager_ready=false");
            MessageBox.Show(
                owner,
                profile.Label + " 目前还是预留位，暂时没有接入完整后端。\n\n管理器已经保留好了模式位和切换语义，后续补固件时不需要再重做界面。",
                "V5.8 模式预留",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        FlashMode flashMode = profile.ModeId == OutputModeId.Recovery ? FlashMode.Repair : FlashMode.Upgrade;
        if (!HasUsableSerialCandidate)
        {
            OverallStatus = "离线";
            ModeSwitchStatus = "已切到 " + profile.Label + " 面板，但当前没有可用的控制板串口，所以没有执行实际刷写。";
            NextAction = "请先连接可用的 ESP32 控制板，再点一次当前模式卡片完成真正切换。";
            AppendLog("[MODE_DECK] requested=" + profile.ModeId + " selected_panel_only=true reason=no_usable_serial_candidate");
            NotifyModeStateChanged();
            return;
        }

        ModeSwitchStatus = "正在请求切换到 " + profile.Label + "，目标 USB 标识=" + profile.ExpectedUsbMarker;
        NextAction = "正在切换到 " + GetModeLabel(profile.ModeId) + "。刷写后请重新插拔原生 USB / OTG。";
        AppendLog("[MODE_DECK] requested=" + profile.ModeId + " profile=" + profile.ProfileId);
        await FlashAsync(profile.ProfileId, flashMode);
    }

    private void SetDesiredMode(OutputModeId mode)
    {
        if (desiredMode == mode)
        {
            return;
        }

        desiredMode = mode;
        settings.DesiredModeId = mode.ToString();
        ManagerSettingsStore.Save(settings);
        NotifyModeStateChanged();
    }

    private static OutputModeId ParseDesiredMode(string? stored, string? fallbackProfileId)
    {
        if (!string.IsNullOrWhiteSpace(stored) &&
            Enum.TryParse(stored, ignoreCase: true, out OutputModeId parsed) &&
            parsed != OutputModeId.Unknown)
        {
            return parsed;
        }

        return OutputModeCatalog.FindByProfileId(fallbackProfileId)?.ModeId ?? OutputModeId.Pro2;
    }

#pragma warning disable CS0162
    private async Task FlashAsync(string profile, FlashMode mode)
    {
        if (!await flashLock.WaitAsync(0))
        {
            ModeSwitchStatus = "已有刷写或模式切换正在进行，已忽略新的刷写请求。";
            NextAction = "请等待当前刷写结束后再切换。";
            AppendLog("[MODE_SWITCH_BUSY] ignored_profile=" + profile);
            return;
        }

        flashInProgress = true;
        NotifyModeStateChanged();
        try
        {
            Busy = true;
            OutputModeProfile? requestedMode = OutputModeCatalog.FindByProfileId(profile);
            if (requestedMode != null)
            {
                SetDesiredMode(requestedMode.ModeId);
            }
            if (false)
            {
            string status = await SendSerialCoreAsync("status lite", 2, logOutput: false);
            string ble = ReadJsonString(status, "ble");
            if (string.Equals(ble, "connected", StringComparison.OrdinalIgnoreCase) &&
                HasReadyBleInput(status))
            {
                string cachedList = await SendSerialCoreAsync("ble list", 4, logOutput: false);
                ApplyBleScanResults(cachedList);
                BleStatus = "Pro2 已连接。为避免打断 Steam 或 tester 输入，本次跳过扫描；如果确实要重扫，请先断开 BLE。";
                return;
            }
            if (string.Equals(ble, "connecting", StringComparison.OrdinalIgnoreCase))
            {
                BleStatus = "BLE 仍在连接中。请等待连接结束，或先断开后再重新扫描。";
                return;
            }
            }
            OverallStatus = "刷写中";
            if (SelectedPort == null) await RefreshPortsAsync(logResult: false);
            if (SelectedPort == null) throw new InvalidOperationException("当前没有可用的 ESP32 串口。请连接控制板，或手动选择正确的 COM 口。");
            if (SelectedPort.LikelyCh343 && !SelectedPort.CanOpen)
            {
                throw new InvalidOperationException("当前选中的串口 " + SelectedPort.PortName + " 处于“" + SelectedPort.Availability + "”状态，暂时不能刷写。");
            }

            PortStatus = "正在将 " + profile + " 刷写到 " + SelectedPort.PortName;
            NextAction = "请等待刷写完成。";
            ModeSwitchStatus = "刷写开始：profile=" + profile + ", port=" + SelectedPort.PortName;
            AppendLog("[MODE_SWITCH_FLASH] profile=" + profile +
                      " port=" + SelectedPort.PortName +
                      " desired=" + desiredMode);
            var progress = new Progress<string>(AppendLog);
            if (!await SerialCommandClient.CloseAsync(1000))
            {
                AppendLog("[SERIAL] close before flash timed out; continuing with esptool, UI remains responsive");
            }
            using var flashTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
            await flasher.FlashAsync(SelectedPort.PortName, profile, mode, progress, flashTimeout.Token);
            settings.LastPortName = SelectedPort.PortName;
            settings.PreviousSuccessfulProfileId = settings.LastSuccessfulProfileId;
            settings.LastSuccessfulProfileId = profile;
            settings.PendingProfileId = profile;
            settings.PendingExpectedUsbMarker = OutputModeCatalog.FindByProfileId(profile)?.ExpectedUsbMarker ?? "";
            settings.PendingRequestedUtc = DateTime.UtcNow;
            ManagerSettingsStore.Save(settings);
            serialBoardReady = false;
            nextSerialAutoProbeAt = DateTime.UtcNow.AddSeconds(2);
            OverallStatus = "刷写完成";
            PortStatus = "刷写完成。请重新插拔原生 USB / OTG。";
            NextAction = "重新插拔原生 USB / OTG 后，请执行一次 USB 检查。";
            ModeSwitchStatus = "刷写完成：profile=" + profile + "，等待新的 USB 枚举。";
            await TryCaptureFirmwareIdentityAfterFlashAsync(profile);
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            PortStatus = "刷写失败：" + FirstLine(ex.Message);
            ModeSwitchStatus = "刷写失败：profile=" + profile + "，error=" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                AppendLog("[离线] 刷写 " + profile + "： " + FirstLine(ex.Message));
                NextAction = "请先连接可用的 ESP32 控制板串口，再重试模式切换。";
            }
            else
            {
                AppendLog("ERROR flash: " + ex);
            }
        }
        finally
        {
            Busy = false;
            flashInProgress = false;
            flashLock.Release();
            NotifyModeStateChanged();
        }
    }
#pragma warning restore CS0162

    private async Task CheckUsbAsync(bool logResult = true)
    {
        UsbProbeResult usb = await Task.Run(() => DeviceInspector.ProbeUsb());
        UsbStatus = usb.Summary;
        AppendLog("[USB CHECK RAW]");
        AppendLog(UsbStatus);
        UpdateStateFromUsbSummary(UsbStatus);
        ApplyPendingUsbModeVerification();
        if (logResult)
        {
            AppendLog("[USB CHECK]");
            AppendLog("desired=" + desiredMode + " current=" + currentMode + " pending_profile=" + settings.PendingProfileId +
                      " pending_marker=" + settings.PendingExpectedUsbMarker);
        }
        if (UsbStatus.Contains("VID_045E&PID_028E", StringComparison.OrdinalIgnoreCase) ||
            UsbStatus.Contains("XInput", StringComparison.OrdinalIgnoreCase))
        {
            NextAction = "当前是 Xbox / XInput 模式。可以先用网页 tester 或 Steam 验证输入，再运行 XInput 普通震动探针。";
        }
        else if (UsbStatus.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase) ||
                 UsbStatus.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
        {
            NextAction = "如果音频端点显示为 4 声道，可以先做一次 dry-run 图样测试。";
        }
        else
        {
            NextAction = "如果刚刷完板子，请重新插拔原生 USB / OTG。";
        }
    }

    private void ApplyPendingUsbModeVerification()
    {
        if (string.IsNullOrWhiteSpace(settings.PendingProfileId) ||
            string.IsNullOrWhiteSpace(settings.PendingExpectedUsbMarker))
        {
            return;
        }

        if (UsbStatus.Contains(settings.PendingExpectedUsbMarker, StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("[MODE_SWITCH] confirmed profile=" + settings.PendingProfileId +
                      " marker=" + settings.PendingExpectedUsbMarker);
            ModeSwitchStatus = "USB 已确认切换成功：profile=" + settings.PendingProfileId +
                               "，marker=" + settings.PendingExpectedUsbMarker;
            settings.PendingProfileId = "";
            settings.PendingExpectedUsbMarker = "";
            settings.PendingRequestedUtc = null;
            ManagerSettingsStore.Save(settings);
            return;
        }

        AppendLog("[MODE_SWITCH] pending profile=" + settings.PendingProfileId +
                  " expected_marker=" + settings.PendingExpectedUsbMarker +
                  " current_usb_summary=" + UsbStatus.Replace(Environment.NewLine, " | "));
        ModeSwitchStatus = "USB 仍未确认到目标模式：期望 " + settings.PendingExpectedUsbMarker +
                           "，当前看到的是 " + CurrentModeLabel;

        if (settings.PendingRequestedUtc is DateTime requestedUtc &&
            DateTime.UtcNow - requestedUtc > TimeSpan.FromMinutes(5) &&
            !string.IsNullOrWhiteSpace(settings.PreviousSuccessfulProfileId))
        {
            NextAction = "USB 还没有确认目标模式。如果你想回退，可以重新刷写 " +
                         settings.PreviousSuccessfulProfileId + "。";
        }
    }

    private async Task AutoRefreshStateAsync()
    {
        if (stateRefreshInProgress || Busy || GameMonitorRunning) return;
        stateRefreshInProgress = true;
        try
        {
            if (SelectedPort != null)
            {
                string output = "";
                if (serialBoardReady)
                {
                    output = await SendSerialCoreAsync("status lite", 1, logOutput: false);
                }

                if (!string.IsNullOrWhiteSpace(output))
                {
                    BleStatus = SummarizeBle(output, "auto");
                    if (ReadJsonCounter(output, "raw02_ble_writes") >= 0)
                    {
                        HapticStatus = SummarizeHaptic(output);
                    }
                }
            }

            if (DateTime.UtcNow >= nextUsbAutoCheck)
            {
                UsbStatus = (await Task.Run(() => DeviceInspector.ProbeUsb())).Summary;
                UpdateStateFromUsbSummary(UsbStatus);
                nextUsbAutoCheck = DateTime.UtcNow.AddSeconds(10);
            }
        }
        catch (Exception ex)
        {
            serialBoardReady = false;
            nextSerialAutoProbeAt = DateTime.UtcNow.AddSeconds(8);
            PortStatus = "自动刷新失败：" + FirstLine(ex.Message);
        }
        finally
        {
            stateRefreshInProgress = false;
        }
    }

    private async Task ScanBleAsync()
    {
        try
        {
            Busy = true;
            string status = await SendSerialCoreAsync("status lite", 2, logOutput: false);
            string ble = ReadJsonString(status, "ble");
            if (string.Equals(ble, "connected", StringComparison.OrdinalIgnoreCase) &&
                HasReadyBleInput(status))
            {
                string cachedList = await SendSerialCoreAsync("ble list", 4, logOutput: false);
                ApplyBleScanResults(cachedList);
                BleCandidates = SummarizeBleList(cachedList);
                BleStatus = "Pro2 已连接。为避免打断 Steam 或 tester 输入，本次跳过扫描；如果确实要重扫，请先断开 BLE。";
                return;
            }
            if (string.Equals(ble, "connecting", StringComparison.OrdinalIgnoreCase))
            {
                BleStatus = "BLE 仍在连接中。请等待连接结束，或先断开后再重新扫描。";
                return;
            }
            BleStatus = "正在扫描 BLE Pro2...";
            BleCandidates = "扫描进行中。请保持 Pro2 唤醒，并在配对灯闪烁期间保持稳定。";
            await SendSerialCoreAsync("ble scan", 18);
            string list = await SendSerialCoreAsync("ble list", 6);
            ApplyBleScanResults(list);
            BleCandidates = SummarizeBleList(list);
            BleStatus = SummarizeBle(list, "scan");
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            BleStatus = "BLE 扫描失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("BLE 扫描", ex);
            }
            else
            {
                AppendLog("ERROR ble scan: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ListBleAsync()
    {
        try
        {
            Busy = true;
            string list = await SendSerialCoreAsync("ble list", 6);
            ApplyBleScanResults(list);
            BleCandidates = SummarizeBleList(list);
            BleStatus = SummarizeBle(list, "list");
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            BleStatus = "读取 BLE 列表失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("BLE 列表", ex);
            }
            else
            {
                AppendLog("ERROR ble list: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private void ApplyBleScanResults(string output)
    {
        string selectedTarget = SelectedBleDevice?.Target ?? BleTarget.Trim();
        MatchCollection matches = Regex.Matches(
            output,
            "\\{\"index\":(\\d+),\"target\":\"([^\"]+)\",\"addr\":\"([^\"]+)\",\"name\":\"([^\"]*)\",\"rssi\":(-?\\d+),\"candidate\":(true|false)\\}",
            RegexOptions.IgnoreCase);

        BleDevices.Clear();
        BleScanItem? matchedItem = null;
        foreach (Match match in matches)
        {
            string target = match.Groups[2].Value;
            string address = match.Groups[3].Value;
            string name = match.Groups[4].Value;
            int rssi = int.Parse(match.Groups[5].Value);
            bool candidate = string.Equals(match.Groups[6].Value, "true", StringComparison.OrdinalIgnoreCase);
            var item = new BleScanItem(target, address, name, rssi, candidate);
            BleDevices.Add(item);
            if (!string.IsNullOrWhiteSpace(selectedTarget) &&
                (string.Equals(selectedTarget, target, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(selectedTarget, address, StringComparison.OrdinalIgnoreCase)))
            {
                matchedItem = item;
            }
        }

        if (matchedItem != null)
        {
            SelectedBleDevice = matchedItem;
        }
        else if (BleDevices.Count == 0)
        {
            SelectedBleDevice = null;
        }

        OnPropertyChanged(nameof(BleDevices));
    }

    private async Task ReconnectBleAsync()
    {
        try
        {
            Busy = true;
            string status = await SendSerialCoreAsync("status lite", 2, logOutput: false);
            if (string.Equals(ReadJsonString(status, "ble"), "connected", StringComparison.OrdinalIgnoreCase) &&
                HasReadyBleInput(status))
            {
                BleStatus = "BLE 已连接且输入新鲜，无需重新连接。";
                return;
            }

            await SendSerialCoreAsync("ble reconnect", 20);
            string refreshed = await SendSerialCoreAsync("status lite", 2, logOutput: false);
            BleStatus = SummarizeBle(refreshed, "reconnect");
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            BleStatus = "BLE 重连失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("BLE 重连", ex);
            }
            else
            {
                AppendLog("ERROR ble reconnect: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ConnectBleAsync()
    {
        try
        {
            Busy = true;
            string target = !string.IsNullOrWhiteSpace(BleTarget)
                ? BleTarget.Trim()
                : SelectedBleDevice != null
                    ? (!string.IsNullOrWhiteSpace(SelectedBleDevice.Target) ? SelectedBleDevice.Target : SelectedBleDevice.Address)
                    : "last";
            await SendSerialCoreAsync("ble connect " + target, 20);
            BleStatus = "已请求 BLE 连接。";
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            BleStatus = "BLE 连接失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("BLE 连接", ex);
            }
            else
            {
                AppendLog("ERROR ble connect: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SetAudioParserAsync(string parser)
    {
        try
        {
            Busy = true;
            await SendSerialCoreAsync("audio parser " + parser, 4);
            string status = await SendSerialCoreAsync("status lite", 2, logOutput: false);
            string output = status;
            HapticStatus = "控制器音频解析器已切换到 " + parser + "。 " + SummarizeHaptic(output);
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            HapticStatus = "切换音频解析器失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("音频解析器切换", ex);
            }
            else
            {
                AppendLog("ERROR audio parser: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SendAudioPatternAsync()
    {
        string args = "-DeviceName \"" + AudioDeviceName.Replace("\"", "") + "\" -Pattern " +
                      AudioPattern + " -DurationMs " + AudioDurationMs + " -Intensity " + AudioIntensity;
        try
        {
            Busy = true;
            OverallStatus = "音频震动";
            await WaitForBleInputReadyAsync(20);

            await SendSerialCoreAsync("haptic mode auto", 2);
            await SendSerialCoreAsync("haptic source pcm", 2);
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

            AudioStatus = $"已发送音频图样。source=pcm 自检，active={active}, raw02={livePackets}, BLE writes={writes}, errors={errors}";
            HapticStatus = $"PCM 自检 -> raw02 -> Pro2：left={ShortHex(left)}, right={ShortHex(right)}";
            OverallStatus = errors == 0 && writes > 0 ? "震动正常" : "检查震动";
            NextAction = errors == 0 && writes > 0
                ? "传输计数看起来正常；接下来仍需要真实游戏输出 DualSense 震动音频。"
                : "如果 BLE 已连接但 writes 一直不增长，请检查日志里的 raw02_error。";
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            AudioStatus = "音频震动测试失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("PCM / 图样自测", ex);
            }
            else
            {
                AppendLog("ERROR audio haptic: " + ex);
            }
        }
        finally
        {
            foreach (string command in new[] { "haptic test live stop", "haptic source hd_only", "haptic dryrun on", "haptic raw02 off" })
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
            OverallStatus = "游戏监听";
            MonitorStatus = "正在准备游戏监听：检查 BLE、开启 Live raw02，并启用 HD-only 过滤。";

            await WaitForBleInputReadyAsync(20, token);

            await SendSerialCoreAsync("haptic mode auto", 2);
            await SendSerialCoreAsync("haptic source hd_only", 2);
            await SendSerialCoreAsync("haptic interval 10", 2);
            await SendSerialCoreAsync("haptic max 96", 2);
            await SendSerialCoreAsync("haptic gain 2.0", 2);
            await SendSerialCoreAsync("haptic transient_gain 1.5", 2);
            await SendSerialCoreAsync("haptic threshold 256", 2);
            await SendSerialCoreAsync("haptic raw02 on", 2);
            await SendSerialCoreAsync("haptic dryrun off", 2);

            Busy = false;
            GameMonitorRunning = true;
            OverallStatus = "监听中";
            NextAction = "现在可以开始游戏测试。监听运行时请避免点 Pattern / Stop；结束后用“停止监听”。";
            AppendLog("[GAME_MONITOR_START] seconds=" + seconds +
                      " live_forwarding=true dry_run=false source=hd_only interval_ms=10 max=96 gain=2.0 transient_gain=1.5 threshold=256 status=lite");

            await RunGameMonitorLoopAsync(seconds, token);
            await DisableLiveForwardingAfterMonitorAsync();
        }
        catch (OperationCanceledException)
        {
            AppendLog("[GAME_MONITOR_CANCELLED]");
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            MonitorStatus = "游戏监听失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("游戏监听", ex);
            }
            else
            {
                AppendLog("ERROR game monitor: " + ex);
            }
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
        await SendSerialCoreAsync("haptic source hd_only", 2);
        await SendSerialCoreAsync("haptic dryrun on", 2);
        await SendSerialCoreAsync("haptic raw02 off", 2);
        AppendLog("[GAME_MONITOR_END_SAFE_OFF] live_forwarding=false dry_run=true source=hd_only");
    }

    private async Task RecalibrateInputAsync()
    {
        try
        {
            Busy = true;
            string output = await SendSerialCoreAsync("input recalibrate", 4);
            AppendLog("[INPUT_RECALIBRATE] keep_sticks_centered=true");
            BleStatus = "摇杆中心校准已重置。请让两个摇杆静止 1 秒。";
            HapticStatus = SummarizeHaptic(output);
            OverallStatus = "输入已重置";
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            BleStatus = "摇杆中心重置失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("重置摇杆中心", ex);
            }
            else
            {
                AppendLog("ERROR input recalibrate: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RunGameMonitorLoopAsync(int seconds, CancellationToken token)
    {
        string previous = await SendSerialCoreAsync("status lite", 2);
        MonitorSnapshot baseline = MonitorSnapshot.FromStatus(previous);
        MonitorSnapshot last = baseline;
        int samples = 0;
        int activeSamples = 0;
        int writeSamples = 0;

        AppendLog("[GAME_MONITOR_BASELINE] " + baseline.ToLogString());

        for (int elapsed = 1; elapsed <= seconds; elapsed++)
        {
            await Task.Delay(1000, token);

            string status = await SendSerialCoreAsync("status lite", 2);
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
                $"监听中 {elapsed}/{seconds}s：音频包 +{total.AudioPackets}, 活跃包 +{total.AudioActive}, HD 候选 +{total.HdCandidates}, 屏蔽 PCM +{total.DroppedPcm}, raw02 +{total.Raw02Live}, BLE writes +{total.BleWrites}, errors={current.BleErrors}";
            HapticStatus = SummarizeHaptic(status);
            last = current;
        }

        MonitorDelta finalDelta = last.DeltaFrom(baseline);
        bool noControllerAudioStream = finalDelta.AudioPackets == 0 &&
                                       baseline.AudioPackets == 0 &&
                                       last.AudioAlt == 0 &&
                                       !last.AudioStreaming;
        bool gameAudioDetected = finalDelta.AudioPackets > 0 && finalDelta.AudioActive > 0;
        bool hdCandidateDetected = finalDelta.HdCandidates > 0;
        bool liveForwarded = finalDelta.Raw02Live > 0 && finalDelta.BleWrites > 0;
        bool mostlyPcm = finalDelta.DroppedPcm > 0 &&
                         finalDelta.AudioActive > 0 &&
                         finalDelta.DroppedPcm >= (finalDelta.AudioActive * 3 / 4);
        string conclusion = noControllerAudioStream
            ? "no_controller_audio_stream_opened"
            : gameAudioDetected && mostlyPcm
            ? "ordinary_pcm_audio_blocked_not_hd"
            : gameAudioDetected && hdCandidateDetected && liveForwarded
            ? "game_haptic_forwarded"
            : gameAudioDetected
                ? "game_audio_seen_but_no_hd_candidate"
                : "no_game_haptic_audio_detected";

        AppendLog("[GAME_MONITOR_RESULT] conclusion=" + conclusion +
                  " samples=" + samples +
                  " active_samples=" + activeSamples +
                  " write_samples=" + writeSamples +
                  " no_controller_audio_stream=" + noControllerAudioStream.ToString().ToLowerInvariant() +
                  " mostly_pcm=" + mostlyPcm.ToString().ToLowerInvariant() +
                  " " + finalDelta.ToLogString());
        MonitorStatus = conclusion == "no_controller_audio_stream_opened"
            ? "监听完成：游戏从未打开 Wireless Controller Audio，因此没有任何 DualSense 震动音频进入开发板。"
            : "监听完成：" + conclusion + "。日志里已经记录了 GAME_MONITOR_RESULT。";
        if (conclusion == "no_controller_audio_stream_opened")
        {
            NextAction = "请先换一个真正支持 PC DualSense 震动音频的标题；本次会话没有向控制器音频端点发送任何数据。";
        }
        OverallStatus = conclusion == "game_haptic_forwarded" ? "监听正常" : "缺少上游源";
    }

    private async Task StopGameMonitorAsync()
    {
        gameMonitorCts?.Cancel();
        try
        {
            await SendSerialCoreAsync("haptic test live stop", 2);
            await SendSerialCoreAsync("haptic source hd_only", 2);
            await SendSerialCoreAsync("haptic dryrun on", 2);
            await SendSerialCoreAsync("haptic raw02 off", 2);
            MonitorStatus = "监听已停止，Live raw02 已安全关闭。";
            HapticStatus = "Live raw02 已关闭，Dry-run 已重新开启。";
            OverallStatus = "监听已停止";
            AppendLog("[GAME_MONITOR_STOP] live_forwarding=false dry_run=true source=hd_only");
        }
        catch (Exception ex)
        {
            MonitorStatus = "停止监听时出现串口错误：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("停止监听", ex);
            }
            else
            {
                AppendLog("WARN game monitor stop: " + ex);
            }
        }
    }

    private async Task ListAudioAsync()
    {
        await RunAudioSenderAsync("-ListDevices");
        DualSenseAudioCompatProbeResult probe = DualSenseAudioCompatProbe.Run();
        string summary = DualSenseAudioCompatProbe.FormatSummary(probe);
        AudioStatus = "音频兼容探针：" + probe.MatchMode;
        AppendLog("[DS5_AUDIO_COMPAT]");
        AppendLog(summary);
    }

    private async Task RunAudioSenderAsync(string arguments)
    {
        try
        {
            Busy = true;
            string output = await RunAudioSenderCoreAsync(arguments);
            AudioStatus = output.Contains("channels=4", StringComparison.OrdinalIgnoreCase)
                ? "音频辅助工具检测到了 4 声道，或已成功发送。"
                : "音频辅助工具已运行；如果你始终只看到 2 声道，请确认已刷入 DualSense-like 4 声道固件，并重新插拔原生 USB。";
            AppendLog(output);
        }
        catch (Exception ex)
        {
            AudioStatus = "音频测试失败：" + FirstLine(ex.Message);
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

    private async Task<bool> EnsureSerialBoardReadyAsync(string actionName)
    {
        if (SelectedPort == null)
        {
            await RefreshPortsAsync(logResult: false);
        }

        if (SelectedPort == null)
        {
            serialBoardReady = false;
            OverallStatus = "离线";
            PortStatus = "没有检测到可用的 ESP32 控制板串口，暂时无法执行“" + actionName + "”。";
            NextAction = "请先连接控制板，或手动选择正确的串口。";
            return false;
        }

        if (SelectedPort.LikelyCh343 && !SelectedPort.CanOpen)
        {
            serialBoardReady = false;
            OverallStatus = "离线";
            PortStatus = "串口 " + SelectedPort.PortName + " 当前为“" + SelectedPort.Availability + "”，暂时无法执行“" + actionName + "”。";
            NextAction = "请先关闭占用串口的程序，或重新选择正确的控制板串口。";
            return false;
        }

        if (serialBoardReady)
        {
            return true;
        }

        string probe = await ProbeSelectedPortAsync(logOutput: false);
        if (!string.IsNullOrWhiteSpace(probe))
        {
            return true;
        }

        OverallStatus = "离线";
        NextAction = "串口已发现，但控制板尚未响应。请检查 ESP32 是否已连接、是否处于正常运行状态。";
        return false;
    }

    private async Task<string> ProbeSelectedPortAsync(bool logOutput)
    {
        PortItem? port = SelectedPort;
        if (port == null)
        {
            return "";
        }

        await serialLock.WaitAsync();
        try
        {
            IProgress<string> progress = logOutput
                ? new Progress<string>(AppendLog)
                : new Progress<string>(_ => { });
            string output = await SerialCommandClient.SendAsync(port.PortName, "status lite", 1, progress);
            if (LooksLikeBoardStatus(output))
            {
                serialBoardReady = true;
                nextSerialAutoProbeAt = DateTime.UtcNow;
                PortStatus = "控制板串口已就绪：" + port.PortName;
                UpdateStateFromText(output);
                return output;
            }

            serialBoardReady = false;
            nextSerialAutoProbeAt = DateTime.UtcNow.AddSeconds(8);
            await SerialCommandClient.CloseAsync(500);
            PortStatus = "已发现串口 " + port.PortName + "，但没有读到控制板状态回包，当前保持离线安全态。";
            return "";
        }
        catch (Exception ex)
        {
            serialBoardReady = false;
            nextSerialAutoProbeAt = DateTime.UtcNow.AddSeconds(8);
            await SerialCommandClient.CloseAsync(500);
            PortStatus = "串口 " + port.PortName + " 当前未响应控制板状态请求：" + FirstLine(ex.Message);
            if (logOutput)
            {
                AppendLog("WARN serial probe: " + ex.Message);
            }
            return "";
        }
        finally
        {
            serialLock.Release();
        }
    }

    private static bool LooksLikeBoardStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        return output.Contains("\"ble\"", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("\"haptic\"", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("\"cmd\"", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("\"ok\":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBoardUnavailableException(Exception ex)
    {
        string message = ex.Message ?? "";
        return ex is InvalidOperationException &&
               (message.Contains("当前没有可用的 ESP32 控制板串口", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("暂时无法执行", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("暂时不能刷写", StringComparison.OrdinalIgnoreCase));
    }

    private void LogBoardUnavailable(string scope, Exception ex)
    {
        AppendLog("[离线] " + scope + "： " + FirstLine(ex.Message));
    }

    private async Task SendSerialAsync(string command, int readSeconds, Action<string> after)
    {
        if (string.Equals(command, "ble reconnect", StringComparison.OrdinalIgnoreCase))
        {
            await ReconnectBleAsync();
            return;
        }
        if (command.StartsWith("ble connect ", StringComparison.OrdinalIgnoreCase))
        {
            await ConnectBleAsync();
            return;
        }

        try
        {
            Busy = true;
            string output = await SendSerialCoreAsync(command, readSeconds);
            after(output);
        }
        catch (Exception ex)
        {
            if (IsBoardUnavailableException(ex))
            {
                OverallStatus = "离线";
                LogBoardUnavailable(LabelForCommand(command), ex);
            }
            else
            {
                AppendLog("ERROR serial: " + ex);
                OverallStatus = "错误";
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task<string> SendSerialCoreAsync(string command, int readSeconds)
    {
        return await SendSerialCoreAsync(command, readSeconds, true);
    }

    private async Task<string> SendSerialCoreAsync(string command, int readSeconds, bool logOutput)
    {
        if (!await EnsureSerialBoardReadyAsync(LabelForCommand(command)))
        {
            throw new InvalidOperationException("当前没有可用的 ESP32 控制板串口。");
        }

        await serialLock.WaitAsync();
        try
        {
            if (SelectedPort == null)
            {
                throw new InvalidOperationException("当前没有可用的 ESP32 控制板串口。");
            }
            IProgress<string> progress = logOutput
                ? new Progress<string>(AppendLog)
                : new Progress<string>(_ => { });
            string output = await SerialCommandClient.SendAsync(
                SelectedPort.PortName,
                command,
                readSeconds,
                progress);
            serialBoardReady = true;
            nextSerialAutoProbeAt = DateTime.UtcNow;
            UpdateStateFromText(output);
            return output;
        }
        catch
        {
            serialBoardReady = false;
            nextSerialAutoProbeAt = DateTime.UtcNow.AddSeconds(8);
            await SerialCommandClient.CloseAsync(500);
            throw;
        }
        finally
        {
            serialLock.Release();
        }
    }

    private static string LabelForCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "串口命令";
        }

        if (command.StartsWith("ble ", StringComparison.OrdinalIgnoreCase))
        {
            return "BLE 操作";
        }

        if (command.StartsWith("haptic ", StringComparison.OrdinalIgnoreCase))
        {
            return "震动操作";
        }

        if (command.StartsWith("audio ", StringComparison.OrdinalIgnoreCase))
        {
            return "音频操作";
        }

        if (command.StartsWith("input ", StringComparison.OrdinalIgnoreCase))
        {
            return "输入校准";
        }

        if (command.StartsWith("status", StringComparison.OrdinalIgnoreCase))
        {
            return "状态查询";
        }

        return command;
    }

    private async Task RunSafeHapticTestAsync()
    {
        MessageBoxResult result = MessageBox.Show(owner,
            "这会执行一次短促、低强度的全链路实体震动测试：4 声道 DualSense 音频 -> raw02 -> BLE Pro2。测试会自动停止，并在结束后恢复 Dry-run。",
            "安全实体震动测试",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (result != MessageBoxResult.OK) return;

        Busy = true;
        OverallStatus = "震动测试";
        HapticStatus = "正在准备安全实体震动测试。";
        try
        {
            await WaitForBleInputReadyAsync(20);

            await SendSerialCoreAsync("haptic defaults", 3);
            await SendSerialCoreAsync("haptic source pcm", 3);
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
                    $"全链路未确认：BLE writes={writes}, live packets={livePackets}, errors={errors}。");
            }

            HapticStatus = $"传输链路已通过：BLE writes={writes}, errors={errors}。请手上确认手柄是否真的震动。";
            OverallStatus = "确认体感";
            NextAction = "传输链路已经通过，接下来还需要用手感确认实体震动是否正常。";
            AppendLog("[SAFE_HAPTIC_TEST] result=transport_passed physical_confirmation=required ble_writes=" +
                      writes + " live_packets=" + livePackets + " errors=" + errors);
        }
        catch (Exception ex)
        {
            HapticStatus = "安全实体震动测试失败：" + FirstLine(ex.Message);
            OverallStatus = "错误";
            AppendLog("ERROR safe haptic test: " + ex);
        }
        finally
        {
            foreach (string command in new[] { "haptic test live stop", "haptic source hd_only", "haptic dryrun on", "haptic raw02 off" })
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

    private void UpdateStateFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        string lower = text.ToLowerInvariant();
        string mode = ReadJsonString(text, "mode");
        string profile = ReadJsonString(text, "profile");
        DeviceUiMode previousMode = currentMode;
        if (string.Equals(mode, "dualsense", StringComparison.OrdinalIgnoreCase) ||
            profile.Contains("ds5", StringComparison.OrdinalIgnoreCase) ||
            profile.Contains("dualsense", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("vid_054c&pid_0ce6"))
        {
            SetCurrentMode(DeviceUiMode.DualSense);
        }
        else if (string.Equals(mode, "nintendo", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(mode, "pro2", StringComparison.OrdinalIgnoreCase) ||
                 profile.Contains("pro2", StringComparison.OrdinalIgnoreCase) ||
                 profile.Contains("switch2", StringComparison.OrdinalIgnoreCase) ||
                 lower.Contains("vid_057e&pid_2069"))
        {
            SetCurrentMode(DeviceUiMode.Pro2);
        }
        else if (string.Equals(mode, "xinput", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(mode, "xbox", StringComparison.OrdinalIgnoreCase) ||
                 profile.Contains("xinput", StringComparison.OrdinalIgnoreCase) ||
                 profile.Contains("xbox", StringComparison.OrdinalIgnoreCase) ||
                 lower.Contains("vid_045e&pid_028e"))
        {
            SetCurrentMode(DeviceUiMode.Xbox);
        }
        else if (profile.Contains("hid_only", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(mode, "hid_only", StringComparison.OrdinalIgnoreCase))
        {
            SetCurrentMode(DeviceUiMode.Recovery);
        }

        if (previousMode != currentMode)
        {
            AppendLog("[MODE_DETECT_SERIAL] previous=" + previousMode +
                      " current=" + currentMode +
                      " reason=" + DescribeFirmwareIdentity(text));
            ModeSwitchStatus = "串口识别到当前固件模式：" + CurrentModeLabel +
                               "；详细信息见日志。";
        }

        TryAlignDesiredModeWithDetectedUsb();

        string ble = ReadJsonString(text, "ble");
        if (!string.IsNullOrWhiteSpace(ble))
        {
            bool hasInputMetrics = !string.IsNullOrWhiteSpace(ReadJsonBoolString(text, "input_live")) ||
                                   ReadJsonCounter(text, "input_updates") >= 0 ||
                                   ReadJsonCounter(text, "input_age_ms") >= 0;
            bool healthy = string.Equals(ble, "connected", StringComparison.OrdinalIgnoreCase) &&
                           (!hasInputMetrics || HasReadyBleInput(text));
            SetBleVisualState(ble, healthy);
        }
    }

    private void UpdateStateFromUsbSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            SetUsbDetected(false);
            return;
        }

        bool dualSense = summary.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase);
        bool pro2 = summary.Contains("VID_057E&PID_2069", StringComparison.OrdinalIgnoreCase);
        bool xbox = summary.Contains("VID_045E&PID_028E", StringComparison.OrdinalIgnoreCase) ||
                    summary.Contains("XInput", StringComparison.OrdinalIgnoreCase);
        DeviceUiMode previousMode = currentMode;

        SetUsbDetected(dualSense || pro2 || xbox);
        if (dualSense)
        {
            SetCurrentMode(DeviceUiMode.DualSense);
        }
        else if (pro2)
        {
            SetCurrentMode(DeviceUiMode.Pro2);
        }
        else if (xbox)
        {
            SetCurrentMode(DeviceUiMode.Xbox);
        }
        else if (summary.Contains("HID-only", StringComparison.OrdinalIgnoreCase))
        {
            SetCurrentMode(DeviceUiMode.Recovery);
        }

        if (previousMode != currentMode)
        {
            AppendLog("[MODE_DETECT_USB] previous=" + previousMode +
                      " current=" + currentMode +
                      " summary=" + summary.Replace(Environment.NewLine, " | "));
            ModeSwitchStatus = "USB 当前识别为：" + CurrentModeLabel;
        }

        TryAlignDesiredModeWithDetectedUsb();
    }

    private void SetCurrentMode(DeviceUiMode mode)
    {
        if (currentMode == mode) return;
        currentMode = mode;
        NotifyModeStateChanged();
    }

    private void SetUsbDetected(bool value)
    {
        if (usbDetected == value) return;
        usbDetected = value;
        NotifyModeStateChanged();
    }

    private void SetBleVisualState(string state, bool healthy)
    {
        string normalized = string.IsNullOrWhiteSpace(state) ? "unknown" : state.ToLowerInvariant();
        bool changed = !string.Equals(bleTransportState, normalized, StringComparison.Ordinal) ||
                       bleInputHealthy != healthy;
        bleTransportState = normalized;
        bleInputHealthy = healthy;
        SetBleConnected(healthy);
        if (changed)
        {
            NotifyModeStateChanged();
        }
    }

    private void SetBleConnected(bool value)
    {
        if (bleConnected == value) return;
        bleConnected = value;
        NotifyModeStateChanged();
    }

    private string GetModeLabel(OutputModeId mode)
    {
        return mode switch
        {
            OutputModeId.DualSenseLike => "DualSense-like",
            OutputModeId.Pro2 => "Pro2 / Nintendo",
            OutputModeId.Xbox => "Xbox / XInput",
            OutputModeId.Recovery => "HID 纯恢复",
            _ => "未知"
        };
    }

    private string GetModeDescription(OutputModeId mode)
    {
        return mode switch
        {
            OutputModeId.DualSenseLike => "DualSense-like USB 身份，并保留控制器音频实验链路。",
            OutputModeId.Pro2 => "面向稳定普通震动路线调好的 Nintendo-like / Pro2 桥接。",
            OutputModeId.Xbox => "真实 Xbox 360 / XInput 风格 USB 后端，普通震动会回传到 Pro2 BLE。",
            OutputModeId.Recovery => "用于重刷与 USB 救援的最小恢复固件。",
            _ => "尚未选择目标模式。"
        };
    }

    private string DescribeToolState(OutputModeId selectedMode, bool currentUsbMatches, string readyText, string pendingText)
    {
        if (desiredMode != selectedMode)
        {
            return "当前没有选中这个模式面板。";
        }

        if (!HasUsableSerialCandidate)
        {
            return "当前没有可用的控制板串口。你可以先查看界面和日志，但真实串口/BLE/震动操作会保持离线。";
        }

        if (!currentUsbMatches)
        {
            return pendingText + " 请先完成刷写、重插 USB，再执行一次“USB 检查”。";
        }

        return "可用：" + readyText;
    }

    private string GetModeStateText(OutputModeId mode, bool managerReady)
    {
        if (!managerReady)
        {
            return "预留";
        }

        if (desiredMode == mode && IsCurrentMode(mode))
        {
            return "在线";
        }

        if (desiredMode == mode)
        {
            return "目标";
        }

        if (IsCurrentMode(mode))
        {
            return "当前";
        }

        return "切换";
    }

    private Brush GetModeCardBackground(OutputModeId mode)
    {
        if (desiredMode == mode && IsCurrentMode(mode))
        {
            return mode switch
            {
                OutputModeId.DualSenseLike => new SolidColorBrush(Color.FromRgb(231, 244, 255)),
                OutputModeId.Pro2 => new SolidColorBrush(Color.FromRgb(255, 241, 242)),
                OutputModeId.Xbox => new SolidColorBrush(Color.FromRgb(236, 252, 233)),
                _ => Brushes.White
            };
        }

        if (desiredMode == mode)
        {
            return new SolidColorBrush(Color.FromRgb(248, 250, 252));
        }

        return new SolidColorBrush(Color.FromRgb(255, 255, 255));
    }

    private Brush GetModeCardBorderBrush(OutputModeId mode, bool managerReady)
    {
        if (!managerReady)
        {
            return new SolidColorBrush(Color.FromRgb(120, 53, 15));
        }

        if (desiredMode == mode && IsCurrentMode(mode))
        {
            return GetModeAccentBrush(mode);
        }

        if (desiredMode == mode)
        {
            return new SolidColorBrush(Color.FromRgb(59, 130, 246));
        }

        if (IsCurrentMode(mode))
        {
            return GetModeAccentBrush(mode);
        }

        return new SolidColorBrush(Color.FromRgb(206, 214, 224));
    }

    private Brush GetModeBadgeBrush(OutputModeId mode, bool managerReady)
    {
        if (!managerReady)
        {
            return new SolidColorBrush(Color.FromRgb(120, 53, 15));
        }

        if (desiredMode == mode && IsCurrentMode(mode))
        {
            return new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }

        if (desiredMode == mode)
        {
            return new SolidColorBrush(Color.FromRgb(59, 130, 246));
        }

        if (IsCurrentMode(mode))
        {
            return GetModeAccentBrush(mode);
        }

        return new SolidColorBrush(Color.FromRgb(100, 116, 139));
    }

    private Brush GetModeAccentBrush(OutputModeId mode)
    {
        return mode switch
        {
            OutputModeId.DualSenseLike => new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            OutputModeId.Pro2 => new SolidColorBrush(Color.FromRgb(225, 29, 72)),
            OutputModeId.Xbox => new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            _ => new SolidColorBrush(Color.FromRgb(100, 116, 139))
        };
    }

    private bool IsCurrentMode(OutputModeId mode)
    {
        return mode switch
        {
            OutputModeId.DualSenseLike => currentMode == DeviceUiMode.DualSense,
            OutputModeId.Pro2 => currentMode == DeviceUiMode.Pro2,
            OutputModeId.Xbox => currentMode == DeviceUiMode.Xbox,
            OutputModeId.Recovery => currentMode == DeviceUiMode.Recovery,
            _ => false
        };
    }

    private OutputModeId OutputModeFromCurrentMode()
    {
        return currentMode switch
        {
            DeviceUiMode.DualSense => OutputModeId.DualSenseLike,
            DeviceUiMode.Pro2 => OutputModeId.Pro2,
            DeviceUiMode.Xbox => OutputModeId.Xbox,
            DeviceUiMode.Recovery => OutputModeId.Recovery,
            _ => OutputModeId.Unknown
        };
    }

    private void TryAlignDesiredModeWithDetectedUsb()
    {
        if (desiredModeAutoAligned ||
            HasUsableSerialCandidate ||
            !string.IsNullOrWhiteSpace(settings.PendingProfileId))
        {
            return;
        }

        OutputModeId detected = OutputModeFromCurrentMode();
        if (detected == OutputModeId.Unknown || detected == desiredMode)
        {
            return;
        }

        desiredModeAutoAligned = true;
        desiredMode = detected;
        settings.DesiredModeId = detected.ToString();
        ManagerSettingsStore.Save(settings);
        ModeSwitchStatus = "已根据当前 USB 身份自动对齐到 " + GetModeLabel(detected) + " 面板。";
        NotifyModeStateChanged();
    }

    private void NotifyModeStateChanged()
    {
        OnPropertyChanged(nameof(IsDualSenseToolsEnabled));
        OnPropertyChanged(nameof(IsPro2ToolsEnabled));
        OnPropertyChanged(nameof(IsXboxToolsEnabled));
        OnPropertyChanged(nameof(IsUnknownMode));
        OnPropertyChanged(nameof(HasUsableSerialCandidate));
        OnPropertyChanged(nameof(CanSwitchModes));
        OnPropertyChanged(nameof(CanUseBleButtons));
        OnPropertyChanged(nameof(CanUsePro2ToolButtons));
        OnPropertyChanged(nameof(CanUseDualSenseToolButtons));
        OnPropertyChanged(nameof(CanUseMonitorButtons));
        OnPropertyChanged(nameof(CanUseAudioPatternButton));
        OnPropertyChanged(nameof(CanSendCustomSerialCommand));
        OnPropertyChanged(nameof(DesiredModeLabel));
        OnPropertyChanged(nameof(DesiredModeDescription));
        OnPropertyChanged(nameof(ModeDeckHint));
        OnPropertyChanged(nameof(CurrentModeLabel));
        OnPropertyChanged(nameof(CurrentModeDescription));
        OnPropertyChanged(nameof(UsbLightText));
        OnPropertyChanged(nameof(BleLightText));
        OnPropertyChanged(nameof(BleDisplayText));
        OnPropertyChanged(nameof(XboxToolStateText));
        OnPropertyChanged(nameof(DualSenseToolStateText));
        OnPropertyChanged(nameof(Pro2ToolStateText));
        OnPropertyChanged(nameof(Pro2LabVisibility));
        OnPropertyChanged(nameof(DualSenseCardStateText));
        OnPropertyChanged(nameof(Pro2CardStateText));
        OnPropertyChanged(nameof(XboxCardStateText));
        OnPropertyChanged(nameof(DualSenseCardBackground));
        OnPropertyChanged(nameof(Pro2CardBackground));
        OnPropertyChanged(nameof(XboxCardBackground));
        OnPropertyChanged(nameof(DualSenseCardBorderBrush));
        OnPropertyChanged(nameof(Pro2CardBorderBrush));
        OnPropertyChanged(nameof(XboxCardBorderBrush));
        OnPropertyChanged(nameof(DualSenseCardBadgeBrush));
        OnPropertyChanged(nameof(Pro2CardBadgeBrush));
        OnPropertyChanged(nameof(XboxCardBadgeBrush));
        OnPropertyChanged(nameof(DualSenseLabVisibility));
        OnPropertyChanged(nameof(DualSenseLabDisabledVisibility));
        OnPropertyChanged(nameof(XboxLabVisibility));
        OnPropertyChanged(nameof(UsbIndicatorBrush));
        OnPropertyChanged(nameof(BleIndicatorBrush));
        OnPropertyChanged(nameof(BleDisplayBrush));
        OnPropertyChanged(nameof(BleDisplayBackgroundBrush));
        OnPropertyChanged(nameof(BleDisplayBorderBrush));
        OnPropertyChanged(nameof(BleDisplayForegroundBrush));
        OnPropertyChanged(nameof(ModeCardBrush));
        OnPropertyChanged(nameof(ModeCardBorderBrush));
        OnPropertyChanged(nameof(OverallStatusBackgroundBrush));
        OnPropertyChanged(nameof(OverallStatusForegroundBrush));
    }

    private static long ReadJsonCounter(string text, string name)
    {
        MatchCollection matches = Regex.Matches(
            text,
            "\"" + Regex.Escape(name) + "\"\\s*:\\s*(-?\\d+)",
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

    private static string ReadJsonBoolString(string text, string name)
    {
        MatchCollection matches = Regex.Matches(
            text,
            "\"" + Regex.Escape(name) + "\"\\s*:\\s*(true|false)",
            RegexOptions.IgnoreCase);
        return matches.Count == 0 ? "" : matches[^1].Groups[1].Value.ToLowerInvariant();
    }

    private static string ShortHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "无";
        return text.Length <= 12 ? text : text[..12] + "...";
    }

    private static string DescribeFirmwareIdentity(string statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return "没有收到任何固件状态。";
        }

        string mode = ReadJsonString(statusText, "mode");
        string profile = ReadJsonString(statusText, "profile");
        string version = ReadJsonString(statusText, "version");
        string ble = ReadJsonString(statusText, "ble");
        return "mode=" + (string.IsNullOrWhiteSpace(mode) ? "?" : mode) +
               ", profile=" + (string.IsNullOrWhiteSpace(profile) ? "-" : profile) +
               ", version=" + (string.IsNullOrWhiteSpace(version) ? "-" : version) +
               ", ble=" + (string.IsNullOrWhiteSpace(ble) ? "-" : ble);
    }

    private static void RequireBleConnected(string statusText)
    {
        string ble = ReadJsonString(statusText, "ble");
        if (!string.Equals(ble, "connected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Pro2 BLE 尚未连接，因此已拒绝真实震动发送。");
        }
    }

    private async Task SendLiveHapticPulseAsync(string testName, string label)
    {
        try
        {
            Busy = true;
            OverallStatus = "震动脉冲";
            await WaitForBleInputReadyAsync(15);

            await SendSerialCoreAsync("haptic test live " + testName, 4);
            if (testName.Equals("punch", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(90);
                await SendSerialCoreAsync("haptic test live " + testName, 4);
                await Task.Delay(160);
            }
            else
            {
                await Task.Delay(110);
            }
            await SendSerialCoreAsync("haptic test live stop", 3);

            string status = await SendSerialCoreAsync("status", 3);
            long writes = ReadJsonCounter(status, "raw02_ble_writes");
            long errors = ReadJsonCounter(status, "raw02_ble_errors");
            string left = ReadJsonString(status, "raw02_left");
            string right = ReadJsonString(status, "raw02_right");
            HapticStatus = $"{label} 已发送一次实体震动；BLE writes={writes}, errors={errors}, left={ShortHex(left)}, right={ShortHex(right)}";
            OverallStatus = errors == 0 ? "震动正常" : "检查震动";
        }
        catch (Exception ex)
        {
            OverallStatus = "错误";
            HapticStatus = label + " 实体震动失败：" + FirstLine(ex.Message);
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
            "这会在 HD-only 过滤后开启真实 haptic audio / raw02 转发。普通音频会被阻断，只有疑似专用触觉通道的内容才会转发到 BLE Pro2。",
            "确认开启实时 raw02（HD-only）",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;
        try
        {
            Busy = true;
            await WaitForBleInputReadyAsync(15);
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            HapticStatus = "开启 Live raw02 失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("开启实时 raw02", ex);
            }
            else
            {
                AppendLog("ERROR live on: " + ex);
            }
            return;
        }
        finally
        {
            Busy = false;
        }
        await SendSerialAsync("haptic mode auto", 3, _ => { });
        await SendSerialAsync("haptic source hd_only", 3, _ => { });
        await SendSerialAsync("haptic interval 20", 3, _ => { });
        await SendSerialAsync("haptic max 64", 3, _ => { });
        await SendSerialAsync("haptic raw02 on", 4, _ => { });
        await SendSerialAsync("haptic dryrun off", 4, _ => HapticStatus = "实时 raw02 已开启，试运行已关闭，source=hd_only。");
    }

    private async Task TurnLiveOffAsync()
    {
        await SendSerialAsync("haptic test live stop", 3, _ => { });
        await SendSerialAsync("haptic source hd_only", 3, _ => { });
        await SendSerialAsync("haptic dryrun on", 3, _ => { });
        await SendSerialAsync("haptic raw02 off", 3, _ => HapticStatus = "实时 raw02 已关闭，试运行已开启。");
    }

    private string SummarizeHaptic(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "已请求读取震动状态，但没有收到任何输出。";
        string ble = ReadJsonString(output, "ble");
        string haptic = ReadJsonString(output, "haptic");
        string mode = ReadJsonString(output, "haptic_mode");
        string source = ReadJsonString(output, "haptic_source");
        string audioStreaming = ReadJsonBoolString(output, "audio_streaming");
        string audioParser = ReadJsonString(output, "audio_parser");
        string audioPair = ReadJsonString(output, "audio_pair");
        string lastMode = ReadJsonString(output, "raw02_last_mode");
        string left = ReadJsonString(output, "raw02_left");
        string right = ReadJsonString(output, "raw02_right");
        string hdCandidate = ReadJsonBoolString(output, "hd_candidate");
        long audioPackets = ReadJsonCounter(output, "audio_packets");
        long audioActive = ReadJsonCounter(output, "audio_active");
        long hdPackets = ReadJsonCounter(output, "raw02_hd_candidate_packets");
        long droppedPcm = ReadJsonCounter(output, "raw02_dropped_pcm");
        long livePackets = ReadJsonCounter(output, "raw02_live_packets");
        long writes = ReadJsonCounter(output, "raw02_ble_writes");
        long errors = ReadJsonCounter(output, "raw02_ble_errors");
        if (!string.IsNullOrWhiteSpace(ble) || writes >= 0)
        {
            return $"BLE={ble}, 震动={haptic}, 模式={mode}, 源={source}, 音频流={audioStreaming}, 音频包={audioPackets}, 活跃包={audioActive}, 解析器={audioParser}, 声道对={audioPair}, HD 候选={hdCandidate}, HD 包={hdPackets}, 屏蔽 PCM={droppedPcm}, raw02_live={livePackets}, BLE writes={writes}, errors={errors}, last={lastMode}, L={ShortHex(left)}, R={ShortHex(right)}";
        }
        string compact = output.Replace("\r", " ").Replace("\n", " ");
        return compact.Length > 220 ? compact[..220] + "..." : compact;
    }

    private string SummarizePro2Rumble(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "没有收到 Pro2 震动状态。";
        }

        string ble = ReadJsonString(status, "ble");
        string rumble = ReadJsonString(status, "rumble");
        long updates = ReadJsonCounter(status, "rumble_updates");
        long writes = ReadJsonCounter(status, "rumble_writes");
        long stops = ReadJsonCounter(status, "rumble_stops");
        long errors = ReadJsonCounter(status, "rumble_errors");
        long scale = ReadJsonCounter(status, "rumble_scale_percent");
        long holdMs = ReadJsonCounter(status, "rumble_hold_ms");
        long tickMs = ReadJsonCounter(status, "rumble_tick_ms");
        long stopPackets = ReadJsonCounter(status, "rumble_stop_packets");
        return $"BLE={ble}, rumble={rumble}, updates={updates}, writes={writes}, stops={stops}, errors={errors}, scale={scale}, hold_ms={holdMs}, tick_ms={tickMs}, stop_packets={stopPackets}";
    }

    private async Task EnsurePro2BridgeReadyAsync()
    {
        await WaitForBleInputReadyAsync(15);
        string status = await SendSerialCoreAsync("status", 3, logOutput: false);
        if (ReadJsonCounter(status, "rumble_writes") < 0)
        {
            throw new InvalidOperationException("当前串口回包看起来不是 Pro2 / Nintendo 桥接固件。请先切换到 Pro2 模式并执行一次 USB 检查。");
        }
    }

    private async Task RefreshPro2RumbleStatusAsync()
    {
        try
        {
            Busy = true;
            await EnsurePro2BridgeReadyAsync();
            string status = await SendSerialCoreAsync("status", 3);
            Pro2RumbleStatus = SummarizePro2Rumble(status);
            OverallStatus = "Pro2 震动";
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            Pro2RumbleStatus = "读取 Pro2 震动状态失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("读取 Pro2 震动状态", ex);
            }
            else
            {
                AppendLog("ERROR pro2 rumble status: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ApplyPro2RumbleTuneAsync()
    {
        try
        {
            Busy = true;
            await EnsurePro2BridgeReadyAsync();
            int scale = ParseIntOrDefault(Pro2RumbleScale, 140, 10, 250);
            int holdMs = ParseIntOrDefault(Pro2RumbleHoldMs, 220, 50, 1000);
            int tickMs = ParseIntOrDefault(Pro2RumbleTickMs, 12, 5, 50);
            int stopPackets = ParseIntOrDefault(Pro2RumbleStopPackets, 3, 1, 8);

            string output = await SendSerialCoreAsync($"rumble tune {scale} {holdMs} {tickMs} {stopPackets}", 3);
            Pro2RumbleScale = scale.ToString();
            Pro2RumbleHoldMs = holdMs.ToString();
            Pro2RumbleTickMs = tickMs.ToString();
            Pro2RumbleStopPackets = stopPackets.ToString();
            Pro2RumbleStatus = "已应用 Pro2 震动参数。 " + SummarizePro2Rumble(output);
            OverallStatus = "Pro2 震动";
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            Pro2RumbleStatus = "应用 Pro2 震动参数失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("应用 Pro2 震动参数", ex);
            }
            else
            {
                AppendLog("ERROR pro2 rumble tune: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RunPro2RumblePresetAsync(string label, int scale, int holdMs, int tickMs, int stopPackets)
    {
        try
        {
            Busy = true;
            OverallStatus = "Pro2 震动";
            await EnsurePro2BridgeReadyAsync();

            await SendSerialCoreAsync($"rumble tune {scale} {holdMs} {tickMs} {stopPackets}", 3);
            string output = await SendSerialCoreAsync($"rumble hold {holdMs}", 3);
            await Task.Delay(Math.Min(holdMs + 120, 1200));
            string status = await SendSerialCoreAsync("status", 3);
            Pro2RumbleStatus = $"{label} 已发送。 {SummarizePro2Rumble(status)}";
            AppendLog("[PRO2_RUMBLE_PRESET] label=" + label + " tune=" + scale + "/" + holdMs + "/" + tickMs + "/" + stopPackets);
            AppendLog(output);
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            Pro2RumbleStatus = label + " 失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("发送 " + label, ex);
            }
            else
            {
                AppendLog("ERROR pro2 rumble preset: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RunPro2ManualHoldAsync()
    {
        try
        {
            Busy = true;
            OverallStatus = "Pro2 震动";
            await EnsurePro2BridgeReadyAsync();
            int holdMs = ParseIntOrDefault(Pro2RumbleHoldMs, 220, 100, 1000);
            string output = await SendSerialCoreAsync($"rumble hold {holdMs}", 3);
            await Task.Delay(Math.Min(holdMs + 120, 1200));
            string status = await SendSerialCoreAsync("status", 3);
            Pro2RumbleStatus = "已发送手动脉冲。 " + SummarizePro2Rumble(status);
            AppendLog(output);
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            Pro2RumbleStatus = "发送手动脉冲失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("发送手动脉冲", ex);
            }
            else
            {
                AppendLog("ERROR pro2 rumble hold: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task StopPro2RumbleAsync()
    {
        try
        {
            Busy = true;
            string output = await SendSerialCoreAsync("rumble stop", 3);
            string status = await SendSerialCoreAsync("status", 3);
            Pro2RumbleStatus = "已发送停止。 " + SummarizePro2Rumble(status);
            OverallStatus = "Pro2 震动";
            AppendLog(output);
        }
        catch (Exception ex)
        {
            OverallStatus = IsBoardUnavailableException(ex) ? "离线" : "错误";
            Pro2RumbleStatus = "停止 Pro2 震动失败：" + FirstLine(ex.Message);
            if (IsBoardUnavailableException(ex))
            {
                LogBoardUnavailable("停止 Pro2 震动", ex);
            }
            else
            {
                AppendLog("ERROR pro2 rumble stop: " + ex);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RunXInputProbeAsync()
    {
        try
        {
            Busy = true;
            OverallStatus = "XInput 探针";
            XboxStatus = "正在运行 XInput 探针，请稍候。结束后会写出日志。";

            int seconds = ParseIntOrDefault(XInputProbeSeconds, 18, 5, 180);
            int low = ParseIntOrDefault(XInputProbeLow, 32000, 0, 65535);
            int high = ParseIntOrDefault(XInputProbeHigh, 52000, 0, 65535);
            string logRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PRO2WirelessReceiverControlBoard",
                "logs");
            Directory.CreateDirectory(logRoot);
            string logPath = Path.Combine(logRoot, "xinput_probe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");

            FirmwarePackage package = EmbeddedAssets.EnsurePackage();
            AppendLog("[XINPUT_PROBE_START] seconds=" + seconds + " low=" + low + " high=" + high + " log=" + logPath);
            string output = await RunProcessAsync(
                package.XInputProbePath,
                "--seconds " + seconds +
                " --pulse-ms 420 --gap-ms 280 --low " + low +
                " --high " + high +
                " --log \"" + logPath + "\"");

            XboxStatus = "XInput 探针已完成。日志：" + logPath;
            NextAction = "请对照日志里的 XInput 识别、输入和震动调用结果。如果主机发出普通震动，固件会尝试回传到 Pro2。";
            OverallStatus = "探针完成";
            AppendLog("[XINPUT_PROBE]");
            AppendLog(string.IsNullOrWhiteSpace(output) ? "(probe produced no console output)" : output);
            AppendLog("XInput 探针日志已保存：" + logPath);
        }
        catch (Exception ex)
        {
            OverallStatus = "错误";
            XboxStatus = "XInput 探针失败：" + FirstLine(ex.Message);
            AppendLog("ERROR xinput probe: " + ex);
        }
        finally
        {
            Busy = false;
        }
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
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动进程：" + fileName);
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
            AppendLog("ERROR 打开 " + target + ": " + ex.Message);
        }
    }

    private void SaveLog()
    {
        var dialog = new SaveFileDialog
        {
            FileName = "v5_8_manager_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log",
            Filter = "Log file|*.log|Text file|*.txt"
        };
        if (dialog.ShowDialog(owner) == true)
        {
            File.WriteAllText(dialog.FileName, log.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            AppendLog("日志已保存：" + dialog.FileName);
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

    private static string OneLine(string text)
    {
        return Regex.Replace(text.Replace("\r", " ").Replace("\n", " "), "\\s+", " ").Trim();
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

    private static bool HasReadyBleInput(string statusText)
    {
        string ble = ReadJsonString(statusText, "ble");
        string inputLive = ReadJsonBoolString(statusText, "input_live");
        long inputUpdates = ReadJsonCounter(statusText, "input_updates");
        long inputAgeMs = ReadJsonCounter(statusText, "input_age_ms");
        return string.Equals(ble, "connected", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(inputLive, "true", StringComparison.OrdinalIgnoreCase) &&
               inputUpdates > 0 &&
               inputAgeMs >= 0 &&
               inputAgeMs <= 500;
    }

    private async Task<string> WaitForBleInputReadyAsync(int timeoutSeconds, CancellationToken token = default)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(3, timeoutSeconds));
        DateTime nextReconnectAt = DateTime.MinValue;
        string lastStatus = "";
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            lastStatus = await SendSerialCoreAsync("status lite", 2, logOutput: false);
            if (!string.IsNullOrWhiteSpace(lastStatus))
            {
                BleStatus = SummarizeBle(lastStatus, "wait");
                if (HasReadyBleInput(lastStatus))
                {
                    return lastStatus;
                }
            }

            string ble = ReadJsonString(lastStatus, "ble");
            if (DateTime.UtcNow >= nextReconnectAt &&
                !string.Equals(ble, "connecting", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog("[BLE_WAIT] 已请求重连，正在等待 Pro2 实时输入。");
                await SendSerialCoreAsync("ble reconnect", 6);
                nextReconnectAt = DateTime.UtcNow.AddSeconds(4);
            }

            await Task.Delay(1000, token);
        }

        throw new InvalidOperationException(
            "Pro2 BLE 还没有提供实时输入。请确认手柄只连接到桥接板，然后在 BLE 区域使用“重连上次”或“扫描”。");
    }

    private string SummarizeBle(string output, string source)
    {
        if (string.IsNullOrWhiteSpace(output)) return "没有收到 BLE 状态回包。";
        string ble = ReadJsonString(output, "ble");
        string auto = ReadJsonString(output, "ble_auto");
        string target = ReadJsonString(output, "ble_target");
        long inputUpdates = ReadJsonCounter(output, "input_updates");
        long inputAgeMs = ReadJsonCounter(output, "input_age_ms");
        long inputRateMilliHz = ReadJsonCounter(output, "input_rate_millihz");
        long scanSeen = ReadJsonCounter(output, "scan_seen");

        if (!string.IsNullOrWhiteSpace(ble))
        {
            string rateText = inputRateMilliHz > 0
                ? (inputRateMilliHz / 1000.0).ToString("0.0") + " Hz"
                : "0 Hz";
            string ageText = inputAgeMs >= 0 ? inputAgeMs + " ms" : "无数据";
            string targetText = string.IsNullOrWhiteSpace(target) ? "无" : target;
            string autoText = string.IsNullOrWhiteSpace(auto) ? "?" : auto;
            return $"BLE={ble}, 自动重连={autoText}, 目标={targetText}, 输入计数={Math.Max(0, inputUpdates)}, 输入时延={ageText}, 输入频率={rateText}, 来源={source}";
        }

        if (scanSeen >= 0)
        {
            return $"已读取 BLE 列表，scan_seen={scanSeen}。";
        }

        return "BLE 状态已更新。";
    }

    private static string SummarizeBleList(string output)
    {
        MatchCollection matches = Regex.Matches(
            output,
            "\\{\"index\":(\\d+),\"target\":\"([^\"]+)\",\"addr\":\"([^\"]+)\",\"name\":\"([^\"]*)\",\"rssi\":(-?\\d+),\"candidate\":(true|false)\\}",
            RegexOptions.IgnoreCase);
        if (matches.Count == 0)
        {
            long scanSeen = ReadJsonCounter(output, "scan_seen");
            return scanSeen > 0
                ? $"本次扫描了 {scanSeen} 个设备，但没有发现候选项。请保持 Pro2 唤醒后再试一次。"
                : "暂时没有找到 BLE 候选设备。请点击“扫描”，并保持 Pro2 处于唤醒/配对状态。";
        }

        var builder = new StringBuilder();
        foreach (Match match in matches)
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.Append('#').Append(match.Groups[1].Value)
                .Append("  ").Append(match.Groups[3].Value);
            string name = match.Groups[4].Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                builder.Append("  ").Append(name);
            }
            builder.Append("  RSSI=").Append(match.Groups[5].Value);
            builder.Append("  候选=").Append(match.Groups[6].Value);
        }
        return builder.ToString();
    }

    private sealed class MonitorSnapshot
    {
        public long AudioPackets { get; init; }
        public long AudioActive { get; init; }
        public long HdCandidates { get; init; }
        public long Raw02Live { get; init; }
        public long BleWrites { get; init; }
        public long BleErrors { get; init; }
        public long DroppedRate { get; init; }
        public long DroppedSilence { get; init; }
        public long DroppedPcm { get; init; }
        public long InputUpdates { get; init; }
        public long InputAgeMs { get; init; }
        public long InputRateMilliHz { get; init; }
        public long InputLx { get; init; }
        public long InputLy { get; init; }
        public long InputRx { get; init; }
        public long InputRy { get; init; }
        public string Ble { get; init; } = "";
        public string Haptic { get; init; } = "";
        public string Source { get; init; } = "";
        public bool AudioStreaming { get; init; }
        public long AudioAlt { get; init; }
        public string AudioParser { get; init; } = "";
        public string AudioPair { get; init; } = "";
        public string HdCandidate { get; init; } = "";
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
                HdCandidates = Counter(status, "raw02_hd_candidate_packets"),
                Raw02Live = Counter(status, "raw02_live_packets"),
                BleWrites = Counter(status, "raw02_ble_writes"),
                BleErrors = Counter(status, "raw02_ble_errors"),
                DroppedRate = Counter(status, "raw02_dropped_rate"),
                DroppedSilence = Counter(status, "raw02_dropped_silence"),
                DroppedPcm = Counter(status, "raw02_dropped_pcm"),
                InputUpdates = Counter(status, "input_updates"),
                InputAgeMs = ReadJsonCounter(status, "input_age_ms"),
                InputRateMilliHz = Counter(status, "input_rate_millihz"),
                InputLx = Counter(status, "input_lx"),
                InputLy = Counter(status, "input_ly"),
                InputRx = Counter(status, "input_rx"),
                InputRy = Counter(status, "input_ry"),
                Ble = ReadJsonString(status, "ble"),
                Haptic = ReadJsonString(status, "haptic"),
                Source = ReadJsonString(status, "haptic_source"),
                AudioStreaming = string.Equals(ReadJsonBoolString(status, "audio_streaming"), "true", StringComparison.OrdinalIgnoreCase),
                AudioAlt = Counter(status, "audio_alt"),
                AudioParser = ReadJsonString(status, "audio_parser"),
                AudioPair = ReadJsonString(status, "audio_pair"),
                HdCandidate = ReadJsonBoolString(status, "hd_candidate"),
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
                HdCandidates = Math.Max(0, HdCandidates - previous.HdCandidates),
                Raw02Live = Math.Max(0, Raw02Live - previous.Raw02Live),
                BleWrites = Math.Max(0, BleWrites - previous.BleWrites),
                BleErrors = Math.Max(0, BleErrors - previous.BleErrors),
                DroppedRate = Math.Max(0, DroppedRate - previous.DroppedRate),
                DroppedSilence = Math.Max(0, DroppedSilence - previous.DroppedSilence),
                DroppedPcm = Math.Max(0, DroppedPcm - previous.DroppedPcm)
            };
        }

        public string ToLogString()
        {
            return "ble=" + Ble +
                   " haptic=" + Haptic +
                   " source=" + Source +
                   " audio_streaming=" + (AudioStreaming ? "true" : "false") +
                   " audio_alt=" + AudioAlt +
                   " parser=" + AudioParser +
                   " pair=" + AudioPair +
                   " audio_packets=" + AudioPackets +
                   " audio_active=" + AudioActive +
                   " hd_candidate=" + HdCandidate +
                   " hd_packets=" + HdCandidates +
                   " raw02_live=" + Raw02Live +
                   " ble_writes=" + BleWrites +
                   " ble_errors=" + BleErrors +
                   " dropped_rate=" + DroppedRate +
                   " dropped_silence=" + DroppedSilence +
                   " dropped_pcm=" + DroppedPcm +
                   " input_updates=" + InputUpdates +
                   " input_age_ms=" + InputAgeMs +
                   " input_rate_millihz=" + InputRateMilliHz +
                   " input_axes=(" + InputLx + "," + InputLy + "," + InputRx + "," + InputRy + ")" +
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
        public long HdCandidates { get; init; }
        public long Raw02Live { get; init; }
        public long BleWrites { get; init; }
        public long BleErrors { get; init; }
        public long DroppedRate { get; init; }
        public long DroppedSilence { get; init; }
        public long DroppedPcm { get; init; }

        public string ToLogString()
        {
            return "audio_packets=+" + AudioPackets +
                   " audio_active=+" + AudioActive +
                   " hd_packets=+" + HdCandidates +
                   " raw02_live=+" + Raw02Live +
                   " ble_writes=+" + BleWrites +
                   " ble_errors=+" + BleErrors +
                   " dropped_rate=+" + DroppedRate +
                   " dropped_silence=+" + DroppedSilence +
                   " dropped_pcm=+" + DroppedPcm;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
