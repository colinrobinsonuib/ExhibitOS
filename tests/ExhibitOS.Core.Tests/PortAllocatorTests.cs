using ExhibitOS.Core.Network;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class PortAllocatorTests
{
    [Fact]
    public void GetAvailableLoopbackPort_ReturnsValidPort()
    {
        var port = PortAllocator.GetAvailableLoopbackPort();
        Assert.InRange(port, 1024, 65535);
    }
}
