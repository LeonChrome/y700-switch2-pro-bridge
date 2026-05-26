using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace Y700Switch2Manager;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SerialDeviceClient serial = new();
    private readonly StringBuilder log = new();
    private string? selectedPort;
    private string connectionStatus = "Disconnected";
    private string firmwareVersion = "unknown";
    private string deviceMode = "unknown";
    private string usbStatus = "unknown";
    private string bleStatus = "unknown";
    private string hidStatus = "unknown";
    private string lastDeviceMessage = "";
    private string lastCommandJson = "";
    private string scriptOutput = "";
    private object? logFilter;

    public ObservableCollection<string> Ports { get; } = new();
    public ManagerSettings Settings { get; } = ManagerSettings.Load();
    public bool AutoScroll { get; set; } = true;
    public string FirmwareDirectory { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "firmware", "esp32s3_switch2_bridge"));

    public string? SelectedPort { get => selectedPort; set { selectedPort = value; OnPropertyChanged(); } }
    public string ConnectionStatus { get => connectionStatus; set { connectionStatus = value; OnPropertyChanged(); } }
    public string FirmwareVersion { get => firmwareVersion; set { firmwareVersion = value; OnPropertyChanged(); } }
    public string DeviceMode { get => deviceMode; set { deviceMode = value; OnPropertyChanged(); } }
    public string UsbStatus { get => usbStatus; set { usbStatus = value; OnPropertyChanged(); } }
    public string BleStatus { get => bleStatus; set { bleStatus = value; OnPropertyChanged(); } }
    public string HidStatus { get => hidStatus; set { hidStatus = value; OnPropertyChanged(); } }
    public string LastDeviceMessage { get => lastDeviceMessage; set { lastDeviceMessage = value; OnPropertyChanged(); } }
    public string LastCommandJson { get => lastCommandJson; set { lastCommandJson = value; OnPropertyChanged(); } }
    public string ScriptOutput { get => scriptOutput; set { scriptOutput = value; OnPropertyChanged(); } }
    public object? LogFilter { get => logFilter; set { logFilter = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredLogText)); } }
    public string FilteredLogText => BuildFilteredLog();

    public string RecognitionChecklist { get; } =
        "PENDING_HARDWARE_TEST checklist\r\n" +
        "1. Device Manager: record device class, VID, PID, product string, manufacturer string.\r\n" +
        "2. joy.cpl: record whether Generic HID mode appears and whether A toggles.\r\n" +
        "3. Steam Controller Settings: record Generic vs Nintendo experimental behavior.\r\n" +
        "4. Steam logs: search for If_Hid, Nintendo, Switch, Pro Controller, 057e, 2069.\r\n" +
        "5. If If_Hid appears, record exact strings and attach logs.\r\n" +
        "6. If Generic works but Nintendo fails, switch back to Generic and preserve descriptors.";

    public ICommand RefreshPortsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand StatusCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RebootCommand { get; }
    public ICommand ModeGenericCommand { get; }
    public ICommand ModeNintendoCommand { get; }
    public ICommand HidTestACommand { get; }
    public ICommand HidNeutralCommand { get; }
    public ICommand BleScanCommand { get; }
    public ICommand BleDisconnectCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand SaveLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand BuildFirmwareCommand { get; }
    public ICommand FlashFirmwareCommand { get; }
    public ICommand MonitorCommand { get; }
    public ICommand OpenJoyCommand { get; }
    public ICommand OpenDeviceManagerCommand { get; }
    public ICommand OpenSteamLogsCommand { get; }
    public ICommand CopyChecklistCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public MainViewModel()
    {
        serial.LineReceived += OnSerialLine;
        serial.Error += message => AppendLog("ERROR serial " + message);

        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        ConnectCommand = new RelayCommand(_ => Connect());
        DisconnectCommand = new RelayCommand(_ => Disconnect());
        StatusCommand = new RelayCommand(_ => Send("status"));
        StartCommand = new RelayCommand(_ => Send("start"));
        StopCommand = new RelayCommand(_ => Send("stop"));
        RebootCommand = new RelayCommand(_ => Send("reboot"));
        ModeGenericCommand = new RelayCommand(_ => Send("mode generic"));
        ModeNintendoCommand = new RelayCommand(_ => Send("mode nintendo"));
        HidTestACommand = new RelayCommand(_ => Send("hid test_a"));
        HidNeutralCommand = new RelayCommand(_ => Send("hid neutral"));
        BleScanCommand = new RelayCommand(_ => Send("ble scan"));
        BleDisconnectCommand = new RelayCommand(_ => Send("ble disconnect"));
        ClearLogsCommand = new RelayCommand(_ => { log.Clear(); OnPropertyChanged(nameof(FilteredLogText)); });
        SaveLogsCommand = new RelayCommand(_ => SaveLogs());
        CopyLogsCommand = new RelayCommand(_ => Clipboard.SetText(log.ToString()));
        BuildFirmwareCommand = new RelayCommand(_ => RunScript("build.ps1"));
        FlashFirmwareCommand = new RelayCommand(_ => RunScript("flash.ps1", SelectedPort));
        MonitorCommand = new RelayCommand(_ => RunScript("monitor.ps1", SelectedPort));
        OpenJoyCommand = new RelayCommand(_ => Process.Start(new ProcessStartInfo("joy.cpl") { UseShellExecute = true }));
        OpenDeviceManagerCommand = new RelayCommand(_ => Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }));
        OpenSteamLogsCommand = new RelayCommand(_ => OpenSteamLogs());
        CopyChecklistCommand = new RelayCommand(_ => Clipboard.SetText(RecognitionChecklist));
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());

        RefreshPorts();
    }

    private void RefreshPorts()
    {
        Ports.Clear();
        foreach (string port in SerialDeviceClient.GetPorts().OrderBy(p => p))
        {
            Ports.Add(port);
        }
        SelectedPort ??= Settings.LastPort ?? Ports.FirstOrDefault();
        AppendLog("MODE manager refreshed COM ports; CH343P identification is PENDING_HARDWARE_TEST");
    }

    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort))
        {
            ConnectionStatus = "No COM port selected.";
            return;
        }
        try
        {
            serial.Connect(SelectedPort);
            ConnectionStatus = "Connected to " + SelectedPort;
            Settings.LastPort = SelectedPort;
            Send("status");
        }
        catch (Exception ex)
        {
            ConnectionStatus = "Connect failed: " + ex.Message;
            AppendLog("ERROR connect " + ex.Message);
        }
    }

    private void Disconnect()
    {
        serial.Disconnect();
        ConnectionStatus = "Disconnected";
    }

    private void Send(string command)
    {
        try
        {
            AppendLog("JSON TX " + command);
            serial.SendCommand(command);
        }
        catch (Exception ex)
        {
            LastCommandJson = "{\"ok\":false,\"error\":\"" + EscapeJson(ex.Message) + "\"}";
            AppendLog("ERROR command " + ex.Message);
        }
    }

    private void OnSerialLine(string line)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            AppendLog((line.StartsWith("{") ? "JSON RX " : "") + line);
            LastDeviceMessage = line;
            if (line.StartsWith("{"))
            {
                LastCommandJson = line;
                ParseStatus(line);
            }
        });
    }

    private void ParseStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("version", out var version)) FirmwareVersion = version.GetString() ?? "unknown";
            if (root.TryGetProperty("mode", out var mode)) DeviceMode = mode.GetString() ?? "unknown";
            if (root.TryGetProperty("usb", out var usb)) UsbStatus = usb.GetString() ?? "unknown";
            if (root.TryGetProperty("ble", out var ble)) BleStatus = ble.GetString() ?? "unknown";
            if (root.TryGetProperty("hid", out var hid)) HidStatus = hid.GetString() ?? "unknown";
        }
        catch
        {
        }
    }

    private void AppendLog(string text)
    {
        log.Append(DateTime.Now.ToString("HH:mm:ss.fff ")).AppendLine(text);
        OnPropertyChanged(nameof(FilteredLogText));
    }

    private string BuildFilteredLog()
    {
        string filter = LogFilter is System.Windows.Controls.ComboBoxItem item ? item.Content?.ToString() ?? "" : LogFilter ?? "";
        if (string.IsNullOrWhiteSpace(filter)) return log.ToString();
        return string.Join(Environment.NewLine, log.ToString().Split(Environment.NewLine).Where(l => l.Contains(filter, StringComparison.OrdinalIgnoreCase)));
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

    private void OpenSteamLogs()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "logs");
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            AppendLog("ERROR Steam log folder not found");
        }
    }

    private void SaveSettings()
    {
        Settings.LastPort = SelectedPort;
        Settings.Save();
        AppendLog("settings saved");
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

    private static string EscapeJson(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
