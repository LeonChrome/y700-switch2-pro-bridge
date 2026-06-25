using System;
using System.IO;
using System.Text.Json;

namespace Y700Switch2V60Viiper;

public sealed class V60UserSettings
{
    private static readonly object FileGate = new();

    public double RumbleMultiplier { get; set; } = 1.0;
    public string PushRateLabel { get; set; } = ViiperPushRateOption.Default.Label;
    public string BackendLabel { get; set; } = VirtualBackendOption.Default.Label;
    public string StickProcessingLabel { get; set; } = StickProcessingOption.Default.Label;
    public string SelectedModeKey { get; set; } = "dualsense";
    public bool AudioEndpointGuardEnabled { get; set; } = true;
    public double Ps5GyroScalePitch { get; set; } = 1.0;
    public double Ps5GyroScaleYaw { get; set; } = 1.0;
    public double Ps5GyroScaleRoll { get; set; } = 1.0;
    public bool ProfessionalInvertGyroPitch { get; set; }
    public bool ProfessionalInvertGyroYaw { get; set; }
    public bool ProfessionalInvertGyroRoll { get; set; }

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
                loaded.BackendLabel =
                    VirtualBackendOption.FromLabel(loaded.BackendLabel).Label;
                loaded.StickProcessingLabel =
                    StickProcessingOption.FromLabel(loaded.StickProcessingLabel).Label;
                loaded.SelectedModeKey =
                    NormalizeModeKey(loaded.SelectedModeKey);
                loaded.Ps5GyroScalePitch =
                    NormalizePs5GyroScale(loaded.Ps5GyroScalePitch);
                loaded.Ps5GyroScaleYaw =
                    NormalizePs5GyroScale(loaded.Ps5GyroScaleYaw);
                loaded.Ps5GyroScaleRoll =
                    NormalizePs5GyroScale(loaded.Ps5GyroScaleRoll);
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
            BackendLabel = VirtualBackendOption.FromLabel(BackendLabel).Label;
            StickProcessingLabel = StickProcessingOption.FromLabel(StickProcessingLabel).Label;
            SelectedModeKey = NormalizeModeKey(SelectedModeKey);
            Ps5GyroScalePitch = NormalizePs5GyroScale(Ps5GyroScalePitch);
            Ps5GyroScaleYaw = NormalizePs5GyroScale(Ps5GyroScaleYaw);
            Ps5GyroScaleRoll = NormalizePs5GyroScale(Ps5GyroScaleRoll);
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

    public static double NormalizePs5GyroScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }
        return Math.Round(Math.Clamp(value, 0.1, 4.0), 2);
    }

    public static string NormalizeModeKey(string? value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "dualsenseedge" or "dualsense_edge" or "edge" or "ps5edge" => "dualsenseedge",
            "dualsenseproimu" or "dualsense_professional_imu" or "ps5proimu" or "ps5_professional_imu" => "dualsenseproimu",
            "pro2" or "ns2pro" or "nintendo" => "pro2",
            "xboxproimu" or "xbox_professional_imu" or "xinputproimu" => "xboxproimu",
            "xbox" or "xinput" or "xbox360" => "xbox",
            _ => "dualsense"
        };
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PRO2WirelessReceiverControlBoard",
        "v6_settings.json");
}
