using System.IO;
using Microsoft.Win32;

namespace Shabpa.Core;

/// <summary>A game launcher found on this PC (or added by hand).</summary>
public sealed class LauncherInfo
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    /// <summary>Lower-case process name without ".exe", to tell whether it is already open.</summary>
    public string Process { get; set; } = "";
    /// <summary>The person pointed at this file themselves (Shabpa did not find it).</summary>
    public bool Manual { get; set; }
}

/// <summary>What the search for launchers looks at. Everything is a function so tests can describe a whole PC.</summary>
internal sealed class LauncherProbe
{
    public Func<string, string, string?> Registry { get; init; } = (_, _) => null;
    public Func<string, bool> Exists { get; init; } = _ => false;
    /// <summary>Program Files folders.</summary>
    public IEnumerable<string> Roots { get; init; } = [];
    /// <summary>Roots of the fixed drives, for launchers installed outside Program Files.</summary>
    public IEnumerable<string> Drives { get; init; } = [];
    public string ProgramData { get; init; } = "";
    /// <summary>Installed programs from the Uninstall registry keys: display name, install folder, icon file.</summary>
    public Func<IEnumerable<(string name, string? folder, string? icon)>> Installed { get; init; } = () => [];
    /// <summary>Start-menu shortcuts: shortcut name and the file it points to.</summary>
    public Func<IEnumerable<(string name, string target)>> Shortcuts { get; init; } = () => [];
    /// <summary>Full paths of the programs running right now whose file name is one of the given names.</summary>
    public Func<string[], IEnumerable<string>> Running { get; init; } = _ => [];
}

/// <summary>Looks for the usual game launchers. A PC rarely keeps them in the default folder, so it checks, in order:
/// the launcher's own registry entry, the list of installed programs, "App Paths", the start-menu shortcuts, a launcher
/// that is already running, and finally the usual folders on every fixed drive. Nothing is read from inside the
/// launchers and nothing is sent anywhere; it only runs when the user asks.</summary>
public static class LauncherDetector
{
    private sealed record Spec(string Key, string Name, string Process, string[] Exe, string[] Titles, string[] Relative);

    private static readonly Spec[] Specs =
    [
        new("steam", "Steam", "steam", ["steam.exe"], ["Steam"], [@"Steam\steam.exe"]),
        new("epic", "Epic Games", "epicgameslauncher", ["EpicGamesLauncher.exe"], ["Epic Games Launcher"],
            [@"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", @"Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe"]),
        new("riot", "Riot Client", "riotclientservices", ["RiotClientServices.exe"], ["Riot Client", "Riot Games"], [@"Riot Games\Riot Client\RiotClientServices.exe"]),
        new("ea", "EA app", "eadesktop", ["EADesktop.exe"], ["EA app", "EA Desktop"], [@"Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe"]),
        new("ubisoft", "Ubisoft Connect", "ubisoftconnect", ["UbisoftConnect.exe"], ["Ubisoft Connect", "Uplay"], [@"Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe"]),
        new("gog", "GOG Galaxy", "galaxyclient", ["GalaxyClient.exe"], ["GOG GALAXY", "GOG Galaxy"], [@"GOG Galaxy\GalaxyClient.exe"]),
        new("battlenet", "Battle.net", "battle.net", ["Battle.net Launcher.exe", "Battle.net.exe"], ["Battle.net"], [@"Battle.net\Battle.net Launcher.exe", @"Battle.net\Battle.net.exe"]),
        new("rockstar", "Rockstar Games Launcher", "launcher", ["Launcher.exe"], ["Rockstar Games Launcher"], [@"Rockstar Games\Launcher\Launcher.exe"]),
    ];

    /// <summary>The launchers Shabpa knows how to look for, for a "not found, add it yourself" list.</summary>
    public static IEnumerable<(string key, string name)> KnownNames => Specs.Select(s => (s.Key, s.Name));

    private static readonly string[] ProgramRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    public static List<LauncherInfo> Detect() => Detect(new LauncherProbe
    {
        Registry = RegistryText, Exists = File.Exists, Roots = ProgramRoots, ProgramData = ProgramData(),
        Drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList(),
        Installed = InstalledPrograms, Shortcuts = StartMenuShortcuts, Running = RunningFiles,
    });

    private static string ProgramData() => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>Testable core with the original four inputs (registry value, file check, Program Files folders, ProgramData).</summary>
    internal static List<LauncherInfo> Detect(Func<string, string, string?> registry, Func<string, bool> exists, IEnumerable<string> roots, string programData) =>
        Detect(new LauncherProbe { Registry = registry, Exists = exists, Roots = roots, ProgramData = programData });

