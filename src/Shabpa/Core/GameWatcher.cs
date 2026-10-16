using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shabpa.Core;

/// <summary>A game Shabpa recognised: the program, a readable name, and when the program started.</summary>
public sealed record GameInfo(int Pid, string Process, string Name, DateTime Start);

/// <summary>The decision rules for "is this a game", kept apart from Windows so they can be tested.</summary>
public static class GameRules
{
    /// <summary>Folders games live in. A program running from one of these is a game, in any window mode.</summary>
    private static readonly string[] GameFolders =
    [
        @"\steamapps\common\", @"\epic games\", @"\riot games\", @"\ubisoft game launcher\games\", @"\ubisoft\ubisoft game launcher\games\",
        @"\ea games\", @"\electronic arts\", @"\gog galaxy\games\", @"\gog games\", @"\battle.net\", @"\rockstar games\", @"\xboxgames\", @"\games\",
        @"\origin games\", @"\amazon games\library\", @"\my games\",
    ];

    /// <summary>Programs that go full screen or use the graphics card without being games.</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "shabpa", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "potplayer", "potplayermini64",
        "wmplayer", "spotify", "code", "devenv", "claude", "telegram", "discord", "obs64", "obs32", "obs", "photoshop", "premiere pro", "afterfx", "blender", "resolve", "teams",
        "slack", "zoom", "searchhost", "shellexperiencehost", "applicationframehost", "taskmgr", "mstsc", "anydesk", "teamviewer", "dwm", "lockapp", "steamwebhelper", "epicwebhelper",
        "riotclientux", "riotclientservices", "steam", "epicgameslauncher", "battle.net", "origin", "eadesktop", "ubisoftconnect", "galaxyclient", "rockstar", "launcher",
    };

    public static bool IsNotAGame(string process) => process.Length == 0 || NotGames.Contains(process);

    public static bool IsGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var lower = path.Replace('/', '\\').ToLowerInvariant();
        return GameFolders.Any(lower.Contains);
    }

    /// <param name="notificationState">Windows' "is a full-screen app up" answer: 2 = full screen, 3 = exclusive Direct3D.</param>
    /// <param name="coversScreen">The foreground window fills its whole monitor (borderless or full screen).</param>
    /// <param name="usesGraphics">The program has loaded a game graphics library (Direct3D 9/11/12, Vulkan, OpenGL).</param>
    public static bool Decide(int notificationState, double gpuLoad, bool coversScreen, bool usesGraphics, string? path, string process, IEnumerable<string> userGames)
    {
        if (process.Length == 0 || process.Equals("shabpa", StringComparison.OrdinalIgnoreCase)) return false;
        var listed = userGames.Any(g => string.Equals(RuleEngine.NormalizeProcess(g), process, StringComparison.OrdinalIgnoreCase));
        if (listed) return true;
        if (IsNotAGame(process)) return false;
        if (notificationState == 3) return true;
        if (notificationState == 2 && gpuLoad >= 35) return true;
        // a program from a game folder with a window on top and the graphics card working
        if (IsGamePath(path) && usesGraphics && gpuLoad >= 5) return true;
        // a borderless game: fills the screen, uses a game graphics library and keeps the card busy
        return coversScreen && usesGraphics && gpuLoad >= 15;
    }

    /// <summary>"3:07:12" or "12:05".</summary>
    public static string Elapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";
    }
}

/// <summary>Watches the foreground program and tells whether it is a game. Slow facts (the program's file, its graphics
/// libraries, a readable name) are looked up once per program, not every second.</summary>
public sealed class GameWatcher
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private sealed record Facts(int Pid, string Process, string? Path, bool UsesGraphics, string Name, DateTime Start);

    private Facts? _facts;
    private GameInfo? _last;

    /// <summary>The game that is running right now (in front or not), or null. It stays until its program ends.</summary>
    public GameInfo? Current => _last;

    /// <param name="notificationState">From SHQueryUserNotificationState.</param>
    /// <returns>True while a game is the foreground program.</returns>
    public bool Update(int notificationState, double gpuLoad, IEnumerable<string> userGames)
    {
        try
        {
            // the game from before: still alive even when another window is in front?
            if (_last is { } last && !Alive(last.Pid)) _last = null;

            var hwnd = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            if (_facts is null || _facts.Pid != (int)pid) _facts = Look((int)pid);
            if (_facts is null) return false;

            var covers = Covers(hwnd);
            var game = GameRules.Decide(notificationState, gpuLoad, covers, _facts.UsesGraphics, _facts.Path, _facts.Process, userGames);
            if (game && (_last is null || _last.Pid != _facts.Pid))
                _last = new GameInfo(_facts.Pid, _facts.Process, _facts.Name, _facts.Start);
            return game;
        }
        catch
        {
            return false;
        }
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool Covers(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var monitor = MonitorFromWindow(hwnd, 2); // nearest
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        return r.Left <= info.Monitor.Left + 2 && r.Top <= info.Monitor.Top + 2 && r.Right >= info.Monitor.Right - 2 && r.Bottom >= info.Monitor.Bottom - 2;
    }

    private static Facts? Look(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var name = p.ProcessName.ToLowerInvariant();
            string? path = null;
            var graphics = false;
            var title = name;
            var start = DateTime.Now;
            try
            {
                path = p.MainModule?.FileName;
                start = p.StartTime;
                var info = path is null ? null : FileVersionInfo.GetVersionInfo(path);
                title = info?.ProductName is { Length: > 1 } product && !product.Equals("Unreal Engine", StringComparison.OrdinalIgnoreCase) ? product
                    : info?.FileDescription is { Length: > 1 } description ? description : name;
                foreach (ProcessModule module in p.Modules)
                {
                    var m = module.ModuleName.ToLowerInvariant();
                    if (m is "d3d9.dll" or "d3d11.dll" or "d3d12.dll" or "vulkan-1.dll" or "opengl32.dll") { graphics = true; break; }
                }
            }
            catch
            {
                // another program's details need rights Shabpa may not have; the name alone still works
            }
            return new Facts(pid, name, path, graphics, title.Trim(), start);
        }
        catch
        {
            return null;
        }
    }
}
