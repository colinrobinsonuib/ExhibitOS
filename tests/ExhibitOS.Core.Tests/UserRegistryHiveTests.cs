using ExhibitOS.Provisioning;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class UserRegistryHiveTests
{
    [Fact]
    public void CreateProfileBuffer_DoesNotExceedWin32MaxPath()
    {
        Assert.Equal(260, UserRegistryHive.ProfilePathBufferCapacity);
    }
}
