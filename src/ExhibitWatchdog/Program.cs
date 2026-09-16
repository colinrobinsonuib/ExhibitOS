using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Logging;
using ExhibitOS.Core.Supervisors;

namespace ExhibitWatchdog;

internal class Program
{
    private static async Task Main(string[] args)
    {
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
