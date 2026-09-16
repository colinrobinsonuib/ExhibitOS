using System.Runtime.InteropServices;

namespace ExhibitOSManager;

internal static class ManagerStartupLogger
{
    private static readonly object Sync = new();
    internal static string LogPath { get; } = Path.Combine(AppContext.BaseDirectory, "logs", "manager.log");

    internal static void Log(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {detail}{Environment.NewLine}");
            }
            catch
            {
                // Startup logging must never become another startup failure.
            }
        }
    }

    internal static void ShowFatalError(string phase, Exception exception)
    {
        Log($"Fatal startup error during {phase}.", exception);
        MessageBoxW(IntPtr.Zero,
            $"ExhibitOS Manager could not start during {phase}.\n\n{exception.Message}\n\nLog: {LogPath}",
            "ExhibitOS Manager Startup Error",
            0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
