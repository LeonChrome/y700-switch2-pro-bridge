using System;
using System.IO;
using System.Text.Json;

namespace Y700Switch2V60Viiper;

public sealed class V60UserSettings
{
    private static readonly object FileGate = new();

    public double RumbleMultiplier { get; set; } = 1.0;
    public string PushRateLabel { get; set; } = ViiperPushRateOption.Default.Label;
    public string GyroModeLabel { get; set; } = ViiperGyroModeOption.Default.Label;
    public string BackendLabel { get; set; } = VirtualBackendOption.Default.Label;
    public string StickProcessingLabel { get; set; } = StickProcessingOption.Default.Label;

    public static V60UserSettings Load()
    {
        lock (FileGate)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new V60UserSettings();
                }

                V60UserSettings? loaded =
                    JsonSerializer.Deserialize<V60UserSettings>(
                        File.ReadAllText(SettingsPath));
                if (loaded == null)
                {
                    return new V60UserSettings();
                }
                loaded.RumbleMultiplier =
                    NormalizeRumbleMultiplier(loaded.RumbleMultiplier);
                loaded.PushRateLabel =
                    ViiperPushRateOption.FromLabel(loaded.PushRateLabel).Label;
                loaded.GyroModeLabel =
                    ViiperGyroModeOption.FromLabel(loaded.GyroModeLabel).Label;
                loaded.BackendLabel =
                    VirtualBackendOption.FromLabel(loaded.BackendLabel).Label;
                loaded.StickProcessingLabel =
                    StickProcessingOption.FromLabel(loaded.StickProcessingLabel).Label;
                return loaded;
            }
            catch
            {
                return new V60UserSettings();
            }
        }
    }

    public void Save()
    {
        lock (FileGate)
        {
            string directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            RumbleMultiplier = NormalizeRumbleMultiplier(RumbleMultiplier);
            PushRateLabel = ViiperPushRateOption.FromLabel(PushRateLabel).Label;
            GyroModeLabel = ViiperGyroModeOption.FromLabel(GyroModeLabel).Label;
            BackendLabel = VirtualBackendOption.FromLabel(BackendLabel).Label;
            StickProcessingLabel = StickProcessingOption.FromLabel(StickProcessingLabel).Label;
            string temporary = SettingsPath + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    this,
                    new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
    }

    public static double NormalizeRumbleMultiplier(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }
        return Math.Round(Math.Clamp(value, 0.0, 3.0), 1);
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PRO2WirelessReceiverControlBoard",
        "v6_settings.json");
}
