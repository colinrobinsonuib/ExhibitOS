using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ExhibitOS.Core.Power;

public static class PowerAutomation
{
    private const uint TIMER_ALL_ACCESS = 0x1F0003;
    private const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x00000001;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(
        IntPtr lpTimerAttributes,
        string? lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        SafeWaitHandle hTimer,
        [In] ref long pDueTime,
        int lPeriod,
        IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine,
        [MarshalAs(UnmanagedType.Bool)] bool fResume);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    /// <summary>
    /// Registers a hardware wake timer that wakes the machine at wakeTimeUtc before morning reboot.
    /// Returns a SafeWaitHandle to the active timer.
    /// </summary>
    public static SafeWaitHandle? RegisterWakeTimer(DateTimeOffset wakeTimeUtc)
    {
        var handle = CreateWaitableTimerEx(IntPtr.Zero, "ExhibitOS_WakeTimer", CREATE_WAITABLE_TIMER_MANUAL_RESET, TIMER_ALL_ACCESS);
        if (handle.IsInvalid)
        {
            return null;
        }

        var fileTime = wakeTimeUtc.ToFileTime();
        if (!SetWaitableTimer(handle, ref fileTime, 0, IntPtr.Zero, IntPtr.Zero, true))
        {
            handle.Dispose();
            return null;
        }

        return handle;
    }

    /// <summary>
    /// Puts the system to sleep.
    /// </summary>
    public static bool EnterSystemSleep()
    {
        return SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
    }

    /// <summary>
    /// Executes a clean planned shutdown for the overnight shutdown power mode.
    /// </summary>
    public static void InitiateCleanShutdown()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/s /t 0 /f /d p:0:0",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    /// <summary>
    /// Executes a clean reboot for the morning reboot or post-exhaustion recovery.
    /// </summary>
    public static void InitiateCleanReboot()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/r /t 0 /f /d p:0:0",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
