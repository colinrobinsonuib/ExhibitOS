using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ExhibitOS.Bootstrapper;

internal static class Program
{
    private const string PayloadResourceName = "ExhibitOSPayload.zip";
    private const string DefaultInstallDirectory = @"C:\ExhibitOS";
    private static readonly string SetupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ExhibitOS", "setup.log");

    [STAThread]
    private static int Main()
    {
        try
        {
            Log($"Setup started. Version={Assembly.GetExecutingAssembly().GetName().Version}; OS={Environment.OSVersion}; Architecture={RuntimeInformation.OSArchitecture}");
            Log($"Executable: {Environment.ProcessPath}");
            if (!StopInstalledComponents(DefaultInstallDirectory))
            {
                Log("Setup cancelled because running ExhibitOS components were not closed.");
                return 3;
            }
            InstallPayload(DefaultInstallDirectory);
            Directory.CreateDirectory(Path.Combine(DefaultInstallDirectory, "artwork"));
            Directory.CreateDirectory(Path.Combine(DefaultInstallDirectory, "config"));
            Directory.CreateDirectory(Path.Combine(DefaultInstallDirectory, "logs"));
            RemoveUnusedCultureDirectories(DefaultInstallDirectory);
            File.WriteAllText(
                Path.Combine(DefaultInstallDirectory, "config", "target-environment.marker"),
                "Created by ExhibitOSSetup. This file enables target-only provisioning when the Manager is launched with its explicit system-modification switch.\n");

            var managerPath = Path.Combine(DefaultInstallDirectory, "ExhibitOSManager.exe");
            CreateDesktopShortcut(managerPath);
            var startInfo = new ProcessStartInfo(managerPath, "--enable-system-modifications")
            {
                WorkingDirectory = DefaultInstallDirectory,
                UseShellExecute = false
            };
            startInfo.Environment["EXHIBITOS_TARGET_ENVIRONMENT"] = "target_machine";
            Log($"Launching Manager: {managerPath}");
            using var manager = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start ExhibitOSManager.exe.");

            if (manager.WaitForExit(5000))
            {
                Log($"Manager exited during startup with code {manager.ExitCode} (0x{manager.ExitCode:X8}).");
                MessageBoxW(IntPtr.Zero,
                    $"ExhibitOS was installed to {DefaultInstallDirectory}, but the Manager could not start.\n\n" +
                    $"Exit code: {manager.ExitCode} (0x{manager.ExitCode:X8})\n\n" +
                    $"Setup log: {SetupLogPath}\nManager log: {DefaultInstallDirectory}\\logs\\manager.log",
                    "ExhibitOS Installed — Manager Startup Failed",
                    0x00000010);
                return 2;
            }

            Log("Installation completed and Manager remained running after startup check.");
            MessageBoxW(IntPtr.Zero,
                $"ExhibitOS was installed successfully to:\n{DefaultInstallDirectory}\n\n" +
                "The Manager has been opened and an 'ExhibitOS Manager' shortcut was added to the desktop.\n\n" +
                "Use the Manager to add artwork, test it, and then configure this PC for exhibition.",
                "ExhibitOS Installation Complete",
                0x00000040);

            return 0;
        }
        catch (Exception ex)
        {
            Log("Setup failed: " + ex);
            MessageBoxW(IntPtr.Zero,
                $"ExhibitOS could not be installed.\n\n{ex.Message}\n\nSetup log: {SetupLogPath}",
                "ExhibitOS Setup",
                0x00000010);
            return 1;
        }
    }

