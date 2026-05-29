using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Y700Switch2Manager;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SerialDeviceClient serial = new();
    private readonly HidFeatureControlClient hidFeature = new();
    private readonly BulkControlClient bulk = new();
    private readonly StringBuilder log = new();
    private readonly DispatcherTimer statusTimer = new();

    private string? selectedPort;
    private string connectionStatus = "Disconnected";
    private string firmwareVersion = "unknown";
    private string deviceMode = "unknown";
    private string usbStatus = "unknown";
    private string bulkStatus = "unknown";
    private string hidGuardStatus = "unknown";
    private string bleStatus = "unknown";
    private string bleAutoStatus = "unknown";
    private string bleTarget = "";
    private string hidStatus = "unknown";
    private string liveStatus = "none";
    private int liveUpdates;
    private int liveAgeMs = -1;
    private int reportRateHz = 125;
    private int hidOutCount;
    private string hidOutLast = "none";
    private int hidGetCount;
    private string hidGetLast = "none";
    private int bulkRxCount;
    private int bulkTxCount;
    private int bulkTxDoneCount;
    private int bulkTxSentBytes;
    private string bulkLast = "none";
    private int bulkRxLen;
    private int bulkTxLen;
    private string bulkPending = "0/0";
    private string rumbleStatus = "idle";
    private int rumbleUpdates;
    private int rumbleWrites;
    private int rumbleStops;
    private int rumbleErrors;
    private int rumbleScalePercent = 100;
    private int rumbleHoldMs = 180;
    private int rumbleTickMs = 20;
    private int rumbleStopPackets = 3;
    private string bleConnectTarget = "";
    private string customCommand = "";
    private string lastDeviceMessage = "";
    private string lastCommandJson = "";
    private string scriptOutput = "";
    private string portHint = "Refresh ports to find the CH343P control port.";
    private int reportActualMilliHz;
    private int reportSent;
    private int reportFailed;
    private int reportLastGapUs;
    private int reportMaxGapUs;
    private int bleInputActualMilliHz;
    private int bleInputLastGapUs;
    private int bleInputMaxGapUs;
    private int bleConnIntervalUnits;
    private int bleConnIntervalUs;
    private int bleConnLatency;
    private int bleConnUpdateStartRc = -1;
    private int bleConnUpdateStatus = -1;
    private int bleConnUpdateRequests;

    public ObservableCollection<string> Ports { get; } = new();
    public ObservableCollection<int> ReportRatePresets { get; } = new(new[] { 60, 125, 250, 500, 1000 });
    public ManagerSettings Settings { get; } = ManagerSettings.Load();
    public bool AutoScroll { get; set; } = true;

    public string? SelectedPort { get => selectedPort; set { selectedPort = value; OnPropertyChanged(); } }
    public string ConnectionStatus { get => connectionStatus; set { connectionStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionLampBrush)); } }
    public string FirmwareVersion { get => firmwareVersion; set { firmwareVersion = value; OnPropertyChanged(); } }
    public string DeviceMode { get => deviceMode; set { deviceMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsbIdentityText)); } }
    public string UsbStatus { get => usbStatus; set { usbStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsbLampBrush)); OnPropertyChanged(nameof(UsbRecognitionText)); } }
    public string BulkStatus { get => bulkStatus; set { bulkStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkLampBrush)); OnPropertyChanged(nameof(UsbRecognitionText)); } }
    public string HidGuardStatus { get => hidGuardStatus; set { hidGuardStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsbRecognitionText)); } }
    public string BleStatus { get => bleStatus; set { bleStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleLampBrush)); } }
    public string BleAutoStatus { get => bleAutoStatus; set { bleAutoStatus = value; OnPropertyChanged(); } }
    public string BleTarget { get => bleTarget; set { bleTarget = value; OnPropertyChanged(); } }
    public string HidStatus { get => hidStatus; set { hidStatus = value; OnPropertyChanged(); } }
    public string LiveStatus { get => liveStatus; set { liveStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(LiveLampBrush)); } }
    public int LiveUpdates { get => liveUpdates; set { liveUpdates = value; OnPropertyChanged(); } }
    public int LiveAgeMs { get => liveAgeMs; set { liveAgeMs = value; OnPropertyChanged(); } }
    public int ReportRateHz { get => reportRateHz; set { reportRateHz = Math.Clamp(value, 20, 1000); OnPropertyChanged(); } }
    public int HidOutCount { get => hidOutCount; set { hidOutCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HidOutText)); } }
    public string HidOutLast { get => hidOutLast; set { hidOutLast = value; OnPropertyChanged(); OnPropertyChanged(nameof(HidOutText)); } }
    public int HidGetCount { get => hidGetCount; set { hidGetCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HidGetText)); } }
    public string HidGetLast { get => hidGetLast; set { hidGetLast = value; OnPropertyChanged(); OnPropertyChanged(nameof(HidGetText)); } }
    public int BulkRxCount { get => bulkRxCount; set { bulkRxCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkCountsText)); } }
    public int BulkTxCount { get => bulkTxCount; set { bulkTxCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkCountsText)); } }
    public int BulkTxDoneCount { get => bulkTxDoneCount; set { bulkTxDoneCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkCountsText)); } }
    public int BulkTxSentBytes { get => bulkTxSentBytes; set { bulkTxSentBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkCountsText)); } }
    public string BulkLast { get => bulkLast; set { bulkLast = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkDetailText)); } }
    public int BulkRxLen { get => bulkRxLen; set { bulkRxLen = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkDetailText)); } }
    public int BulkTxLen { get => bulkTxLen; set { bulkTxLen = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkDetailText)); } }
    public string BulkPending { get => bulkPending; set { bulkPending = value; OnPropertyChanged(); OnPropertyChanged(nameof(BulkDetailText)); } }
    public string RumbleStatus { get => rumbleStatus; set { rumbleStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(RumbleLampBrush)); } }
    public int RumbleUpdates { get => rumbleUpdates; set { rumbleUpdates = value; OnPropertyChanged(); } }
    public int RumbleWrites { get => rumbleWrites; set { rumbleWrites = value; OnPropertyChanged(); } }
    public int RumbleStops { get => rumbleStops; set { rumbleStops = value; OnPropertyChanged(); } }
    public int RumbleErrors { get => rumbleErrors; set { rumbleErrors = value; OnPropertyChanged(); OnPropertyChanged(nameof(RumbleLampBrush)); } }
    public int RumbleScalePercent { get => rumbleScalePercent; set { rumbleScalePercent = Math.Clamp(value, 10, 250); OnPropertyChanged(); } }
    public int RumbleHoldMs { get => rumbleHoldMs; set { rumbleHoldMs = Math.Clamp(value, 50, 1000); OnPropertyChanged(); } }
    public int RumbleTickMs { get => rumbleTickMs; set { rumbleTickMs = Math.Clamp(value, 5, 50); OnPropertyChanged(); } }
    public int RumbleStopPackets { get => rumbleStopPackets; set { rumbleStopPackets = Math.Clamp(value, 1, 8); OnPropertyChanged(); } }
    public string BleConnectTarget { get => bleConnectTarget; set { bleConnectTarget = value; OnPropertyChanged(); } }
    public string CustomCommand { get => customCommand; set { customCommand = value; OnPropertyChanged(); } }
    public string LastDeviceMessage { get => lastDeviceMessage; set { lastDeviceMessage = value; OnPropertyChanged(); } }
    public string LastCommandJson { get => lastCommandJson; set { lastCommandJson = value; OnPropertyChanged(); } }
    public string ScriptOutput { get => scriptOutput; set { scriptOutput = value; OnPropertyChanged(); } }
    public string PortHint { get => portHint; set { portHint = value; OnPropertyChanged(); } }
    public int ReportActualMilliHz { get => reportActualMilliHz; set { reportActualMilliHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReportActualText)); } }
    public int ReportSent { get => reportSent; set { reportSent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReportCounterText)); } }
    public int ReportFailed { get => reportFailed; set { reportFailed = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReportCounterText)); } }
    public int ReportLastGapUs { get => reportLastGapUs; set { reportLastGapUs = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReportGapText)); } }
    public int ReportMaxGapUs { get => reportMaxGapUs; set { reportMaxGapUs = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReportGapText)); } }
    public int BleInputActualMilliHz { get => bleInputActualMilliHz; set { bleInputActualMilliHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleInputActualText)); } }
    public int BleInputLastGapUs { get => bleInputLastGapUs; set { bleInputLastGapUs = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleInputGapText)); } }
    public int BleInputMaxGapUs { get => bleInputMaxGapUs; set { bleInputMaxGapUs = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleInputGapText)); } }
    public int BleConnIntervalUnits { get => bleConnIntervalUnits; set { bleConnIntervalUnits = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleConnIntervalText)); } }
    public int BleConnIntervalUs { get => bleConnIntervalUs; set { bleConnIntervalUs = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleConnIntervalText)); } }
    public int BleConnLatency { get => bleConnLatency; set { bleConnLatency = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleConnIntervalText)); } }
    public int BleConnUpdateStartRc { get => bleConnUpdateStartRc; set { bleConnUpdateStartRc = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleConnIntervalText)); } }
    public int BleConnUpdateStatus { get => bleConnUpdateStatus; set { bleConnUpdateStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleConnIntervalText)); } }
    public int BleConnUpdateRequests { get => bleConnUpdateRequests; set { bleConnUpdateRequests = value; OnPropertyChanged(); OnPropertyChanged(nameof(BleConnIntervalText)); } }
    public string LogText => log.ToString();

    public Brush ConnectionLampBrush => serial.IsConnected || hidFeature.IsConnected || bulk.IsConnected ? Brushes.LimeGreen : Brushes.Firebrick;
    public Brush UsbLampBrush => UsbStatus == "mounted" ? Brushes.LimeGreen : Brushes.Firebrick;
    public Brush BulkLampBrush => BulkStatus == "mounted" ? Brushes.LimeGreen : Brushes.Firebrick;
    public Brush BleLampBrush => BleStatus == "connected" ? Brushes.LimeGreen :
        BleStatus == "connecting" || BleStatus == "scanning" ? Brushes.Goldenrod : Brushes.Firebrick;
    public Brush LiveLampBrush => LiveStatus == "active" ? Brushes.LimeGreen : Brushes.Goldenrod;
    public Brush RumbleLampBrush => RumbleErrors > 0 ? Brushes.Firebrick :
        RumbleStatus == "active" ? Brushes.LimeGreen : Brushes.Gray;

    public string UsbIdentityText =>
        DeviceMode == "nintendo"
            ? "Native USB identity: Nintendo Switch Pro Controller, VID 057E, PID 2069"
            : "Native USB identity: Generic HID fallback";

    public string UsbRecognitionText =>
        UsbStatus == "mounted" && BulkStatus == "mounted" && HidGuardStatus == "done"
            ? "Steam/SDL init path is complete."
            : "Waiting for USB mount, vendor bulk, or Steam init guard completion.";
    public string BulkCountsText => $"{BulkRxCount}/{BulkTxCount}, done {BulkTxDoneCount}, sent {BulkTxSentBytes}";
    public string BulkDetailText => $"last {BulkLast}, len {BulkRxLen}/{BulkTxLen}, pending {BulkPending}";
    public string HidOutText => $"{HidOutCount}, last {HidOutLast}";
    public string HidGetText => $"{HidGetCount}, last {HidGetLast}";
    public string ReportActualText => ReportActualMilliHz > 0 ? $"{ReportActualMilliHz / 1000.0:F1} Hz" : "measuring";
    public string ReportCounterText => $"{ReportSent}/{ReportFailed}";
    public string ReportGapText => $"{ReportLastGapUs}/{ReportMaxGapUs} us";
    public string BleInputActualText => BleInputActualMilliHz > 0 ? $"{BleInputActualMilliHz / 1000.0:F1} Hz" : "measuring";
    public string BleInputGapText => $"{BleInputLastGapUs}/{BleInputMaxGapUs} us";
    public string BleConnIntervalText => BleConnIntervalUs > 0
        ? $"{BleConnIntervalUs / 1000.0:F2} ms, units {BleConnIntervalUnits}, latency {BleConnLatency}, rc/status {BleConnUpdateStartRc}/{BleConnUpdateStatus}, req {BleConnUpdateRequests}"
        : "unknown";

    public ICommand RefreshPortsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand StatusCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SetRateCommand { get; }
    public ICommand Rate60Command { get; }
    public ICommand Rate125Command { get; }
    public ICommand Rate250Command { get; }
    public ICommand Rate500Command { get; }
    public ICommand Rate1000Command { get; }
    public ICommand BleReconnectCommand { get; }
    public ICommand BleConnectCommand { get; }
    public ICommand BleDisconnectCommand { get; }
    public ICommand BleFastCommand { get; }
    public ICommand BleAutoOnCommand { get; }
    public ICommand BleAutoOffCommand { get; }
    public ICommand RumbleShortCommand { get; }
    public ICommand RumbleHoldCommand { get; }
    public ICommand RumbleStopCommand { get; }
    public ICommand RumbleConfigCommand { get; }
    public ICommand ApplyRumbleTuneCommand { get; }
    public ICommand LogLevelInfoCommand { get; }
    public ICommand LogLevelDebugCommand { get; }
    public ICommand SendCustomCommandCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand SaveLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand FlashFirmwareCommand { get; }
    public ICommand OpenJoyCommand { get; }
    public ICommand OpenDeviceManagerCommand { get; }

    public MainViewModel()
    {
        serial.LineReceived += OnSerialLine;
        serial.Error += message => AppendLog("ERROR serial " + message);

        statusTimer.Interval = TimeSpan.FromSeconds(2);
        statusTimer.Tick += (_, _) =>
        {
            if (serial.IsConnected || hidFeature.IsConnected || bulk.IsConnected)
            {
                Send("status", logTx: false);
            }
        };

        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        ConnectCommand = new RelayCommand(_ => Connect());
        DisconnectCommand = new RelayCommand(_ => Disconnect());
        StatusCommand = new RelayCommand(_ => Send("status"));
        StartCommand = new RelayCommand(_ => Send("start"));
        StopCommand = new RelayCommand(_ => Send("stop"));
        SetRateCommand = new RelayCommand(_ => SetReportRate(ReportRateHz));
        Rate60Command = new RelayCommand(_ => SetReportRate(60));
        Rate125Command = new RelayCommand(_ => SetReportRate(125));
        Rate250Command = new RelayCommand(_ => SetReportRate(250));
        Rate500Command = new RelayCommand(_ => SetReportRate(500));
        Rate1000Command = new RelayCommand(_ => SetReportRate(1000));
        BleReconnectCommand = new RelayCommand(_ => Send("ble reconnect"));
        BleConnectCommand = new RelayCommand(_ => Send("ble connect " + (string.IsNullOrWhiteSpace(BleConnectTarget) ? "last" : BleConnectTarget.Trim())));
        BleDisconnectCommand = new RelayCommand(_ => Send("ble disconnect"));
        BleFastCommand = new RelayCommand(_ => Send("ble fast"));
        BleAutoOnCommand = new RelayCommand(_ => Send("ble auto on"));
        BleAutoOffCommand = new RelayCommand(_ => Send("ble auto off"));
        RumbleShortCommand = new RelayCommand(_ => Send("rumble hdtest"));
        RumbleHoldCommand = new RelayCommand(_ => Send("rumble hold 3000"));
        RumbleStopCommand = new RelayCommand(_ => Send("rumble stop"));
        RumbleConfigCommand = new RelayCommand(_ => Send("rumble config"));
        ApplyRumbleTuneCommand = new RelayCommand(_ => Send($"rumble tune {RumbleScalePercent} {RumbleHoldMs} {RumbleTickMs} {RumbleStopPackets}"));
        LogLevelInfoCommand = new RelayCommand(_ => Send("loglevel info"));
        LogLevelDebugCommand = new RelayCommand(_ => Send("loglevel debug"));
        SendCustomCommandCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrWhiteSpace(CustomCommand))
            {
                Send(CustomCommand);
            }
        });
        ClearLogsCommand = new RelayCommand(_ => { log.Clear(); OnPropertyChanged(nameof(LogText)); });
        SaveLogsCommand = new RelayCommand(_ => SaveLogs());
        CopyLogsCommand = new RelayCommand(_ => Clipboard.SetText(log.ToString()));
        FlashFirmwareCommand = new RelayCommand(_ => RunScript("flash.ps1", SelectedPort));
        OpenJoyCommand = new RelayCommand(_ => Process.Start(new ProcessStartInfo("joy.cpl") { UseShellExecute = true }));
        OpenDeviceManagerCommand = new RelayCommand(_ => Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }));

        RefreshPorts();
        if (Settings.AutoConnectLastPort && !string.IsNullOrWhiteSpace(SelectedPort))
        {
            _ = Task.Delay(500).ContinueWith(_ => Application.Current.Dispatcher.Invoke(Connect));
        }
    }

    private void RefreshPorts()
    {
        string? previous = SelectedPort;
        var portInfos = GetSerialPortInfos();
        var controlPorts = portInfos.Where(p => !p.IsBluetooth).ToList();

        Ports.Clear();
        foreach (var port in controlPorts)
        {
            Ports.Add(port.PortName);
        }

        if (controlPorts.Count == 0)
        {
            SelectedPort = null;
            PortHint = portInfos.Count == 0
                ? "No CH343P serial port detected. The manager will try native USB HID feature, then bulk control."
                : "No CH343P/USB serial control port detected; only Bluetooth virtual COM ports are present. The manager will try native USB HID feature, then bulk control.";
        }
        else
        {
            SelectedPort = previous != null && Ports.Contains(previous)
                ? previous
                : Settings.LastPort != null && Ports.Contains(Settings.LastPort)
                    ? Settings.LastPort
                    : controlPorts[0].PortName;
            PortHint = "Serial control candidate: " + string.Join(", ", controlPorts.Select(p => $"{p.PortName} ({p.DeviceName})")) + ". Native USB HID feature/bulk are also available when firmware supports them.";
        }
        AppendLog("manager refreshed COM ports: " + PortHint);
    }

    private void Connect()
    {
        Disconnect();

        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            try
            {
                try
                {
                    hidFeature.Connect();
                    ConnectionStatus = "Connected via HID feature";
                    AppendLog("native USB HID feature control opened");
                    Send("status");
                    Send("rumble config");
                    statusTimer.Start();
                    OnPropertyChanged(nameof(ConnectionLampBrush));
                    return;
                }
                catch (Exception hidEx)
                {
                    hidFeature.Disconnect();
                    AppendLog("ERROR HID feature connect " + hidEx.Message);
                }

                try
                {
                    bulk.Connect();
                    ConnectionStatus = "Connected via native USB bulk";
                    AppendLog("native USB bulk control opened");
                    Send("status");
                    Send("rumble config");
                    statusTimer.Start();
                    OnPropertyChanged(nameof(ConnectionLampBrush));
                    return;
                }
                catch (Exception bulkEx)
                {
                    bulk.Disconnect();
                    ConnectionStatus = "Connect failed: no serial port, HID feature/bulk failed";
                    AppendLog("ERROR bulk connect " + bulkEx.Message);
                    OnPropertyChanged(nameof(ConnectionLampBrush));
                    return;
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = "Connect failed: no serial port and native USB control failed";
                AppendLog("ERROR native USB connect " + ex.Message);
                OnPropertyChanged(nameof(ConnectionLampBrush));
                return;
            }
        }

        try
        {
            serial.Connect(SelectedPort);
            ConnectionStatus = "Connected to " + SelectedPort;
            Settings.LastPort = SelectedPort;
            Settings.Save();
            AppendLog("serial opened with DTR=False RTS=False");
            _ = Task.Delay(1200).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (serial.IsConnected)
                    {
                        Send("status");
                        Send("rumble config");
                        statusTimer.Start();
                    }
                }));
        }
        catch (Exception ex)
        {
            ConnectionStatus = "Connect failed: " + ex.Message;
            AppendLog("ERROR connect " + ex.Message);
        }
    }

    private void Disconnect()
    {
        statusTimer.Stop();
        serial.Disconnect();
        hidFeature.Disconnect();
        bulk.Disconnect();
        ConnectionStatus = "Disconnected";
        OnPropertyChanged(nameof(ConnectionLampBrush));
    }

    private void Send(string command, bool logTx = true)
    {
        try
        {
            if (logTx)
            {
                AppendLog("TX " + command);
            }
            if (serial.IsConnected)
            {
                serial.SendCommand(command);
            }
            else if (hidFeature.IsConnected)
            {
                string reply = hidFeature.SendCommand(command);
                OnControlLine(reply);
            }
            else if (bulk.IsConnected)
            {
                string reply = bulk.SendCommand(command);
                OnControlLine(reply);
            }
            else
            {
                throw new InvalidOperationException("No control transport is connected.");
            }
        }
        catch (Exception ex)
        {
            LastCommandJson = "{\"ok\":false,\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            AppendLog("ERROR command " + ex.Message);
        }
    }

    private void SetReportRate(int rateHz)
    {
        ReportRateHz = Math.Clamp(rateHz, 20, 1000);
        Send("rate " + ReportRateHz);
    }

    private void OnSerialLine(string line)
    {
        Application.Current.Dispatcher.Invoke(() => OnControlLine(line));
    }

    private void OnControlLine(string line)
    {
        AppendLog((line.StartsWith("{") ? "RX " : "") + line);
        LastDeviceMessage = line;
        if (line.StartsWith("{"))
        {
            LastCommandJson = line;
            ParseJson(line);
        }
    }

    private void ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("version", out var version)) FirmwareVersion = version.GetString() ?? "unknown";
            if (root.TryGetProperty("mode", out var mode)) DeviceMode = mode.GetString() ?? "unknown";
            if (root.TryGetProperty("usb", out var usb)) UsbStatus = usb.GetString() ?? "unknown";
            if (root.TryGetProperty("bulk", out var bulk)) BulkStatus = bulk.GetString() ?? "unknown";
            if (root.TryGetProperty("hid_guard", out var guard)) HidGuardStatus = guard.GetString() ?? "unknown";
            if (root.TryGetProperty("ble", out var ble)) BleStatus = ble.GetString() ?? "unknown";
            if (root.TryGetProperty("ble_auto", out var bleAuto)) BleAutoStatus = bleAuto.GetString() ?? "unknown";
            if (root.TryGetProperty("ble_target", out var target)) BleTarget = target.GetString() ?? "";
            if (root.TryGetProperty("hid", out var hid)) HidStatus = hid.GetString() ?? "unknown";
            if (root.TryGetProperty("rate_hz", out var rate) && rate.TryGetInt32(out int parsedRate)) ReportRateHz = parsedRate;
            if (root.TryGetProperty("report_actual_mhz", out var actualMilliHz) && actualMilliHz.TryGetInt32(out int parsedActualMilliHz)) ReportActualMilliHz = parsedActualMilliHz;
            if (root.TryGetProperty("report_sent", out var reportSentJson) && reportSentJson.TryGetInt32(out int parsedReportSent)) ReportSent = parsedReportSent;
            if (root.TryGetProperty("report_failed", out var reportFailedJson) && reportFailedJson.TryGetInt32(out int parsedReportFailed)) ReportFailed = parsedReportFailed;
            if (root.TryGetProperty("report_last_gap_us", out var lastGapJson) && lastGapJson.TryGetInt32(out int parsedLastGap)) ReportLastGapUs = parsedLastGap;
            if (root.TryGetProperty("report_max_gap_us", out var maxGapJson) && maxGapJson.TryGetInt32(out int parsedMaxGap)) ReportMaxGapUs = parsedMaxGap;
            if (root.TryGetProperty("ble_input_actual_mhz", out var bleInputMilliHz) && bleInputMilliHz.TryGetInt32(out int parsedBleInputMilliHz)) BleInputActualMilliHz = parsedBleInputMilliHz;
            if (root.TryGetProperty("ble_input_last_gap_us", out var bleInputLastGap) && bleInputLastGap.TryGetInt32(out int parsedBleInputLastGap)) BleInputLastGapUs = parsedBleInputLastGap;
            if (root.TryGetProperty("ble_input_max_gap_us", out var bleInputMaxGap) && bleInputMaxGap.TryGetInt32(out int parsedBleInputMaxGap)) BleInputMaxGapUs = parsedBleInputMaxGap;
            if (root.TryGetProperty("ble_conn_interval_us", out var bleConnInterval) && bleConnInterval.TryGetInt32(out int parsedBleConnInterval)) BleConnIntervalUs = parsedBleConnInterval;
            if (root.TryGetProperty("ble_conn_interval_units", out var bleConnIntervalUnitsJson) && bleConnIntervalUnitsJson.TryGetInt32(out int parsedBleConnIntervalUnits)) BleConnIntervalUnits = parsedBleConnIntervalUnits;
            if (root.TryGetProperty("ble_conn_latency", out var bleConnLatencyJson) && bleConnLatencyJson.TryGetInt32(out int parsedBleConnLatency)) BleConnLatency = parsedBleConnLatency;
            if (root.TryGetProperty("ble_conn_update_start_rc", out var bleConnStartRcJson) && bleConnStartRcJson.TryGetInt32(out int parsedBleConnStartRc)) BleConnUpdateStartRc = parsedBleConnStartRc;
            if (root.TryGetProperty("ble_conn_update_status", out var bleConnStatusJson) && bleConnStatusJson.TryGetInt32(out int parsedBleConnStatus)) BleConnUpdateStatus = parsedBleConnStatus;
            if (root.TryGetProperty("ble_conn_update_requests", out var bleConnRequestsJson) && bleConnRequestsJson.TryGetInt32(out int parsedBleConnRequests)) BleConnUpdateRequests = parsedBleConnRequests;
            if (root.TryGetProperty("live", out var live)) LiveStatus = live.GetString() ?? "none";
            if (root.TryGetProperty("live_updates", out var updates) && updates.TryGetInt32(out int parsedUpdates)) LiveUpdates = parsedUpdates;
            if (root.TryGetProperty("live_age_ms", out var age) && age.TryGetInt32(out int parsedAge)) LiveAgeMs = parsedAge;
            if (root.TryGetProperty("hid_out", out var hidOut) && hidOut.TryGetInt32(out int parsedHidOut)) HidOutCount = parsedHidOut;
            if (root.TryGetProperty("hid_out_last", out var hidOutLastJson)) HidOutLast = hidOutLastJson.GetString() ?? "none";
            if (root.TryGetProperty("hid_get", out var hidGet) && hidGet.TryGetInt32(out int parsedHidGet)) HidGetCount = parsedHidGet;
            if (root.TryGetProperty("hid_get_last", out var hidGetLastJson)) HidGetLast = hidGetLastJson.GetString() ?? "none";
            if (root.TryGetProperty("bulk_rx", out var bulkRx) && bulkRx.TryGetInt32(out int parsedBulkRx)) BulkRxCount = parsedBulkRx;
            if (root.TryGetProperty("bulk_tx", out var bulkTx) && bulkTx.TryGetInt32(out int parsedBulkTx)) BulkTxCount = parsedBulkTx;
            if (root.TryGetProperty("bulk_tx_done", out var bulkTxDone) && bulkTxDone.TryGetInt32(out int parsedBulkTxDone)) BulkTxDoneCount = parsedBulkTxDone;
            if (root.TryGetProperty("bulk_tx_sent", out var bulkTxSent) && bulkTxSent.TryGetInt32(out int parsedBulkTxSent)) BulkTxSentBytes = parsedBulkTxSent;
            if (root.TryGetProperty("bulk_last", out var bulkLastJson)) BulkLast = bulkLastJson.GetString() ?? "none";
            if (root.TryGetProperty("bulk_rx_len", out var bulkRxLenJson) && bulkRxLenJson.TryGetInt32(out int parsedBulkRxLen)) BulkRxLen = parsedBulkRxLen;
            if (root.TryGetProperty("bulk_tx_len", out var bulkTxLenJson) && bulkTxLenJson.TryGetInt32(out int parsedBulkTxLen)) BulkTxLen = parsedBulkTxLen;
            if (root.TryGetProperty("bulk_pending", out var bulkPendingJson)) BulkPending = bulkPendingJson.GetString() ?? "0/0";
            if (root.TryGetProperty("rumble", out var rumble)) RumbleStatus = rumble.GetString() ?? "idle";
            if (root.TryGetProperty("rumble_updates", out var rumbleUpdate) && rumbleUpdate.TryGetInt32(out int parsedRumbleUpdate)) RumbleUpdates = parsedRumbleUpdate;
            if (root.TryGetProperty("rumble_writes", out var rumbleWrite) && rumbleWrite.TryGetInt32(out int parsedRumbleWrite)) RumbleWrites = parsedRumbleWrite;
            if (root.TryGetProperty("rumble_stops", out var rumbleStop) && rumbleStop.TryGetInt32(out int parsedRumbleStop)) RumbleStops = parsedRumbleStop;
            if (root.TryGetProperty("rumble_errors", out var rumbleError) && rumbleError.TryGetInt32(out int parsedRumbleError)) RumbleErrors = parsedRumbleError;
            if (root.TryGetProperty("rumble_scale_percent", out var rumbleScale) && rumbleScale.TryGetInt32(out int parsedScale)) RumbleScalePercent = parsedScale;
            if (root.TryGetProperty("rumble_hold_ms", out var rumbleHold) && rumbleHold.TryGetInt32(out int parsedHold)) RumbleHoldMs = parsedHold;
            if (root.TryGetProperty("rumble_tick_ms", out var rumbleTick) && rumbleTick.TryGetInt32(out int parsedTick)) RumbleTickMs = parsedTick;
            if (root.TryGetProperty("rumble_stop_packets", out var rumbleStopPacketsJson) && rumbleStopPacketsJson.TryGetInt32(out int parsedStopPackets)) RumbleStopPackets = parsedStopPackets;
        }
        catch (Exception ex)
        {
            AppendLog("ERROR parse " + ex.Message);
        }
    }

    private void AppendLog(string text)
    {
        log.Append(DateTime.Now.ToString("HH:mm:ss.fff ")).AppendLine(text);
        OnPropertyChanged(nameof(LogText));
    }

    private void SaveLogs()
    {
        var dialog = new SaveFileDialog { Filter = "Text log|*.txt", FileName = "esp32s3-manager-log.txt" };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, log.ToString());
        }
    }

    private void RunScript(string scriptName, string? port = null)
    {
        string root = FindRepoRoot();
        string script = Path.Combine(root, "tools", "esp32s3", scriptName);
        string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
        if (!string.IsNullOrWhiteSpace(port))
        {
            args += " -Port " + port;
        }
        RunProcess("powershell", args);
    }

    private void RunProcess(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            ScriptOutput = output;
            AppendLog("SCRIPT exit=" + p.ExitCode);
        }
        catch (Exception ex)
        {
            ScriptOutput = ex.ToString();
            AppendLog("ERROR script " + ex.Message);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "esp32s3")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static IReadOnlyList<SerialPortInfo> GetSerialPortInfos()
    {
        var names = SerialDeviceClient.GetPorts().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var infos = new List<SerialPortInfo>();

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key != null)
        {
            foreach (string valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is string portName && names.Contains(portName))
                {
                    bool isBluetooth = valueName.Contains("Bth", StringComparison.OrdinalIgnoreCase);
                    infos.Add(new SerialPortInfo(portName, valueName, isBluetooth));
                }
            }
        }

        foreach (string portName in names)
        {
            if (!infos.Any(info => string.Equals(info.PortName, portName, StringComparison.OrdinalIgnoreCase)))
            {
                infos.Add(new SerialPortInfo(portName, "unknown serial device", false));
            }
        }

        return infos
            .OrderBy(info => info.IsBluetooth)
            .ThenBy(info => PortSortKey(info.PortName))
            .ToList();
    }

    private static int PortSortKey(string portName) =>
        int.TryParse(new string(portName.Where(char.IsDigit).ToArray()), out int parsed) ? parsed : int.MaxValue;

    private sealed record SerialPortInfo(string PortName, string DeviceName, bool IsBluetooth);

    private static string EscapeJson(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
