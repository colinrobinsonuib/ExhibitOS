using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExhibitOS.Core.Audio;

public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}

public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> GetAudioRenderDevices()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new AudioDeviceInfo { Id = "default", Name = "Default Audio Output Device" }
        };

        try
        {
            // Query sound devices via PowerShell CIM / WMI
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"Get-CimInstance Win32_SoundDevice | Select-Object -ExpandProperty Name\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && !devices.Any(d => d.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        devices.Add(new AudioDeviceInfo { Id = trimmed, Name = trimmed });
                    }
                }
            }
        }
        catch
        {
            // Return default list if query fails
        }

        return devices;
    }
}