    private static void InstallPayload(string installDirectory)
    {
        Log($"Extracting payload to {installDirectory}");
        Directory.CreateDirectory(installDirectory);
        var installRoot = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("The setup payload is missing. Rebuild with tools/build-distribution.ps1.");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        Log($"Payload contains {archive.Entries.Count} entries.");

        var extractedFiles = 0;
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(installRoot, entry.FullName));
            if (!destination.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe path in setup payload: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporaryFile = destination + ".installing";
            using (var source = entry.Open())
            using (var target = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }

            ReplaceInstalledFile(temporaryFile, destination);
            extractedFiles++;
        }
        Log($"Payload extraction completed. Files written: {extractedFiles}.");
    }

    private static bool StopInstalledComponents(string installDirectory)
    {
        var installRoot = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var running = new List<Process>();

        foreach (var processName in new[] { "ExhibitOSManager", "ExhibitWatchdog" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (executable is not null && Path.GetFullPath(executable).StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        running.Add(process);
                    }
                    else
                    {
                        process.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Could not inspect running process {processName} (PID {process.Id}); leaving it unchanged. {ex.Message}");
                    process.Dispose();
                }
            }
        }

        if (running.Count == 0)
        {
            return true;
        }

        var answer = MessageBoxW(IntPtr.Zero,
            "ExhibitOS is currently running and must be closed before it can be updated.\n\n" +
            "Close ExhibitOS and continue with the installation?",
            "Update ExhibitOS",
            0x00000034); // MB_YESNO | MB_ICONWARNING
        if (answer != 6) // IDYES
        {
            foreach (var process in running) process.Dispose();
            return false;
        }

        foreach (var process in running)
        {
            using (process)
            {
                try
                {
                    Log($"Closing {process.ProcessName} (PID {process.Id}) before updating.");
                    if (process.ProcessName.Equals("ExhibitOSManager", StringComparison.OrdinalIgnoreCase))
                    {
                        process.CloseMainWindow();
                        if (process.WaitForExit(3000)) continue;
                    }

                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                    {
                        throw new TimeoutException($"{process.ProcessName} did not close in time.");
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between discovery and shutdown.
                }
                catch (Exception ex)
                {
                    Log($"Could not close {process.ProcessName} (PID {process.Id}): {ex}");
                    MessageBoxW(IntPtr.Zero,
                        $"Setup could not close {process.ProcessName}. Close it manually, then run setup again.\n\nSetup log: {SetupLogPath}",
                        "ExhibitOS Is Still Running",
                        0x00000010);
                    return false;
                }
            }
        }

        return true;
    }

    private static void ReplaceInstalledFile(string temporaryFile, string destination)
    {
        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Move(temporaryFile, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < attempts)
                {
                    Thread.Sleep(250);
                    continue;
                }

                throw new IOException(
                    $"Setup could not replace '{destination}'. Close any program using this file, then run setup again.",
                    ex);
            }
        }
    }

    private static void CreateDesktopShortcut(string managerPath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "ExhibitOS Manager.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable; the desktop shortcut could not be created.");
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shell!.CreateShortcut(shortcutPath);
            shortcut.TargetPath = managerPath;
            shortcut.Arguments = "--enable-system-modifications";
            shortcut.WorkingDirectory = DefaultInstallDirectory;
            shortcut.Description = "Configure and maintain ExhibitOS";
            shortcut.IconLocation = Path.Combine(DefaultInstallDirectory, "Assets", "AppIcon.ico");
            shortcut.Save();
            Log($"Created desktop shortcut: {shortcutPath}");
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void RemoveUnusedCultureDirectories(string installDirectory)
    {
        var installRoot = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var removed = 0;
        foreach (var directory in Directory.EnumerateDirectories(installRoot, "*", SearchOption.TopDirectoryOnly))
        {
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(Path.GetFileName(directory));
            }
            catch (CultureNotFoundException)
            {
                continue;
            }

            if (string.Equals(culture.Name, "en-US", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to remove a culture directory outside the installation root: {resolved}");
            }

            Directory.Delete(resolved, recursive: true);
            removed++;
        }
        Log($"Removed {removed} unused satellite-language directories.");
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SetupLogPath)!);
            File.AppendAllText(SetupLogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Setup must still be able to report errors through its dialog.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
