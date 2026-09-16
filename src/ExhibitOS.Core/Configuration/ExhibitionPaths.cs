namespace ExhibitOS.Core.Configuration;

public class ExhibitionPaths
{
    public const string DefaultRoot = @"C:\ExhibitOS";

    public string RootDirectory { get; }
    public string ArtworkDirectory => Path.Combine(RootDirectory, "artwork");
    public string ConfigDirectory => Path.Combine(RootDirectory, "config");
    public string ConfigFile => Path.Combine(ConfigDirectory, "exhibition.json");
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public string ExhibitLog => Path.Combine(LogsDirectory, "exhibit.log");
    public string WatchdogLog => Path.Combine(LogsDirectory, "watchdog.log");
    public string RuntimeDirectory => Path.Combine(RootDirectory, "runtime");
    public string WatchdogExe => Path.Combine(RuntimeDirectory, "ExhibitWatchdog.exe");
    public string BinDirectory => Path.Combine(RuntimeDirectory, "bin");
    public string MpvExe => Path.Combine(BinDirectory, "mpv", "mpv.exe");
    public string NodeExe => Path.Combine(BinDirectory, "node", "node.exe");
    public string StaticServerJs => Path.Combine(BinDirectory, "node", "static-server.js");
    public string ManagerExe => Path.Combine(RootDirectory, "ExhibitOSManager.exe");

    public ExhibitionPaths(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? DefaultRoot : Path.GetFullPath(rootDirectory);
    }
}
