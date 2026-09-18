using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Schedule;
using Microsoft.Win32;

namespace ExhibitOS.Provisioning.Diagnostics;

public static class WindowsStateVerifier
{
    private const string ArtworkUsername = "ArtworkUser";
    private const string RebootTaskName = "ExhibitOS_DailyReboot";

    public static async Task<DiagnosticReport> InspectAsync(
        ExhibitionConfig config,
        ExhibitionPaths paths,
        CancellationToken ct = default)
    {
        var report = new DiagnosticReport();
        AddFileChecks(report, paths);

        var account = await RunCliAsync("net", $"user {ArtworkUsername}", ct);
        var accountExists = account.ExitCode == 0;
        report.Add("Restricted artwork account",
            accountExists ? DiagnosticState.Confirmed : DiagnosticState.Missing,
            accountExists ? $"Local account '{ArtworkUsername}' exists." : $"Local account '{ArtworkUsername}' was not found.",
            "Run Configure This PC for Exhibition.");

        var sid = accountExists ? GetUserSid(ArtworkUsername) : null;
        InspectAutoLogon(report);
        InspectBlankPasswordPolicy(report);
        InspectEdgePolicies(report);
        await InspectPerUserRegistryAsync(report, sid, paths, ct);
        await InspectRebootTaskAsync(report, config, ct);
        await InspectClosingPowerTimerAsync(report, config, paths, ct);
        await InspectFirewallAsync(report, config.NetworkingMode, ct);

        return report;
    }

    private static void AddFileChecks(DiagnosticReport report, ExhibitionPaths paths)
    {
        AddFile(report, "Saved configuration", paths.ConfigFile, "Complete and save the setup wizard.");
        AddFile(report, "Watchdog executable", paths.WatchdogExe, "Reinstall ExhibitOS.");
        AddFile(report, "Bundled mpv runtime", paths.MpvExe, "Reinstall ExhibitOS.");
        AddFile(report, "Bundled Node.js runtime", paths.NodeExe, "Reinstall ExhibitOS.");
        report.Add("Artwork directory",
            Directory.Exists(paths.ArtworkDirectory) ? DiagnosticState.Confirmed : DiagnosticState.Missing,
            Directory.Exists(paths.ArtworkDirectory)
                ? $"Directory exists at {paths.ArtworkDirectory}."
                : $"Directory is missing: {paths.ArtworkDirectory}.",
            "Create the artwork directory and add the exhibition files.");
    }

    private static void AddFile(DiagnosticReport report, string name, string path, string repair)
    {
        var exists = File.Exists(path);
        report.Add(name, exists ? DiagnosticState.Confirmed : DiagnosticState.Missing,
            exists ? $"Confirmed on disk: {path}" : $"Missing from disk: {path}", repair);
    }

