using ExhibitOS.Core.Configuration;
using ExhibitOS.Provisioning.Diagnostics;

namespace ExhibitOS.Provisioning;

public interface IWindowsProvisioningService
{
    bool IsDryRun { get; }

    Task<ProvisioningResult> CreateArtworkUserAsync(string username = "ArtworkUser", CancellationToken ct = default);
    Task<ProvisioningResult> HardenArtworkUserSecurityAsync(string username = "ArtworkUser", CancellationToken ct = default);
    Task<ProvisioningResult> ApplyVisitorLockdownAsync(string username = "ArtworkUser", CancellationToken ct = default);
    Task<ProvisioningResult> ConfigureAutoAdminLogonAsync(string username = "ArtworkUser", CancellationToken ct = default);
    Task<ProvisioningResult> SetCustomShellAsync(string username, string shellPath, CancellationToken ct = default);
    Task<ProvisioningResult> ConfigureScheduledRebootAsync(TimeOnly rebootTime, CancellationToken ct = default);
    Task<ProvisioningResult> ConfigureClosingPowerTaskAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default);
    Task<ProvisioningResult> ConfigureFirewallRulesAsync(NetworkingMode mode, CancellationToken ct = default);
    Task<ProvisioningResult> ConfigureEdgePoliciesAsync(CancellationToken ct = default);
    Task<ProvisioningResult> ApplyFullProvisioningAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default);
    Task<ProvisioningResult> RestoreToNormalUseAsync(bool deleteArtworkUser = false, CancellationToken ct = default);
    Task<DiagnosticReport> RunSystemDiagnosticAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default);
}
