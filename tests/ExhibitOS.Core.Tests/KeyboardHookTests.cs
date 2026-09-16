using ExhibitOS.Core.Lockdown;
using Xunit;

namespace ExhibitOS.Core.Tests;

public class KeyboardHookTests
{
    private const uint VK_TAB = 0x09;
    private const uint VK_ESCAPE = 0x1B;
    private const uint VK_F4 = 0x73;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_KEY_A = 0x41;
    private const uint LLKHF_ALTDOWN = 0x20;

    [Fact]
    public void ShouldBlockKey_WindowsKeys_AreBlocked()
    {
        using var hook = new LowLevelKeyboardHook();
        Assert.True(hook.ShouldBlockKey(VK_LWIN, 0));
        Assert.True(hook.ShouldBlockKey(VK_RWIN, 0));
    }

    [Fact]
    public void ShouldBlockKey_AltTab_IsBlocked()
    {
        using var hook = new LowLevelKeyboardHook();
        Assert.True(hook.ShouldBlockKey(VK_TAB, LLKHF_ALTDOWN));
        // Regular tab without Alt is NOT blocked
        Assert.False(hook.ShouldBlockKey(VK_TAB, 0));
    }

    [Fact]
    public void ShouldBlockKey_AltF4_IsBlocked()
    {
        using var hook = new LowLevelKeyboardHook();
        Assert.True(hook.ShouldBlockKey(VK_F4, LLKHF_ALTDOWN));
        // Regular F4 without Alt is NOT blocked
        Assert.False(hook.ShouldBlockKey(VK_F4, 0));
    }

    [Fact]
    public void ShouldBlockKey_NormalKeys_AreNotBlocked()
    {
        using var hook = new LowLevelKeyboardHook();
        Assert.False(hook.ShouldBlockKey(VK_KEY_A, 0));
        Assert.False(hook.ShouldBlockKey(VK_KEY_A, LLKHF_ALTDOWN));
    }
}
