using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Y700Switch2Manager;

public sealed class SerialDeviceClient : IDisposable
{
    private SerialPort? port;
    private CancellationTokenSource? readCts;

    public bool IsConnected => port?.IsOpen == true;
    public string? PortName => port?.PortName;

    public event Action<string>? LineReceived;
    public event Action<string>? Error;

    public static string[] GetPorts() => SerialPort.GetPortNames();

    public void Connect(string portName, int baudRate = 115200)
    {
        Disconnect();
        port = new SerialPort(portName, baudRate)
        {
            NewLine = "\n",
            Encoding = Encoding.UTF8,
            ReadTimeout = 500,
            WriteTimeout = 1000,
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false
        };
        port.Open();
        readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoop(readCts.Token));
    }

    public void Disconnect()
    {
        readCts?.Cancel();
        readCts = null;
        if (port != null)
        {
            try { port.Close(); } catch { }
            port.Dispose();
            port = null;
        }
    }

    public void SendCommand(string command)
    {
        if (port?.IsOpen != true)
        {
            throw new InvalidOperationException("Serial port is not connected.");
        }
        port.Write(command.TrimEnd() + "\n");
    }

    private void ReadLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && port?.IsOpen == true)
        {
            try
            {
                string line = port.ReadLine().TrimEnd('\r', '\n');
                LineReceived?.Invoke(line);
            }
            catch (TimeoutException)
            {
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
                break;
            }
        }
    }

    public void Dispose() => Disconnect();
}
