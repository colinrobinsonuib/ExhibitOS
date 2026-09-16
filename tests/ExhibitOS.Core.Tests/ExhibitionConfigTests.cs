using ExhibitOS.Core.Configuration;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class ExhibitionConfigTests
{
    [Fact]
    public void ExhibitionConfig_SerializationAndDeserialization_RoundTripsSuccessfully()
    {
        var config = new ExhibitionConfig
        {
            ExhibitionName = "Bergen Rain",
            Artwork = new ArtworkConfig
            {
                Type = ArtworkType.BackendWeb,
                ArtworkDirectory = @"C:\ExhibitOS\artwork",
                EntryPoint = "server.js"
            },
            Schedule = new ScheduleConfig
            {
                OpeningTime = "08:00",
                ClosingTime = "19:30",
                MorningRebootTime = "07:30",
                TimeZoneId = "Europe/Oslo",
                OvernightPowerMode = OvernightPowerMode.SleepWithWakeTimers
            },
            DisplayAndSound = new DisplaySoundConfig
            {
                AudioDeviceName = "Line Out",
                HideCursor = true,
                OvernightDisplayBehavior = OvernightDisplayBehavior.SignalOff
            },
            NetworkingMode = NetworkingMode.LocalhostOnly
        };

        var json = config.ToJson();
        Assert.Contains("\"backendWeb\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"bergen Rain\"", json, StringComparison.OrdinalIgnoreCase);

        var deserialized = ExhibitionConfig.FromJson(json);
        Assert.NotNull(deserialized);
        Assert.Equal("Bergen Rain", deserialized.ExhibitionName);
        Assert.Equal(ArtworkType.BackendWeb, deserialized.Artwork.Type);
        Assert.Equal("08:00", deserialized.Schedule.OpeningTime);
        Assert.Equal("19:30", deserialized.Schedule.ClosingTime);
        Assert.Equal(OvernightPowerMode.SleepWithWakeTimers, deserialized.Schedule.OvernightPowerMode);
        Assert.Equal(NetworkingMode.LocalhostOnly, deserialized.NetworkingMode);
    }

    [Theory]
    [InlineData("OfflineExhibition")]
    [InlineData("LocalNetworkOnly")]
    public void ExhibitionConfig_MigratesLegacyNetworkModes(string legacyMode)
    {
        var config = ExhibitionConfig.FromJson($$"""{"networkingMode":"{{legacyMode}}"}""");
        Assert.Equal(NetworkingMode.LocalhostOnly, config.NetworkingMode);
    }

    [Fact]
    public void ExhibitionPaths_DefaultRoot_MatchesSpec()
    {
        var paths = new ExhibitionPaths();
        Assert.Equal(@"C:\ExhibitOS", paths.RootDirectory);
        Assert.Equal(@"C:\ExhibitOS\artwork", paths.ArtworkDirectory);
        Assert.Equal(@"C:\ExhibitOS\config\exhibition.json", paths.ConfigFile);
        Assert.Equal(@"C:\ExhibitOS\runtime\ExhibitWatchdog.exe", paths.WatchdogExe);
        Assert.Equal(@"C:\ExhibitOS\runtime\bin\mpv\mpv.exe", paths.MpvExe);
        Assert.Equal(@"C:\ExhibitOS\runtime\bin\node\node.exe", paths.NodeExe);
        Assert.Equal(@"C:\ExhibitOS\runtime\bin\node\static-server.js", paths.StaticServerJs);
    }
}
