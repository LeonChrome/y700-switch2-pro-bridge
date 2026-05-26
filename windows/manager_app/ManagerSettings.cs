using System.IO;
using System.Text.Json;

namespace Y700Switch2Manager;

public sealed class ManagerSettings
{
    public bool AutoConnectLastPort { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; }
    public string DefaultMode { get; set; } = "generic";
    public string LogLevel { get; set; } = "info";
    public string? LastPort { get; set; }

    public static string SettingsPath =>
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Y700Switch2Manager", "settings.json");

    public static ManagerSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(SettingsPath)) ?? new ManagerSettings();
            }
        }
        catch
        {
        }
        return new ManagerSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
