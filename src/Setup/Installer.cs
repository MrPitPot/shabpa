using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Shabpa.Setup;

/// <summary>Where things go. The real one is per-machine; tests point everything at temp folders and HKCU.</summary>
public sealed record InstallTarget(string Dir, RegistryKey RegistryRoot, string StartMenuDir, string DesktopDir)
{
    public static InstallTarget Machine
    {
        get
        {
            if (TestHooks.Root is { } root)
            {
                // Test harness (Debug / SHABPA_TESTHOOKS builds only): a temp folder, a throwaway HKCU key, temp shortcut folders.
                var key = Registry.CurrentUser.CreateSubKey(@"Software\ShabpaSetupTest\" + (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(root));
                return new InstallTarget(Path.Combine(root, "Program Files", "Shabpa"), key, Path.Combine(root, "Start"), Path.Combine(root, "Desktop"));
            }
            return new InstallTarget(
                Path.Combine(ProgramFilesDir(), "Shabpa"),
                RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                SpecialDir(Environment.SpecialFolder.CommonPrograms, Path.Combine(CommonAppData(), "Microsoft", "Windows", "Start Menu", "Programs")),
                SpecialDir(Environment.SpecialFolder.CommonDesktopDirectory, Path.Combine(PublicDir(), "Desktop")));
        }
    }

    /// <summary>The 64-bit Program Files, whatever the bitness of this process.</summary>
    public static string ProgramFilesDir()
    {
        foreach (var candidate in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), SafeFolder(Environment.SpecialFolder.ProgramFiles) })
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate!;
        return Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Program Files");
    }

    private static string CommonAppData() =>
        SpecialDir(Environment.SpecialFolder.CommonApplicationData, Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData");

    private static string PublicDir() => Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

    private static string SafeFolder(Environment.SpecialFolder folder)
    {
        try { return Environment.GetFolderPath(folder); } catch { return ""; }
    }

    /// <summary>A shell folder, or <paramref name="fallback"/> when Windows reports none: a stripped image can have
    /// none, and an empty path would put the shortcut into the working directory.</summary>
    private static string SpecialDir(Environment.SpecialFolder folder, string fallback)
    {
        var path = SafeFolder(folder);
        return string.IsNullOrWhiteSpace(path) ? fallback : path;
    }

    public string Exe => Path.Combine(Dir, "Shabpa.exe");
    public string Uninstaller => Path.Combine(Dir, "uninstall.exe");
    public string StartMenuLink => Path.Combine(StartMenuDir, "Shabpa.lnk");
    public string DesktopLink => Path.Combine(DesktopDir, "Shabpa.lnk");
}

public sealed record InstallOptions(bool DesktopShortcut, bool Autostart);

