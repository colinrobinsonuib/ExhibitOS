using System.Diagnostics;
using System.Security.Principal;
using ExhibitOS.Core.Configuration;
using ExhibitOS.Provisioning.Diagnostics;
using ExhibitOS.Provisioning.Safety;
using Microsoft.Win32;

namespace ExhibitOS.Provisioning;

public class RealWindowsProvisioningService : IWindowsProvisioningService
{
    private readonly bool _allowSystemModifications;

    public bool IsDryRun => false;

    public RealWindowsProvisioningService(bool allowSystemModifications = false)
    {
        _allowSystemModifications = allowSystemModifications;
    }

    private void AssertSafety(string action)
    {
        DevelopmentSafetyGuard.AssertSafeToExecute(action, _allowSystemModifications);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(string fileName, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }

    public async Task<ProvisioningResult> CreateArtworkUserAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        AssertSafety(nameof(CreateArtworkUserAsync));

        // Check if user already exists
        var (checkCode, _, _) = await RunCliAsync("net", $"user {username}", ct);
        if (checkCode == 0)
        {
            return ProvisioningResult.Ok($"User '{username}' already exists.", new[] { $"User {username} verified" });
        }

        // Create passwordless user
        var (createCode, outStr, errStr) = await RunCliAsync("net", $"user {username} \"\" /add /passwordchg:no", ct);
        if (createCode != 0)
        {
            return ProvisioningResult.Fail($"Failed to create user '{username}': {errStr} {outStr}");
        }

        // Set password never expires via PowerShell
        var psScript = $"Set-LocalUser -Name '{username}' -PasswordNeverExpires $true";
        await RunCliAsync("powershell", $"-NoProfile -Command \"{psScript}\"", ct);

        return ProvisioningResult.Ok($"Created local user '{username}'.", new[] { $"Created user {username}" });
    }

    public async Task<ProvisioningResult> HardenArtworkUserSecurityAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        AssertSafety(nameof(HardenArtworkUserSecurityAsync));

        // Enforce blank password console-only policy in registry
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa", writable: true))
        {
            key?.SetValue("LimitBlankPasswordUse", 1, RegistryValueKind.DWord);
        }

        // Use secedit or ntrights equivalent via PowerShell script to assign deny logon rights
        var psScript = $@"
$secEditInf = [System.IO.Path]::GetTempFileName()
$secEditSdb = [System.IO.Path]::GetTempFileName()
secedit /export /cfg $secEditInf /areas USER_RIGHTS
$content = Get-Content $secEditInf
# Add deny rights for {username}
if ($content -notmatch 'SeDenyRemoteInteractiveLogonRight') {{
    $content += 'SeDenyRemoteInteractiveLogonRight = *S-1-5-32-545'
}}
Set-Content $secEditInf $content
secedit /configure /db $secEditSdb /cfg $secEditInf /areas USER_RIGHTS
Remove-Item $secEditInf, $secEditSdb -ErrorAction SilentlyContinue
";
        await RunCliAsync("powershell", $"-NoProfile -Command \"{psScript}\"", ct);

