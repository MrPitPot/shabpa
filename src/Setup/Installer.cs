using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace Shabpa.Setup;

/// <summary>Where things go. The real one is per-machine; tests point everything at temp folders and HKCU.</summary>
public sealed record InstallTarget(string Dir, RegistryKey RegistryRoot, string StartMenuDir, string DesktopDir)
{
    public static InstallTarget Machine => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Shabpa"),
        Registry.LocalMachine,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

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
        try { EventWaitHandle.OpenExisting("Shabpa.Quit").Set(); }
        catch (WaitHandleCannotBeOpenedException) { /* not running, or an old version without the event */ }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && Process.GetProcessesByName("Shabpa").Length > 0) Thread.Sleep(200);
        foreach (var process in Process.GetProcessesByName("Shabpa"))
        {
            try { process.Kill(); process.WaitForExit(3000); }
            catch { /* already gone */ }
            finally { process.Dispose(); }
        }
    }

    /// <summary>Writes the files, the uninstall entry and the shortcuts. <paramref name="progress"/> gets 0..1.</summary>
    public static string Install(InstallTarget target, byte[] payload, InstallOptions options, string? uninstallerSource, Action<double>? progress = null)
    {
        var (version, files) = Payload.Unpack(payload);
        Directory.CreateDirectory(target.Dir);

        // Files the previous version had and this one does not are removed.
        var manifest = Path.Combine(target.Dir, Manifest);
        string[] old = File.Exists(manifest) ? File.ReadAllLines(manifest) : [];
        var current = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in old.Where(p => !current.Contains(p)))
            TryDelete(Path.Combine(target.Dir, stale));

        long total = files.Sum(f => (long)f.Data.Length), done = 0;
        foreach (var file in files)
        {
            var path = Path.Combine(target.Dir, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteWithRetry(path, file.Data);
            done += file.Data.Length;
            progress?.Invoke(0.9 * done / Math.Max(1, total));
        }
        File.WriteAllLines(manifest, current.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

        if (uninstallerSource is not null && File.Exists(uninstallerSource) &&
            !string.Equals(Path.GetFullPath(uninstallerSource), Path.GetFullPath(target.Uninstaller), StringComparison.OrdinalIgnoreCase))
            WriteWithRetry(target.Uninstaller, File.ReadAllBytes(uninstallerSource));

        using (var key = target.RegistryRoot.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "شب‌پا (Shabpa)");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Mr.PitPot");
            key.SetValue("DisplayIcon", target.Exe);
            key.SetValue("InstallLocation", target.Dir);
            key.SetValue("UninstallString", $"\"{target.Uninstaller}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{target.Uninstaller}\" --uninstall --quiet");
            key.SetValue("EstimatedSize", (int)(total / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        Shortcut(target.StartMenuLink, target.Exe, target.Dir);
        if (options.DesktopShortcut) Shortcut(target.DesktopLink, target.Exe, target.Dir);
        else TryDelete(target.DesktopLink);
        progress?.Invoke(1);
        return version;
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
    public static void Uninstall(InstallTarget target, bool removeData, bool scheduleFolderRemoval, bool cleanSystem = true)
    {
        if (cleanSystem) RemoveSystemTraces();

        TryDelete(target.StartMenuLink);
        TryDelete(target.DesktopLink);
        target.RegistryRoot.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        if (removeData)
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shabpa");
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
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
            run?.DeleteValue("Shabpa", false);

        // The WinDivert driver service is left alone on purpose: it unloads by itself once no program has it
        // open, and other tools (GoodbyeDPI and friends) share the same service name.

    }

    private static void Shortcut(string link, string exe, string workingDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
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
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
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
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
