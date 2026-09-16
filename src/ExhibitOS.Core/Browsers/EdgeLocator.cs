using Microsoft.Win32;
using System.Runtime.Versioning;

namespace ExhibitOS.Core.Browsers;

public static class EdgeLocator
{
    public static string? FindInstalledEdge(IEnumerable<string>? additionalCandidates = null)
    {
        var candidates = new List<string>();
        if (additionalCandidates is not null)
        {
            candidates.AddRange(additionalCandidates);
        }

        if (OperatingSystem.IsWindows())
        {
            AddRegistryCandidate(candidates, RegistryHive.LocalMachine, RegistryView.Registry64);
            AddRegistryCandidate(candidates, RegistryHive.LocalMachine, RegistryView.Registry32);
            AddRegistryCandidate(candidates, RegistryHive.CurrentUser, RegistryView.Default);
        }

        AddCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe"));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryCandidate(List<string> candidates, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPath = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            if (appPath?.GetValue(null) is string path)
            {
                candidates.Add(path.Trim('"'));
            }
        }
        catch
        {
            // Continue through the known installation paths.
        }
    }

    private static void AddCandidate(List<string> candidates, string programFiles)
    {
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"));
        }
    }
}
