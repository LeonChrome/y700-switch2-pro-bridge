using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Y700Switch2V60Viiper;

public sealed record UsbipRuntime(string ExePath, string DirectoryPath);

public static class UsbipRuntimeLocator
{
    public static UsbipRuntime? Find()
    {
        foreach (string candidate in CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return new UsbipRuntime(candidate, Path.GetDirectoryName(candidate) ?? "");
            }
        }

        return null;
    }

    public static string BuildPathWithUsbipDirectory(string currentPath, UsbipRuntime runtime)
    {
        string safeCurrentPath = currentPath ?? "";
        if (string.IsNullOrWhiteSpace(runtime.DirectoryPath))
        {
            return safeCurrentPath;
        }

        string[] parts = safeCurrentPath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ContainsDirectory(parts, runtime.DirectoryPath))
        {
            return safeCurrentPath;
        }

        return runtime.DirectoryPath + Path.PathSeparator + safeCurrentPath;
    }

    private static bool ContainsDirectory(IEnumerable<string> parts, string target)
    {
        string fullTarget;
        try
        {
            fullTarget = Path.GetFullPath(target);
        }
        catch
        {
            return false;
        }

        foreach (string part in parts)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(part), fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        foreach (string pathCandidate in PathEnvironmentCandidates())
        {
            yield return pathCandidate;
        }

        foreach (string localRoot in LocalSearchRoots())
        {
            foreach (string found in TryFindUnder(localRoot))
            {
                yield return found;
            }
        }

        foreach (string programRoot in ProgramFilesRoots())
        {
            yield return Path.Combine(programRoot, "USBip", "usbip.exe");
            yield return Path.Combine(programRoot, "USBIP", "usbip.exe");
            yield return Path.Combine(programRoot, "usbip-win2", "usbip.exe");
            yield return Path.Combine(programRoot, "USBip-win2", "usbip.exe");
        }
    }

    private static IEnumerable<string> PathEnvironmentCandidates()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir, "usbip.exe");
            }
            catch
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static IEnumerable<string> LocalSearchRoots()
    {
        string[] relativeRoots =
        [
            Path.Combine("tools", "usbip-win2"),
            Path.Combine("tools", "usbip"),
            "usbip-win2",
            "usbip"
        ];

        string? cursor = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(cursor); i++)
        {
            foreach (string relative in relativeRoots)
            {
                yield return Path.GetFullPath(Path.Combine(cursor, relative));
            }
            cursor = Directory.GetParent(cursor)?.FullName;
        }

        foreach (string relative in relativeRoots)
        {
            yield return Path.GetFullPath(relative);
        }
    }

    private static IEnumerable<string> TryFindUnder(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "usbip.exe", SearchOption.AllDirectories)
                .Take(8)
                .ToList();
        }
        catch
        {
            yield break;
        }

        foreach (string file in files)
        {
            yield return file;
        }
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return programFiles;
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return programFilesX86;
        }
    }
}
