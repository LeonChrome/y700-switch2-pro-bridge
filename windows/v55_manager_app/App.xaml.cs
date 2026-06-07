using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Y700Switch2V55Manager;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception."));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        int verifyIndex = Array.FindIndex(
            e.Args,
            arg => string.Equals(arg, "--verify-package", StringComparison.OrdinalIgnoreCase));
        if (verifyIndex >= 0)
        {
            string? outputPath = verifyIndex + 1 < e.Args.Length ? e.Args[verifyIndex + 1] : null;
            VerifyPackageAndExit(outputPath);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void VerifyPackageAndExit(string? outputPath)
    {
        try
        {
            FirmwarePackage package = EmbeddedAssets.EnsurePackage();
            FirmwareProfile haptic = package.GetProfile("hid_audio_uac1_4ch_ds5like");
            FirmwareProfile recovery = package.GetProfile("hid_only");
            FirmwareProfile pro2 = package.GetProfile("pro2_bridge_v5_5");
            int assetCount = package.Manifest.Profiles.Sum(profile => profile.Assets.Count);
            string result = string.Join(Environment.NewLine, new[]
            {
                "result=passed",
                "package=" + package.Manifest.PackageVersion,
                "firmware=" + package.Manifest.FirmwareVersion,
                "profiles=" + string.Join(",", package.Manifest.Profiles.Select(profile => profile.Id)),
                "haptic_assets=" + haptic.Assets.Count,
                "recovery_assets=" + recovery.Assets.Count,
                "pro2_assets=" + pro2.Assets.Count,
                "asset_count=" + assetCount,
                "esptool=" + package.EsptoolPath
            });
            WriteVerificationResult(outputPath, result);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            WriteVerificationResult(outputPath, "result=failed" + Environment.NewLine + ex);
            Shutdown(2);
        }
    }

    private static void WriteVerificationResult(string? outputPath, string result)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, result, Encoding.UTF8);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string path = WriteCrashLog(e.Exception);
        MessageBox.Show(
            "V5.5 Manager 启动或运行时发生错误。\n\n" + e.Exception.Message + "\n\n诊断日志：\n" + path,
            "Y700 Switch2 V5.5 Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private static string WriteCrashLog(Exception exception)
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Y700Switch2V55Manager",
                "logs");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            var text = new StringBuilder()
                .AppendLine("Y700 Switch2 V5.5 Manager crash")
                .AppendLine("time=" + DateTime.Now.ToString("O"))
                .AppendLine("version=" + typeof(App).Assembly.GetName().Version)
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }
        catch
        {
            return "(failed to write crash log)";
        }
    }
}
