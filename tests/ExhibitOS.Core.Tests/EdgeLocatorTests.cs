using ExhibitOS.Core.Browsers;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class EdgeLocatorTests
{
    [Fact]
    public void FindInstalledEdge_AcceptsExistingExplicitCandidate()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"edge-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(tempFile, Array.Empty<byte>());
        try
        {
            Assert.Equal(Path.GetFullPath(tempFile), EdgeLocator.FindInstalledEdge(new[] { tempFile }));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