public static class Installer
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Shabpa";
    private const string Manifest = "install.manifest";

    public static byte[] EmbeddedPayload()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.bin")
                           ?? throw new InvalidDataException("this build has no payload; run build-installer.sh");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Version currently installed at <paramref name="target"/>, or null.</summary>
    public static string? InstalledVersion(InstallTarget target)
    {
        using var key = target.RegistryRoot.OpenSubKey(UninstallKey);
        return key?.GetValue("DisplayVersion") as string;
    }

    /// <summary>Asks a running Shabpa to exit, then makes sure no copy is left running.</summary>
    public static void StopRunningApp()
    {
        if (TestHooks.Active)
        {
            SetupLog.Info("stop running app: skipped (test root)");
            return;
        }
        try
        {
            try { EventWaitHandle.OpenExisting("Shabpa.Quit").Set(); SetupLog.Info("asked a running Shabpa to quit"); }
            catch (WaitHandleCannotBeOpenedException) { /* not running, or an old version without the event */ }

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && Process.GetProcessesByName("Shabpa").Length > 0) Thread.Sleep(200);
            foreach (var process in Process.GetProcessesByName("Shabpa"))
            {
                try { SetupLog.Warn("killing Shabpa pid " + process.Id); MarkRunFlag(null, process.Id); process.Kill(); process.WaitForExit(3000); }
                catch { /* already gone */ }
                finally { process.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            // Never let "could not look at the process list" stop an install.
            SetupLog.Warn("stop running app failed (continuing): " + ex.Message);
        }
    }

    /// <summary>Before a copy that did not quit is killed: its running.flag gets the exit reason "installer", so the next start of Shabpa
    /// logs "ended by the installer" instead of "ended unexpectedly". Only the flag of that very process is touched. The flag is JSON
    /// written by the app; the installer has no JSON library, so the one field is edited as text.</summary>
    public static bool MarkRunFlag(string? dataDir, int pid)
    {
        try
        {
            var data = dataDir ?? Environment.GetEnvironmentVariable("SHABPA_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shabpa");
            var file = Path.Combine(data, "running.flag");
            if (!File.Exists(file)) return false;
            var text = File.ReadAllText(file);
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, "\"Pid\"\\s*:\\s*" + pid + "\\b")) return false;
            const string field = "\"Exit\":\"installer\"";
            const string existing = "\"Exit\"\\s*:\\s*\"[^\"]*\"";
            var edited = System.Text.RegularExpressions.Regex.IsMatch(text, existing)
                ? System.Text.RegularExpressions.Regex.Replace(text, existing, field)
                : text.TrimEnd().TrimEnd('}') + "," + field + "}";
            File.WriteAllText(file, edited);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Writes the files, the uninstall entry and the shortcuts. <paramref name="progress"/> gets 0..1.
    /// Failures carry an exit code (<see cref="SetupException"/>); a shortcut that cannot be made is only logged.</summary>
    public static string Install(InstallTarget target, byte[] payload, InstallOptions options, string? uninstallerSource, Action<double>? progress = null)
    {
        SetupLog.Info("install to " + target.Dir);
        string version;
        System.Collections.Generic.List<PayloadFile> files;
        try { (version, files) = Payload.Unpack(payload); }
        catch (InvalidDataException ex) { throw new SetupException(ExitCodes.PayloadDamaged, ex.Message, ex); }
        SetupLog.Info("payload: version " + version + ", " + files.Count + " files");

        long total = files.Sum(f => (long)f.Data.Length), done = 0;
        try
        {
            Directory.CreateDirectory(target.Dir);

            // Files the previous version had and this one does not are removed.
            var manifest = Path.Combine(target.Dir, Manifest);
            string[] old = File.Exists(manifest) ? File.ReadAllLines(manifest) : [];
            var current = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in old.Where(p => !current.Contains(p)))
                TryDelete(Path.Combine(target.Dir, stale));

            foreach (var file in files)
            {
                var path = Path.Combine(target.Dir, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                WriteWithRetry(path, file.Data);
                done += file.Data.Length;
                progress?.Invoke(0.9 * done / Math.Max(1, total));
            }
            File.WriteAllLines(manifest, current.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            SetupLog.Info("files written (" + total + " bytes)");

            if (uninstallerSource is not null && File.Exists(uninstallerSource) &&
                !string.Equals(Path.GetFullPath(uninstallerSource), Path.GetFullPath(target.Uninstaller), StringComparison.OrdinalIgnoreCase))
            {
                WriteWithRetry(target.Uninstaller, File.ReadAllBytes(uninstallerSource));
                SetupLog.Info("uninstaller copied to " + target.Uninstaller);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            throw new SetupException(ExitCodes.WriteFailed, "could not write the program files: " + ex.Message, ex);
        }

        try
        {
            WriteUninstallEntry(target, version, total);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            throw new SetupException(ExitCodes.RegistryFailed, "could not write the uninstall entry: " + ex.Message, ex);
        }

        // Shortcuts are a convenience: the install is complete without them (a stripped image may have no shell folders).
        TryShortcut(target.StartMenuLink, target.Exe, target.Dir);
        if (options.DesktopShortcut) TryShortcut(target.DesktopLink, target.Exe, target.Dir);
        else TryDelete(target.DesktopLink);
        progress?.Invoke(1);
        SetupLog.Info("install finished");
        return version;
    }

    /// <summary>Every value winget (and Apps &amp; features) reads, written in one place and read back.</summary>
    private static void WriteUninstallEntry(InstallTarget target, string version, long totalBytes)
    {
        var values = new (string Name, object Value, RegistryValueKind Kind)[]
        {
            ("DisplayName", "شب‌پا (Shabpa)", RegistryValueKind.String),
            ("DisplayVersion", version, RegistryValueKind.String),
            ("Publisher", "Mr.PitPot", RegistryValueKind.String),
            ("DisplayIcon", target.Exe, RegistryValueKind.String),
            ("InstallLocation", target.Dir, RegistryValueKind.String),
            ("UninstallString", $"\"{target.Uninstaller}\" --uninstall", RegistryValueKind.String),
            ("QuietUninstallString", $"\"{target.Uninstaller}\" --uninstall --quiet", RegistryValueKind.String),
            ("EstimatedSize", Math.Max(1, (int)(totalBytes / 1024)), RegistryValueKind.DWord),
            ("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String),
            ("URLInfoAbout", "https://github.com/MrPitPot/shabpa", RegistryValueKind.String),
            ("HelpLink", "https://github.com/MrPitPot/shabpa/issues", RegistryValueKind.String),
            ("NoModify", 1, RegistryValueKind.DWord),
            ("NoRepair", 1, RegistryValueKind.DWord),
        };
        using var key = target.RegistryRoot.CreateSubKey(UninstallKey) ?? throw new IOException("the uninstall key could not be created");
        foreach (var (name, value, kind) in values) key.SetValue(name, value, kind);

        foreach (var required in new[] { "DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "UninstallString", "QuietUninstallString", "DisplayIcon" })
            if (key.GetValue(required) is not string { Length: > 0 })
                throw new IOException("the uninstall entry is missing " + required + " after writing it");
        SetupLog.Info("uninstall entry written: " + key.Name + "  DisplayVersion=" + version);
    }

    /// <summary>Asks the installed app to switch start-with-Windows on or off (it knows how: as
    /// administrator it uses a logon task that keeps its rights).</summary>
    public static void SetAutostart(InstallTarget target, bool on)
    {
        using var process = Process.Start(new ProcessStartInfo(target.Exe, $"--autostart {(on ? "on" : "off")}") { UseShellExecute = false });
        process?.WaitForExit(20000);
    }

    /// <summary>Starts the installed app. After an update it gets "--updated", so it comes to the front (even when it
    /// normally starts hidden next to the clock) and says which version it is now.</summary>
    public static void Launch(InstallTarget target, bool updated = false) =>
        Process.Start(new ProcessStartInfo(target.Exe, LaunchArguments(updated)) { UseShellExecute = true, WorkingDirectory = target.Dir });

    public static string LaunchArguments(bool updated) => updated ? "--updated" : "";

    /// <summary>Removes everything the installer and the app created. The folder itself is deleted by a
    /// short-lived helper once this process (which lives in it) has exited.</summary>
    public static void Uninstall(InstallTarget target, bool removeData, bool scheduleFolderRemoval, bool cleanSystem = true, string? dataDir = null)
    {
        SetupLog.Info("uninstall from " + target.Dir + " (removeData=" + removeData + ")");
        if (cleanSystem) RemoveSystemTraces();

        TryDelete(target.StartMenuLink);
        TryDelete(target.DesktopLink);
        try { target.RegistryRoot.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new SetupException(ExitCodes.RegistryFailed, "could not remove the uninstall entry: " + ex.Message, ex);
        }

        if (removeData)
        {
            var data = dataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shabpa");
            try { if (Directory.Exists(data)) Directory.Delete(data, true); } catch { /* in use: leave it */ }
        }

        var manifest = Path.Combine(target.Dir, Manifest);
        if (File.Exists(manifest))
            foreach (var file in File.ReadAllLines(manifest)) TryDelete(Path.Combine(target.Dir, file));
        TryDelete(manifest);

        if (scheduleFolderRemoval)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{target.Dir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        else
        {
            try { Directory.Delete(target.Dir, true); } catch { /* best effort */ }
        }
        SetupLog.Info("uninstall finished");
    }

    /// <summary>Scheduled tasks the app made (its logon task and every wake timer live in the \Shabpa
    /// folder) and an old-style start-with-Windows entry.</summary>
    private static void RemoveSystemTraces()
    {
        var (_, tasks) = Run("schtasks.exe", "/Query /FO CSV /NH");
        foreach (var line in tasks.Split('\n'))
        {
            var name = line.Split(',')[0].Trim().Trim('"');
            if (name.StartsWith(@"\Shabpa\", StringComparison.OrdinalIgnoreCase)) Run("schtasks.exe", $"/Delete /TN \"{name}\" /F");
        }
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            run?.DeleteValue("Shabpa", false);
        }
        catch (Exception ex) { SetupLog.Warn("could not clear the old Run entry: " + ex.Message); }

        // The WinDivert driver service is left alone on purpose: it unloads by itself once no program has it
        // open, and other tools (GoodbyeDPI and friends) share the same service name.
    }

    private static void TryShortcut(string link, string exe, string workingDir)
    {
        try
        {
            Shortcut(link, exe, workingDir);
            SetupLog.Info("shortcut: " + link);
        }
        catch (Exception ex)
        {
            SetupLog.Warn("shortcut " + link + " was not created: " + ex.Message);
        }
    }

    private static void Shortcut(string link, string exe, string workingDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(link))!);
        try
        {
            // IShellLink straight from the shell: no scripting host needed (it can be disabled on hardened images).
            var shellLink = (IShellLinkW)new ShellLink();
            try
            {
                shellLink.SetPath(exe);
                shellLink.SetWorkingDirectory(workingDir);
                shellLink.SetIconLocation(exe, 0);
                shellLink.SetDescription("Shabpa");
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)shellLink).Save(link, true);
            }
            finally { Marshal.FinalReleaseComObject(shellLink); }
            return;
        }
        catch (Exception ex)
        {
            SetupLog.Warn("IShellLink failed (" + ex.Message + "), trying WScript.Shell");
        }
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is missing");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = workingDir;
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "Shabpa";
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    /// <summary>A just-closed app can hold its files for a moment; retry instead of failing the install.</summary>
    private static void WriteWithRetry(string path, byte[] data)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllBytes(path, data);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private static (int code, string output) Run(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            // Read the output on the side so a stuck tool cannot block us past the wait below.
            var reader = System.Threading.Tasks.Task.Run(() => p.StandardOutput.ReadToEnd());
            if (!p.WaitForExit(15000))
            {
                try { p.Kill(); } catch { /* gone */ }
                return (-1, "timed out");
            }
            return (p.ExitCode, reader.Wait(5000) ? reader.Result : "");
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
