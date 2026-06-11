using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2V55Manager;

public enum FlashMode
{
    Upgrade,
    Repair,
    EraseAndFlash
}

public sealed class FirmwareFlasher
{
    public async Task FlashAsync(
        string port,
        string profileId,
        FlashMode mode,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            throw new InvalidOperationException("请先选择 CH343P 对应的 COM 串口。");
        }

        FirmwarePackage package = EmbeddedAssets.EnsurePackage();
        FirmwareProfile profile = package.GetProfile(profileId);
        int firstBaud = mode == FlashMode.Repair ? 115200 : 460800;
        bool firstNoStub = mode == FlashMode.Repair;

        if (!await SerialCommandClient.CloseAsync(5000))
        {
            throw new InvalidOperationException("刷写前无法释放 " + port + "。请关闭串口监视器、旧版 Manager、PowerShell send_command/monitor，或拔插 CH343P 控制口后重试。");
        }
        await CleanupStaleEsptoolProcessesAsync(package.EsptoolPath, port, progress);
        await EnsurePortCanOpenAsync(port);

        progress.Report("内置固件: " + package.Manifest.FirmwareVersion + " / " + profile.Label);
        progress.Report("工具: " + package.EsptoolPath);
        progress.Report("目标: " + port + ", baud " + firstBaud);

        string chipOutput;
        try
        {
            chipOutput = await RunEsptoolAsync(package.EsptoolPath,
                CommonArgs(port, firstBaud, firstNoStub, "chip_id"), progress, cancellationToken);
        }
        catch (PortBusyException)
        {
            throw;
        }
        catch
        {
            progress.Report("chip_id 高速探测失败，切到 115200 + --no-stub 重试。");
            chipOutput = await RunEsptoolAsync(package.EsptoolPath,
                CommonArgs(port, 115200, true, "chip_id"), progress, cancellationToken);
        }

        if (!chipOutput.Contains("ESP32-S3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("所选串口没有识别为 ESP32-S3，已拒绝刷入。");
        }

        if (mode == FlashMode.EraseAndFlash)
        {
            progress.Report("正在整片擦除。");
            await RunEsptoolAsync(package.EsptoolPath,
                CommonArgs(port, 115200, true, "erase_flash"), progress, cancellationToken);
        }

        try
        {
            await WriteFlashAsync(package, profile, port, firstBaud, firstNoStub, progress, cancellationToken);
        }
        catch (Exception ex) when (mode != FlashMode.Repair && ex is not PortBusyException)
        {
            progress.Report("高速刷入失败，使用 115200 + --no-stub 修复路径重试。");
            await WriteFlashAsync(package, profile, port, 115200, true, progress, cancellationToken);
        }

        progress.Report("刷入完成。请重新插拔原生 USB / OTG，然后点击“USB 检查”。");
    }

    private static async Task WriteFlashAsync(
        FirmwarePackage package,
        FirmwareProfile profile,
        string port,
        int baud,
        bool noStub,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var args = CommonArgs(port, baud, noStub, "write_flash");
        args.Add("--flash_mode");
        args.Add(package.Manifest.FlashMode);
        args.Add("--flash_freq");
        args.Add(package.Manifest.FlashFreq);
        args.Add("--flash_size");
        args.Add(package.Manifest.FlashSize);
        foreach (FirmwareAsset asset in profile.Assets)
        {
            args.Add(asset.Offset);
            args.Add(Path.Combine(package.FirmwareRoot, asset.Path.Replace('/', Path.DirectorySeparatorChar)));
        }
        await RunEsptoolAsync(package.EsptoolPath, args, progress, cancellationToken);
    }

    private static List<string> CommonArgs(string port, int baud, bool noStub, string command)
    {
        var args = new List<string> { "--chip", "esp32s3" };
        if (noStub)
        {
            args.Add("--no-stub");
        }

        args.AddRange(new[]
        {
            "-p", port,
            "-b", baud.ToString(),
            "--before", "default_reset",
            "--after", "hard_reset",
            command
        });
        return args;
    }

    private static async Task<string> RunEsptoolAsync(
        string esptoolPath,
        IReadOnlyList<string> args,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        string targetPort = FindPort(args);
        if (!string.IsNullOrWhiteSpace(targetPort))
        {
            await CleanupStaleEsptoolProcessesAsync(esptoolPath, targetPort, progress);
            await EnsurePortCanOpenAsync(targetPort);
        }

        var lines = new List<string>();
        var psi = new ProcessStartInfo(esptoolPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        progress.Report("esptool " + string.Join(" ", args));
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Capture(e.Data, lines, progress);
        process.ErrorDataReceived += (_, e) => Capture(e.Data, lines, progress);

        int processId = 0;
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 esptool。");
        }
        processId = process.Id;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            CleanupEsptoolChildren(processId, progress);
        }
        string combined = string.Join(Environment.NewLine, lines);
        if (process.ExitCode != 0)
        {
            if (IsPortBusyFailure(combined))
            {
                string port = FindPort(args);
                await CleanupStaleEsptoolProcessesAsync(esptoolPath, port, progress);
                throw new PortBusyException(BuildPortBusyMessage(port, combined));
            }
            throw new InvalidOperationException("esptool 失败，exit=" + process.ExitCode + Environment.NewLine + combined);
        }

