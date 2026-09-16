using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Inspection;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class ArtworkInspectorTests
{
    [Fact]
    public void Inspect_StaticWeb_DetectsIndexHtml()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ExhibitOS_Inspect_" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "index.html"), "<html><body>Artwork</body></html>");

        var result = ArtworkInspector.Inspect(temp);
        Assert.Equal(ArtworkType.StaticWeb, result.DetectedType);
        Assert.Equal("index.html", result.DetectedEntryPoint);
        Assert.Contains(result.Messages, m => m.Contains("Static"));

        try { Directory.Delete(temp, true); } catch { }
    }

    [Fact]
    public void Inspect_BackendWeb_DetectsServerJs()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ExhibitOS_Inspect_" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "server.js"), "console.log('server');");

        var result = ArtworkInspector.Inspect(temp);
        Assert.Equal(ArtworkType.BackendWeb, result.DetectedType);
        Assert.Equal("server.js", result.DetectedEntryPoint);
        Assert.Contains(result.Messages, m => m.Contains("Node backend"));

        try { Directory.Delete(temp, true); } catch { }
    }

    [Fact]
    public void Inspect_VideoFiles_DetectsVideos()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ExhibitOS_Inspect_" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        File.WriteAllBytes(Path.Combine(temp, "video1.mp4"), new byte[] { 0, 1, 2 });
        File.WriteAllBytes(Path.Combine(temp, "video2.mkv"), new byte[] { 0, 1, 2 });

        var result = ArtworkInspector.Inspect(temp);
        Assert.Equal(ArtworkType.VideoFolder, result.DetectedType);
        Assert.Contains(result.Messages, m => m.Contains("2 video file(s)"));

        try { Directory.Delete(temp, true); } catch { }
    }

    [Fact]
    public void Inspect_TestArtworks_StaticWeb_DetectsSuccessfully()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;

        var staticWebDir = Path.Combine(repoRoot, "test-artworks", "static-web");
        if (!Directory.Exists(staticWebDir)) return;

        var result = ArtworkInspector.Inspect(staticWebDir);
        Assert.Equal(ArtworkType.StaticWeb, result.DetectedType);
        Assert.Equal("index.html", result.DetectedEntryPoint);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Inspect_TestArtworks_NodeApp_DetectsSuccessfully()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;

        var nodeAppDir = Path.Combine(repoRoot, "test-artworks", "node-app");
        if (!Directory.Exists(nodeAppDir)) return;

        var result = ArtworkInspector.Inspect(nodeAppDir);
        Assert.Equal(ArtworkType.BackendWeb, result.DetectedType);
        Assert.Equal("server.js", result.DetectedEntryPoint);
        Assert.Empty(result.Errors);
    }

    private static string? FindRepoRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ExhibitOS.slnx")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        return null;
    }
}
