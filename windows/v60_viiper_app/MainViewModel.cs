using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    private const int MaxUiLogCharacters = 120000;
    private const int TrimmedUiLogCharacters = 70000;
    private readonly StringBuilder log = new();
    private readonly Pro2BleInputSource inputSource = new();
    private readonly ProfessionalHidAuditController professionalHidAuditController = new();
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
    private string status = "V6.2.20 正式版已就绪。PS5 / Edge 陀螺仪已固化 R7 成果，测试模式不在正式界面显示。";
    private string inputStatus = "真实 Pro2 BLE 输入未连接。";
    private string selectedPushRateLabel = ViiperPushRateOption.Default.Label;
    private string selectedBackendLabel = VirtualBackendOption.Default.Label;
    private string selectedStickProcessingLabel = StickProcessingOption.Default.Label;
    private bool audioEndpointGuardEnabled = true;
    private bool running;
    private bool busy;
    private bool shuttingDown;
    private bool autoReconnectEnabled;
    private string runtimeReadinessText = "";
    private string professionalBiasStatusText =
        "Bias: NotCalibrated / source=none / applied=false / updates=0 / raw=0,0,0";
    private string professionalRawGyroText = "Raw Gyro raw: X=0 Y=0 Z=0";
    private string professionalCorrectedGyroText = "Corrected Gyro Preview (°/s): Pitch Rate=0 Yaw Rate=0 Roll Rate=0";
    private string professionalOutputGyroText = "Output Gyro (°/s): Pitch Rate=0 Yaw Rate=0 Roll Rate=0";
    private string professionalIntegratedAngleText = "Integrated Angle (°): Pitch Angle=0 Yaw Angle=0 Roll Angle=0 · integral_state=Disabled · integral_running=false";
    private string professionalNinetyDegreeTestText = "90° test idle.";
    private string selectedProfessionalHidAuditMode = ProfessionalHidAuditMode.Normal.ToString();
    private string professionalStaticGyroXRaw = "0";
    private string professionalStaticGyroYRaw = "0";
    private string professionalStaticGyroZRaw = "0";
    private string professionalHidAuditStatusText = "HID Audit: Normal · final report gyro follows selected_output_ds_raw.";
    private double rumbleMultiplier = 1.0;
    private double ps5GyroScale = 1.0;
    private bool professionalInvertGyroPitch;
    private bool professionalInvertGyroYaw = true;
    private bool professionalInvertGyroRoll = true;
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
        FixAudioDefaultsCommand = new RelayCommand(_ => RunExclusiveAsync("修复音频默认设备", FixAudioDefaultsAsync));
        DumpControllerEnumerationCommand = new RelayCommand(_ => RunExclusiveAsync("设备枚举诊断", DumpControllerEnumerationAsync));
        CleanupStaleVirtualDevicesCommand = new RelayCommand(_ => RunExclusiveAsync("清理残留虚拟设备", CleanupStaleVirtualDevicesAsync));
        ExportDiagnosticsLogCommand = new RelayCommand(_ => RunExclusiveAsync("导出诊断包", ExportDiagnosticsLogAsync));
        StartDualSenseCommand = new RelayCommand(_ => RunExclusiveAsync(
            "切换 新和联胜 / PS5",
            token => SwitchModeAsync(ViiperDeviceProfile.DualSenseLike, token)));
        StartDualSenseEdgeCommand = new RelayCommand(_ => RunExclusiveAsync(
            "切换 PS5 Edge / 背键",
            token => SwitchModeAsync(ViiperDeviceProfile.DualSenseEdge, token)));
        StartDualSenseProfessionalImuCommand = new RelayCommand(_ => RunExclusiveAsync(
            "切换 PS5 Professional IMU Test",
            token => SwitchModeAsync(ViiperDeviceProfile.DualSenseProfessionalImuTest, token)));
        StartPro2Command = new RelayCommand(_ => RunExclusiveAsync(
            "切换 Pro2 / Nintendo",
            token => SwitchModeAsync(ViiperDeviceProfile.Pro2, token)));
        StartXboxCommand = new RelayCommand(_ => RunExclusiveAsync(
            "切换 Xbox / XInput",
            token => SwitchModeAsync(ViiperDeviceProfile.Xbox, token)));
        StartXboxProfessionalImuCommand = new RelayCommand(_ => RunExclusiveAsync(
            "切换 Xbox Professional IMU Test",
            token => SwitchModeAsync(ViiperDeviceProfile.XboxProfessionalImuTest, token)));
        CalibrateGyroBiasCommand = new RelayCommand(_ =>
            ExecuteProfessionalImuAction("Calibrate Gyro Bias 3s", s => s.StartGyroBiasCalibration()));
        ResetGyroBiasCommand = new RelayCommand(_ =>
            ExecuteProfessionalImuAction("Reset Gyro Bias", s => s.ResetGyroBias()));
        ResetIntegralCommand = new RelayCommand(_ =>
            ExecuteProfessionalImuAction("Reset Integral", s => s.ResetProfessionalIntegral()));
        StartPitch90TestCommand = new RelayCommand(_ =>
            ExecuteProfessionalImuAction("Start Pitch 90° Test", s => s.StartNinetyDegreeTest(ProfessionalImuTestAxis.Pitch)));
        StartYaw90TestCommand = new RelayCommand(_ =>
            ExecuteProfessionalImuAction("Start Yaw 90° Test", s => s.StartNinetyDegreeTest(ProfessionalImuTestAxis.Yaw)));
        StartRoll90TestCommand = new RelayCommand(_ =>
            ExecuteProfessionalImuAction("Start Roll 90° Test", s => s.StartNinetyDegreeTest(ProfessionalImuTestAxis.Roll)));
        Stop90TestCommand = new RelayCommand(_ =>
            ExecuteProfessionalImuAction("Stop 90° Test", s => s.StopNinetyDegreeTest()));
        PulseGyroXCommand = new RelayCommand(_ => StartProfessionalHidPulse("X"));
        PulseGyroYCommand = new RelayCommand(_ => StartProfessionalHidPulse("Y"));
        PulseGyroZCommand = new RelayCommand(_ => StartProfessionalHidPulse("Z"));
        StopCommand = new RelayCommand(_ => RunExclusiveAsync("停止虚拟设备", _ => StopAsync()));
        ClearLogCommand = new RelayCommand(_ => ClearLog());
        rumbleMultiplier =
            V60UserSettings.NormalizeRumbleMultiplier(
                userSettings.RumbleMultiplier);
        ps5GyroScale =
            V60UserSettings.NormalizePs5GyroScale(
                userSettings.Ps5GyroScalePitch);
        professionalInvertGyroPitch = userSettings.ProfessionalInvertGyroPitch;
        professionalInvertGyroYaw = userSettings.ProfessionalInvertGyroYaw;
        professionalInvertGyroRoll = userSettings.ProfessionalInvertGyroRoll;
        selectedPushRateLabel =
            ViiperPushRateOption.FromLabel(userSettings.PushRateLabel).Label;
        selectedBackendLabel =
            VirtualBackendOption.FromLabel(userSettings.BackendLabel).Label;
        selectedStickProcessingLabel =
            StickProcessingOption.FromLabel(userSettings.StickProcessingLabel).Label;
        audioEndpointGuardEnabled = userSettings.AudioEndpointGuardEnabled;
        selectedMode = ModeFromKey(userSettings.SelectedModeKey);
        inputSource.SetRumbleGain(rumbleMultiplier);
        inputSource.SetStickProcessingMode(SelectedStickProcessingOption.Mode);
        RefreshRuntimeReadiness();
        AppendLog("[SESSION_LOG] " + sessionLog.FilePath);
        if (!string.IsNullOrWhiteSpace(StartupProcessGuard.LastSummary))
        {
            AppendLog(StartupProcessGuard.LastSummary);
        }
        AppendLog("V6.2.20 正式版说明：新和联胜 PS5 保留 HD 音频震动；PS5 / Edge 陀螺仪采用 R7 验证方向；测试模式、HID Audit 和三轴反向调试入口已从正式界面移除。");
        AppendLog("[LOG_POLICY] previous v6 logs are cleaned at startup; manager log limit=" +
                  (SessionLogWriter.MaxLogBytes / 1024 / 1024) + "MB; VIIPER server log level=info.");
        AppendLog("[RUNTIME] " + RuntimeReadinessText);
        AppendLog("[RUMBLE_GAIN] multiplier=" + rumbleMultiplier.ToString("F1"));
        AppendLog("[LINK_TUNING] push_hz=" + SelectedPushRateOption.Hz.ToString("F1") +
                  " gyro_mode=" + ViiperGyroModeOption.Default.Label +
                  " ps5_imu_map=" + Ps5ImuMappingOption.Default.Mapping.TelemetryValue +
                  " ps5_output_imu=" + SelectedPs5OutputImuTuning.TelemetryValue +
                  " gyro_axis_inv=" + default(GyroAxisInversion).TelemetryValue +
                  " stick=" + SelectedStickProcessingOption.Label +
                  " audio_guard=" + audioEndpointGuardEnabled +
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

    public double Ps5GyroScale
    {
        get => ps5GyroScale;
        set
        {
            double normalized =
                V60UserSettings.NormalizePs5GyroScale(value);
            if (Math.Abs(ps5GyroScale - normalized) < 0.001)
            {
                return;
            }

            ps5GyroScale = normalized;
            userSettings.Ps5GyroScalePitch = normalized;
            userSettings.Ps5GyroScaleYaw = normalized;
            userSettings.Ps5GyroScaleRoll = normalized;
            SaveUserSettings("[PS5_IMU]");
            AppendLog("[PS5_IMU] gyro_scale_pitch=" + normalized.ToString("0.##") +
                      " gyro_scale_yaw=" + normalized.ToString("0.##") +
                      " gyro_scale_roll=" + normalized.ToString("0.##") +
                      (Running ? " apply=next_session" : " apply=next_start"));
            if (session is { HasProfessionalImuRuntime: true })
            {
                AppendLog("[PRO_IMU] " + session.ResetProfessionalIntegral() +
                          " reason=ps5_gyro_scale_changed");
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Ps5GyroScaleText));
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public string Ps5GyroScaleText =>
        ps5GyroScale.ToString("0.00") + "x";

    public bool ProfessionalInvertGyroPitch
    {
        get => professionalInvertGyroPitch;
        set
        {
            if (professionalInvertGyroPitch == value)
            {
                return;
            }

            professionalInvertGyroPitch = value;
            ApplyProfessionalGyroInversionSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfessionalGyroInversionSummary));
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public bool ProfessionalInvertGyroYaw
    {
        get => professionalInvertGyroYaw;
        set
        {
            if (professionalInvertGyroYaw == value)
            {
                return;
            }

            professionalInvertGyroYaw = value;
            ApplyProfessionalGyroInversionSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfessionalGyroInversionSummary));
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public bool ProfessionalInvertGyroRoll
    {
        get => professionalInvertGyroRoll;
        set
        {
            if (professionalInvertGyroRoll == value)
            {
                return;
            }

            professionalInvertGyroRoll = value;
            ApplyProfessionalGyroInversionSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfessionalGyroInversionSummary));
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public string ProfessionalGyroInversionSummary =>
        "Pitch=" + (professionalInvertGyroPitch ? "invert" : "normal") +
        ", Yaw=" + (professionalInvertGyroYaw ? "invert" : "normal") +
        ", Roll=" + (professionalInvertGyroRoll ? "invert" : "normal");

    public IReadOnlyList<string> PushRateChoices =>
        ViiperPushRateOption.All.Select(o => o.Label).ToArray();

    public IReadOnlyList<string> BackendChoices =>
        VirtualBackendOption.All.Select(o => o.Label).ToArray();

    public IReadOnlyList<string> StickProcessingChoices =>
        StickProcessingOption.All.Select(o => o.Label).ToArray();

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

    public string SelectedStickProcessing
    {
        get => selectedStickProcessingLabel;
        set
        {
            string normalized = StickProcessingOption.FromLabel(value).Label;
            if (selectedStickProcessingLabel == normalized)
            {
                return;
            }

            selectedStickProcessingLabel = normalized;
            userSettings.StickProcessingLabel = normalized;
            inputSource.SetStickProcessingMode(SelectedStickProcessingOption.Mode);
            SaveUserSettings("[STICK_MODE]");
            AppendLog("[STICK_MODE] selected=" + SelectedStickProcessingOption.Label +
                      " mode=" + SelectedStickProcessingOption.Mode +
                      " apply=immediate");
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public bool AudioEndpointGuardEnabled
    {
        get => audioEndpointGuardEnabled;
        set
        {
            if (audioEndpointGuardEnabled == value)
            {
                return;
            }

            audioEndpointGuardEnabled = value;
            userSettings.AudioEndpointGuardEnabled = value;
            SaveUserSettings("[AUDIO_GUARD]");
            AppendLog("[AUDIO_GUARD] enabled=" + value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioEndpointGuardText));
            OnPropertyChanged(nameof(LinkTuningSummary));
        }
    }

    public string AudioEndpointGuardText => AudioEndpointGuardEnabled
        ? "音频保护已开启：PS5 模式会自动阻止 DualSense 成为默认播放/通信/麦克风。"
        : "音频保护已关闭：Windows 可能把 DualSense 设为默认音频或麦克风。";

    public string LinkTuningSummary =>
        "push=" + SelectedPushRateOption.Hz.ToString("F1") +
        "Hz · ps5_imu=R7 fixed accel x2/x2/-2 gyro pitch-/yaw+/roll+ scale=" +
        SelectedPs5OutputImuTuning.GyroScalePitch.ToString("0.##") + "x" +
        " · stick=" + SelectedStickProcessingOption.Label +
        " · audio_guard=" + (AudioEndpointGuardEnabled ? "on" : "off") +
        " · backend=" + SelectedBackendOption.Label;

    public bool IsDualSenseSelected => selectedMode == ViiperVirtualMode.DualSenseLike;
    public bool IsDualSenseEdgeSelected => selectedMode == ViiperVirtualMode.DualSenseEdge;
    public bool IsDualSenseProfessionalImuSelected => selectedMode == ViiperVirtualMode.DualSenseProfessionalImuTest;
    public bool IsPro2Selected => selectedMode == ViiperVirtualMode.Pro2;
    public bool IsXboxSelected => selectedMode == ViiperVirtualMode.Xbox;
    public bool IsXboxProfessionalImuSelected => selectedMode == ViiperVirtualMode.XboxProfessionalImuTest;
    public bool IsDualSenseActive => Running && activeMode == ViiperVirtualMode.DualSenseLike;
    public bool IsDualSenseEdgeActive => Running && activeMode == ViiperVirtualMode.DualSenseEdge;
    public bool IsDualSenseProfessionalImuActive => Running && activeMode == ViiperVirtualMode.DualSenseProfessionalImuTest;
    public bool IsPro2Active => Running && activeMode == ViiperVirtualMode.Pro2;
    public bool IsXboxActive => Running && activeMode == ViiperVirtualMode.Xbox;
    public bool IsXboxProfessionalImuActive => Running && activeMode == ViiperVirtualMode.XboxProfessionalImuTest;
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
        ViiperVirtualMode.DualSenseEdge => "KRATOS EDGE",
        ViiperVirtualMode.DualSenseProfessionalImuTest => "KRATOS · LAB",
        ViiperVirtualMode.Pro2 => "MARIO",
        ViiperVirtualMode.Xbox => "MASTER CHIEF",
        ViiperVirtualMode.XboxProfessionalImuTest => "MASTER CHIEF · LAB",
        _ => ""
    };
    public string SelectedModeSubtitle => selectedMode switch
    {
        ViiperVirtualMode.DualSenseLike => "新和联胜 · DUALSENSE IDENTITY · 054C:0CE6",
        ViiperVirtualMode.DualSenseEdge => "PS5 EDGE · L4/R4 PADDLES · 054C:0DF2",
        ViiperVirtualMode.DualSenseProfessionalImuTest => "PROFESSIONAL IMU TEST · RAW→G/DPS→DS RAW · HD HAPTIC",
        ViiperVirtualMode.Pro2 => "NINTENDO PROTOCOL · HD RUMBLE · 057E:2069",
        ViiperVirtualMode.Xbox => "XINPUT PROTOCOL · 045E:028E",
        ViiperVirtualMode.XboxProfessionalImuTest => "PROFESSIONAL IMU TEST · DIAGNOSTIC ONLY · XINPUT",
        _ => ""
    };
    public string ModeHeadline => Running && activeMode.HasValue
        ? "ACTIVE LOADOUT · " + ActiveProfile.Label
        : "SELECTED LOADOUT · " + SelectedModeLabel;

    public string ProfessionalBiasStatusText
    {
        get => professionalBiasStatusText;
        private set { professionalBiasStatusText = value; OnPropertyChanged(); }
    }

    public string ProfessionalRawGyroText
    {
        get => professionalRawGyroText;
        private set { professionalRawGyroText = value; OnPropertyChanged(); }
    }

    public string ProfessionalCorrectedGyroText
    {
        get => professionalCorrectedGyroText;
        private set { professionalCorrectedGyroText = value; OnPropertyChanged(); }
    }

    public string ProfessionalOutputGyroText
    {
        get => professionalOutputGyroText;
        private set { professionalOutputGyroText = value; OnPropertyChanged(); }
    }

    public string ProfessionalIntegratedAngleText
    {
        get => professionalIntegratedAngleText;
        private set { professionalIntegratedAngleText = value; OnPropertyChanged(); }
    }

    public string ProfessionalNinetyDegreeTestText
    {
        get => professionalNinetyDegreeTestText;
        private set { professionalNinetyDegreeTestText = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> ProfessionalHidAuditModeChoices =>
        Enum.GetNames<ProfessionalHidAuditMode>();

    public string SelectedProfessionalHidAuditMode
    {
        get => selectedProfessionalHidAuditMode;
        set
        {
            if (!Enum.TryParse(value, out ProfessionalHidAuditMode mode))
            {
                return;
            }

            string normalized = mode.ToString();
            if (selectedProfessionalHidAuditMode == normalized)
            {
                return;
            }

            selectedProfessionalHidAuditMode = normalized;
            string summary = professionalHidAuditController.SetMode(mode);
            ProfessionalHidAuditStatusText = "HID Audit: " + summary;
            AppendLog("[PRO_IMU_AUDIT] mode changed " + summary);
            OnPropertyChanged();
        }
    }

    public string ProfessionalStaticGyroXRaw
    {
        get => professionalStaticGyroXRaw;
        set
        {
            professionalStaticGyroXRaw = value;
            ApplyProfessionalStaticGyroRaw();
            OnPropertyChanged();
        }
    }

    public string ProfessionalStaticGyroYRaw
    {
        get => professionalStaticGyroYRaw;
        set
        {
            professionalStaticGyroYRaw = value;
            ApplyProfessionalStaticGyroRaw();
            OnPropertyChanged();
        }
    }

    public string ProfessionalStaticGyroZRaw
    {
        get => professionalStaticGyroZRaw;
        set
        {
            professionalStaticGyroZRaw = value;
            ApplyProfessionalStaticGyroRaw();
            OnPropertyChanged();
        }
    }

    public string ProfessionalHidAuditStatusText
    {
        get => professionalHidAuditStatusText;
        private set { professionalHidAuditStatusText = value; OnPropertyChanged(); }
    }

    public string LogText => log.ToString();

    public ICommand PingCommand { get; }
    public ICommand InstallUsbipCommand { get; }
    public ICommand StartViiperServerCommand { get; }
    public ICommand ScanPro2InputCommand { get; }
    public ICommand ConnectPro2InputCommand { get; }
    public ICommand DisconnectPro2InputCommand { get; }
    public ICommand FixAudioDefaultsCommand { get; }
    public ICommand DumpControllerEnumerationCommand { get; }
    public ICommand CleanupStaleVirtualDevicesCommand { get; }
    public ICommand ExportDiagnosticsLogCommand { get; }
    public ICommand StartDualSenseCommand { get; }
    public ICommand StartDualSenseEdgeCommand { get; }
    public ICommand StartDualSenseProfessionalImuCommand { get; }
    public ICommand StartPro2Command { get; }
    public ICommand StartXboxCommand { get; }
    public ICommand StartXboxProfessionalImuCommand { get; }
    public ICommand CalibrateGyroBiasCommand { get; }
    public ICommand ResetGyroBiasCommand { get; }
    public ICommand ResetIntegralCommand { get; }
    public ICommand StartPitch90TestCommand { get; }
    public ICommand StartYaw90TestCommand { get; }
    public ICommand StartRoll90TestCommand { get; }
    public ICommand Stop90TestCommand { get; }
    public ICommand PulseGyroXCommand { get; }
    public ICommand PulseGyroYCommand { get; }
    public ICommand PulseGyroZCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearLogCommand { get; }

    private void ExecuteProfessionalImuAction(
        string operation,
        Func<ViiperBridgeSession, string> action)
    {
        if (session is not { HasProfessionalImuRuntime: true } activeSession)
        {
            string message = "[PRO_IMU] " + operation +
                             " ignored: start PS5 / Professional IMU Test or Xbox / Professional IMU Test first.";
            AppendLog(message);
            Status = "请先进入 Professional IMU Test 模式。";
            return;
        }

        string result = action(activeSession);
        AppendLog("[PRO_IMU_UI] " + operation + ": " + result);
        Status = result;
    }

    private void UpdateProfessionalImuSnapshot(ProfessionalImuUiSnapshot snapshot)
    {
        ProfessionalBiasStatusText = snapshot.BiasStatusText;
        ProfessionalRawGyroText = snapshot.RawGyroText;
        ProfessionalCorrectedGyroText = snapshot.CorrectedGyroText;
        ProfessionalOutputGyroText = snapshot.OutputGyroText;
        ProfessionalIntegratedAngleText = snapshot.IntegratedAngleText;
        ProfessionalNinetyDegreeTestText = snapshot.NinetyDegreeTestText;
    }

    private void ApplyProfessionalGyroInversionSettings()
    {
        userSettings.ProfessionalInvertGyroPitch = professionalInvertGyroPitch;
        userSettings.ProfessionalInvertGyroYaw = professionalInvertGyroYaw;
        userSettings.ProfessionalInvertGyroRoll = professionalInvertGyroRoll;
        SaveUserSettings("[PRO_IMU]");
        string summary = ProfessionalGyroInversionSummary;
        AppendLog("[PRO_IMU] gyro_output_inversion " + summary +
                  (session is { HasProfessionalImuRuntime: true }
                      ? " apply=immediate"
                      : " apply=next_professional_session"));
        if (session is { HasProfessionalImuRuntime: true } activeSession)
        {
            AppendLog("[PRO_IMU] " + activeSession.SetProfessionalGyroInversion(
                professionalInvertGyroPitch,
                professionalInvertGyroYaw,
                professionalInvertGyroRoll));
        }
    }

    private void ApplyProfessionalStaticGyroRaw()
    {
        if (!short.TryParse(professionalStaticGyroXRaw, out short x) ||
            !short.TryParse(professionalStaticGyroYRaw, out short y) ||
            !short.TryParse(professionalStaticGyroZRaw, out short z))
        {
            ProfessionalHidAuditStatusText = "HID Audit: static raw 输入无效，范围应为 -32768..32767。";
            return;
        }

        string summary = professionalHidAuditController.SetStaticRaw(x, y, z);
        ProfessionalHidAuditStatusText = "HID Audit: " + summary;
        AppendLog("[PRO_IMU_AUDIT] static raw changed " + summary);
    }

    private void StartProfessionalHidPulse(string axis)
    {
        string summary = professionalHidAuditController.StartPulse(
            axis,
            8192,
            TimeSpan.FromSeconds(2),
            Stopwatch.GetTimestamp());
        selectedProfessionalHidAuditMode = ProfessionalHidAuditMode.ForceFinalGyroSyntheticPulse.ToString();
        OnPropertyChanged(nameof(SelectedProfessionalHidAuditMode));
        ProfessionalHidAuditStatusText = "HID Audit: synthetic pulse " + axis + " +8192 raw for 2s · " + summary;
        AppendLog("[PRO_IMU_AUDIT] synthetic pulse start axis=" + axis +
                  " raw=8192 duration_seconds=2 " + summary);
    }

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
        return await PingCoreAsync(ParsePort(), cancellationToken);
    }

    private async Task<string> PingCoreAsync(int apiPort, CancellationToken cancellationToken)
    {
        var client = new ViiperProtocolClient(Host, apiPort);
        return await client.PingAsync(cancellationToken);
    }

    private async Task StartLocalViiperServerAsync(CancellationToken cancellationToken)
    {
        int requestedApiPort = ParsePort();
        if (!IsLoopbackHost(Host))
        {
            throw new InvalidOperationException("启动本地 VIIPER 时 Host 必须是 localhost、127.0.0.1 或 ::1。");
        }

        DisposeExitedViiperProcess();
        if (viiperProcess is { HasExited: false })
        {
            if (localViiperApiPort.HasValue && localViiperApiPort.Value != requestedApiPort)
            {
                Port = localViiperApiPort.Value.ToString();
                requestedApiPort = localViiperApiPort.Value;
                AppendLog("[VIIPER_SERVER] restored active local API port " + requestedApiPort + ".");
            }
            Status = "本地 VIIPER server 已在运行，pid=" + viiperProcess.Id;
            AppendLog("[VIIPER_SERVER] already_running pid=" + viiperProcess.Id);
            string response = await PingCoreAsync(cancellationToken);
            AppendLog("[PING] " + response);
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

        IReadOnlyList<ViiperPortPair> candidates = BuildViiperPortCandidates(requestedApiPort);
        var failures = new List<string>();
        AppendLog("[VIIPER_PREFLIGHT] requested_api=127.0.0.1:" + requestedApiPort +
                  " candidates=" + string.Join(",", candidates.Select(c => c.ApiPort + "/" + c.UsbPort)));
        foreach (ViiperPortPair candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool portsReady = await PrepareViiperPortCandidateAsync(
                candidate,
                failures,
                cancellationToken);
            if (!portsReady)
            {
                continue;
            }

            ViiperStartupFailure? failure = await TryStartLocalViiperServerAsync(
                exe,
                usbip,
                candidate,
                logRoot,
                cancellationToken);
            if (failure == null)
            {
                return;
            }

            failures.Add(failure.Summary);
            if (failure.PortConflict)
            {
                AppendLog("[VIIPER_PREFLIGHT] retrying alternate port pair after port conflict. failed_api=" +
                          candidate.ApiPort + " failed_usb=" + candidate.UsbPort);
                continue;
            }

            throw new InvalidOperationException(failure.UserMessage);
        }

        throw new InvalidOperationException(
            "VIIPER server 启动失败：所有候选端口都不可用或启动失败。诊断：" +
            string.Join(" | ", failures.Take(8)));
    }

    private async Task<bool> PrepareViiperPortCandidateAsync(
        ViiperPortPair candidate,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        bool apiFree = IsLoopbackTcpPortAvailable(candidate.ApiPort, out string apiDetail);
        bool usbFree = IsLoopbackTcpPortAvailable(candidate.UsbPort, out string usbDetail);
        AppendLog("[VIIPER_PREFLIGHT] candidate source=" + candidate.Source +
                  " api=127.0.0.1:" + candidate.ApiPort + " " + apiDetail +
                  " usb=127.0.0.1:" + candidate.UsbPort + " " + usbDetail);

        if (!apiFree)
        {
            string? ping = await TryPingViiperOnPortAsync(
                candidate.ApiPort,
                TimeSpan.FromMilliseconds(850),
                cancellationToken);
            if (ping != null && LooksLikeViiperPing(ping))
            {
                AppendLog("[VIIPER_PREFLIGHT] api port " + candidate.ApiPort +
                          " is occupied by a VIIPER server; cleaning stale process before retry. " + ping);
                AppendLog(StartupProcessGuard.CleanupConflictingProcesses());
                await Task.Delay(900, cancellationToken);
                apiFree = IsLoopbackTcpPortAvailable(candidate.ApiPort, out apiDetail);
                usbFree = IsLoopbackTcpPortAvailable(candidate.UsbPort, out usbDetail);
                AppendLog("[VIIPER_PREFLIGHT] after cleanup api=127.0.0.1:" + candidate.ApiPort + " " + apiDetail +
                          " usb=127.0.0.1:" + candidate.UsbPort + " " + usbDetail);
            }
        }

        if (!apiFree)
        {
            string summary = "api " + candidate.ApiPort + " unavailable: " + apiDetail;
            failures.Add(summary);
            AppendLog("[VIIPER_PREFLIGHT] " + summary + " auto_fallback=1");
            return false;
        }

        if (!usbFree)
        {
            string summary = "usb " + candidate.UsbPort + " unavailable: " + usbDetail;
            failures.Add(summary);
            AppendLog("[VIIPER_PREFLIGHT] " + summary + " auto_fallback=1");
            return false;
        }

        return true;
    }

    private async Task<ViiperStartupFailure?> TryStartLocalViiperServerAsync(
        string exe,
        UsbipRuntime usbip,
        ViiperPortPair ports,
        string logRoot,
        CancellationToken cancellationToken)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string logPath = Path.Combine(
            logRoot,
            "viiper_server_" + stamp + "_api" + ports.ApiPort + "_usb" + ports.UsbPort + ".log");
        string args = "server --api.addr=127.0.0.1:" + ports.ApiPort +
                      " --usb.addr=127.0.0.1:" + ports.UsbPort +
                      " --api.device-handler-connect-timeout=60s" +
                      " --usb.write-batch-flush-interval=0ms" +
                      " --update-notify=none" +
                      " --log.level=info" +
                      " --log.file=\"" + logPath + "\"";
        ProcessStartInfo startInfo = new()
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["PATH"] = UsbipRuntimeLocator.BuildPathWithUsbipDirectory(
            startInfo.Environment.TryGetValue("PATH", out string? path)
                ? path ?? ""
                : Environment.GetEnvironmentVariable("PATH") ?? "",
            usbip);
        startInfo.WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;

        ProcessOutputTail processOutput = new(80);
        Process? started = Process.Start(startInfo);
        if (started == null)
        {
            return new ViiperStartupFailure(
                false,
                "process_start returned null",
                "无法启动 VIIPER server：Process.Start 返回空进程。");
        }

        processOutput.Attach(started);
        viiperProcess = started;
        localViiperApiPort = ports.ApiPort;
        Port = ports.ApiPort.ToString();

        Status = "正在启动本地 VIIPER server，pid=" + started.Id;
        AppendLog("[USBIP] using " + usbip.ExePath);
        AppendLog("[VIIPER_SERVER] launch pid=" + started.Id +
                  " api=127.0.0.1:" + ports.ApiPort +
                  " usb=127.0.0.1:" + ports.UsbPort +
                  " exe=" + exe +
                  " args=" + args +
                  " log=" + logPath);

        ViiperStartupFailure? failure = await WaitForViiperStartupAsync(
            started,
            ports,
            logPath,
            processOutput,
            cancellationToken);
        if (failure != null)
        {
            await CleanupFailedViiperProcessAsync(started);
            if (ReferenceEquals(viiperProcess, started))
            {
                viiperProcess = null;
                localViiperApiPort = null;
            }
            return failure;
        }

        return null;
    }

    private async Task<ViiperStartupFailure?> WaitForViiperStartupAsync(
        Process process,
        ViiperPortPair ports,
        string logPath,
        ProcessOutputTail processOutput,
        CancellationToken cancellationToken)
    {
        string lastPingError = "";
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            await Task.Delay(250, cancellationToken);
            if (process.HasExited)
            {
                return BuildViiperStartupFailure(
                    process,
                    ports,
                    logPath,
                    processOutput,
                    "early_exit",
                    lastPingError);
            }

            using var pingTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            pingTimeout.CancelAfter(TimeSpan.FromMilliseconds(650));
            try
            {
                string ping = await PingCoreAsync(ports.ApiPort, pingTimeout.Token);
                Status = "本地 VIIPER server 已就绪，pid=" + process.Id + "。";
                AppendLog("[PING] " + ping);
                if (ports.ApiPort != 3242)
                {
                    AppendLog("[VIIPER_SERVER] using alternate ports api=127.0.0.1:" +
                              ports.ApiPort + " usb=127.0.0.1:" + ports.UsbPort +
                              " reason=preferred_port_unavailable_or_failed");
                }
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastPingError = "ping timeout attempt=" + attempt;
            }
            catch (Exception ex)
            {
                lastPingError = FirstLine(ex.Message);
            }
        }

        return BuildViiperStartupFailure(
            process,
            ports,
            logPath,
            processOutput,
            "api_ping_timeout",
            lastPingError);
    }

    private ViiperStartupFailure BuildViiperStartupFailure(
        Process process,
        ViiperPortPair ports,
        string logPath,
        ProcessOutputTail processOutput,
        string stage,
        string lastPingError)
    {
        int? exitCode = null;
        if (process.HasExited)
        {
            exitCode = process.ExitCode;
        }

        string logTail = ReadTailText(logPath, 40);
        string processTail = processOutput.Snapshot();
        string combined = logTail + "\n" + processTail + "\n" + lastPingError;
        bool portConflict = LooksLikePortConflict(combined);
        string category = portConflict
            ? "port_conflict"
            : LooksLikeUsbipDriverIssue(combined)
                ? "usbip_driver_or_permission"
                : stage == "api_ping_timeout"
                    ? "api_unreachable"
                    : "early_exit_unknown";
        string exitText = exitCode.HasValue ? exitCode.Value.ToString() : "running";
        AppendLog("[VIIPER_DIAG] category=" + category +
                  " stage=" + stage +
                  " exit=" + exitText +
                  " api=127.0.0.1:" + ports.ApiPort +
                  " usb=127.0.0.1:" + ports.UsbPort +
                  " last_ping=\"" + SanitizeForLog(lastPingError) + "\"" +
                  " log=" + logPath);
        if (!string.IsNullOrWhiteSpace(logTail))
        {
            AppendLog("[VIIPER_LOG_TAIL]\n" + logTail);
        }
        if (!string.IsNullOrWhiteSpace(processTail))
        {
            AppendLog("[VIIPER_PROCESS_TAIL]\n" + processTail);
        }

        string hint = category switch
        {
            "port_conflict" =>
                "端口被占用、保留或权限拒绝。程序会自动尝试下一组 API/USBIP 端口；若全部失败，请关闭旧 VIIPER/USBIP/占用端口程序或重启 Windows。",
            "usbip_driver_or_permission" =>
                "更像 USBIP 驱动、权限或安全软件问题。请安装/修复 usbip-win2；若刚安装完成，请重启 Windows 后再试。",
            "api_unreachable" =>
                "VIIPER 进程未退出，但 API 端口一直无法响应。程序已清理该进程，避免留下半启动状态。",
            _ =>
                "VIIPER 在写出明确错误前退出。EXE 已回灌 viiper_server 日志尾部和 stdout/stderr 尾部，请按上方 category 与 tail 定位。"
        };
        string summary = "category=" + category +
                         " exit=" + exitText +
                         " api=" + ports.ApiPort +
                         " usb=" + ports.UsbPort +
                         (string.IsNullOrWhiteSpace(lastPingError) ? "" : " last_ping=" + lastPingError);
        string userMessage =
            "VIIPER server 启动失败：" + summary + "。日志：" + logPath + "。" + hint;
        return new ViiperStartupFailure(portConflict, summary, userMessage);
    }

    private async Task CleanupFailedViiperProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            AppendLog("[VIIPER_DIAG] failed startup cleanup warning: " + ex.Message);
        }
        finally
        {
            process.Dispose();
        }
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
                string diagnostic = inputSource.LastScanDiagnostic;
                InputStatus = string.IsNullOrWhiteSpace(diagnostic)
                    ? "没有扫描到真实 Pro2 BLE。唤醒手柄并确保没有被 ESP32、Switch、手机或旧进程占用。"
                    : "没有扫描到真实 Pro2 BLE：" + diagnostic;
                Status = InputStatus;
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
        int failedAttemptStreak = 0;
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
                    failedAttemptStreak = 0;
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
                    failedAttemptStreak++;
                    TimeSpan retryDelay = Pro2ReconnectDelayForAttempt(failedAttemptStreak);
                    AppendLog("[PRO2_AUTO] attempt=" + attempt + " failed: " + ex);
                    InputStatus = "本轮连接异常：" + FirstLine(ex.Message) + "。" +
                                  retryDelay.TotalSeconds.ToString("F1") +
                                  " 秒后自动继续，连续失败会退避以避免手柄进入冷却。";
                    Status = "Pro2 自动重连仍在运行；失败退避中，不会反复猛连手柄。";
                    AppendLog("[PRO2_AUTO] attempt=" + attempt +
                              " retry_delay_ms=" + retryDelay.TotalMilliseconds.ToString("F0") +
                              " failed_streak=" + failedAttemptStreak +
                              " reason=connect_exception cooldown_guard=1.");
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }
                RaiseConnectionStateChanged();

                if (!inputSource.IsRunning)
                {
                    failedAttemptStreak++;
                    TimeSpan retryDelay = Pro2ReconnectDelayForAttempt(failedAttemptStreak);
                    string diagnostic = inputSource.LastScanDiagnostic;
                    InputStatus = string.IsNullOrWhiteSpace(diagnostic)
                        ? "本轮未连接到 Pro2，" + retryDelay.TotalSeconds.ToString("F1") +
                          " 秒后自动继续扫描。可在手动控制区停止自动重连。"
                        : "本轮未连接到 Pro2：" + diagnostic + " " +
                          retryDelay.TotalSeconds.ToString("F1") + " 秒后自动继续扫描。";
                    Status = failedAttemptStreak >= 4
                        ? "自动重连持续运行中。已进入温和退避，避免连续连接触发手柄冷却。"
                        : "自动重连持续运行中。本轮没有找到可用 Pro2，将自动开始下一轮。";
                    AppendLog("[PRO2_AUTO] attempt=" + attempt +
                              " no_live; retry_delay_ms=" + retryDelay.TotalMilliseconds.ToString("F0") +
                              " failed_streak=" + failedAttemptStreak +
                              " cooldown_guard=1.");
                    await Task.Delay(retryDelay, cancellationToken);
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

    private static TimeSpan Pro2ReconnectDelayForAttempt(int failedAttemptStreak)
    {
        if (failedAttemptStreak <= 3)
        {
            return TimeSpan.FromMilliseconds(2500);
        }
        if (failedAttemptStreak <= 6)
        {
            return TimeSpan.FromSeconds(5);
        }
        if (failedAttemptStreak <= 10)
        {
            return TimeSpan.FromSeconds(10);
        }

        return TimeSpan.FromSeconds(30);
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
            ViiperGyroModeOption gyro = ViiperGyroModeOption.Default;
            GyroAxisInversion gyroAxisInversion = default;
            Ps5ImuMappingOption ps5ImuMapping = Ps5ImuMappingOption.Default;
            Ps5OutputImuTuning ps5OutputImuTuning = SelectedPs5OutputImuTuning;
            ProfessionalImuOptions professionalImuOptions = profile.IsProfessionalImuTest
                ? ProfessionalImuOptions.ForTestModes(
                    ps5OutputImuTuning,
                    professionalInvertGyroPitch,
                    professionalInvertGyroYaw,
                    professionalInvertGyroRoll)
                : ProfessionalImuOptions.Default;
            string professionalTelemetry = professionalImuOptions.Enabled
                ? professionalImuOptions.TelemetryValue
                : "disabled";
            ViiperDeviceProfile runtimeProfile = profile with { SendInterval = pushRate.Interval };
            AppendLog("[START] mode=" + runtimeProfile.Label +
                      " type=" + runtimeProfile.DeviceType +
                      " push_hz=" + pushRate.Hz.ToString("F1") +
                      " interval_ms=" + pushRate.Interval.TotalMilliseconds.ToString("F1") +
                      " gyro_mode=" + gyro.Label +
                      " ps5_imu_map=" + ps5ImuMapping.Mapping.TelemetryValue +
                      " ps5_output_imu=" + ps5OutputImuTuning.TelemetryValue +
                      " gyro_axis_inv=" + gyroAxisInversion.TelemetryValue +
                      " professional_imu=" + professionalTelemetry +
                      " backend=" + SelectedBackendOption.Label +
                      " flush=immediate");
            if (runtimeProfile.Mode == ViiperVirtualMode.DualSenseLike ||
                runtimeProfile.Mode == ViiperVirtualMode.DualSenseProfessionalImuTest)
            {
                AppendDualSenseAudioEndpointHint();
            }
            else if (runtimeProfile.Mode == ViiperVirtualMode.DualSenseEdge)
            {
                AppendLog("[EDGE_IDENTITY] type=dualsenseedge vid=0x054c pid=0x0df2 product=\"DualSense Edge Wireless Controller\" paddles=L4/R4 feedback=ordinary_6byte edge_hd_status=blocked_by_viiper_feedback_contract");
            }
            if (runtimeProfile.Mode == ViiperVirtualMode.DualSenseProfessionalImuTest)
            {
                AppendLog("[PRO_IMU] PS5 test mode uses DualSense haptic identity, so ordinary PS5 HD audio-to-rumble path is reused.");
            }
            if (runtimeProfile.Mode == ViiperVirtualMode.XboxProfessionalImuTest)
            {
                AppendLog("[PRO_IMU] Xbox test mode does not claim native gyro; XboxOutputMode=Off by default, IMU is diagnostic CSV/log only.");
            }
            if (runtimeProfile.IsProfessionalImuTest)
            {
                AppendLog("[PRO_IMU] Professional IMU mode entered output_mode=" + runtimeProfile.Label +
                          " sample_handling=" + professionalImuOptions.OutputSampleMode +
                          " source_rate=current_ble" +
                          " virtual_report_rate=" + professionalImuOptions.OutputReportRateMode +
                          " ProjectAccel=+X,+Y,-Z ProjectGyro=+X,-Z,+Y" +
                          " GyroScale=" + ps5OutputImuTuning.GyroScalePitch.ToString("0.###") +
                          "," + ps5OutputImuTuning.GyroScaleYaw.ToString("0.###") +
                          "," + ps5OutputImuTuning.GyroScaleRoll.ToString("0.###") +
                          " DualSenseGyroRawPerDps=" + ProfessionalImuConverter.ProfessionalDualSenseGyroRawPerDps.ToString("0.###") +
                          " output_gyro_invert_pitch=" + professionalInvertGyroPitch.ToString().ToLowerInvariant() +
                          " output_gyro_invert_yaw=" + professionalInvertGyroYaw.ToString().ToLowerInvariant() +
                          " output_gyro_invert_roll=" + professionalInvertGyroRoll.ToString().ToLowerInvariant() +
                          " bias_status=NotCalibrated bias_source=none" +
                          " professional_gyro_uncalibrated_behavior=" + professionalImuOptions.ProfessionalGyroUncalibratedBehavior +
                          " output_gyro_muted_until_calibrated=true" +
                          " integral_state=Disabled integral_running=false" +
                          " hid_audit_builder=" + DualSenseProfessionalHidLayout.BuilderName +
                          " hid_audit_report_id=" + DualSenseProfessionalHidLayout.ReportIdLabel +
                          " hid_audit_report_len=" + DualSenseProfessionalHidLayout.ReportLength +
                          " hid_audit_gyro_offsets=" + DualSenseProfessionalHidLayout.GyroXOffset +
                          "," + DualSenseProfessionalHidLayout.GyroYOffset +
                          "," + DualSenseProfessionalHidLayout.GyroZOffset +
                          " hid_audit_accel_offsets=" + DualSenseProfessionalHidLayout.AccelXOffset +
                          "," + DualSenseProfessionalHidLayout.AccelYOffset +
                          "," + DualSenseProfessionalHidLayout.AccelZOffset +
                          " legacy_ps5_mapper_after_professional=false");
            }
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
                gyro.Mode,
                gyroAxisInversion,
                ps5ImuMapping.Mapping,
                ps5OutputImuTuning,
                professionalImuOptions,
                professionalHidAuditController,
                new Progress<ProfessionalImuUiSnapshot>(UpdateProfessionalImuSnapshot));
            session = createdSession;
            await createdSession.StartAsync(cancellationToken);
            SetActiveMode(runtimeProfile.Mode);
            if ((runtimeProfile.Mode == ViiperVirtualMode.DualSenseLike ||
                 runtimeProfile.Mode == ViiperVirtualMode.DualSenseProfessionalImuTest) &&
                AudioEndpointGuardEnabled)
            {
                await ApplyDualSenseAudioGuardAsync(cancellationToken);
            }
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

    private void AppendDualSenseAudioEndpointHint()
    {
        AppendLog("[AUDIO_HINT] PS5 mode exposes a DualSense audio/haptic endpoint for HD vibration. " +
                  "Do not set \"DualSense Wireless Controller\" as the Windows default speaker/headset. " +
                  "If desktop/game audio stutters or disappears, set the default playback device back to the real speakers/headset; " +
                  "the DualSense endpoint should be used by games only for controller haptic audio. The app will now guard defaults automatically.");
    }

    private async Task ApplyDualSenseAudioGuardAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(900, cancellationToken);
            RunAudioEndpointGuard();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppendLog("[AUDIO_GUARD] warning: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private Task FixAudioDefaultsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RunAudioEndpointGuard();
        Status = "已检查 Windows 默认播放/通信/麦克风，DualSense 不会作为默认音频设备。";
        return Task.CompletedTask;
    }

    private async Task DumpControllerEnumerationAsync(CancellationToken cancellationToken)
    {
        var client = new ViiperProtocolClient(Host, ParsePort());
        IReadOnlyList<string> lines =
            await ControllerEnumerationDiagnostics.DumpAsync(
                client,
                sessionLog.FilePath,
                cancellationToken);
        foreach (string line in lines)
        {
            AppendLog(line);
        }
        Status = "设备枚举诊断已写入日志。重点看 [VIIPER_DUMP]、[USBIP_PORT]、[PNP_HID]。";
    }

    private async Task CleanupStaleVirtualDevicesAsync(CancellationToken cancellationToken)
    {
        if (!IsLoopbackHost(Host))
        {
            throw new InvalidOperationException("清理残留虚拟设备只允许本地 VIIPER（localhost/127.0.0.1），避免误删远端 USBIP bus。");
        }

        await StopAutoReconnectAsync();
        await StopSessionAsync(updateStatus: false);
        var client = new ViiperProtocolClient(Host, ParsePort());
        IReadOnlyList<string> lines =
            await ControllerEnumerationDiagnostics.CleanupStaleVirtualDevicesAsync(
                client,
                cancellationToken);
        foreach (string line in lines)
        {
            AppendLog(line);
        }
        Status = "残留虚拟设备清理已完成。若 [PNP_HID] 仍有 If_Hid，请根据 MI/usage 判断是否是描述符命名问题。";
    }

    private async Task ExportDiagnosticsLogAsync(CancellationToken cancellationToken)
    {
        var client = new ViiperProtocolClient(Host, ParsePort());
        IReadOnlyList<string> dump =
            await ControllerEnumerationDiagnostics.DumpAsync(
                client,
                sessionLog.FilePath,
                cancellationToken);
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRO2WirelessReceiverControlBoard",
            "v6_logs");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "diagnostics_v6_2_20_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
        var builder = new StringBuilder();
        builder.AppendLine("# V6.2.20 diagnostics export");
        builder.AppendLine("# session_log=" + sessionLog.FilePath);
        builder.AppendLine();
        foreach (string line in dump)
        {
            builder.AppendLine(line);
        }
        builder.AppendLine();
        builder.AppendLine("# UI log snapshot");
        builder.Append(LogText);
        await File.WriteAllTextAsync(
            path,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        AppendLog("[DIAG_EXPORT] path=\"" + path + "\"");
        Status = "诊断包已导出：" + path;
    }

    private void RunAudioEndpointGuard()
    {
        foreach (string line in AudioEndpointGuard.EnsureDualSenseIsNotDefault())
        {
            AppendLog(line);
        }
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
            "v6.2.20",
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

    private static IReadOnlyList<ViiperPortPair> BuildViiperPortCandidates(int requestedApiPort)
    {
        var candidates = new List<ViiperPortPair>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(int apiPort, string source)
        {
            int usbPort = DeriveUsbPort(apiPort);
            if (!IsValidTcpPort(apiPort) ||
                !IsValidTcpPort(usbPort) ||
                apiPort == usbPort)
            {
                return;
            }

            string key = apiPort + "/" + usbPort;
            if (seen.Add(key))
            {
                candidates.Add(new ViiperPortPair(apiPort, usbPort, source));
            }
        }

        Add(requestedApiPort, "requested");
        if (requestedApiPort != 3242)
        {
            Add(requestedApiPort + 100, "requested_plus_100");
            Add(requestedApiPort + 200, "requested_plus_200");
            Add(requestedApiPort + 1000, "requested_plus_1000");
        }

        Add(33242, "fallback_1");
        Add(34242, "fallback_2");
        Add(35242, "fallback_3");
        Add(36242, "fallback_4");
        Add(37242, "fallback_5");
        return candidates;
    }

    private static int DeriveUsbPort(int apiPort)
    {
        if (apiPort == 3242)
        {
            return 3241;
        }

        return apiPort == 1 ? 2 : apiPort - 1;
    }

    private static bool IsValidTcpPort(int port)
    {
        return port is >= 1 and <= 65535;
    }

    private static bool IsLoopbackTcpPortAvailable(int port, out string detail)
    {
        if (!IsValidTcpPort(port))
        {
            detail = "invalid_port";
            return false;
        }

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();
            detail = "free";
            return true;
        }
        catch (SocketException ex)
        {
            detail = "busy socket_error=" + ex.SocketErrorCode + " message=\"" + SanitizeForLog(ex.Message) + "\"";
            return false;
        }
        catch (Exception ex)
        {
            detail = "probe_error=" + ex.GetType().Name + " message=\"" + SanitizeForLog(ex.Message) + "\"";
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }

    private async Task<string?> TryPingViiperOnPortAsync(
        int apiPort,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await PingCoreAsync(apiPort, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadTailText(string path, int maxLines)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "";
            }

            return string.Join(
                Environment.NewLine,
                File.ReadLines(path)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .TakeLast(maxLines));
        }
        catch (Exception ex)
        {
            return "read_tail_failed " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    private static bool LooksLikePortConflict(string text)
    {
        string value = text ?? "";
        return value.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("only one usage of each socket address", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("forbidden by its access permissions", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("通常每个套接字地址", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("bind:", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("listen tcp", StringComparison.OrdinalIgnoreCase) &&
               value.Contains("permission denied", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUsbipDriverIssue(string text)
    {
        string value = text ?? "";
        return value.Contains("usbip", StringComparison.OrdinalIgnoreCase) &&
               (value.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("attach", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("denied", StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeForLog(string value)
    {
        return (value ?? "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\"", "'");
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

    private sealed record ViiperPortPair(
        int ApiPort,
        int UsbPort,
        string Source);

    private sealed record ViiperStartupFailure(
        bool PortConflict,
        string Summary,
        string UserMessage);

    private sealed class ProcessOutputTail
    {
        private readonly int maxLines;
        private readonly Queue<string> lines = new();
        private readonly object gate = new();

        public ProcessOutputTail(int maxLines)
        {
            this.maxLines = Math.Max(4, maxLines);
        }

        public void Attach(Process process)
        {
            process.OutputDataReceived += (_, e) => Add("stdout", e.Data);
            process.ErrorDataReceived += (_, e) => Add("stderr", e.Data);
            try
            {
                process.BeginOutputReadLine();
            }
            catch (Exception ex)
            {
                Add("stdout_capture", ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Add("stderr_capture", ex.GetType().Name + ": " + ex.Message);
            }
        }

        public string Snapshot()
        {
            lock (gate)
            {
                return string.Join(Environment.NewLine, lines);
            }
        }

        private void Add(string stream, string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (gate)
            {
                lines.Enqueue(stream + ": " + line);
                while (lines.Count > maxLines)
                {
                    lines.Dequeue();
                }
            }
        }
    }

    private ViiperDeviceProfile SelectedProfile => ProfileFor(selectedMode);
    private ViiperDeviceProfile ActiveProfile => ProfileFor(activeMode ?? selectedMode);
    private ViiperPushRateOption SelectedPushRateOption =>
        ViiperPushRateOption.FromLabel(selectedPushRateLabel);
    private VirtualBackendOption SelectedBackendOption =>
        VirtualBackendOption.FromLabel(selectedBackendLabel);
    private StickProcessingOption SelectedStickProcessingOption =>
        StickProcessingOption.FromLabel(selectedStickProcessingLabel);
    private Ps5OutputImuTuning SelectedPs5OutputImuTuning =>
        Ps5OutputImuTuning.Default;

    private static ViiperDeviceProfile ProfileFor(ViiperVirtualMode mode)
    {
        return mode switch
        {
            ViiperVirtualMode.DualSenseLike => ViiperDeviceProfile.DualSenseLike,
            ViiperVirtualMode.DualSenseEdge => ViiperDeviceProfile.DualSenseEdge,
            ViiperVirtualMode.Pro2 => ViiperDeviceProfile.Pro2,
            ViiperVirtualMode.Xbox => ViiperDeviceProfile.Xbox,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static ViiperVirtualMode ModeFromKey(string? key)
    {
        return V60UserSettings.NormalizeModeKey(key) switch
        {
            "dualsenseedge" => ViiperVirtualMode.DualSenseEdge,
            "pro2" => ViiperVirtualMode.Pro2,
            "xbox" => ViiperVirtualMode.Xbox,
            _ => ViiperVirtualMode.DualSenseLike
        };
    }

    private static string ModeKey(ViiperVirtualMode mode)
    {
        return mode switch
        {
            ViiperVirtualMode.DualSenseEdge => "dualsenseedge",
            ViiperVirtualMode.Pro2 => "pro2",
            ViiperVirtualMode.Xbox => "xbox",
            _ => "dualsense"
        };
    }

    private void SelectMode(ViiperVirtualMode mode)
    {
        if (selectedMode == mode)
        {
            return;
        }

        selectedMode = mode;
        userSettings.SelectedModeKey = ModeKey(mode);
        SaveUserSettings("[MODE_SELECT]");
        OnPropertyChanged(nameof(IsDualSenseSelected));
        OnPropertyChanged(nameof(IsDualSenseEdgeSelected));
        OnPropertyChanged(nameof(IsDualSenseProfessionalImuSelected));
        OnPropertyChanged(nameof(IsPro2Selected));
        OnPropertyChanged(nameof(IsXboxSelected));
        OnPropertyChanged(nameof(IsXboxProfessionalImuSelected));
        OnPropertyChanged(nameof(SelectedModeLabel));
        OnPropertyChanged(nameof(SelectedHeroName));
        OnPropertyChanged(nameof(SelectedModeSubtitle));
        OnPropertyChanged(nameof(ModeHeadline));
    }

    private void SetActiveMode(ViiperVirtualMode? mode)
    {
        activeMode = mode;
        OnPropertyChanged(nameof(IsDualSenseActive));
        OnPropertyChanged(nameof(IsDualSenseEdgeActive));
        OnPropertyChanged(nameof(IsDualSenseProfessionalImuActive));
        OnPropertyChanged(nameof(IsPro2Active));
        OnPropertyChanged(nameof(IsXboxActive));
        OnPropertyChanged(nameof(IsXboxProfessionalImuActive));
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
        if (log.Length > MaxUiLogCharacters)
        {
            log.Remove(0, log.Length - TrimmedUiLogCharacters);
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
