using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Y700Switch2V55Manager;

public static class EmbeddedAssets
{
    public const string BundledPackageVersion = "v5.8.2-aio";
    public const string BundledFirmwareVersion = "5.8.2-manager";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRO2WirelessReceiverControlBoard", "embedded", BundledPackageVersion);

    public static FirmwarePackage EnsurePackage()
    {
        string firmwareRoot = Path.Combine(RootDirectory, "firmware", "v5.8");
        string toolsRoot = Path.Combine(RootDirectory, "tools");
        ExtractPrefix("embedded/firmware/v5.8/", firmwareRoot);
        ExtractPrefix("embedded/tools/", toolsRoot);

        string manifestPath = Path.Combine(firmwareRoot, "firmware_manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Bundled V5.8 firmware manifest was not extracted.", manifestPath);
        }

        FirmwareManifest manifest = JsonSerializer.Deserialize<FirmwareManifest>(
                File.ReadAllText(manifestPath),
                ManifestJsonOptions)
            ?? throw new InvalidOperationException("Bundled V5.8 firmware manifest is invalid.");
        if (manifest.Profiles is not { Count: > 0 })
        {
            throw new InvalidOperationException("Bundled V5.8 firmware manifest contains no profiles.");
        }

        foreach (FirmwareProfile profile in manifest.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || profile.Assets is not { Count: > 0 })
            {
                throw new InvalidOperationException("Bundled V5.8 firmware manifest contains an incomplete profile.");
            }
            foreach (FirmwareAsset asset in profile.Assets)
            {
                string path = Path.Combine(firmwareRoot, asset.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Bundled firmware asset is missing.", path);
                }
                string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Bundled firmware hash mismatch: " + asset.Path);
                }
            }
        }

        string esptool = Path.Combine(toolsRoot, "esptool.exe");
        if (!File.Exists(esptool))
        {
            throw new FileNotFoundException("Bundled esptool.exe is missing.", esptool);
        }

        string audioSender = Path.Combine(toolsRoot, "SendV55HapticAudioTest.exe");
        if (!File.Exists(audioSender))
        {
            throw new FileNotFoundException("Bundled haptic audio sender is missing.", audioSender);
        }

        string xinputProbe = Path.Combine(toolsRoot, "SteamXInputRumbleProbe.exe");
        if (!File.Exists(xinputProbe))
        {
            throw new FileNotFoundException("Bundled XInput rumble probe is missing.", xinputProbe);
        }

        return new FirmwarePackage(manifest, firmwareRoot, toolsRoot, esptool, audioSender, xinputProbe);
    }

    private static void ExtractPrefix(string resourcePrefix, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            string normalized = resourceName.Replace('\\', '/');
            if (!normalized.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = normalized[resourcePrefix.Length..];
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            string destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            string fullDestination = Path.GetFullPath(destination);
            string fullRoot = Path.GetFullPath(destinationRoot);
            if (!fullDestination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unsafe embedded resource path: " + relative);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
            using Stream? source = assembly.GetManifestResourceStream(resourceName);
            if (source == null)
            {
                throw new InvalidOperationException("Embedded resource not found: " + resourceName);
            }
            using FileStream target = File.Create(fullDestination);
            source.CopyTo(target);
        }
    }
}

public sealed record FirmwarePackage(
    FirmwareManifest Manifest,
    string FirmwareRoot,
    string ToolsRoot,
    string EsptoolPath,
    string AudioSenderPath,
    string XInputProbePath)
{
    public FirmwareProfile GetProfile(string id)
    {
        return Manifest.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Bundled firmware profile not found: " + id);
    }
}

public sealed class FirmwareManifest
{
    public string PackageVersion { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public string Target { get; set; } = "esp32s3";
    public string FlashMode { get; set; } = "dio";
    public string FlashFreq { get; set; } = "80m";
    public string FlashSize { get; set; } = "16MB";
    public string DefaultProfile { get; set; } = "hid_audio_uac1_4ch_ds5like";
    public List<FirmwareProfile> Profiles { get; set; } = new();
    public string Notes { get; set; } = "";
}

public sealed class FirmwareProfile
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string App { get; set; } = "";
    public List<FirmwareAsset> Assets { get; set; } = new();
}

public sealed class FirmwareAsset
{
    public string Offset { get; set; } = "";
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
