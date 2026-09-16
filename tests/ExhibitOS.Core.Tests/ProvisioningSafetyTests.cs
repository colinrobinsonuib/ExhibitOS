using ExhibitOS.Core.Configuration;
using ExhibitOS.Provisioning;
using ExhibitOS.Provisioning.Safety;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class ProvisioningSafetyTests
{
    [Fact]
    public async Task RealWindowsProvisioningService_ThrowsSafetyException_OnDevPC()
    {
        // By default without explicit allowance or target env, real provisioning must throw!
        var realService = new RealWindowsProvisioningService(allowSystemModifications: false);

        await Assert.ThrowsAsync<DevelopmentEnvironmentSafetyException>(async () =>
        {
            await realService.CreateArtworkUserAsync("ArtworkUser");
        });

        await Assert.ThrowsAsync<DevelopmentEnvironmentSafetyException>(async () =>
        {
            await realService.ApplyVisitorLockdownAsync("ArtworkUser");
        });

        await Assert.ThrowsAsync<DevelopmentEnvironmentSafetyException>(async () =>
        {
            await realService.ConfigureAutoAdminLogonAsync("ArtworkUser");
        });
    }

    [Fact]
    public void ProvisioningServiceFactory_DefaultsToDryRun()
    {
        var service = ProvisioningServiceFactory.Create(allowSystemModifications: false);
        Assert.True(service.IsDryRun);
        Assert.IsType<DryRunWindowsProvisioningService>(service);
    }

    [Fact]
    public async Task DryRunWindowsProvisioningService_AppliesAllActions_WithoutThrowing()
    {
        var dryRun = new DryRunWindowsProvisioningService();
        var config = new ExhibitionConfig();
        var tempPaths = new ExhibitionPaths(Path.Combine(Path.GetTempPath(), "ExhibitOS_Test"));

        var result = await dryRun.ApplyFullProvisioningAsync(config, tempPaths);

        Assert.True(result.Success);
        Assert.True(result.IsDryRun);
        Assert.NotEmpty(result.AppliedActions);
        Assert.True(dryRun.ActionLog.Count >= 7);
    }
}
