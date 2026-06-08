using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        progress.Report("内置固件: " + package.Manifest.FirmwareVersion + " / " + profile.Label);
        progress.Report("工具: " + package.EsptoolPath);
        progress.Report("目标: " + port + ", baud " + firstBaud);

        string chipOutput;
        try
        {
            chipOutput = await RunEsptoolAsync(package.EsptoolPath,
                CommonArgs(port, firstBaud, firstNoStub, "chip_id"), progress, cancellationToken);
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
        catch when (mode != FlashMode.Repair)
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

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 esptool。");
        }

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

        await process.WaitForExitAsync(cancellationToken);
        string combined = string.Join(Environment.NewLine, lines);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("esptool 失败，exit=" + process.ExitCode + Environment.NewLine + combined);
        }

        return combined;
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
}