    internal static List<LauncherInfo> Detect(LauncherProbe probe)
    {
        var roots = probe.Roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Program Files folders and, on every drive, the root, "Games" and the two Program Files folders.
        var places = new List<string>(roots);
        foreach (var drive in probe.Drives.Where(d => !string.IsNullOrEmpty(d)))
            places.AddRange([drive, Path.Combine(drive, "Games"), Path.Combine(drive, "Program Files"), Path.Combine(drive, "Program Files (x86)")]);
        places = [.. places.Distinct(StringComparer.OrdinalIgnoreCase)];

        List<(string name, string? folder, string? icon)>? installed = null;
        List<(string name, string target)>? shortcuts = null;
        var found = new List<LauncherInfo>();

        foreach (var spec in Specs)
        {
            var candidates = Candidates(spec, probe, places, () => installed ??= [.. SafeList(probe.Installed)], () => shortcuts ??= [.. SafeList(probe.Shortcuts)]);
            foreach (var path in candidates)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var clean = Normalize(path.Trim().Trim('"'));
                if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !probe.Exists(clean)) continue;
                found.Add(new LauncherInfo { Key = spec.Key, Name = spec.Name, ExePath = clean, Process = spec.Process });
                break;
            }
        }
        return found;
    }

    /// <summary>Steam and Riot store their paths with forward slashes ("c:/program files (x86)/steam/steam.exe"). Explorer
    /// does not understand those and opens the Documents folder instead, so every path is written the Windows way.</summary>
    public static string Normalize(string path)
    {
        try { return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)); }
        catch { return path.Replace('/', Path.DirectorySeparatorChar); }
    }

    private static IEnumerable<T> SafeList<T>(Func<IEnumerable<T>> source)
    {
        try { return source().ToList(); }
        catch { return []; }
    }

    private static bool IsOneOf(string? path, string[] names) =>
        !string.IsNullOrWhiteSpace(path) && names.Any(n => string.Equals(Path.GetFileName(path.Trim().Trim('"')), n, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string?> Candidates(Spec spec, LauncherProbe probe, List<string> places, Func<List<(string name, string? folder, string? icon)>> installed,
        Func<List<(string name, string target)>> shortcuts)
    {
        string? Reg(string key, string name) => probe.Registry(key, name);
        static string? Combine(string? folder, string file) => string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder.Trim().Trim('"'), file);

        // 1. the launcher's own registry entry
        switch (spec.Key)
        {
            case "steam":
                yield return Reg(@"HKCU\Software\Valve\Steam", "SteamExe");
                yield return Combine(Reg(@"HKCU\Software\Valve\Steam", "SteamPath"), "steam.exe");
                yield return Combine(Reg(@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"), "steam.exe");
                yield return Combine(Reg(@"HKLM\SOFTWARE\Valve\Steam", "InstallPath"), "steam.exe");
                break;
            case "riot":
                yield return RiotClientPath(probe.ProgramData, probe.Exists);
                break;
            case "ubisoft":
                yield return Combine(Reg(@"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir"), "UbisoftConnect.exe");
                yield return Combine(Reg(@"HKLM\SOFTWARE\Ubisoft\Launcher", "InstallDir"), "UbisoftConnect.exe");
                break;
            case "gog":
                yield return Combine(Reg(@"HKLM\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "client"), "GalaxyClient.exe");
                break;
            case "rockstar":
                yield return Combine(Reg(@"HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Launcher", "InstallFolder"), "Launcher.exe");
                yield return Combine(Reg(@"HKLM\SOFTWARE\Rockstar Games\Launcher", "InstallFolder"), "Launcher.exe");
                break;
        }

        // 2. the list of installed programs (Uninstall keys): the install folder, or the icon file, which is the .exe
        foreach (var entry in installed())
        {
            if (!spec.Titles.Any(t => entry.name.StartsWith(t, StringComparison.OrdinalIgnoreCase))) continue;
            if (spec.Key == "rockstar" && !entry.name.Contains("Rockstar", StringComparison.OrdinalIgnoreCase)) continue;
            var icon = entry.icon?.Split(',')[0].Trim().Trim('"');
            if (IsOneOf(icon, spec.Exe)) yield return icon;
            foreach (var exe in spec.Exe) yield return Combine(entry.folder, exe);
            // the install folder may be any level of the usual path: try every tail of it
            foreach (var rel in spec.Relative)
            {
                var parts = rel.Split((char)92);
                for (var i = 0; i < parts.Length - 1; i++) yield return Combine(entry.folder, string.Join((char)92, parts[i..]));
            }
        }

        // 3. App Paths: the entry Windows keeps so "Run" can start a program by its file name
        foreach (var exe in spec.Exe)
        {
            yield return Reg($@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}", "");
            yield return Reg($@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}", "");
        }

        // 4. start-menu and desktop shortcuts that point at the launcher
        foreach (var (name, target) in shortcuts())
            if (IsOneOf(target, spec.Exe) && spec.Titles.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase))
                && (spec.Key != "rockstar" || target.Contains("Rockstar", StringComparison.OrdinalIgnoreCase)))
                yield return target;

        // 5. already running
        foreach (var path in probe.Running(spec.Exe).Where(p => IsOneOf(p, spec.Exe)))
            if (spec.Key != "rockstar" || path.Contains("Rockstar", StringComparison.OrdinalIgnoreCase)) yield return path;

        // 6. the usual folders, in Program Files and on every drive
        foreach (var place in places)
            foreach (var rel in spec.Relative)
                yield return Path.Combine(place, rel);
    }

    /// <summary>Riot lists its installs in a small JSON file under ProgramData.</summary>
    private static string? RiotClientPath(string programData, Func<string, bool> exists)
    {
        try
        {
            if (string.IsNullOrEmpty(programData)) return null;
            var file = Path.Combine(programData, "Riot Games", "RiotClientInstalls.json");
            if (!exists(file)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            foreach (var name in new[] { "rc_default", "rc_live" })
                if (doc.RootElement.TryGetProperty(name, out var p) && p.GetString() is { Length: > 0 } path) return path;
        }
        catch { /* unreadable: fall back to the folders */ }
        return null;
    }

    // ---- adding one by hand -----------------------------------------------------------------------

    /// <summary>Why a file cannot be used as a launcher, or null when it can.</summary>
    public static string? Reject(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return L.T("مسیر خالیه.", "The path is empty.");
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return L.T("فایل باید یه برنامه (.exe) باشه.", "The file must be a program (.exe).");
        if (!Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return L.T("فقط فایل روی دیسک‌های همین کامپیوتر قبول می‌شه.", "Only a file on this PC's own drives is accepted.");
        if (!File.Exists(path)) return L.T("این فایل وجود نداره.", "That file does not exist.");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0 && path.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return L.T("برنامه‌های خود ویندوز به‌عنوان لانچر قبول نمی‌شن.", "Windows' own programs are not accepted as launchers.");
        return null;
    }

    /// <summary>A launcher from a file the person picked. A known launcher's file keeps its own key (so its built-in
    /// resume help still applies); anything else is a generic launcher that is only opened.</summary>
    public static LauncherInfo FromFile(string path)
    {
        path = Normalize(path);
        var file = Path.GetFileName(path);
        var known = Specs.FirstOrDefault(s => IsOneOf(path, s.Exe) && (s.Key != "rockstar" || path.Contains("Rockstar", StringComparison.OrdinalIgnoreCase)));
        if (known is not null)
            return new LauncherInfo { Key = known.Key, Name = known.Name, ExePath = path, Process = known.Process, Manual = true };
        string name;
        try { name = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName is { Length: > 0 } p ? p : Path.GetFileNameWithoutExtension(file); }
        catch { name = Path.GetFileNameWithoutExtension(file); }
        var process = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        return new LauncherInfo { Key = "manual:" + process, Name = name, ExePath = path, Process = process, Manual = true };
    }

    /// <summary>The detected launchers plus the ones added by hand that are still on disk.</summary>
    public static List<LauncherInfo> Merge(IEnumerable<LauncherInfo> detected, IEnumerable<LauncherInfo> before, Func<string, bool> exists)
    {
        var list = detected.ToList();
        foreach (var manual in before.Where(l => l.Manual && exists(l.ExePath)))
            if (list.All(l => !string.Equals(l.ExePath, manual.ExePath, StringComparison.OrdinalIgnoreCase)) && list.All(l => l.Key != manual.Key))
                list.Add(manual);
        return list;
    }

    // ---- reading the real PC ------------------------------------------------------------------------------

    private static string? RegistryText(string key, string name)
    {
        try
        {
            var split = key.IndexOf((char)92);
            var hive = key[..split] == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using var k = hive.OpenSubKey(key[(split + 1)..]);
            return k?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<(string name, string? folder, string? icon)> InstalledPrograms()
    {
        const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var places = new (RegistryHive hive, RegistryView view)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default),
        };
        var result = new List<(string, string?, string?)>();
        foreach (var (hive, view) in places)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(Uninstall);
                if (root is null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(name);
                    if (k?.GetValue("DisplayName") is string display)
                        result.Add((display, k.GetValue("InstallLocation") as string, k.GetValue("DisplayIcon") as string));
                }
            }
            catch
            {
                // an unreadable branch only means fewer hints
            }
        }
        return result;
    }

    private static IEnumerable<(string name, string target)> StartMenuShortcuts()
    {
        var result = new List<(string, string)>();
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        var shell = Type.GetTypeFromProgID("WScript.Shell");
        if (shell is null) return result;
        dynamic? wsh = Activator.CreateInstance(shell);
        if (wsh is null) return result;
        foreach (var folder in folders.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)))
        {
            List<string> links;
            try { links = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch { continue; }
            foreach (var link in links)
            {
                try
                {
                    string target = wsh.CreateShortcut(link).TargetPath;
                    if (!string.IsNullOrWhiteSpace(target)) result.Add((Path.GetFileNameWithoutExtension(link), target));
                }
                catch
                {
                    // a broken shortcut
                }
            }
        }
        return result;
    }

    private static IEnumerable<string> RunningFiles(string[] names)
    {
        var result = new List<string>();
        foreach (var name in names.Select(Path.GetFileNameWithoutExtension))
        {
            System.Diagnostics.Process[] processes;
            try { processes = System.Diagnostics.Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var process in processes)
            {
                try { if (process.MainModule?.FileName is { } file) result.Add(file); }
                catch { /* not allowed to look at it */ }
                process.Dispose();
            }
        }
        return result;
    }

    /// <summary>Opens a launcher with the person's normal rights. When Shabpa runs as administrator the file is handed to
    /// Explorer, so the launcher does not inherit administrator rights (and a changed path in the saved data can never
    /// become a way to run something with them).</summary>
    public static void Open(string exePath)
    {
        exePath = Normalize(exePath);
        if (Hub.Elevated && Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } windows)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(windows, "explorer.exe"), $"\"{exePath}\"") { UseShellExecute = false });
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
    }
}

