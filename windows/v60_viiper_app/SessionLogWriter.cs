using System;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Y700Switch2V60Viiper;

public sealed class SessionLogWriter : IAsyncDisposable
{
    private readonly Channel<string> channel =
        Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
    private readonly Task writerTask;

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
        writerTask = Task.Run(WriteLoopAsync);
    }

    public string FilePath { get; }

    public void Write(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            channel.Writer.TryWrite(text);
        }
    }

    private async Task WriteLoopAsync()
    {
        await using FileStream file = new(
            FilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            16 * 1024,
            useAsync: true);
        await using StreamWriter writer = new(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        await foreach (string text in channel.Reader.ReadAllAsync())
        {
            await writer.WriteAsync(text);
        }
    }

    public async ValueTask DisposeAsync()
    {
        channel.Writer.TryComplete();
        await writerTask;
    }
}
