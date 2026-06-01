using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Y700Switch2Manager;

public static class EmbeddedAssets
{
    public const string BundledFirmwareVersion = "5.0.0";
    public const string BundledPackageVersion = "v5.0.0-aio";

    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Y700Switch2Manager", "embedded", BundledPackageVersion);

    public static FirmwarePackage EnsureFirmwarePackage()
    {
        string packageRoot = Path.Combine(RootDirectory, "firmware", BundledFirmwareVersion);
        ExtractPrefix("embedded/firmware/v5.0.0/", packageRoot);
        ExtractPrefix("embedded/tools/", Path.Combine(RootDirectory, "tools"));
        ExtractPrefix("embedded/drivers/", Path.Combine(RootDirectory, "drivers"));

        string manifestPath = Path.Combine(packageRoot, "firmware_manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Bundled firmware manifest was not extracted.", manifestPath);
        }

        FirmwareManifest manifest = JsonSerializer.Deserialize<FirmwareManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException("Bundled firmware manifest is invalid.");

        foreach (FirmwareAsset asset in manifest.Assets)
        {
            string path = Path.Combine(packageRoot, asset.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Bundled firmware asset is missing.", path);
            }

            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Bundled firmware hash mismatch for {asset.Path}.");
            }
        }

        string bootloader = Path.Combine(packageRoot, "bootloader", "bootloader.bin");
        string partition = Path.Combine(packageRoot, "partition_table", "partition-table.bin");
        string app = Path.Combine(packageRoot, "esp32s3_switch2_bridge.bin");
        string esptool = Path.Combine(RootDirectory, "tools", "esptool.exe");
        string drivers = Path.Combine(RootDirectory, "drivers");
        return new FirmwarePackage(manifest, packageRoot, bootloader, partition, app, esptool, drivers);
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

            string destination = Path.Combine(destinationRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));
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
    string RootDirectory,
    string BootloaderPath,
    string PartitionPath,
    string AppPath,
    string EsptoolPath,
    string DriverDirectory);

public sealed class FirmwareManifest
{
    public string PackageVersion { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public string Target { get; set; } = "esp32s3";
    public string FlashMode { get; set; } = "dio";
    public string FlashFreq { get; set; } = "80m";
    public string FlashSize { get; set; } = "16MB";
    public List<FirmwareAsset> Assets { get; set; } = new();
    public string Notes { get; set; } = "";
}

public sealed class FirmwareAsset
{
    public string Offset { get; set; } = "";
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
}
