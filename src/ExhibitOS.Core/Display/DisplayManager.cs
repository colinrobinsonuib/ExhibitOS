using System.Runtime.InteropServices;

namespace ExhibitOS.Core.Display;

public enum MonitorPowerState
{
    On = -1,
    Standby = 1,
    Off = 2
}

public class DisplayManager : IDisposable
{
    private const int HWND_BROADCAST = 0xFFFF;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const int COLOR_WINDOWTEXT = 8;
    private const int BLACK_BRUSH = 4;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly WndProc StaticWndProc = DefWindowProc;
    private static bool _classRegistered = false;
    private const string WindowClassName = "ExhibitOS_BlackoutWindowClass";

    private IntPtr _blackoutHwnd = IntPtr.Zero;
    private bool _cursorHidden = false;

    public static void SetMonitorPower(MonitorPowerState state)
    {
        SendMessage((IntPtr)HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)state);
    }

    public void ShowBlackoutScreen()
    {
        if (_blackoutHwnd != IntPtr.Zero) return;

        EnsureClassRegistered();

        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        _blackoutHwnd = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            WindowClassName,
            "ExhibitOS Blackout",
            WS_POPUP | WS_VISIBLE,
            x, y, width, height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        SetCursorVisibility(false);
    }

    public void CloseBlackoutScreen()
    {
        if (_blackoutHwnd != IntPtr.Zero)
        {
            DestroyWindow(_blackoutHwnd);
            _blackoutHwnd = IntPtr.Zero;
        }

        SetCursorVisibility(true);
    }

    public void SetCursorVisibility(bool visible)
    {
        if (visible && _cursorHidden)
        {
            ShowCursor(true);
            _cursorHidden = false;
        }
        else if (!visible && !_cursorHidden)
        {
            ShowCursor(false);
            _cursorHidden = true;
        }
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = StaticWndProc,
            hInstance = GetModuleHandle(null),
            hbrBackground = GetStockObject(BLACK_BRUSH),
            lpszClassName = WindowClassName
        };

        RegisterClassEx(ref wc);
        _classRegistered = true;
    }

    public void Dispose()
    {
        CloseBlackoutScreen();
    }
}
