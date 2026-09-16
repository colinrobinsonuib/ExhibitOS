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
}