        return combined;
    }

    private static bool IsPortBusyFailure(string output)
    {
        return output.Contains("PermissionError", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("port is busy", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("could not open port", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnsurePortCanOpenAsync(string port)
    {
        Task<Exception?> openTask = Task.Run(() => TryOpenPort(port));
        Task completed = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromMilliseconds(1800)));
        if (completed != openTask)
        {
            throw new PortBusyException(BuildPortBusyMessage(port, "串口打开预检超时：CH343 驱动没有在 1.8 秒内返回。"));
        }

        Exception? error = await openTask;
        if (error != null)
        {
            throw new PortBusyException(BuildPortBusyMessage(port, error.Message));
        }
    }

    private static Exception? TryOpenPort(string port)
    {
        try
        {
            using var serial = new SerialPort(port, 115200, Parity.None, 8, StopBits.One)
            {
                DtrEnable = false,
                RtsEnable = false,
                Handshake = Handshake.None,
                ReadTimeout = 200,
                WriteTimeout = 500
            };
            serial.Open();
            serial.DtrEnable = false;
            serial.RtsEnable = false;
            return null;
        }
        catch (Exception ex) when (ex is IOException ||
                                   ex is UnauthorizedAccessException ||
                                   ex is InvalidOperationException)
        {
            return ex;
        }
    }

    private static string BuildPortBusyMessage(string port, string detail)
    {
        return "esptool 无法打开 " + port + "：串口被占用，或 CH343 驱动里有未释放的句柄。已停止自动重试，避免留下多个 esptool。请关闭旧版 Manager、串口监视器、PowerShell send_command/monitor、ESP-IDF monitor；如果仍然拒绝访问，请拔插 CH343P 控制口，或用管理员权限重启该设备后再试。"
            + Environment.NewLine + detail;
    }

    private static void CleanupStaleEsptoolProcesses(string esptoolPath, string port, IProgress<string> progress)
    {
        foreach (int pid in FindMatchingEsptoolProcesses(esptoolPath, port))
        {
            KillProcess(pid, "[FLASH_CLEANUP] stale esptool", progress);
        }
    }

    private static async Task CleanupStaleEsptoolProcessesAsync(string esptoolPath, string port, IProgress<string> progress)
    {
        Task cleanupTask = Task.Run(() => CleanupStaleEsptoolProcesses(esptoolPath, port, progress));
        Task completed = await Task.WhenAny(cleanupTask, Task.Delay(TimeSpan.FromMilliseconds(1200)));
        if (completed != cleanupTask)
        {
            progress.Report("[FLASH_CLEANUP] stale esptool cleanup timed out; continuing with port preflight");
            return;
        }
        await cleanupTask;
    }

    private static void CleanupEsptoolChildren(int parentPid, IProgress<string> progress)
    {
        if (parentPid <= 0) return;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId,Name FROM Win32_Process WHERE ParentProcessId=" + parentPid);
            foreach (ManagementObject item in searcher.Get())
            {
                string name = Convert.ToString(item["Name"]) ?? "";
                if (!string.Equals(name, "esptool.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int pid = Convert.ToInt32(item["ProcessId"]);
                KillProcess(pid, "[FLASH_CLEANUP] child esptool", progress);
            }
        }
        catch
        {
        }
    }

    private static IEnumerable<int> FindMatchingEsptoolProcesses(string esptoolPath, string port)
    {
        string expectedPath = Path.GetFullPath(esptoolPath);
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId,ExecutablePath,CommandLine FROM Win32_Process WHERE Name='esptool.exe'");
        foreach (ManagementObject item in searcher.Get())
        {
            string executablePath = Convert.ToString(item["ExecutablePath"]) ?? "";
            string commandLine = Convert.ToString(item["CommandLine"]) ?? "";
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(executablePath);
            }
            catch
            {
                continue;
            }

            if (!string.Equals(fullPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!commandLine.Contains(port, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            yield return Convert.ToInt32(item["ProcessId"]);
        }
    }

    private static void KillProcess(int pid, string label, IProgress<string> progress)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                progress.Report(label + " pid=" + pid + " is already exiting");
                return;
            }
            process.Kill(entireProcessTree: true);
            if (process.WaitForExit(1500))
            {
                progress.Report(label + " pid=" + pid + " killed");
            }
            else
            {
                progress.Report(label + " pid=" + pid + " did not exit; unplug/replug CH343P if COM remains busy");
            }
        }
        catch (Exception ex)
        {
            progress.Report(label + " pid=" + pid + " cleanup failed: " + ex.Message);
        }
    }

    private static string FindPort(IReadOnlyList<string> args)
    {
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (string.Equals(args[i], "-p", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return "所选串口";
    }

    private static void Capture(string? line, List<string> lines, IProgress<string> progress)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (lines)
        {
            lines.Add(line);
        }
        progress.Report(line);
    }

    private sealed class PortBusyException : InvalidOperationException
    {
        public PortBusyException(string message) : base(message)
        {
        }
    }
}
