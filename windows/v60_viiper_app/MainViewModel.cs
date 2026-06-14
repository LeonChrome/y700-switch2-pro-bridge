using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
    private Process? viiperProcess;
    private ViiperBridgeSession? session;
    private string host = "127.0.0.1";
    private string port = "3242";
    private string status = "V6.0 VIIPER Windows-only 技术预览已就绪。如未安装 usbip-win2，请先点击安装/修复。";
    private string inputStatus = "真实 Pro2 BLE 输入未连接。";
    private bool running;

    public MainViewModel()
    {
        PingCommand = new RelayCommand(async _ => await PingAsync());
        InstallUsbipCommand = new RelayCommand(async _ => await InstallUsbipAsync());
        StartViiperServerCommand = new RelayCommand(async _ => await StartLocalViiperServerAsync());
        ScanPro2InputCommand = new RelayCommand(async _ => await ScanPro2InputAsync());
        ConnectPro2InputCommand = new RelayCommand(async _ => await ConnectPro2InputAsync());
        DisconnectPro2InputCommand = new RelayCommand(async _ => await DisconnectPro2InputAsync());
        StartDualSenseCommand = new RelayCommand(async _ => await StartAsync(ViiperDeviceProfile.DualSenseLike));
        StartPro2Command = new RelayCommand(async _ => await StartAsync(ViiperDeviceProfile.Pro2));
        StartXboxCommand = new RelayCommand(async _ => await StartAsync(ViiperDeviceProfile.Xbox));
        StopCommand = new RelayCommand(async _ => await StopAsync());
        ClearLogCommand = new RelayCommand(_ => ClearLog());
        AppendLog("V6.0 说明：当前 EXE 已能创建 VIIPER 三模虚拟 USB 手柄，并由 Windows 端 BLE central 直接连接真实 Pro2，不再要求 Windows HID 蓝牙配对。");
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
        }
    }

    public bool CanStart => !Running;

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

    private async Task PingAsync()
    {
        try
        {
            var client = new ViiperProtocolClient(Host, ParsePort());
            string response = await client.PingAsync(CancellationToken.None);
            Status = "VIIPER server 已响应。";
            AppendLog("[PING] " + response);
        }
        catch (Exception ex)
        {
            Status = "VIIPER server 未就绪：" + FirstLine(ex.Message);
            AppendLog("ERROR ping: " + ex);
        }
    }

    private async Task StartLocalViiperServerAsync()
    {
        if (viiperProcess is { HasExited: false })
        {
            Status = "本地 VIIPER server 已在运行，pid=" + viiperProcess.Id;
            AppendLog("[VIIPER_SERVER] already_running pid=" + viiperProcess.Id);
            await PingAsync();
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
            return;
        }

        string logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRO2WirelessReceiverControlBoard",
            "v6_logs");
        Directory.CreateDirectory(logRoot);
        string logPath = Path.Combine(logRoot, "viiper_server_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
        string args = "server --api.addr=127.0.0.1:3242 --usb.addr=127.0.0.1:3241 --log.file=\"" + logPath + "\"";
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

        Status = "正在启动本地 VIIPER server，pid=" + viiperProcess.Id;
        AppendLog("[USBIP] using " + usbip.ExePath);
        AppendLog("[VIIPER_SERVER] started pid=" + viiperProcess.Id + " exe=" + exe + " log=" + logPath);
        await Task.Delay(1000);
        await PingAsync();
    }

    private async Task InstallUsbipAsync()
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

            await process.WaitForExitAsync();
            AppendLog("[USBIP] installer exited code=" + process.ExitCode);
            await Task.Delay(1000);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status = "已取消 usbip-win2 安装。";
            AppendLog("[USBIP] installer cancelled by user.");
            return;
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
            Status = "usbip-win2 已就绪：" + runtime.ExePath;
            AppendLog("[USBIP] ready " + runtime.ExePath);
        }
        else
        {
            Status = "usbip-win2 安装器已结束，但还没找到 usbip.exe。若安装器提示重启，请重启后再打开本 EXE。";
            AppendLog("[USBIP] usbip.exe still not found after installer exit; reboot may be required.");
        }
    }

    private async Task ScanPro2InputAsync()
    {
        var progress = new Progress<string>(AppendLog);
        var candidates = await inputSource.ScanAsync(progress, TimeSpan.FromSeconds(8), CancellationToken.None);
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

    private async Task ConnectPro2InputAsync()
    {
        var progress = new Progress<string>(AppendLog);
        await inputSource.StartAsync(progress, CancellationToken.None);
        InputStatus = inputSource.Status;
        if (!inputSource.IsRunning)
        {
            Status = "真实 Pro2 BLE 未连接。请唤醒手柄，并确认没有被其他设备占用。";
        }
    }

    private async Task DisconnectPro2InputAsync()
    {
        await inputSource.StopAsync();
        InputStatus = inputSource.Status;
    }

    private async Task StartAsync(ViiperDeviceProfile profile)
    {
        if (Running)
        {
            return;
        }

        try
        {
            if (!inputSource.IsRunning)
            {
                await ConnectPro2InputAsync();
            }
            bool inputLive = inputSource.IsRunning &&
                             inputSource.TryGetLatest(out _, out TimeSpan inputAge) &&
                             inputAge <= TimeSpan.FromMilliseconds(500);
            Running = true;
            Status = "正在启动 " + profile.Label + " 虚拟手柄...";
            AppendLog("[START] mode=" + profile.Label + " type=" + profile.DeviceType);
            var progress = new Progress<string>(AppendLog);
            session = new ViiperBridgeSession(
                new ViiperProtocolClient(Host, ParsePort()),
                profile,
                progress,
                inputLive ? inputSource : null,
                inputLive ? inputSource : null);
            await session.StartAsync(CancellationToken.None);
            Status = profile.Label + " 虚拟设备已连接。当前输入源：" +
                (inputLive ? "Pro2 BLE live，rumble 写回已启用" : "neutral/synthetic，尚未确认真实 Pro2 BLE 输入") + "。";
        }
        catch (Exception ex)
        {
            Status = "启动失败：" + ExplainStartFailure(ex);
            AppendLog("ERROR start: " + ex);
            await StopAsync();
        }
    }

    public async Task StopAsync()
    {
        ViiperBridgeSession? active = session;
        session = null;
        if (active != null)
        {
            await active.DisposeAsync();
        }
        Running = false;
        Status = "已停止虚拟设备。";
    }

    public async Task ShutdownAsync()
    {
        await StopAsync();
        await inputSource.DisposeAsync();
        if (viiperProcess is { HasExited: false })
        {
            try
            {
                viiperProcess.Kill(entireProcessTree: true);
                await viiperProcess.WaitForExitAsync();
                AppendLog("[VIIPER_SERVER] stopped local server.");
            }
            catch (Exception ex)
            {
                AppendLog("[VIIPER_SERVER] stop warning: " + ex.Message);
            }
        }
    }

    private static string? FindLocalViiperExe()
    {
        string relative = Path.Combine("tools", "viiper", "v0.7.0", "viiper.exe");
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
            "v6.0.0-preview",
            "viiper",
            "v0.7.0");
        Directory.CreateDirectory(root);
        string exe = Path.Combine(root, "viiper.exe");
        string licenses = Path.Combine(root, "licenses.txt");

        if (!ExtractResourceIfAvailable(assembly, "Embedded.viiper.exe", exe))
        {
            return null;
        }
        ExtractResourceIfAvailable(assembly, "Embedded.viiper.licenses.txt", licenses);
        return exe;
    }

    private static bool ExtractResourceIfAvailable(Assembly assembly, string resourceName, string destination)
    {
        using Stream? source = assembly.GetManifestResourceStream(resourceName);
        if (source == null)
        {
            return false;
        }

        if (File.Exists(destination) && new FileInfo(destination).Length == source.Length)
        {
            return true;
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
        return int.TryParse(Port, out int parsed) && parsed > 0
            ? parsed
            : 3242;
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

        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            log.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ").AppendLine(line);
        }
        if (log.Length > 160000)
        {
            log.Remove(0, log.Length - 90000);
            log.Insert(0, "[UI LOG TRIMMED]\r\n");
        }
        OnPropertyChanged(nameof(LogText));
    }

    private static string FirstLine(string text)
    {
        return (text ?? "").Replace("\r", "").Split('\n')[0];
    }

    private static string ExplainStartFailure(Exception ex)
    {
        string first = FirstLine(ex.Message);
        if (first.Contains("usbip", StringComparison.OrdinalIgnoreCase) &&
            first.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "VIIPER 找不到 usbip.exe。请点击“安装/修复 usbip-win2”，完成后重新启动本地 VIIPER。";
        }

        return first;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