    private static void InspectAutoLogon(DiagnosticReport report)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
            var enabled = string.Equals(key?.GetValue("AutoAdminLogon")?.ToString(), "1", StringComparison.Ordinal);
            var username = key?.GetValue("DefaultUserName")?.ToString();
            var passwordValuePresent = key?.GetValueNames().Contains("DefaultPassword", StringComparer.OrdinalIgnoreCase) == true;
            var confirmed = enabled && string.Equals(username, ArtworkUsername, StringComparison.OrdinalIgnoreCase) && passwordValuePresent;
            report.Add("Automatic sign-in", confirmed ? DiagnosticState.Confirmed : DiagnosticState.Missing,
                confirmed
                    ? $"Winlogon AutoAdminLogon=1 and DefaultUserName={ArtworkUsername}; DefaultPassword value is present."
                    : $"Observed AutoAdminLogon={(enabled ? "1" : "not enabled")}, DefaultUserName={username ?? "<missing>"}.",
                "Reapply exhibition provisioning.");
        }
        catch (Exception ex)
        {
            report.Add("Automatic sign-in", DiagnosticState.Error, $"Registry check failed: {ex.Message}");
        }
    }

    private static void InspectBlankPasswordPolicy(DiagnosticReport report)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
            var actual = Convert.ToInt32(key?.GetValue("LimitBlankPasswordUse") ?? 0);
            report.Add("Blank-password remote-use restriction",
                actual == 1 ? DiagnosticState.Confirmed : DiagnosticState.Missing,
                $"HKLM LSA LimitBlankPasswordUse={actual}.",
                "Reapply account security hardening.");
        }
        catch (Exception ex)
        {
            report.Add("Blank-password remote-use restriction", DiagnosticState.Error, $"Registry check failed: {ex.Message}");
        }
    }

    private static void InspectEdgePolicies(DiagnosticReport report)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge");
            var actual = Convert.ToInt32(key?.GetValue("AutoplayAllowed") ?? 0);
            report.Add("Edge autoplay policy",
                actual == 1 ? DiagnosticState.Confirmed : DiagnosticState.Missing,
                actual == 1
                    ? "Edge AutoplayAllowed policy is enabled."
                    : $"Observed Edge AutoplayAllowed={actual}.",
                "Reapply exhibition provisioning.");
        }
        catch (Exception ex)
        {
            report.Add("Edge autoplay policy", DiagnosticState.Error, $"Registry check failed: {ex.Message}");
        }
    }

    private static async Task InspectPerUserRegistryAsync(
        DiagnosticReport report,
        string? sid,
        ExhibitionPaths paths,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            report.Add("Custom artwork shell", DiagnosticState.Missing, "Cannot verify because ArtworkUser has no SID.");
            report.Add("Visitor lockdown policies", DiagnosticState.Missing, "Cannot verify because ArtworkUser has no SID.");
            return;
        }

        try
        {
            await using var hive = await UserRegistryHive.OpenAsync(
                ArtworkUsername, writable: false, createProfileIfMissing: false, ct);
            if (hive is null)
            {
                report.Add("Custom artwork shell", DiagnosticState.Missing,
                    "ArtworkUser exists, but its Windows profile has not been created yet.",
                    "Reapply exhibition provisioning.");
                report.Add("Visitor lockdown policies", DiagnosticState.Missing,
                    "ArtworkUser exists, but its Windows profile has not been created yet.",
                    "Reapply exhibition provisioning.");
                return;
            }

            using var shellKey = hive.Root.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon");
            var actualShell = shellKey?.GetValue("Shell")?.ToString();
            var shellConfirmed = string.Equals(
                Path.GetFullPath(actualShell ?? string.Empty),
                Path.GetFullPath(paths.WatchdogExe),
                StringComparison.OrdinalIgnoreCase);
            report.Add("Custom artwork shell", shellConfirmed ? DiagnosticState.Confirmed : DiagnosticState.Missing,
                shellConfirmed ? $"ArtworkUser shell points to {actualShell}." : $"Observed shell: {actualShell ?? "<missing>"}.",
                "Reapply exhibition provisioning.");

            using var systemKey = hive.Root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            using var explorerKey = hive.Root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            var taskManagerDisabled = Convert.ToInt32(systemKey?.GetValue("DisableTaskMgr") ?? 0) == 1;
            var winKeysDisabled = Convert.ToInt32(explorerKey?.GetValue("NoWinKeys") ?? 0) == 1;
            var lockdownConfirmed = taskManagerDisabled && winKeysDisabled;
            report.Add("Visitor lockdown policies", lockdownConfirmed ? DiagnosticState.Confirmed : DiagnosticState.Missing,
                $"DisableTaskMgr={(taskManagerDisabled ? 1 : 0)}, NoWinKeys={(winKeysDisabled ? 1 : 0)}.",
                "Reapply exhibition provisioning.");
        }
        catch (Exception ex)
        {
            report.Add("Per-user registry policies", DiagnosticState.Error, $"Registry check failed: {ex.Message}");
        }
    }

    private static async Task InspectRebootTaskAsync(DiagnosticReport report, ExhibitionConfig config, CancellationToken ct)
    {
        var query = await RunCliAsync("schtasks", $"/query /tn \"{RebootTaskName}\" /xml", ct);
        if (query.ExitCode != 0)
        {
            report.Add("Daily reboot timer", DiagnosticState.Missing,
                $"Windows Task Scheduler task '{RebootTaskName}' was not found.",
                "Reapply exhibition provisioning.");
            return;
        }

        try
        {
            var document = XDocument.Parse(query.StdOut);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var enabled = !string.Equals(document.Descendants(ns + "Enabled").FirstOrDefault()?.Value, "false", StringComparison.OrdinalIgnoreCase);
            var startBoundary = document.Descendants(ns + "StartBoundary").FirstOrDefault()?.Value;
            var command = document.Descendants(ns + "Command").FirstOrDefault()?.Value ?? string.Empty;
            var arguments = document.Descendants(ns + "Arguments").FirstOrDefault()?.Value ?? string.Empty;
            var userId = document.Descendants(ns + "UserId").FirstOrDefault()?.Value ?? string.Empty;
            var wakeToRun = string.Equals(document.Descendants(ns + "WakeToRun").FirstOrDefault()?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var expected = new ScheduleEvaluator(config.Schedule).MorningRebootTime;
            var actual = DateTime.TryParse(startBoundary, out var start) ? TimeOnly.FromDateTime(start) : (TimeOnly?)null;
            var runsAsSystem = string.Equals(userId, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(userId, "SYSTEM", StringComparison.OrdinalIgnoreCase);
            var confirmed = enabled && runsAsSystem && wakeToRun && actual == expected && command.Contains("shutdown", StringComparison.OrdinalIgnoreCase) && arguments.Contains("/r", StringComparison.OrdinalIgnoreCase);
            report.Add("Daily reboot timer", confirmed ? DiagnosticState.Confirmed : DiagnosticState.Missing,
                $"Task enabled={enabled}; runs as SYSTEM={runsAsSystem}; wake enabled={wakeToRun}; actual trigger={actual?.ToString("HH:mm") ?? "unknown"}; expected trigger={expected:HH:mm}; action={command} {arguments}.".Trim(),
                "Reapply exhibition provisioning so the actual Task Scheduler trigger matches the requested reboot time.");
        }
        catch (Exception ex)
        {
            report.Add("Daily reboot timer", DiagnosticState.Error, $"Task XML could not be verified: {ex.Message}");
        }
    }

    private static async Task InspectClosingPowerTimerAsync(
        DiagnosticReport report,
        ExhibitionConfig config,
        ExhibitionPaths paths,
        CancellationToken ct)
    {
        const string taskName = "ExhibitOS_ClosingPower";
        var query = await RunCliAsync("schtasks", $"/query /tn \"{taskName}\" /xml", ct);
        if (query.ExitCode != 0)
        {
            report.Add("Closing power timer", DiagnosticState.Missing,
                $"Windows Task Scheduler task '{taskName}' was not found.",
                "Reapply exhibition provisioning.");
            return;
        }

        try
        {
            var document = XDocument.Parse(query.StdOut);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var enabled = !string.Equals(document.Descendants(ns + "Enabled").FirstOrDefault()?.Value, "false", StringComparison.OrdinalIgnoreCase);
            var startBoundary = document.Descendants(ns + "StartBoundary").FirstOrDefault()?.Value;
            var command = document.Descendants(ns + "Command").FirstOrDefault()?.Value ?? string.Empty;
            var arguments = document.Descendants(ns + "Arguments").FirstOrDefault()?.Value ?? string.Empty;
            var userId = document.Descendants(ns + "UserId").FirstOrDefault()?.Value ?? string.Empty;
            var runsAsSystem = string.Equals(userId, "S-1-5-18", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(userId, "SYSTEM", StringComparison.OrdinalIgnoreCase);
            var expectedTime = ScheduleEvaluator.ParseTime(config.Schedule.ClosingTime, new TimeOnly(20, 0));
            var actualTime = DateTime.TryParse(startBoundary, out var start) ? TimeOnly.FromDateTime(start) : (TimeOnly?)null;
            var expectedAction = config.Schedule.OvernightPowerMode switch
            {
                OvernightPowerMode.Shutdown => "shutdown",
                OvernightPowerMode.SleepWithWakeTimers => "sleep",
                _ => "idle"
            };
            var executableMatches = string.Equals(
                Path.GetFullPath(command), Path.GetFullPath(paths.WatchdogExe), StringComparison.OrdinalIgnoreCase);
            var actionMatches = arguments.Contains($"--power-action {expectedAction}", StringComparison.OrdinalIgnoreCase);
            var confirmed = enabled && runsAsSystem && actualTime == expectedTime && executableMatches && actionMatches;
            report.Add("Closing power timer", confirmed ? DiagnosticState.Confirmed : DiagnosticState.Missing,
                $"Task enabled={enabled}; runs as SYSTEM={runsAsSystem}; actual trigger={actualTime?.ToString("HH:mm") ?? "unknown"}; expected trigger={expectedTime:HH:mm}; requested action={expectedAction}; executable={command}; arguments={arguments}.",
                "Reapply exhibition provisioning so the actual closing task matches the requested schedule and power action.");
        }
        catch (Exception ex)
        {
            report.Add("Closing power timer", DiagnosticState.Error, $"Task XML could not be verified: {ex.Message}");
        }
    }

    private static async Task InspectFirewallAsync(DiagnosticReport report, NetworkingMode mode, CancellationToken ct)
    {
        // Rules are identified by their reserved ExhibitOS_ name prefix. `netsh
        // advfirewall firewall add rule` does not accept a group argument on the
        // supported Windows builds, so relying on Group makes valid rules invisible.
        var script = "$r=Get-NetFirewallRule -DisplayName 'ExhibitOS_*' -ErrorAction SilentlyContinue; $r | ForEach-Object { \"$($_.DisplayName)|$($_.Enabled)|$($_.Direction)|$($_.Action)\" }";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var query = await RunCliAsync("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encodedScript}", ct);
        if (query.ExitCode != 0)
        {
            report.Add("ExhibitOS firewall policy", DiagnosticState.Error, $"Firewall query failed: {query.StdErr}".Trim());
            return;
        }

        var lines = query.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var enabledRules = lines
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length == 4 &&
                            string.Equals(parts[1], "True", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(parts[2], "Outbound", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(parts => parts[0], parts => parts[3], StringComparer.OrdinalIgnoreCase);
        bool HasRule(string name, string action) =>
            enabledRules.TryGetValue(name, out var actualAction) && string.Equals(actualAction, action, StringComparison.OrdinalIgnoreCase);
        var externalInterfacesBlocked =
            HasRule("ExhibitOS_Block_LAN_Out", "Block") &&
            HasRule("ExhibitOS_Block_Wireless_Out", "Block") &&
            HasRule("ExhibitOS_Block_RAS_Out", "Block");
        var confirmed = mode switch
        {
            NetworkingMode.LocalhostOnly => externalInterfacesBlocked,
            NetworkingMode.InternetEnabled => !enabledRules.Keys.Any(name => name.StartsWith("ExhibitOS_Block_", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
        report.Add("ExhibitOS firewall policy", confirmed ? DiagnosticState.Confirmed : DiagnosticState.Missing,
            $"Requested mode={mode}; enabled ExhibitOS outbound rules={(enabledRules.Count == 0 ? "none" : string.Join(", ", enabledRules.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")))}.",
            "Reapply networking configuration.");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(
        string fileName, string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, await stdout, await stderr);
    }

    private static string? GetUserSid(string username)
    {
        try
        {
            return ((SecurityIdentifier)new NTAccount(username).Translate(typeof(SecurityIdentifier))).Value;
        }
        catch
        {
            return null;
        }
    }
}
