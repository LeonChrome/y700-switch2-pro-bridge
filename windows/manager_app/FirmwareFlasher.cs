using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2Manager;

public enum FlashMode
{
    Upgrade,
    Repair,
    EraseOnly,
    EraseAndFlash
}

public sealed class FirmwareFlasher
{
    public async Task FlashAsync(string port, FlashMode mode, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            throw new InvalidOperationException("Choose the CH343P COM port before flashing.");
        }

        FirmwarePackage package = EmbeddedAssets.EnsureFirmwarePackage();
        if (!File.Exists(package.EsptoolPath))
        {
            throw new FileNotFoundException("Bundled esptool.exe is missing.", package.EsptoolPath);
        }

        int baud = mode == FlashMode.Upgrade ? 460800 : 115200;
        bool chipNoStub = mode == FlashMode.Repair;
        bool writeNoStub = mode == FlashMode.Repair;
        progress.Report($"Bundled firmware {package.Manifest.FirmwareVersion} ready.");
        progress.Report($"Using {Path.GetFileName(package.EsptoolPath)} on {port}, baud {baud}.");

        string chipOutput = await RunEsptoolAsync(package.EsptoolPath, BuildCommonArgs(port, baud, chipNoStub, "chip_id"), progress, cancellationToken);
        if (!chipOutput.Contains("ESP32-S3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected port did not identify as ESP32-S3. Refusing to flash the wrong device.");
        }

        if (mode is FlashMode.EraseOnly or FlashMode.EraseAndFlash)
        {
            progress.Report("Whole-chip erase requested.");
            await EraseFlashAsync(package, port, baud, progress, cancellationToken);
            if (mode == FlashMode.EraseOnly)
            {
                progress.Report("Erase complete. The board is blank until firmware is flashed again.");
                return;
            }
        }

        try
        {
            await WriteFlashAsync(package, port, baud, writeNoStub, progress, cancellationToken);
        }
        catch when (mode is FlashMode.Upgrade or FlashMode.EraseAndFlash)
        {
            progress.Report("Fast flash failed; retrying repair path at 115200 baud with --no-stub.");
            await WriteFlashAsync(package, port, 115200, noStub: true, progress, cancellationToken);
        }

        progress.Report("Flash complete. Replug native USB if Windows/Steam does not re-enumerate.");
    }

    private static async Task EraseFlashAsync(
        FirmwarePackage package,
        string port,
        int baud,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            progress.Report("Erasing whole chip using the esptool stub.");
            await RunEsptoolAsync(package.EsptoolPath, BuildCommonArgs(port, baud, noStub: false, "erase_flash"), progress, cancellationToken);
        }
        catch (Exception ex)
        {
            progress.Report("Stub erase_flash failed: " + ex.Message.Split(Environment.NewLine)[0]);
            progress.Report("Trying ROM erase_region fallback.");
            var args = BuildCommonArgs(port, 115200, noStub: true, "erase_region");
            args.Add("0x0");
            args.Add("0x" + ParseFlashSizeBytes(package.Manifest.FlashSize).ToString("X"));
            await RunEsptoolAsync(package.EsptoolPath, args, progress, cancellationToken);
        }
    }

    private static async Task WriteFlashAsync(
        FirmwarePackage package,
        string port,
        int baud,
        bool noStub,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var args = BuildCommonArgs(port, baud, noStub, "write_flash");
        args.Add("--flash_mode");
        args.Add(package.Manifest.FlashMode);
        args.Add("--flash_freq");
        args.Add(package.Manifest.FlashFreq);
        args.Add("--flash_size");
        args.Add(package.Manifest.FlashSize);
        args.Add("0x0");
        args.Add(package.BootloaderPath);
        args.Add("0x8000");
        args.Add(package.PartitionPath);
        args.Add("0x10000");
        args.Add(package.AppPath);

        await RunEsptoolAsync(package.EsptoolPath, args, progress, cancellationToken);
    }

    private static List<string> BuildCommonArgs(string port, int baud, bool noStub, string command)
    {
        var args = new List<string>
        {
            "--chip", "esp32s3"
        };
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

    private static int ParseFlashSizeBytes(string flashSize)
    {
        string normalized = flashSize.Trim().ToUpperInvariant();
        if (normalized.EndsWith("MB", StringComparison.Ordinal))
        {
            return int.Parse(normalized[..^2]) * 1024 * 1024;
        }
        if (normalized.EndsWith("M", StringComparison.Ordinal))
        {
            return int.Parse(normalized[..^1]) * 1024 * 1024;
        }
        return int.Parse(normalized);
    }

    private static async Task<string> RunEsptoolAsync(
        string esptoolPath,
        IReadOnlyList<string> args,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var output = new List<string>();
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

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) => Capture(e.Data, output, progress);
        process.ErrorDataReceived += (_, e) => Capture(e.Data, output, progress);
        process.Exited += (_, _) => exited.TrySetResult(process.ExitCode);

        progress.Report("esptool " + string.Join(" ", args));
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start bundled esptool.");
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

        int exitCode = await exited.Task;
        process.WaitForExit();
        string combined = string.Join(Environment.NewLine, output);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"esptool failed with exit code {exitCode}.{Environment.NewLine}{combined}");
        }
        return combined;
    }

    private static void Capture(string? line, List<string> output, IProgress<string> progress)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (output)
        {
            output.Add(line);
        }
        progress.Report(line);
    }
}
