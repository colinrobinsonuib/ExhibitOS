using System.Text.Json.Serialization;

namespace ExhibitOS.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtworkType
{
    VideoFolder,
    StaticWeb,
    BackendWeb,
    Application
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OvernightPowerMode
{
    SleepWithWakeTimers,
    Shutdown,
    Idle
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OvernightDisplayBehavior
{
    SignalOff,
    BlackoutScreen
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkingMode
{
    OfflineExhibition,
    LocalNetworkOnly,
    InternetEnabled
}

public class ArtworkConfig
{
    public ArtworkType Type { get; set; } = ArtworkType.VideoFolder;
    public string? ArtworkDirectory { get; set; }
    public string? EntryPoint { get; set; }
    public string? LaunchArguments { get; set; }
}

public class ScheduleConfig
{
    public string OpeningTime { get; set; } = "07:00";
    public string ClosingTime { get; set; } = "20:00";
    public string MorningRebootTime { get; set; } = "06:45";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public OvernightPowerMode OvernightPowerMode { get; set; } = OvernightPowerMode.SleepWithWakeTimers;
}

public class DisplaySoundConfig
{
    public string? AudioDeviceId { get; set; }
    public string? AudioDeviceName { get; set; }
    public bool HideCursor { get; set; } = true;
    public OvernightDisplayBehavior OvernightDisplayBehavior { get; set; } = OvernightDisplayBehavior.SignalOff;
}

public class ExhibitionConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string ExhibitionName { get; set; } = "Exhibition";
    public ArtworkConfig Artwork { get; set; } = new();
    public ScheduleConfig Schedule { get; set; } = new();
    public DisplaySoundConfig DisplayAndSound { get; set; } = new();
    public NetworkingMode NetworkingMode { get; set; } = NetworkingMode.OfflineExhibition;

    public static readonly System.Text.Json.JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, DefaultJsonOptions);
    }

    public static ExhibitionConfig FromJson(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<ExhibitionConfig>(json, DefaultJsonOptions)
               ?? throw new System.Text.Json.JsonException("Deserialized exhibition configuration was null.");
    }

    public void SaveToFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, ToJson(), System.Text.Encoding.UTF8);
    }

    public static ExhibitionConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file not found: {path}");
        }
        var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        return FromJson(json);
    }
}
