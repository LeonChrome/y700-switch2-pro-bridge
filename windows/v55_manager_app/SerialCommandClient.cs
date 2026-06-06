using System;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2V55Manager;

public static class SerialCommandClient
{
    private static readonly SemaphoreSlim PortGate = new(1, 1);
    private static readonly Regex AnsiEscape = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled);

    public static async Task<string> SendAsync(
        string portName,
        string command,
        int readSeconds,
        IProgress<string> progress)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new InvalidOperationException("请先选择 CH343P COM 口。");
        if (PortGate.CurrentCount == 0)
        {
            progress.Report("[SERIAL] waiting for " + portName);
        }
        await PortGate.WaitAsync();
        try
        {
            var builder = new StringBuilder();
            using var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
            {
                NewLine = "\n",
                Encoding = Encoding.UTF8,
                ReadTimeout = 200,
                WriteTimeout = 1000,
                DtrEnable = false,
                RtsEnable = false,
                Handshake = Handshake.None
            };

            port.Open();
            port.DtrEnable = false;
            port.RtsEnable = false;
            await Task.Delay(150);
            port.DiscardInBuffer();

            progress.Report("> " + command);
            port.WriteLine(command.TrimEnd());

            DateTime deadline = DateTime.UtcNow.AddSeconds(readSeconds);
            DateTime? responseSeenAt = null;
            while (DateTime.UtcNow < deadline)
            {
                string chunk = port.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    string clean = AnsiEscape.Replace(chunk, "");
                    builder.Append(clean);
                    foreach (string line in clean.Replace("\r", "").Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line)) progress.Report(line);
                    }
                    if (clean.Contains("{\"ok\":", StringComparison.Ordinal) ||
                        clean.Contains("{\"cmd\":", StringComparison.Ordinal))
                    {
                        responseSeenAt = DateTime.UtcNow;
                    }
                }
                if (responseSeenAt.HasValue &&
                    DateTime.UtcNow - responseSeenAt.Value >= TimeSpan.FromMilliseconds(200))
                {
                    break;
                }
                await Task.Delay(75);
            }
            return builder.ToString();
        }
        finally
        {
            PortGate.Release();
        }
    }
}
