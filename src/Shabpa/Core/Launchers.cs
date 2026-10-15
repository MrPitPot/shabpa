using System.IO;
using Microsoft.Win32;

namespace Shabpa.Core;

/// <summary>A game launcher found on this PC.</summary>
public sealed class LauncherInfo
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    /// <summary>Lower-case process name without ".exe", to tell whether it is already open.</summary>
    public string Process { get; set; } = "";
}

/// <summary>Looks for the usual game launchers by checking a few well-known registry values and install folders.
/// Nothing is read from inside the launchers and nothing is sent anywhere; it only runs when the user asks.</summary>
public static class LauncherDetector
{
    private static readonly string[] ProgramRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    public static List<LauncherInfo> Detect() => Detect(RegistryText, File.Exists, ProgramRoots, ProgramData());

    private static string ProgramData() => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>Testable core: <paramref name="registry"/> returns a value for (hive-qualified key, value name).</summary>
    internal static List<LauncherInfo> Detect(Func<string, string, string?> registry, Func<string, bool> exists, IEnumerable<string> roots, string programData)
    {
        var roots2 = roots.Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList();
        var found = new List<LauncherInfo>();

        void Add(string key, string name, string process, IEnumerable<string?> candidates)
        {
            foreach (var path in candidates)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var clean = path.Trim().Trim('"');
                if (!exists(clean)) continue;
                found.Add(new LauncherInfo { Key = key, Name = name, ExePath = clean, Process = process });
                return;
            }
        }

        string[] Under(string relative) => [.. roots2.Select(r => Path.Combine(r, relative))];

        Add("steam", "Steam", "steam", [
            registry(@"HKCU\Software\Valve\Steam", "SteamExe"),
            Combine(registry(@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"), "steam.exe"),
            .. Under(@"Steam\steam.exe")]);

        Add("epic", "Epic Games", "epicgameslauncher", [
            .. Under(@"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
            .. Under(@"Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe")]);

        Add("riot", "Riot Client", "riotclientservices", [
            RiotClientPath(programData, exists),
            .. Under(@"Riot Games\Riot Client\RiotClientServices.exe")]);

        Add("ea", "EA app", "eadesktop", [
            .. Under(@"Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe")]);

        Add("ubisoft", "Ubisoft Connect", "ubisoftconnect", [
            Combine(registry(@"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir"), "UbisoftConnect.exe"),
            Combine(registry(@"HKLM\SOFTWARE\Ubisoft\Launcher", "InstallDir"), "UbisoftConnect.exe"),
            .. Under(@"Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe")]);

        Add("gog", "GOG Galaxy", "galaxyclient", [
            Combine(registry(@"HKLM\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "client"), "GalaxyClient.exe"),
            .. Under(@"GOG Galaxy\GalaxyClient.exe")]);

        Add("battlenet", "Battle.net", "battle.net", [
            .. Under(@"Battle.net\Battle.net Launcher.exe"),
            .. Under(@"Battle.net\Battle.net.exe")]);

        Add("rockstar", "Rockstar Games Launcher", "launcher", [
            .. Under(@"Rockstar Games\Launcher\Launcher.exe")]);

        return found;
    }

    private static string? Combine(string? folder, string file) => string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder.Trim().Trim('"'), file);

    /// <summary>Riot lists its installs in a small JSON file under ProgramData.</summary>
    private static string? RiotClientPath(string programData, Func<string, bool> exists)
    {
        try
        {
            var file = Path.Combine(programData, "Riot Games", "RiotClientInstalls.json");
            if (!exists(file)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            foreach (var name in new[] { "rc_default", "rc_live" })
                if (doc.RootElement.TryGetProperty(name, out var p) && p.GetString() is { Length: > 0 } path) return path;
        }
        catch { /* unreadable: fall back to the folders */ }
        return null;
    }

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
}

public enum PlanState { Waiting, Running }

/// <summary>"Open this launcher at this time and shut the PC down when its download is finished."</summary>
public sealed class DownloadPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LauncherKey { get; set; } = "";
    public string LauncherName { get; set; } = "";
    public string ExePath { get; set; } = "";
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

    public bool Repeats => RepeatDays.Count > 0;
}
