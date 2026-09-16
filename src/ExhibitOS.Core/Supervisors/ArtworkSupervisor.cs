using System.Diagnostics;
using ExhibitOS.Core.Configuration;
using ExhibitOS.Core.Logging;
using ExhibitOS.Core.Network;
using ExhibitOS.Core.Power;
using ExhibitOS.Core.ProcessSupervision;
using ExhibitOS.Core.Schedule;

namespace ExhibitOS.Core.Supervisors;

public class ArtworkSupervisor : IDisposable
{
    private readonly ExhibitionConfig _config;
    private readonly ExhibitionPaths _paths;
    private readonly IJobObjectFactory _jobFactory;
    private readonly WatchdogLogger _logger;
    private readonly ScheduleEvaluator _scheduleEvaluator;
    private readonly HttpHealthProbes _probes;
    private readonly Lockdown.LowLevelKeyboardHook _keyboardHook;
    private readonly Display.DisplayManager _displayManager;

    private IJobObject? _primaryJob;
    private IJobObject? _secondaryJob; // e.g. Edge kiosk when primary is Node
    private int _allocatedPort;
    private int _consecutiveCrashCount;
    private DateTimeOffset _lastHealthyTime = DateTimeOffset.UtcNow;
    private bool _isDisposed;

    public ArtworkSupervisor(
        ExhibitionConfig config,
        ExhibitionPaths paths,
        IJobObjectFactory? jobFactory = null,
        WatchdogLogger? logger = null,
        HttpHealthProbes? probes = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _jobFactory = jobFactory ?? new WindowsJobObjectFactory();
        _logger = logger ?? new WatchdogLogger(paths.WatchdogLog);
        _scheduleEvaluator = new ScheduleEvaluator(config.Schedule);
        _probes = probes ?? new HttpHealthProbes();
        _keyboardHook = new Lockdown.LowLevelKeyboardHook();
        _displayManager = new Display.DisplayManager();
    }

