using ExhibitOS.Core.Configuration;

namespace ExhibitOS.Core.Inspection;

public class ArtworkInspectionResult
{
    public ArtworkType DetectedType { get; set; }
    public bool HasFiles { get; set; }
    public List<string> Messages { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public string? DetectedEntryPoint { get; set; }
    public bool EdgeAvailable { get; set; }
}

public static class ArtworkInspector
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v" };

    public static ArtworkInspectionResult Inspect(string directoryPath)
    {
        var result = new ArtworkInspectionResult();

        if (!Directory.Exists(directoryPath))
        {
            result.Errors.Add($"Directory does not exist: {directoryPath}");
            return result;
        }

        var files = Directory.GetFiles(directoryPath);
        result.HasFiles = files.Length > 0;

        // Check for Web backend
        var serverJs = Path.Combine(directoryPath, "server.js");
        if (File.Exists(serverJs))
        {
            result.DetectedType = ArtworkType.BackendWeb;
            result.DetectedEntryPoint = "server.js";
            result.Messages.Add("✓ Web artwork with Node backend detected (server.js found)");
        }
        else
        {
            // Check for Static Web
            var indexHtml = Path.Combine(directoryPath, "index.html");
            if (File.Exists(indexHtml))
            {
                result.DetectedType = ArtworkType.StaticWeb;
                result.DetectedEntryPoint = "index.html";
                result.Messages.Add("✓ Web artwork detected (Static — index.html found)");
            }
        }

        // Check for Video files
        var videoFiles = files.Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
        if (videoFiles.Count > 0)
        {
            if (result.DetectedEntryPoint == null)
            {
                result.DetectedType = ArtworkType.VideoFolder;
            }
            var exts = string.Join(", ", videoFiles.Select(f => Path.GetExtension(f).TrimStart('.')).Distinct());
            result.Messages.Add($"✓ Found {videoFiles.Count} video file(s) ({exts})");
        }

        // Check for executables
        var exeFiles = files.Where(f => Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (exeFiles.Count > 0)
        {
            if (result.DetectedEntryPoint == null && videoFiles.Count == 0)
            {
                result.DetectedType = ArtworkType.Application;
                result.DetectedEntryPoint = Path.GetFileName(exeFiles[0]);
            }
            result.Messages.Add($"✓ Executable found: {Path.GetFileName(exeFiles[0])}");
        }

        if (result.Messages.Count == 0)
        {
            result.Errors.Add("✗ No supported artwork files (videos, index.html, server.js, or .exe) found in folder.");
        }

        // Check Edge browser availability
        result.EdgeAvailable = IsEdgeInstalled();
        if (!result.EdgeAvailable)
        {
            result.Errors.Add("✗ Microsoft Edge is required for Web Artwork — install Edge and retry");
        }

        return result;
    }

    public static bool IsEdgeInstalled()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };

        return possiblePaths.Any(File.Exists);
    }
}
