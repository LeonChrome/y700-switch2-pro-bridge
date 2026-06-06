using System;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

namespace Y700Switch2V55Manager;

public static class SerialCommandClient
{
    public static async Task<string> SendAsync(
        string portName,
        string command,
        int readSeconds,
        IProgress<string> progress)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new InvalidOperationException("请先选择 CH343P COM 口。");
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
        await Task.Delay(250);
        port.DiscardInBuffer();

        progress.Report("> " + command);
        port.WriteLine(command.TrimEnd());

        DateTime deadline = DateTime.Now.AddSeconds(readSeconds);
        while (DateTime.Now < deadline)
        {
            string chunk = port.ReadExisting();
            if (!string.IsNullOrEmpty(chunk))
            {
                builder.Append(chunk);
                foreach (string line in chunk.Replace("\r", "").Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line)) progress.Report(line);
                }
            }
            await Task.Delay(100);
        }
        return builder.ToString();
    }
}