        return ProvisioningResult.Ok($"Security hardening applied for '{username}'.", new[] { "LimitBlankPasswordUse=1", "Deny Remote Interactive Logon" });
    }

    public async Task<ProvisioningResult> ApplyVisitorLockdownAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        AssertSafety(nameof(ApplyVisitorLockdownAsync));

        var sid = GetUserSid(username);
        if (string.IsNullOrEmpty(sid))
        {
            return ProvisioningResult.Fail($"Cannot locate SID for user '{username}'.");
        }

        // Registry lockdown for user SID
        var subKeySystem = $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Policies\System";
        using (var key = Registry.Users.CreateSubKey(subKeySystem))
        {
            key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
        }

        var subKeyExplorer = $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        using (var key = Registry.Users.CreateSubKey(subKeyExplorer))
        {
            key.SetValue("NoWinKeys", 1, RegistryValueKind.DWord);
        }

        return await Task.FromResult(ProvisioningResult.Ok($"Visitor lockdown applied for '{username}'.", new[] { "DisableTaskMgr=1", "NoWinKeys=1" }));
    }

    public async Task<ProvisioningResult> ConfigureAutoAdminLogonAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        AssertSafety(nameof(ConfigureAutoAdminLogonAsync));

        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true))
        {
            if (key != null)
            {
                key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
                key.SetValue("DefaultUserName", username, RegistryValueKind.String);
                key.SetValue("DefaultPassword", "", RegistryValueKind.String);
                key.SetValue("DefaultDomainName", ".", RegistryValueKind.String);
            }
        }

        return await Task.FromResult(ProvisioningResult.Ok($"AutoAdminLogon configured for '{username}'.", new[] { "AutoAdminLogon=1", $"DefaultUserName={username}" }));
    }

    public async Task<ProvisioningResult> SetCustomShellAsync(string username, string shellPath, CancellationToken ct = default)
    {
        AssertSafety(nameof(SetCustomShellAsync));

        var sid = GetUserSid(username);
        if (string.IsNullOrEmpty(sid))
        {
            return ProvisioningResult.Fail($"Cannot locate SID for user '{username}'.");
        }

        var subKey = $@"{sid}\Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
        using (var key = Registry.Users.CreateSubKey(subKey))
        {
            key.SetValue("Shell", shellPath, RegistryValueKind.String);
        }

        return await Task.FromResult(ProvisioningResult.Ok($"Custom shell set for '{username}' to '{shellPath}'.", new[] { $"Shell={shellPath}" }));
    }

    public async Task<ProvisioningResult> ConfigureScheduledRebootAsync(TimeOnly rebootTime, CancellationToken ct = default)
    {
        AssertSafety(nameof(ConfigureScheduledRebootAsync));

        var timeStr = rebootTime.ToString("HH:mm");
        var (exitCode, stdout, stderr) = await RunCliAsync("schtasks", $"/create /tn \"ExhibitOS_DailyReboot\" /tr \"shutdown /r /t 0 /f\" /sc daily /st {timeStr} /ru \"SYSTEM\" /f", ct);

        if (exitCode != 0)
        {
            return ProvisioningResult.Fail($"Failed to register reboot task: {stderr} {stdout}");
        }

        return ProvisioningResult.Ok($"Registered daily morning reboot task for {timeStr}.", new[] { $"Reboot task at {timeStr}" });
    }

    public async Task<ProvisioningResult> ConfigureFirewallRulesAsync(NetworkingMode mode, CancellationToken ct = default)
    {
        AssertSafety(nameof(ConfigureFirewallRulesAsync));

        // First remove existing ExhibitOS rules
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=all dir=out profile=any", ct);

        switch (mode)
        {
            case NetworkingMode.OfflineExhibition:
                // Block all outbound except loopback
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Block_All_Out\" dir=out action=block profile=any", ct);
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Allow_Loopback_Out_v4\" dir=out action=allow remoteip=127.0.0.1 profile=any", ct);
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Allow_Loopback_Out_v6\" dir=out action=allow remoteip=::1 profile=any", ct);
                break;

            case NetworkingMode.LocalNetworkOnly:
                // Block outbound except private ranges and loopback
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Block_All_Out\" dir=out action=block profile=any", ct);
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Allow_Loopback_Out_v4\" dir=out action=allow remoteip=127.0.0.1 profile=any", ct);
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Allow_Loopback_Out_v6\" dir=out action=allow remoteip=::1 profile=any", ct);
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Allow_LAN_1\" dir=out action=allow remoteip=10.0.0.0/8 profile=any", ct);
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Allow_LAN_2\" dir=out action=allow remoteip=172.16.0.0/12 profile=any", ct);
                await RunCliAsync("netsh", "advfirewall firewall add rule name=\"ExhibitOS_Allow_LAN_3\" dir=out action=allow remoteip=192.168.0.0/16 profile=any", ct);
                break;

            case NetworkingMode.InternetEnabled:
                // Default Windows firewall allows outbound
                break;
        }

        return ProvisioningResult.Ok($"Configured firewall for {mode}.", new[] { $"Firewall mode: {mode}" });
    }

    public async Task<ProvisioningResult> ApplyFullProvisioningAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default)
    {
        AssertSafety(nameof(ApplyFullProvisioningAsync));

        var actions = new List<string>();

        var r1 = await CreateArtworkUserAsync("ArtworkUser", ct);
        if (!r1.Success) return r1;
        actions.AddRange(r1.AppliedActions);

        var r2 = await HardenArtworkUserSecurityAsync("ArtworkUser", ct);
        if (!r2.Success) return r2;
        actions.AddRange(r2.AppliedActions);

        var r3 = await ApplyVisitorLockdownAsync("ArtworkUser", ct);
        if (!r3.Success) return r3;
        actions.AddRange(r3.AppliedActions);

        var r4 = await ConfigureAutoAdminLogonAsync("ArtworkUser", ct);
        if (!r4.Success) return r4;
        actions.AddRange(r4.AppliedActions);

        var r5 = await SetCustomShellAsync("ArtworkUser", paths.WatchdogExe, ct);
        if (!r5.Success) return r5;
        actions.AddRange(r5.AppliedActions);

        var evaluator = new ExhibitOS.Core.Schedule.ScheduleEvaluator(config.Schedule);
        var r6 = await ConfigureScheduledRebootAsync(evaluator.MorningRebootTime, ct);
        if (!r6.Success) return r6;
        actions.AddRange(r6.AppliedActions);

        var r7 = await ConfigureFirewallRulesAsync(config.NetworkingMode, ct);
        if (!r7.Success) return r7;
        actions.AddRange(r7.AppliedActions);

        return ProvisioningResult.Ok("Full exhibition provisioning completed successfully.", actions);
    }

    public async Task<ProvisioningResult> RestoreToNormalUseAsync(bool deleteArtworkUser = false, CancellationToken ct = default)
    {
        AssertSafety(nameof(RestoreToNormalUseAsync));

        var actions = new List<string>();

        // Disable AutoAdminLogon
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true))
        {
            key?.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
        }
        actions.Add("Disabled AutoAdminLogon");

        // Remove custom shell and lockdown keys
        var sid = GetUserSid("ArtworkUser");
        if (!string.IsNullOrEmpty(sid))
        {
            using (var key = Registry.Users.OpenSubKey($@"{sid}\Software\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true))
            {
                key?.DeleteValue("Shell", false);
            }
            using (var key = Registry.Users.OpenSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: true))
            {
                key?.DeleteValue("DisableTaskMgr", false);
            }
            using (var key = Registry.Users.OpenSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", writable: true))
            {
                key?.DeleteValue("NoWinKeys", false);
            }
            actions.Add("Removed custom shell and lockdown policies");
        }

        // Delete scheduled task
        await RunCliAsync("schtasks", "/delete /tn \"ExhibitOS_DailyReboot\" /f", ct);
        actions.Add("Deleted scheduled reboot task");

        // Delete custom firewall rules
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Block_All_Out\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_Loopback_Out_v4\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_Loopback_Out_v6\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_LAN_1\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_LAN_2\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_LAN_3\"", ct);
        actions.Add("Removed custom firewall rules");

        if (deleteArtworkUser)
        {
            await RunCliAsync("net", "user ArtworkUser /delete", ct);
            actions.Add("Deleted ArtworkUser account");
        }

        return ProvisioningResult.Ok("Restored PC to normal use.", actions);
    }

    public async Task<DiagnosticReport> RunSystemDiagnosticAsync(ExhibitionConfig config, ExhibitionPaths paths, CancellationToken ct = default)
    {
        var report = new DiagnosticReport();

        report.Add("Artwork Directory", Directory.Exists(paths.ArtworkDirectory), $"Artwork directory exists: {paths.ArtworkDirectory}", "Create artwork folder.");
        report.Add("Config File", File.Exists(paths.ConfigFile), $"Config file exists: {paths.ConfigFile}", "Run setup wizard to generate configuration.");
        report.Add("Watchdog Binary", File.Exists(paths.WatchdogExe), $"Watchdog executable exists: {paths.WatchdogExe}", "Compile and install ExhibitWatchdog.exe.");
        report.Add("Bundled mpv", File.Exists(paths.MpvExe), $"mpv executable exists: {paths.MpvExe}", "Install mpv in runtime/bin/mpv.");
        report.Add("Bundled node", File.Exists(paths.NodeExe), $"node executable exists: {paths.NodeExe}", "Install node in runtime/bin/node.");

        // Check ArtworkUser
        var (userCode, _, _) = await RunCliAsync("net", "user ArtworkUser", ct);
        report.Add("ArtworkUser Account", userCode == 0, userCode == 0 ? "ArtworkUser account exists." : "ArtworkUser account not found.", "Run configuration to create ArtworkUser.");

        return report;
    }

    private static string? GetUserSid(string username)
    {
        try
        {
            var account = new NTAccount(username);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            return sid.Value;
        }
        catch
        {
            return null;
        }
    }
}
