using System;
using System.IO;
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