    public async Task RunSupervisionLoopAsync(CancellationToken ct)
    {
        _logger.Info("Starting ExhibitOS Artwork Supervision Loop.");

        // Check for missed morning reboot on startup
        var lastBoot = PowerManager.GetSystemLastBootTimeUtc();
        if (_scheduleEvaluator.IsMorningRebootMissed(DateTimeOffset.UtcNow, lastBoot))
        {
            _logger.Warn("Missed morning reboot detected on startup. (In production exhibition machine, system would reboot before opening).");
        }

        while (!ct.IsCancellationRequested && !_isDisposed)
        {
            try
            {
                var isOpen = _scheduleEvaluator.IsExhibitionOpen(DateTimeOffset.UtcNow);

                if (isOpen)
                {
                    PowerManager.PreventSleepAndKeepDisplayOn();

                    if (!IsArtworkRunning())
                    {
                        await HandleArtworkRestartAsync(ct);
                    }
                    else
                    {
                        await PerformHealthChecksAsync(ct);
                    }
                }
                else
                {
                    PowerManager.AllowNormalSleep();

                    if (IsArtworkRunning())
                    {
                        _logger.Info("Exhibition closing time reached. Stopping artwork processes.");
                        StopArtwork();

                        if (_config.DisplayAndSound.OvernightDisplayBehavior == OvernightDisplayBehavior.BlackoutScreen)
                        {
                            _logger.Info("Applying Blackout Screen for overnight display.");
                            _displayManager.ShowBlackoutScreen();
                        }
                        else
                        {
                            _logger.Info("Cutting display signal (DPMS Signal Off) for overnight display.");
                            Display.DisplayManager.SetMonitorPower(Display.MonitorPowerState.Off);
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Unexpected error in supervision loop", ex);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        _logger.Info("Stopping ExhibitOS Artwork Supervision Loop.");
        StopArtwork();
    }

    public bool IsArtworkRunning()
    {
        if (_primaryJob == null || !_primaryJob.IsActive)
        {
            return false;
        }

        // For web artworks, Edge kiosk is also required to be running
        if (_secondaryJob != null && !_secondaryJob.IsActive)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> StartArtworkAsync(CancellationToken ct = default)
    {
        StopArtwork();

        _logger.Info($"Starting artwork of type: {_config.Artwork.Type}");

        _displayManager.CloseBlackoutScreen();
        Display.DisplayManager.SetMonitorPower(Display.MonitorPowerState.On);

        if (_config.DisplayAndSound.HideCursor)
        {
            _displayManager.SetCursorVisibility(false);
        }

        _keyboardHook.Install();

        switch (_config.Artwork.Type)
        {
            case ArtworkType.VideoFolder:
                return StartVideoArtwork();

            case ArtworkType.StaticWeb:
                return await StartStaticWebArtworkAsync(ct);

            case ArtworkType.BackendWeb:
                return await StartBackendWebArtworkAsync(ct);

            case ArtworkType.Application:
                return StartApplicationArtwork();

            default:
                _logger.Error($"Unsupported artwork type: {_config.Artwork.Type}");
                return false;
        }
    }

    public void StopArtwork()
    {
        _keyboardHook.Uninstall();
        _displayManager.SetCursorVisibility(true);

        if (_secondaryJob != null)
        {
            try
            {
                _secondaryJob.Terminate();
                _secondaryJob.Dispose();
            }
            catch { }
            _secondaryJob = null;
        }

        if (_primaryJob != null)
        {
            try
            {
                _primaryJob.Terminate();
                _primaryJob.Dispose();
            }
            catch { }
            _primaryJob = null;
        }
    }

    private bool StartVideoArtwork()
    {
        _primaryJob = _jobFactory.CreateJobObject("ExhibitOS_mpv");

        var mpvExe = _paths.MpvExe;
        var artworkDir = _config.Artwork.ArtworkDirectory ?? _paths.ArtworkDirectory;

        var args = $"--fs --no-osc --loop-playlist=inf --cursor-autohide=always \"{artworkDir}\"";
        if (!string.IsNullOrEmpty(_config.DisplayAndSound.AudioDeviceId))
        {
            args += $" --audio-device=\"{_config.DisplayAndSound.AudioDeviceId}\"";
        }

        var process = LaunchProcess(mpvExe, args, artworkDir);
        if (process != null)
        {
            _primaryJob.AssignProcess(process);
            _logger.Info($"Launched mpv (PID: {process.Id}) in mpv Job Object.");
            return true;
        }

        _logger.Error($"Failed to launch mpv from '{mpvExe}'.");
        return false;
    }

    private async Task<bool> StartStaticWebArtworkAsync(CancellationToken ct)
    {
        _allocatedPort = PortAllocator.GetAvailableLoopbackPort();
        _logger.Info($"Allocated loopback port {_allocatedPort} for static web server.");

        _primaryJob = _jobFactory.CreateJobObject("ExhibitOS_Node");

        var artworkDir = _config.Artwork.ArtworkDirectory ?? _paths.ArtworkDirectory;
        var nodeExe = _paths.NodeExe;
        var staticScript = _paths.StaticServerJs;

        var nodeArgs = $"\"{staticScript}\" --port {_allocatedPort} --dir \"{artworkDir}\"";
        var nodeProc = LaunchProcess(nodeExe, nodeArgs, artworkDir);
        if (nodeProc == null)
        {
            _logger.Error("Failed to launch bundled node for static server.");
            return false;
        }

        _primaryJob.AssignProcess(nodeProc);

        // Readiness probe
        var url = $"http://127.0.0.1:{_allocatedPort}/";
        _logger.Info($"Waiting for static server readiness at {url}...");

        var isReady = await _probes.WaitForReadinessAsync(
            url,
            timeout: TimeSpan.FromSeconds(15),
            initialDelay: TimeSpan.FromMilliseconds(200),
            maxDelay: TimeSpan.FromSeconds(2),
            ct);

        if (!isReady)
        {
            _logger.Error($"Readiness probe failed for static server at {url}.");
            StopArtwork();
            return false;
        }

        // Launch Edge kiosk
        return LaunchEdgeKiosk(url);
    }

    private async Task<bool> StartBackendWebArtworkAsync(CancellationToken ct)
    {
        _allocatedPort = PortAllocator.GetAvailableLoopbackPort();
        _logger.Info($"Allocated loopback port {_allocatedPort} for backend server.");

        _primaryJob = _jobFactory.CreateJobObject("ExhibitOS_Backend");

        var artworkDir = _config.Artwork.ArtworkDirectory ?? _paths.ArtworkDirectory;
        var entryPoint = _config.Artwork.EntryPoint ?? "server.js";
        var serverFile = Path.Combine(artworkDir, entryPoint);
        var nodeExe = _paths.NodeExe;

        var envVars = new Dictionary<string, string>
        {
            { "PORT", _allocatedPort.ToString() },
            { "HOST", "127.0.0.1" }
        };

        var nodeProc = LaunchProcess(nodeExe, $"\"{serverFile}\"", artworkDir, envVars);
        if (nodeProc == null)
        {
            _logger.Error($"Failed to launch backend node process: {serverFile}");
            return false;
        }

        _primaryJob.AssignProcess(nodeProc);

        // Readiness probe
        var url = $"http://127.0.0.1:{_allocatedPort}/";
        _logger.Info($"Waiting for backend readiness at {url}...");

        var isReady = await _probes.WaitForReadinessAsync(
            url,
            timeout: TimeSpan.FromSeconds(30),
            initialDelay: TimeSpan.FromMilliseconds(500),
            maxDelay: TimeSpan.FromSeconds(3),
            ct);

        if (!isReady)
        {
            _logger.Error($"Backend readiness probe timed out for {url}.");
            StopArtwork();
            return false;
        }

        return LaunchEdgeKiosk(url);
    }

    private bool StartApplicationArtwork()
    {
        _primaryJob = _jobFactory.CreateJobObject("ExhibitOS_App");

        var artworkDir = _config.Artwork.ArtworkDirectory ?? _paths.ArtworkDirectory;
        var entryPoint = _config.Artwork.EntryPoint ?? "";
        var exePath = Path.IsPathRooted(entryPoint) ? entryPoint : Path.Combine(artworkDir, entryPoint);
        var args = _config.Artwork.LaunchArguments ?? "";

        var proc = LaunchProcess(exePath, args, artworkDir);
        if (proc != null)
        {
            _primaryJob.AssignProcess(proc);
            _logger.Info($"Launched application '{exePath}' (PID: {proc.Id}) in App Job Object.");
            return true;
        }

        _logger.Error($"Failed to launch application '{exePath}'.");
        return false;
    }

    private bool LaunchEdgeKiosk(string url)
    {
        _secondaryJob = _jobFactory.CreateJobObject("ExhibitOS_Edge");

        var userDataDir = Path.Combine(_paths.RuntimeDirectory, "edge-data");
        var edgeArgs = $"--kiosk \"{url}\" --edge-kiosk-type=fullscreen --no-first-run --overscroll-history-navigation=0 --disable-pinch --user-data-dir=\"{userDataDir}\"";

        var edgeProc = LaunchProcess("msedge.exe", edgeArgs, _paths.RuntimeDirectory);
        if (edgeProc != null)
        {
            _secondaryJob.AssignProcess(edgeProc);
            _logger.Info($"Launched Edge kiosk (PID: {edgeProc.Id}) pointing to {url}.");
            return true;
        }

        _logger.Error("Failed to launch Microsoft Edge kiosk.");
        return false;
    }

    private Process? LaunchProcess(string fileName, string args, string workingDir, IDictionary<string, string>? envVars = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            if (envVars != null)
            {
                foreach (var kvp in envVars)
                {
                    psi.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error launching process '{fileName}': {ex.Message}", ex);
            return null;
        }
    }

    private async Task HandleArtworkRestartAsync(CancellationToken ct)
    {
        _consecutiveCrashCount++;
        _logger.Warn($"Artwork is not running during exhibition hours. Crash count: {_consecutiveCrashCount}");

        const int maxRetries = 5;
        if (_consecutiveCrashCount > maxRetries)
        {
            _logger.Error($"Artwork exceeded max crash threshold ({maxRetries}). Bounded recovery initiated.");
            // Reset crash count after whole-machine recovery interval
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            _consecutiveCrashCount = 0;
            return;
        }

        // Exponential backoff
        var backoffSeconds = Math.Pow(2, Math.Min(_consecutiveCrashCount - 1, 4));
        _logger.Info($"Waiting {backoffSeconds}s before restart attempt...");
        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);

        await StartArtworkAsync(ct);
    }

    private async Task PerformHealthChecksAsync(CancellationToken ct)
    {
        // Reset crash counter if system has been healthy for > 60 seconds
        if (DateTimeOffset.UtcNow - _lastHealthyTime > TimeSpan.FromSeconds(60))
        {
            if (_consecutiveCrashCount > 0)
            {
                _logger.Info("Artwork has been stable for > 60 seconds. Resetting crash counter.");
                _consecutiveCrashCount = 0;
            }
            _lastHealthyTime = DateTimeOffset.UtcNow;
        }

        // HTTP liveness check for web artworks
        if (_config.Artwork.Type == ArtworkType.StaticWeb || _config.Artwork.Type == ArtworkType.BackendWeb)
        {
            if (_allocatedPort > 0)
            {
                var url = $"http://127.0.0.1:{_allocatedPort}/";
                var alive = await _probes.CheckLivenessAsync(url, TimeSpan.FromSeconds(5), ct);
                if (!alive)
                {
                    _logger.Warn($"HTTP liveness check failed for {url}. Wedged process detected. Triggering component restart.");
                    StopArtwork();
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            StopArtwork();
            _keyboardHook.Dispose();
            _displayManager.Dispose();
        }
    }
}
