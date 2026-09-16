using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExhibitOS.Core.Power;

public class PowerManager
{
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    private const uint ES_CONTINUOUS = 0x80000000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    public static void PreventSleepAndKeepDisplayOn()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        }
    }

    public static void AllowNormalSleep()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }
    }

    public static DateTimeOffset GetSystemLastBootTimeUtc()
    {
        var uptimeMs = Environment.TickCount64;
        return DateTimeOffset.UtcNow.AddMilliseconds(-uptimeMs);
    }
}