public enum PlanState { Waiting, Running }

/// <summary>"Open this launcher at this time and shut the PC down when its download is finished."</summary>
public sealed class DownloadPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LauncherKey { get; set; } = "";
    public string LauncherName { get; set; } = "";
    public string ExePath { get; set; } = "";
    /// <summary>The launcher was added by hand (it is opened with normal rights).</summary>
    public bool Manual { get; set; }
    public DateTime StartAt { get; set; }
    public PowerAction Action { get; set; } = PowerAction.Shutdown;
    /// <summary>The download counts as finished after the connection stayed this quiet for this long.</summary>
    public int QuietMinutes { get; set; } = 10;
    public PlanState State { get; set; } = PlanState.Waiting;
    public DateTime? StartedAt { get; set; }
    /// <summary>Empty = once. Otherwise the plan comes back every week on these days at the same time.</summary>
    public List<DayOfWeek> RepeatDays { get; set; } = [];
    /// <summary>Download speed (Mbit/s) below which the connection counts as quiet.</summary>
    public double ThresholdMbps { get; set; } = 1;
    /// <summary>If no download is seen within this many minutes of opening the launcher, nothing is switched off.</summary>
    public int GiveUpMinutes { get; set; } = 45;
    /// <summary>Only open the launcher at the set time; do not watch the download or switch anything off.</summary>
    public bool OpenOnly { get; set; }
    /// <summary>Wake the PC from sleep a minute before the start time (needs wake timers, which most PCs allow).</summary>
    public bool WakeUp { get; set; } = true;
    /// <summary>Replay the clicks the person taught for this launcher, when there are any.</summary>
    public bool UseMacro { get; set; } = true;

    public bool Repeats => RepeatDays.Count > 0;

    /// <summary>Optional end of the download window as minutes after midnight; null = no end. The end is the first time of day
    /// after the start, so a 23:00 start with a 06:00 end runs overnight.</summary>
    public int? EndMinute { get; set; }
    /// <summary>When the download is not finished at the end time, run the action then anyway (default: only stop waiting).</summary>
    public bool ActAtEnd { get; set; }
    /// <summary>The concrete end of the running plan (set when it starts).</summary>
    public DateTime? EndAt { get; set; }

    /// <summary>The first moment after <paramref name="start"/> whose time of day is <paramref name="endMinute"/>.</summary>
    public static DateTime? EndAfter(DateTime start, int? endMinute)
    {
        if (endMinute is not { } m) return null;
        var end = start.Date.AddMinutes(m);
        return end <= start ? end.AddDays(1) : end;
    }
}
