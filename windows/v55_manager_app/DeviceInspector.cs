using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace Y700Switch2V55Manager;

public sealed record PortItem(
    string PortName,
    string Name,
    string Manufacturer,
    string DeviceId,
    bool LikelyCh343,
    bool CanOpen,
    string Availability)
{
    public string DisplayName
    {
        get
        {
            string prefix = LikelyCh343
                ? PortName + "  CH343/USB 串口  "
                : PortName + "  ";
            string suffix = string.IsNullOrWhiteSpace(Availability)
                ? ""
                : "  [" + Availability + "]";
            return prefix + Name + suffix;
        }
    }
}

public sealed record PortScanResult(IReadOnlyList<PortItem> Ports, bool MetadataTimedOut, bool MetadataFailed);
public sealed record UsbProbeResult(string Summary, bool TimedOut, bool Failed);

public static class DeviceInspector
{
    public static PortScanResult ScanPorts(int metadataTimeoutMs = 900)
    {
        string[] names = GetPortNamesSafe();
        Dictionary<string, (string Name, string Manufacturer, string DeviceId)> map = new(StringComparer.OrdinalIgnoreCase);
        bool metadataTimedOut = false;
        bool metadataFailed = false;

        if (names.Length > 0)
        {
            try
            {
                Task<Dictionary<string, (string Name, string Manufacturer, string DeviceId)>> task =
                    Task.Run(() => QueryPortMetadata(names));
                if (task.Wait(Math.Max(200, metadataTimeoutMs)))
                {
                    map = task.Result;
                }
                else
                {
                    metadataTimedOut = true;
                }
            }
            catch
            {
                metadataFailed = true;
            }
        }

        PortItem[] ports = names.Select(port =>
        {
            map.TryGetValue(port, out var info);
            string name = string.IsNullOrWhiteSpace(info.Name) ? port : info.Name;
            string manufacturer = info.Manufacturer ?? "";
            string deviceId = info.DeviceId ?? "";
            bool likely = name.Contains("CH343", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("CH340", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("USB-SERIAL", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("USB Serial", StringComparison.OrdinalIgnoreCase) ||
                          manufacturer.Contains("WCH", StringComparison.OrdinalIgnoreCase) ||
                          manufacturer.Contains("QinHeng", StringComparison.OrdinalIgnoreCase) ||
                          deviceId.Contains("VID_1A86&PID_55D3", StringComparison.OrdinalIgnoreCase);

            bool canOpen = true;
            string availability = likely ? "未主动打开" : "";

            return new PortItem(port, name, manufacturer, deviceId, likely, canOpen, availability);
        }).ToArray();

        return new PortScanResult(ports, metadataTimedOut, metadataFailed);
    }

    public static UsbProbeResult ProbeUsb(int timeoutMs = 1200)
    {
        try
        {
            Task<string> task = Task.Run(QueryUsbSummary);
            if (task.Wait(Math.Max(300, timeoutMs)))
            {
                return new UsbProbeResult(task.Result, false, false);
            }

            return new UsbProbeResult(
                "USB 检查超时。界面会保持可操作；可以稍后再点一次“USB 检查”，或者先继续离线操作。",
                true,
                false);
        }
        catch (Exception ex)
        {
            return new UsbProbeResult("USB 检查失败：" + ex.Message, false, true);
        }
    }

    private static string[] GetPortNamesSafe()
    {
        try
        {
            return SerialPort.GetPortNames().OrderBy(ParsePortNumber).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Dictionary<string, (string Name, string Manufacturer, string DeviceId)> QueryPortMetadata(string[] names)
    {
        var map = new Dictionary<string, (string Name, string Manufacturer, string DeviceId)>(StringComparer.OrdinalIgnoreCase);
        using var searcher = new ManagementObjectSearcher("SELECT Name,Manufacturer,DeviceID FROM Win32_PnPEntity");
        foreach (ManagementObject item in searcher.Get())
        {
            string name = Convert.ToString(item["Name"]) ?? "";
            string manufacturer = Convert.ToString(item["Manufacturer"]) ?? "";
            string deviceId = Convert.ToString(item["DeviceID"]) ?? "";
            foreach (string port in names)
            {
                if (name.Contains("(" + port + ")", StringComparison.OrdinalIgnoreCase))
                {
                    map[port] = (name, manufacturer, deviceId);
                }
            }
        }
        return map;
    }

    private static string QueryUsbSummary()
    {
        var lines = new List<string>();
        using var searcher = new ManagementObjectSearcher("SELECT Name,Status,PNPClass,DeviceID FROM Win32_PnPEntity");
        foreach (ManagementObject item in searcher.Get())
        {
            string deviceId = Convert.ToString(item["DeviceID"]) ?? "";
            string identity;
            if (deviceId.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase))
            {
                identity = "DualSense-like VID_054C&PID_0CE6";
            }
            else if (deviceId.Contains("VID_057E&PID_2069", StringComparison.OrdinalIgnoreCase))
            {
                identity = "Nintendo/Pro2 VID_057E&PID_2069";
            }
            else if (deviceId.Contains("VID_045E&PID_028E", StringComparison.OrdinalIgnoreCase))
            {
                identity = "Xbox / XInput VID_045E&PID_028E";
            }
            else if (deviceId.Contains("VID_045E&PID_0B00", StringComparison.OrdinalIgnoreCase))
            {
                identity = "Xbox Elite 2 / GIP VID_045E&PID_0B00";
            }
            else if (deviceId.Contains("VID_045E&PID_02E3", StringComparison.OrdinalIgnoreCase))
            {
                identity = "Xbox One S legacy test VID_045E&PID_02E3";
            }
            else
            {
                continue;
            }

            lines.Add((Convert.ToString(item["PNPClass"]) ?? "device") + ": " +
                      identity + " / " +
                      (Convert.ToString(item["Name"]) ?? "unknown") + " / " +
                      (Convert.ToString(item["Status"]) ?? "unknown"));
        }

        if (lines.Count == 0)
        {
            return "没有检测到 DualSense-like 054C:0CE6、Nintendo/Pro2 057E:2069、Xbox/XInput 045E:028E 或 Xbox Elite 2/GIP 045E:0B00 设备。如果刚刷完固件，请重新插拔原生 USB / OTG。";
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static int ParsePortNumber(string port)
    {
        string digits = new(port.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int value) ? value : int.MaxValue;
    }
}
