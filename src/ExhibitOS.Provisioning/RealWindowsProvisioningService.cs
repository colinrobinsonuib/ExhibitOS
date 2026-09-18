using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
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

        return await Task.FromResult(ProvisioningResult.Ok(
            $"Security hardening applied for '{username}'.",
            new[] { "LimitBlankPasswordUse=1" }));
    }

    public async Task<ProvisioningResult> ApplyVisitorLockdownAsync(string username = "ArtworkUser", CancellationToken ct = default)
    {
        AssertSafety(nameof(ApplyVisitorLockdownAsync));

        var sid = GetUserSid(username);
        if (string.IsNullOrEmpty(sid))
        {
            return ProvisioningResult.Fail($"Cannot locate SID for user '{username}'.");
        }

        await using var hive = await UserRegistryHive.OpenAsync(
            username, writable: true, createProfileIfMissing: true, ct)
            ?? throw new InvalidOperationException($"Windows profile for '{username}' is unavailable.");

        using (var key = hive.Root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System"))
        {
            key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
        }

        using (var key = hive.Root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"))
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

        await using var hive = await UserRegistryHive.OpenAsync(
            username, writable: true, createProfileIfMissing: true, ct)
            ?? throw new InvalidOperationException($"Windows profile for '{username}' is unavailable.");
        using (var key = hive.Root.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon"))
        {
            key.SetValue("Shell", shellPath, RegistryValueKind.String);
        }

        return await Task.FromResult(ProvisioningResult.Ok($"Custom shell set for '{username}' to '{shellPath}'.", new[] { $"Shell={shellPath}" }));
    }

    public async Task<ProvisioningResult> ConfigureScheduledRebootAsync(TimeOnly rebootTime, CancellationToken ct = default)
    {
        AssertSafety(nameof(ConfigureScheduledRebootAsync));
        return await CreateDailyTaskAsync(
            "ExhibitOS_DailyReboot",
            "ExhibitOS daily reboot with wake timer",
            rebootTime,
            "shutdown.exe",
            "/r /t 0 /f",
            wakeToRun: true,
            ct);
    }

    public async Task<ProvisioningResult> ConfigureClosingPowerTaskAsync(
        ExhibitionConfig config,
        ExhibitionPaths paths,
        CancellationToken ct = default)
    {
        AssertSafety(nameof(ConfigureClosingPowerTaskAsync));
        var closingTime = ExhibitOS.Core.Schedule.ScheduleEvaluator.ParseTime(config.Schedule.ClosingTime, new TimeOnly(20, 0));
        var action = config.Schedule.OvernightPowerMode switch
        {
            OvernightPowerMode.Shutdown => "shutdown",
            OvernightPowerMode.SleepWithWakeTimers => "sleep",
            _ => "idle"
        };
        return await CreateDailyTaskAsync(
            "ExhibitOS_ClosingPower",
            $"ExhibitOS closing power action: {action}",
            closingTime,
            paths.WatchdogExe,
            $"--power-action {action} \"{paths.RootDirectory}\"",
            wakeToRun: false,
            ct);
    }

    private static async Task<ProvisioningResult> CreateDailyTaskAsync(
        string taskName,
        string description,
        TimeOnly triggerTime,
        string command,
        string arguments,
        bool wakeToRun,
        CancellationToken ct)
    {
        var document = BuildDailyTaskDocument(description, triggerTime, command, arguments, wakeToRun);

        var taskXml = Path.Combine(Path.GetTempPath(), $"ExhibitOS-task-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(taskXml, document.ToString(), Encoding.Unicode, ct);
            var (exitCode, stdout, stderr) = await RunCliAsync(
                "schtasks", BuildTaskRegistrationArguments(taskName, taskXml), ct);
            if (exitCode != 0)
            {
                return ProvisioningResult.Fail($"Failed to register task '{taskName}': {stderr} {stdout}".Trim());
            }

            return ProvisioningResult.Ok(
                $"Registered Windows task '{taskName}' for {triggerTime:HH:mm}.",
                new[] { $"Task Scheduler: {taskName} at {triggerTime:HH:mm}" });
        }
        finally
        {
            File.Delete(taskXml);
        }
    }

    internal static XDocument BuildDailyTaskDocument(
        string description,
        TimeOnly triggerTime,
        string command,
        string arguments,
        bool wakeToRun)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var start = DateTime.Today.Add(triggerTime.ToTimeSpan()).ToString("yyyy-MM-dd'T'HH:mm:ss");
        return new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(ns + "Task", new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo", new XElement(ns + "Description", description)),
                new XElement(ns + "Triggers",
                    new XElement(ns + "CalendarTrigger",
                        new XElement(ns + "StartBoundary", start),
                        new XElement(ns + "Enabled", true),
                        new XElement(ns + "ScheduleByDay", new XElement(ns + "DaysInterval", 1)))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "System"),
                        new XElement(ns + "UserId", "S-1-5-18"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", false),
                    new XElement(ns + "StopIfGoingOnBatteries", false),
                    new XElement(ns + "StartWhenAvailable", true),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", false),
                    new XElement(ns + "WakeToRun", wakeToRun),
                    new XElement(ns + "Enabled", true),
                    new XElement(ns + "ExecutionTimeLimit", "PT1H")),
                new XElement(ns + "Actions", new XAttribute("Context", "System"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", command),
                        new XElement(ns + "Arguments", arguments)))));
    }

    internal static string BuildTaskRegistrationArguments(string taskName, string taskXml) =>
        $"/create /tn \"{taskName}\" /xml \"{taskXml}\" /ru SYSTEM /f";

    public async Task<ProvisioningResult> ConfigureFirewallRulesAsync(NetworkingMode mode, CancellationToken ct = default)
    {
        AssertSafety(nameof(ConfigureFirewallRulesAsync));

        // Remove only rules owned by ExhibitOS. Never use `name=all` here: that
        // deletes unrelated Windows and third-party firewall policy.
        var ruleNames = new[]
        {
            "ExhibitOS_Block_All_Out",
            "ExhibitOS_Allow_Loopback_Out_v4",
            "ExhibitOS_Allow_Loopback_Out_v6",
            "ExhibitOS_Allow_LAN_1",
            "ExhibitOS_Allow_LAN_2",
            "ExhibitOS_Allow_LAN_3",
            "ExhibitOS_Block_LAN_Out",
            "ExhibitOS_Block_Wireless_Out",
            "ExhibitOS_Block_RAS_Out"
        };
        foreach (var ruleName in ruleNames)
        {
            await RunCliAsync("netsh", $"advfirewall firewall delete rule name=\"{ruleName}\"", ct);
        }

        var commands = BuildFirewallRuleCommands(mode);

        foreach (var command in commands)
        {
            var result = await RunCliAsync("netsh", command, ct);
            if (result.ExitCode != 0)
            {
                return ProvisioningResult.Fail(
                    $"Windows could not configure the ExhibitOS firewall rules: {result.StdErr} {result.StdOut}".Trim());
            }
        }

        return ProvisioningResult.Ok($"Configured firewall for {mode}.", new[] { $"Firewall mode: {mode}" });
    }

    internal static IReadOnlyList<string> BuildFirewallRuleCommands(NetworkingMode mode) => mode switch
        {
            NetworkingMode.LocalhostOnly => new[]
            {
                "advfirewall firewall add rule name=\"ExhibitOS_Block_LAN_Out\" dir=out action=block profile=any interfacetype=lan enable=yes",
                "advfirewall firewall add rule name=\"ExhibitOS_Block_Wireless_Out\" dir=out action=block profile=any interfacetype=wireless enable=yes",
                "advfirewall firewall add rule name=\"ExhibitOS_Block_RAS_Out\" dir=out action=block profile=any interfacetype=ras enable=yes"
            },
            NetworkingMode.InternetEnabled => Array.Empty<string>(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported networking mode.")
        };

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

        var r8 = await ConfigureClosingPowerTaskAsync(config, paths, ct);
        if (!r8.Success) return r8;
        actions.AddRange(r8.AppliedActions);

        var r9 = await ConfigureEdgePoliciesAsync(ct);
        if (!r9.Success) return r9;
        actions.AddRange(r9.AppliedActions);

        return ProvisioningResult.Ok("Full exhibition provisioning completed successfully.", actions);
    }

    public async Task<ProvisioningResult> ConfigureEdgePoliciesAsync(CancellationToken ct = default)
    {
        AssertSafety(nameof(ConfigureEdgePoliciesAsync));

        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge"))
        {
            key.SetValue("AutoplayAllowed", 1, RegistryValueKind.DWord);
        }

        return await Task.FromResult(ProvisioningResult.Ok(
            "Configured Microsoft Edge policies (AutoplayAllowed=1).",
            new[] { "AutoplayAllowed=1" }));
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

        // Remove Edge autoplay policy
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge", writable: true))
        {
            key?.DeleteValue("AutoplayAllowed", false);
        }
        actions.Add("Removed Edge autoplay policy");

        // Remove custom shell and lockdown keys
        var sid = GetUserSid("ArtworkUser");
        if (!string.IsNullOrEmpty(sid))
        {
            await using var hive = await UserRegistryHive.OpenAsync(
                "ArtworkUser", writable: true, createProfileIfMissing: false, ct);
            using (var key = hive?.Root.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true))
            {
                key?.DeleteValue("Shell", false);
            }
            using (var key = hive?.Root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: true))
            {
                key?.DeleteValue("DisableTaskMgr", false);
            }
            using (var key = hive?.Root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", writable: true))
            {
                key?.DeleteValue("NoWinKeys", false);
            }
            actions.Add("Removed custom shell and lockdown policies");
        }

        // Delete scheduled task
        await RunCliAsync("schtasks", "/delete /tn \"ExhibitOS_DailyReboot\" /f", ct);
        await RunCliAsync("schtasks", "/delete /tn \"ExhibitOS_ClosingPower\" /f", ct);
        actions.Add("Deleted scheduled reboot and closing-power tasks");

        // Delete custom firewall rules
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Block_All_Out\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_Loopback_Out_v4\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_Loopback_Out_v6\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_LAN_1\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_LAN_2\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Allow_LAN_3\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Block_LAN_Out\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Block_Wireless_Out\"", ct);
        await RunCliAsync("netsh", "advfirewall firewall delete rule name=\"ExhibitOS_Block_RAS_Out\"", ct);
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
        return await WindowsStateVerifier.InspectAsync(config, paths, ct);
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
