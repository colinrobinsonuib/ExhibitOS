using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExhibitOS.Core.Lockdown;

public class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_F4 = 0x73;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;

    private const int LLKHF_ALTDOWN = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private bool _suppressAltF4 = true;
    private bool _suppressAltTab = true;
    private bool _suppressWindowsKeys = true;
    private bool _suppressTaskSwitcher = true;

    public bool IsHookActive => _hookHandle != IntPtr.Zero;

    public LowLevelKeyboardHook(
        bool suppressAltF4 = true,
        bool suppressAltTab = true,
        bool suppressWindowsKeys = true,
        bool suppressTaskSwitcher = true)
    {
        _suppressAltF4 = suppressAltF4;
        _suppressAltTab = suppressAltTab;
        _suppressWindowsKeys = suppressWindowsKeys;
        _suppressTaskSwitcher = suppressTaskSwitcher;
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = GetModuleHandle(curModule?.ModuleName);

        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    public bool ShouldBlockKey(uint vkCode, uint flags)
    {
        var isAltDown = (flags & LLKHF_ALTDOWN) != 0;

        // Suppress Windows keys (Win, Win+R, Win+E, Win+X, etc.)
        if (_suppressWindowsKeys && (vkCode == VK_LWIN || vkCode == VK_RWIN))
        {
            return true;
        }

        // Suppress Alt+Tab
        if (_suppressAltTab && isAltDown && vkCode == VK_TAB)
        {
            return true;
        }

        // Suppress Alt+Esc
        if (_suppressTaskSwitcher && isAltDown && vkCode == VK_ESCAPE)
        {
            return true;
        }

        // Suppress Alt+Space (system window menu)
        if (_suppressTaskSwitcher && isAltDown && vkCode == VK_SPACE)
        {
            return true;
        }

        // Suppress Alt+F4
        if (_suppressAltF4 && isAltDown && vkCode == VK_F4)
        {
            return true;
        }

        // Suppress Ctrl+Shift+Esc (Task Manager launch shortcut)
        if (_suppressTaskSwitcher && vkCode == VK_ESCAPE)
        {
            var isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
                             (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 ||
                             (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;

            var isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
                              (GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 ||
                              (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;

            if (isCtrlDown && isShiftDown)
            {
                return true;
            }
        }

        return false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (ShouldBlockKey(hookStruct.vkCode, hookStruct.flags))
            {
                // Non-zero return value consumes the key stroke, preventing Windows from handling it
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
    }
}
