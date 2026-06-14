using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2V55Manager;

public static class SerialCommandClient
{
    private static readonly SemaphoreSlim PortGate = new(1, 1);
    private static SerialPort? activePort;
    private static string activePortName = "";
    private static readonly Regex AnsiEscape = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled);

    public static void Close()
    {
        SerialPort? port = null;
        if (!PortGate.Wait(TimeSpan.FromMilliseconds(250)))
        {
            return;
        }
        try
        {
            port = DetachActivePort();
        }
        finally
        {
            PortGate.Release();
        }
        DisposePort(port);
    }

    public static async Task<bool> CloseAsync(int timeoutMs = 750)
    {
        int timeout = Math.Max(50, timeoutMs);
        SerialPort? port = null;
        if (!await PortGate.WaitAsync(TimeSpan.FromMilliseconds(timeout)))
        {
            return false;
        }
        try
        {
            port = DetachActivePort();
        }
        finally
        {
            PortGate.Release();
        }

        if (port == null)
        {
            return true;
        }

        Task closeTask = Task.Run(() => DisposePort(port));
        Task completed = await Task.WhenAny(closeTask, Task.Delay(timeout));
        if (completed != closeTask)
        {
            return false;
        }
        await closeTask;
        return true;
    }

    public static void CloseInBackground()
    {
        _ = Task.Run(async () => await CloseAsync(750));
    }

    public static async Task<string> SendAsync(
        string portName,
        string command,
        int readSeconds,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new InvalidOperationException("请先选择 ESP32 控制板对应的串口。");
        if (PortGate.CurrentCount == 0)
        {
            progress.Report("[SERIAL] waiting for " + portName);
        }
        await PortGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => SendLocked(portName, command, readSeconds, progress, cancellationToken),
                cancellationToken);
        }
        finally
        {
            PortGate.Release();
        }
    }

    private static string SendLocked(
        string portName,
        string command,
        int readSeconds,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var builder = new StringBuilder();
                SerialPort port = EnsureOpen(portName, progress);

                progress.Report("> " + command);
                port.WriteLine(command.TrimEnd());

                DateTime deadline = DateTime.UtcNow.AddSeconds(readSeconds);
                DateTime? responseSeenAt = null;
                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.WaitHandle.WaitOne(75);
                }
                return builder.ToString();
            }
            catch (Exception ex) when (IsPortFailure(ex) && attempt == 1)
            {
                progress.Report("[SERIAL] port error, reopening " + portName + ": " + ex.Message);
                CloseActivePort();
                cancellationToken.WaitHandle.WaitOne(250);
            }
        }
    }

    private static SerialPort EnsureOpen(string portName, IProgress<string> progress)
    {
        if (activePort != null &&
            activePort.IsOpen &&
            string.Equals(activePortName, portName, StringComparison.OrdinalIgnoreCase))
        {
            return activePort;
        }

        CloseActivePort();
        var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
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
        activePort = port;
        activePortName = portName;
        progress.Report("[SERIAL] opened persistent " + portName);
        Thread.Sleep(350);
        try
        {
            port.DiscardInBuffer();
        }
        catch (InvalidOperationException)
        {
            CloseActivePort();
            throw;
        }
        return port;
    }

    private static void CloseActivePort()
    {
        DisposePort(DetachActivePort());
    }

    private static SerialPort? DetachActivePort()
    {
        SerialPort? port = activePort;
        activePort = null;
        activePortName = "";
        return port;
    }

    private static void DisposePort(SerialPort? port)
    {
        if (port == null) return;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        catch
        {
            // Closing a stale USB serial handle can fail after unplug; discard it either way.
        }
        finally
        {
            port.Dispose();
        }
    }

    private static bool IsPortFailure(Exception ex)
    {
        return ex is IOException ||
               ex is UnauthorizedAccessException ||
               ex is InvalidOperationException ||
               ex is TimeoutException;
    }
}
