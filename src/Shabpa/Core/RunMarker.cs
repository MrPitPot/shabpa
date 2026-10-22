using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Shabpa.Core;

/// <summary>A local breadcrumb for runs that end without a goodbye (a crash, a stack overflow, the process killed from the Task
/// Manager). While the app runs there is a small <c>running.flag</c> in the data folder (version, process id, start time and,
/// refreshed every ten seconds, the page that was open and when). A normal exit deletes it. If the next start finds it, the log
/// gets one line saying the previous run ended unexpectedly and where it was last seen. Nothing leaves the PC and nothing is
/// collected beyond those few fields.</summary>
public static class RunMarker
{
    public const string FileName = "running.flag";
    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(10);

    public sealed class Mark
    {
        public string Version { get; set; } = "";
        public int Pid { get; set; }
        public DateTime Started { get; set; }
        public DateTime Heartbeat { get; set; }
        public string Page { get; set; } = "";
    }

    private static string? _path;
    private static Mark _mark = new();
    private static Func<string> _page = () => "";
    private static Timer? _timer;
    private static readonly object Gate = new();

    /// <summary>The log line for a flag left behind by an earlier run, or null when that run is still alive (a second copy was
    /// started) or the flag cannot be read.</summary>
    public static string? Describe(Mark previous, Func<int, bool> processAlive)
    {
        if (previous.Pid != 0 && previous.Pid != Environment.ProcessId && processAlive(previous.Pid)) return null;
        var last = previous.Heartbeat == default ? previous.Started : previous.Heartbeat;
        var page = previous.Page.Length > 0 ? previous.Page : "?";
        return $"previous run ended unexpectedly: version {previous.Version}, started {previous.Started:yyyy-MM-dd HH:mm:ss}, "
               + $"last seen {last:yyyy-MM-dd HH:mm:ss} on page {page} (no clean exit; a crash, a freeze that was closed by force, or a power cut)";
    }

    /// <summary>Call once at the start of the primary instance. Reports a leftover flag, then writes a fresh one.</summary>
    public static string? Begin(string folder, string version, Func<string> page, bool startTimer = true)
    {
        string? report = null;
        lock (Gate)
        {
            _path = Path.Combine(folder, FileName);
            _page = page;
            try
            {
                if (File.Exists(_path) && JsonSerializer.Deserialize<Mark>(File.ReadAllText(_path)) is { } old)
                    report = Describe(old, Alive);
            }
            catch { /* a flag that cannot be read says nothing */ }
            if (report is not null) Log.Write(report);
            _mark = new Mark { Version = version, Pid = Environment.ProcessId, Started = DateTime.Now, Heartbeat = DateTime.Now };
            Write();
            _timer?.Dispose();
            if (startTimer) _timer = new Timer(_ => Beat(), null, HeartbeatEvery, HeartbeatEvery);
        }
        return report;
    }

    /// <summary>Refreshes the flag with the open page and the time.</summary>
    public static void Beat()
    {
        lock (Gate)
        {
            if (_path is null) return;
            try { _mark.Page = _page() ?? ""; } catch { /* the page name is optional */ }
            _mark.Heartbeat = DateTime.Now;
            Write();
        }
    }

    /// <summary>The clean exit: the flag goes away.</summary>
    public static void End()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_path is null) return;
            try { File.Delete(_path); } catch { /* best effort */ }
            _path = null;
        }
    }

    private static void Write()
    {
        try { File.WriteAllText(_path!, JsonSerializer.Serialize(_mark)); }
        catch { /* the breadcrumb must never disturb the app */ }
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.StartsWith("Shabpa", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>Writes what no handler would otherwise see: exceptions that end the process, and exceptions of tasks nobody awaited.
/// Type, message and stack only.</summary>
public static class CrashLog
{
    private static bool _hooked;

    public static void Install()
    {
        if (_hooked) return;
        _hooked = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write($"unhandled (process {(e.IsTerminating ? "is ending" : "continues")}): {Describe(e.ExceptionObject)}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write($"unobserved task exception: {Describe(e.Exception)}");
            e.SetObserved();
        };
    }

    public static string Describe(object? error) => error is Exception ex ? $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{(ex.InnerException is { } inner ? Environment.NewLine + "caused by " + Describe(inner) : "")}" : $"{error}";
}
