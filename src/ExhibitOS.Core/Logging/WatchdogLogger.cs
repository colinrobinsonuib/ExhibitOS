namespace ExhibitOS.Core.Logging;

public class WatchdogLogger
{
    private readonly string _logFilePath;
    private readonly long _maxSizeBytes;
    private readonly object _lock = new();

    public WatchdogLogger(string logFilePath, long maxSizeBytes = 5 * 1024 * 1024)
    {
        _logFilePath = logFilePath;
        _maxSizeBytes = maxSizeBytes;

        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public void Log(string message, string level = "INFO")
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        Console.WriteLine(line);

        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_logFilePath, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    public void Info(string message) => Log(message, "INFO");
    public void Warn(string message) => Log(message, "WARN");
    public void Error(string message, Exception? ex = null) =>
        Log(ex == null ? message : $"{message} | Exception: {ex.GetType().Name}: {ex.Message}", "ERROR");

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logFilePath)) return;

        var info = new FileInfo(_logFilePath);
        if (info.Length < _maxSizeBytes) return;

        try
        {
            var old1 = _logFilePath + ".1";
            var old2 = _logFilePath + ".2";

            if (File.Exists(old2)) File.Delete(old2);
            if (File.Exists(old1)) File.Move(old1, old2);
            File.Move(_logFilePath, old1);
        }
        catch
        {
            // Ignore rotation errors
        }
    }
}
