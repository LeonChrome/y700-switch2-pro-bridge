using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Y700Switch2V60Viiper;

public sealed class SessionLogWriter : IAsyncDisposable
{
    private readonly object sync = new();

    public SessionLogWriter()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRO2WirelessReceiverControlBoard",
            "v6_logs");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(
            directory,
            "manager_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
        using FileStream file = new(
            FilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
    }

    public string FilePath { get; }

    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (sync)
        {
            File.AppendAllText(
                FilePath,
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}
