using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Logging;
using ExhibitOS.Core.Power;
using ExhibitOS.Core.Supervisors;

namespace ExhibitWatchdog;

internal class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "--power-action", StringComparison.OrdinalIgnoreCase))
        {
            var actionRoot = args.Length >= 3 ? args[2] : null;
            var actionPaths = new ExhibitionPaths(actionRoot);
            var actionLogger = new WatchdogLogger(actionPaths.WatchdogLog);
            var action = args[1].ToLowerInvariant();
            actionLogger.Info($"Executing Task Scheduler power action: {action}.");
            switch (action)
            {
                case "shutdown":
                    PowerAutomation.InitiateCleanShutdown();
                    return;
                case "sleep":
                    if (!PowerAutomation.EnterSystemSleep())
                    {
                        actionLogger.Error("Windows rejected the scheduled sleep request.");
                        Environment.ExitCode = 2;
                    }
                    return;
                case "idle":
                    PowerManager.AllowNormalSleep();
                    actionLogger.Info("Idle overnight mode selected; no power transition was requested.");
                    return;
                default:
                    actionLogger.Error($"Unknown scheduled power action '{action}'.");
                    Environment.ExitCode = 3;
                    return;
            }
        }

        var customRoot = args.Length > 0 ? args[0] : null;
        var paths = new ExhibitionPaths(customRoot);
        var logger = new WatchdogLogger(paths.WatchdogLog);

        logger.Info("==================================================");
        logger.Info("ExhibitWatchdog starting up.");
        logger.Info($"Root Directory: {paths.RootDirectory}");
        logger.Info($"Config File: {paths.ConfigFile}");

        if (!File.Exists(paths.ConfigFile))
        {
            logger.Error($"Configuration file not found at '{paths.ConfigFile}'. Watchdog cannot proceed.");
            return;
        }

        ExhibitionConfig config;
        try
        {
            config = ExhibitionConfig.LoadFromFile(paths.ConfigFile);
            logger.Info($"Loaded exhibition configuration: '{config.ExhibitionName}' ({config.Artwork.Type})");
        }
        catch (Exception ex)
        {
            logger.Error("Failed to parse exhibition configuration.", ex);
            return;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            logger.Info("Received cancellation request (Ctrl+C). Shutting down watchdog...");
            e.Cancel = true;
            cts.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            logger.Info("Process exit event received.");
            cts.Cancel();
        };

        using var supervisor = new ArtworkSupervisor(config, paths, logger: logger);

        try
        {
            await supervisor.RunSupervisionLoopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.Info("Supervision loop cleanly stopped.");
        }
        catch (Exception ex)
        {
            logger.Error("Supervision loop encountered fatal exception.", ex);
        }
        finally
        {
            logger.Info("ExhibitWatchdog shutdown complete.");
        }
    }
}
