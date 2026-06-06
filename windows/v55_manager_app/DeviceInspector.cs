using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;

namespace Y700Switch2V55Manager;

public sealed record PortItem(string PortName, string Name, string Manufacturer, string DeviceId, bool LikelyCh343)
{
    public string DisplayName => LikelyCh343
        ? PortName + "  CH343/USB Serial  " + Name
        : PortName + "  " + Name;
}

public static class DeviceInspector
{
    public static IReadOnlyList<PortItem> GetPorts()
    {
        var names = SerialPort.GetPortNames().OrderBy(ParsePortNumber).ToArray();
        var map = new Dictionary<string, (string Name, string Manufacturer, string DeviceId)>(StringComparer.OrdinalIgnoreCase);
        try
        {
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
        }
        catch
        {
        }

        return names.Select(port =>
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
            return new PortItem(port, name, manufacturer, deviceId, likely);
        }).ToArray();
    }

    public static string GetUsbSummary()
    {
        try
        {
            var lines = new List<string>();
            using var searcher = new ManagementObjectSearcher("SELECT Name,Status,PNPClass,DeviceID FROM Win32_PnPEntity");
            foreach (ManagementObject item in searcher.Get())
            {
                string deviceId = Convert.ToString(item["DeviceID"]) ?? "";
                if (!deviceId.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase)) continue;
                lines.Add((Convert.ToString(item["PNPClass"]) ?? "device") + ": " +
                          (Convert.ToString(item["Name"]) ?? "unknown") + " / " +
                          (Convert.ToString(item["Status"]) ?? "unknown"));
            }
            if (lines.Count == 0)
            {
                return "未发现 VID_054C&PID_0CE6。若刚刷完，请重插 native USB / OTG。";
            }
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return "USB 检查失败: " + ex.Message;
        }
    }

    private static int ParsePortNumber(string port)
    {
        string digits = new(port.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int value) ? value : int.MaxValue;
    }
}
