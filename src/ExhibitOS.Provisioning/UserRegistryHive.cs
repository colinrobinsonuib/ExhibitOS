using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ExhibitOS.Provisioning;

/// <summary>
/// Opens a local user's registry hive even when that user has never signed in.
/// HKEY_USERS only contains loaded profiles, so provisioning must create the
/// profile and temporarily load NTUSER.DAT before writing per-user policy.
/// </summary>
internal sealed class UserRegistryHive : IAsyncDisposable
{
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);
    internal const int ProfilePathBufferCapacity = 260;
    private readonly string? _mountedSid;

    public RegistryKey Root { get; }

    private UserRegistryHive(RegistryKey root, string? mountedSid)
    {
        Root = root;
        _mountedSid = mountedSid;
    }

    public static async Task<UserRegistryHive?> OpenAsync(
        string username,
        bool writable,
        bool createProfileIfMissing,
        CancellationToken ct)
    {
        var sid = GetUserSid(username)
            ?? throw new InvalidOperationException($"Cannot locate SID for user '{username}'.");

        var alreadyLoaded = Registry.Users.OpenSubKey(sid, writable);
        if (alreadyLoaded is not null)
        {
            return new UserRegistryHive(alreadyLoaded, mountedSid: null);
        }

        var profilePath = GetProfilePath(sid);
        if (string.IsNullOrWhiteSpace(profilePath) && createProfileIfMissing)
        {
            profilePath = CreateLocalProfile(sid, username);
        }

        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return null;
        }

        var hiveFile = Path.Combine(profilePath, "NTUSER.DAT");
        if (!File.Exists(hiveFile))
        {
            if (!createProfileIfMissing)
            {
                return null;
            }
            throw new InvalidOperationException(
                $"Windows created the profile for '{username}', but its registry hive was not found at '{hiveFile}'.");
        }

        var load = await RunRegAsync(new[] { "load", $@"HKU\{sid}", hiveFile }, ct);
        if (load.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Windows could not load the registry profile for '{username}': {load.Error}".Trim());
        }

        var root = Registry.Users.OpenSubKey(sid, writable);
        if (root is null)
        {
            await RunRegAsync(new[] { "unload", $@"HKU\{sid}" }, CancellationToken.None);
            throw new InvalidOperationException($"The registry profile for '{username}' was loaded but could not be opened.");
        }

        return new UserRegistryHive(root, sid);
    }

    public async ValueTask DisposeAsync()
    {
        Root.Dispose();
        if (_mountedSid is not null)
        {
            var unload = await RunRegAsync(new[] { "unload", $@"HKU\{_mountedSid}" }, CancellationToken.None);
            if (unload.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Windows could not unload the temporary ArtworkUser registry profile: {unload.Error}".Trim());
            }
        }
    }

    private static string? GetUserSid(string username)
    {
        try
        {
            var account = new NTAccount(Environment.MachineName, username);
            return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetProfilePath(string sid)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        return key?.GetValue("ProfileImagePath") is string path
            ? Environment.ExpandEnvironmentVariables(path)
            : null;
    }

    private static string CreateLocalProfile(string sid, string username)
    {
        // CreateProfile returns RPC_X_BAD_STUB_DATA (0x800706F7) when this
        // buffer exceeds the Win32 MAX_PATH contract, even though the API
        // describes the argument only as a character count.
        var path = new StringBuilder(ProfilePathBufferCapacity);
        var result = CreateProfile(sid, username, path, (uint)path.Capacity);
        if (result < 0 && result != ErrorAlreadyExistsHResult)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        var resolved = path.ToString();
        if (string.IsNullOrWhiteSpace(resolved))
        {
            resolved = GetProfilePath(sid);
        }
        return resolved ?? throw new InvalidOperationException($"Windows did not create a profile for '{username}'.");
    }

    private static async Task<(int ExitCode, string Error)> RunRegAsync(
        IEnumerable<string> arguments,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("reg.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Registry Console Tool could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await outputTask;
        var error = await errorTask;
        return (process.ExitCode, string.Join(' ', new[] { error, output }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim());
    }

    [DllImport("userenv.dll", EntryPoint = "CreateProfile", ExactSpelling = true)]
    private static extern int CreateProfile(
        [MarshalAs(UnmanagedType.LPWStr)] string pszUserSid,
        [MarshalAs(UnmanagedType.LPWStr)] string pszUserName,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszProfilePath,
        uint cchProfilePath);
}
