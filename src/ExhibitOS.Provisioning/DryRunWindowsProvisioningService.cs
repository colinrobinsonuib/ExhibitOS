using ExhibitOS.Core.Configuration;
using ExhibitOS.Provisioning.Diagnostics;

namespace ExhibitOS.Provisioning;

public class DryRunWindowsProvisioningService : IWindowsProvisioningService
{
    private readonly List<string> _actionLog = new();

    public bool IsDryRun => true;
    public IReadOnlyList<string> ActionLog => _actionLog.AsReadOnly();

    private void Record(string action)
    {
        _actionLog.Add($"[DRY-RUN {DateTime.UtcNow:HH:mm:ss}] {action}");
    }

    public Task<ProvisioningResult> CreateArtworkUserAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        Record($"Create local passwordless account '{username}'");
        return Task.FromResult(ProvisioningResult.Ok($"[Dry Run] Account '{username}' would be created.", new[] { $"Create user {username}" }, isDryRun: true));
    }

    public Task<ProvisioningResult> HardenArtworkUserSecurityAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        Record($"Harden security for '{username}' (deny RDP, deny network logon, console blank-password only)");
        return Task.FromResult(ProvisioningResult.Ok($"[Dry Run] Hardened security for '{username}'.", new[] { $"Hardened {username}" }, isDryRun: true));
    }

    public Task<ProvisioningResult> ApplyVisitorLockdownAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        Record($"Apply visitor lockdown policies for '{username}' (DisableTaskMgr, suppress hotkeys, suppress Alt+F4)");
        return Task.FromResult(ProvisioningResult.Ok($"[Dry Run] Visitor lockdown applied for '{username}'.", new[] { $"Visitor lockdown {username}" }, isDryRun: true));
    }

    public Task<ProvisioningResult> ConfigureAutoAdminLogonAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        Record($"Configure AutoAdminLogon in registry for '{username}'");
        return Task.FromResult(ProvisioningResult.Ok($"[Dry Run] AutoAdminLogon configured for '{username}'.", new[] { $"AutoAdminLogon {username}" }, isDryRun: true));
    }

    public Task<ProvisioningResult> SetCustomShellAsync(string username, string shellPath, CancellationToken ct = default)
    {
        Record($"Set custom Winlogon shell for '{username}' to '{shellPath}'");
        return Task.FromResult(ProvisioningResult.Ok($"[Dry Run] Custom shell set for '{username}'.", new[] { $"Custom shell: {shellPath}" }, isDryRun: true));
    }

    public Task<ProvisioningResult> ConfigureScheduledRebootAsync(TimeOnly rebootTime, CancellationToken ct = default)
    {
        Record($"Create daily Task Scheduler morning reboot task for '{rebootTime:HH:mm}' as SYSTEM");
        return Task.FromResult(ProvisioningResult.Ok($"[Dry Run] Scheduled reboot task configured for '{rebootTime:HH:mm}'.", new[] { $"Reboot task at {rebootTime:HH:mm}" }, isDryRun: true));
    }

    public Task<ProvisioningResult> ConfigureClosingPowerTaskAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default)
    {
        Record($"Create daily Task Scheduler closing-power task for '{config.Schedule.ClosingTime}' ({config.Schedule.OvernightPowerMode})");
        return Task.FromResult(ProvisioningResult.Ok(
            $"[Dry Run] Closing-power task would be configured for {config.Schedule.ClosingTime}.",
            new[] { $"Closing task at {config.Schedule.ClosingTime}: {config.Schedule.OvernightPowerMode}" },
            isDryRun: true));
    }

    public Task<ProvisioningResult> ConfigureFirewallRulesAsync(NetworkingMode mode, CancellationToken ct = default)
    {
        Record($"Configure Windows Firewall rules for mode '{mode}'");
        return Task.FromResult(ProvisioningResult.Ok($"[Dry Run] Firewall rules configured for '{mode}'.", new[] { $"Firewall: {mode}" }, isDryRun: true));
    }

    public async Task<ProvisioningResult> ApplyFullProvisioningAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default)
    {
        var actions = new List<string>();

        var r1 = await CreateArtworkUserAsync("ArtworkUser", ct);
        actions.AddRange(r1.AppliedActions);

        var r2 = await HardenArtworkUserSecurityAsync("ArtworkUser", ct);
        actions.AddRange(r2.AppliedActions);

        var r3 = await ApplyVisitorLockdownAsync("ArtworkUser", ct);
        actions.AddRange(r3.AppliedActions);

        var r4 = await ConfigureAutoAdminLogonAsync("ArtworkUser", ct);
        actions.AddRange(r4.AppliedActions);

        var r5 = await SetCustomShellAsync("ArtworkUser", paths.WatchdogExe, ct);
        actions.AddRange(r5.AppliedActions);

        var evaluator = new ExhibitOS.Core.Schedule.ScheduleEvaluator(config.Schedule);
        var r6 = await ConfigureScheduledRebootAsync(evaluator.MorningRebootTime, ct);
        actions.AddRange(r6.AppliedActions);

        var r7 = await ConfigureFirewallRulesAsync(config.NetworkingMode, ct);
        actions.AddRange(r7.AppliedActions);

        var r8 = await ConfigureClosingPowerTaskAsync(config, paths, ct);
        actions.AddRange(r8.AppliedActions);

        Record($"Full provisioning dry-run completed with {actions.Count} actions.");
        return ProvisioningResult.Ok("[Dry Run] Full exhibition provisioning simulated successfully.", actions, isDryRun: true);
    }

    public Task<ProvisioningResult> RestoreToNormalUseAsync(bool deleteArtworkUser = false, CancellationToken ct = default)
    {
        Record($"Restore PC to normal use (remove custom shell, remove lockdown, remove firewall rules, disable AutoAdminLogon, remove reboot task, deleteArtworkUser: {deleteArtworkUser})");
        return Task.FromResult(ProvisioningResult.Ok("[Dry Run] Restore to normal use simulated successfully.", new[] { "Restored normal configuration" }, isDryRun: true));
    }

    public Task<DiagnosticReport> RunSystemDiagnosticAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default)
    {
        // Verification is always read-only and reports observed Windows state,
        // even when provisioning mutations are disabled on a development PC.
        return WindowsStateVerifier.InspectAsync(config, paths, ct);
    }
}
