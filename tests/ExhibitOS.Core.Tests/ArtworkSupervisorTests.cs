using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.ProcessSupervision;
using ExhibitOS.Core.Supervisors;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class ArtworkSupervisorTests
{
    [Fact]
    public async Task StartArtwork_ApplicationType_CreatesJobObject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExhibitOS_SupervisorTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var paths = new ExhibitionPaths(tempDir);

        // Create dummy executable or script
        var dummyApp = Path.Combine(tempDir, "artwork", "app.bat");
        Directory.CreateDirectory(Path.GetDirectoryName(dummyApp)!);
        File.WriteAllText(dummyApp, "@echo off\r\nexit 0");

        var config = new ExhibitionConfig
        {
            Artwork = new ArtworkConfig
            {
                Type = ArtworkType.Application,
                ArtworkDirectory = Path.GetDirectoryName(dummyApp),
                EntryPoint = "app.bat"
            }
        };

        var jobFactory = new MockJobObjectFactory();
        using var supervisor = new ArtworkSupervisor(config, paths, jobFactory: jobFactory);

        var started = await supervisor.StartArtworkAsync();
        Assert.True(started);
        Assert.True(jobFactory.CreatedJobs.ContainsKey("ExhibitOS_App"));

        supervisor.StopArtwork();
        Assert.True(jobFactory.CreatedJobs["ExhibitOS_App"].TerminateCalled);

        // Cleanup
        try { Directory.Delete(tempDir, true); } catch { }
    }
}
