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
    public void ProvisioningServiceFactory_RequiresFlagAndTargetEnvironment()
    {
        var original = Environment.GetEnvironmentVariable(DevelopmentSafetyGuard.TargetEnvVariable);
        try
        {
            Environment.SetEnvironmentVariable(DevelopmentSafetyGuard.TargetEnvVariable, null);
            Assert.IsType<DryRunWindowsProvisioningService>(
                ProvisioningServiceFactory.Create(allowSystemModifications: true));

            Environment.SetEnvironmentVariable(
                DevelopmentSafetyGuard.TargetEnvVariable,
                DevelopmentSafetyGuard.AllowedValueTarget);
            Assert.IsType<RealWindowsProvisioningService>(
                ProvisioningServiceFactory.Create(allowSystemModifications: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevelopmentSafetyGuard.TargetEnvVariable, original);
        }
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

    [Fact]
    public async Task DryRunDiagnostics_ReportObservedState_NotSimulatedSuccess()
    {
        var service = new DryRunWindowsProvisioningService();
        var paths = new ExhibitionPaths(Path.Combine(Path.GetTempPath(), "ExhibitOS_Verification_Test"));

        var report = await service.RunSystemDiagnosticAsync(new ExhibitionConfig(), paths);

        Assert.Contains(report.Items, item => item.Name == "Restricted artwork account");
        Assert.Contains(report.Items, item => item.Name == "Automatic sign-in");
        Assert.Contains(report.Items, item => item.Name == "Daily reboot timer");
        Assert.Contains(report.Items, item => item.Name == "Closing power timer");
        Assert.Contains(report.Items, item => item.Name == "ExhibitOS firewall policy");
        Assert.DoesNotContain(report.Items, item => item.Message.Contains("Simulated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalhostFirewallCommands_UseSupportedNetshArguments()
    {
        var commands = RealWindowsProvisioningService.BuildFirewallRuleCommands(NetworkingMode.LocalhostOnly);

        Assert.Equal(3, commands.Count);
        Assert.All(commands, command => Assert.DoesNotContain("group=", command, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, command => command.Contains("interfacetype=lan", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("interfacetype=wireless", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("interfacetype=ras", StringComparison.Ordinal));
        Assert.All(commands, command => Assert.DoesNotContain("remoteip=", command, StringComparison.OrdinalIgnoreCase));
    }
}
