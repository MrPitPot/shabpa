using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Principal;

namespace Shabpa.Core;

public static class Elevation
{
    public static bool IsElevated { get; } = Check();

    private static bool Check()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>What the user asked for, per application.</summary>
public sealed class AppLimit
{
    /// <summary>Lower-case process name without ".exe".</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>Download ceiling in megabits per second; 0 = unlimited.</summary>
    public double DownMbps { get; set; }
    /// <summary>Upload ceiling in megabits per second; 0 = unlimited.</summary>
    public double UpMbps { get; set; }
    /// <summary>Cut the app off from the network entirely.</summary>
    public bool Blocked { get; set; }
    /// <summary>Never touched by the automatic in-game cap.</summary>
    public bool Exempt { get; set; }

    public bool IsEmpty => DownMbps <= 0 && UpMbps <= 0 && !Blocked && !Exempt;
}

public sealed class NetUsage
{
    public DateOnly Date { get; set; }
    /// <summary>Per-app totals for <see cref="Date"/>, keyed like <see cref="AppLimit.Key"/>.</summary>
    public Dictionary<string, NetUsageEntry> Apps { get; set; } = [];
}

public sealed class NetUsageEntry
{
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public long Down { get; set; }
    public long Up { get; set; }
}

public sealed record ProcInfo(string Key, string Title, string Path);

public sealed record AppTraffic(string Key, string Title, string Path, double DownMbps, double UpMbps, long DownBytes, long UpBytes, DateTime LastUpload);

/// <summary>Turns a stream of "pid sent/received n bytes" into per-application rates and daily totals.</summary>
public sealed class TrafficMeter
{
    public const string Other = "other";

    private sealed class Entry
    {
        public string Title = "";
        public string Path = "";
        public long Down, Up, StepDown, StepUp;
        public DateTime LastUpload;
    }

    private readonly object _gate = new();
    private readonly Func<int, ProcInfo?> _resolve;
    private readonly Dictionary<string, Entry> _apps = [];
    private Dictionary<int, (long down, long up)> _pending = [];
    private DateOnly _date;

    public TrafficMeter(Func<int, ProcInfo?> resolve) => _resolve = resolve;

    /// <summary>Called from the capture thread for every packet batch; must stay cheap.</summary>
    public void Record(int pid, int bytes, bool upload)
    {
        if (bytes <= 0) return;
        lock (_gate)
        {
            _pending.TryGetValue(pid, out var value);
            _pending[pid] = upload ? (value.down, value.up + bytes) : (value.down + bytes, value.up);
        }
    }

    public void Load(NetUsage? usage, DateTime now)
    {
        _date = DateOnly.FromDateTime(now);
        if (usage is null || usage.Date != _date) return;
        foreach (var (key, e) in usage.Apps)
            _apps[key] = new Entry { Title = e.Title, Path = e.Path, Down = e.Down, Up = e.Up };
    }

    public NetUsage Export() => new()
    {
        Date = _date,
        Apps = _apps.Where(a => a.Value.Down + a.Value.Up > 0)
            .ToDictionary(a => a.Key, a => new NetUsageEntry { Title = a.Value.Title, Path = a.Value.Path, Down = a.Value.Down, Up = a.Value.Up }),
    };

    public void Clear()
    {
        lock (_gate) _pending = [];
        _apps.Clear();
    }

    /// <summary>Folds everything recorded since the last call into rates over <paramref name="seconds"/>.</summary>
    public IReadOnlyList<AppTraffic> Snapshot(double seconds, DateTime now)
    {
        Dictionary<int, (long down, long up)> pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = new Dictionary<int, (long, long)>(pending.Count);
        }

        var today = DateOnly.FromDateTime(now);
        if (today != _date)
        {
            _date = today;
            _apps.Clear();
        }

        foreach (var entry in _apps.Values) entry.StepDown = entry.StepUp = 0;
        foreach (var (pid, (down, up)) in pending)
        {
            var info = _resolve(pid) ?? new ProcInfo(Other, L.T("سایر", "Other"), "");
            if (!_apps.TryGetValue(info.Key, out var entry)) _apps[info.Key] = entry = new Entry();
            if (info.Title.Length > 0) entry.Title = info.Title;
            if (info.Path.Length > 0) entry.Path = info.Path;
            entry.StepDown += down;
            entry.StepUp += up;
            entry.Down += down;
            entry.Up += up;
            if (up > 0) entry.LastUpload = now;
        }

        seconds = Math.Max(0.5, seconds);
        return _apps.Select(a => new AppTraffic(a.Key, a.Value.Title.Length > 0 ? a.Value.Title : a.Key, a.Value.Path,
                a.Value.StepDown * 8 / 1e6 / seconds, a.Value.StepUp * 8 / 1e6 / seconds, a.Value.Down, a.Value.Up, a.Value.LastUpload))
            .ToList();
    }
}

/// <summary>Maps process ids to applications, with a short cache because ids are looked up constantly.</summary>
public sealed class ProcessResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);
    private readonly Dictionary<int, (ProcInfo? info, long at)> _cache = [];

    public ProcInfo? Resolve(int pid)
    {
        if (pid is 0 or 4) return new ProcInfo("system", L.T("ویندوز", "Windows"), "");
        var now = Environment.TickCount64;
        if (_cache.TryGetValue(pid, out var hit) && now - hit.at < Ttl.TotalMilliseconds) return hit.info;
        if (_cache.Count > 2000) _cache.Clear();

        ProcInfo? info = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            var key = process.ProcessName.ToLowerInvariant();
            string path = "", title = process.ProcessName;
            try
            {
                path = process.MainModule?.FileName ?? "";
                if (path.Length > 0)
                {
                    var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description)) title = description.Trim();
                }
            }
            catch { /* protected process: name is enough */ }
            info = new ProcInfo(key, title, path);
        }
        catch { /* already exited */ }

        _cache[pid] = (info, now);
        return info;
    }
}

/// <summary>A limit as it should be enforced right now.</summary>
public sealed record EffectiveLimit(string Key, double DownMbps, double UpMbps, bool Blocked, bool Automatic);

/// <summary>Combines the user's per-app limits with the automatic in-game upload cap. Pure logic.</summary>
public static class LimitPlanner
{
    /// <summary>An app counts as "uploading in the background" if it sent anything this recently.</summary>
    public static readonly TimeSpan RecentUpload = TimeSpan.FromMinutes(3);

    private static readonly HashSet<string> NeverTouch = ["system", TrafficMeter.Other, "shabpa", "idle"];

    public static List<EffectiveLimit> Plan(IEnumerable<AppLimit> manual, bool guard, double capMbps, string gameKey,
        IEnumerable<AppTraffic> apps, DateTime now)
    {
        var limits = manual.ToList();
        var plan = new Dictionary<string, EffectiveLimit>();
        foreach (var limit in limits.Where(l => l.DownMbps > 0 || l.UpMbps > 0 || l.Blocked))
            plan[limit.Key] = new EffectiveLimit(limit.Key, Math.Max(0, limit.DownMbps), Math.Max(0, limit.UpMbps), limit.Blocked, false);

        if (guard && capMbps > 0 && gameKey.Length > 0)
        {
            var exempt = limits.Where(l => l.Exempt).Select(l => l.Key).ToHashSet();
            foreach (var app in apps)
            {
                if (app.Key == gameKey || NeverTouch.Contains(app.Key) || exempt.Contains(app.Key)) continue;
                if (now - app.LastUpload > RecentUpload) continue;
                if (plan.TryGetValue(app.Key, out var existing))
                {
                    // The stricter of the user's own cap and the in-game cap wins.
                    if (!existing.Blocked && (existing.UpMbps <= 0 || existing.UpMbps > capMbps))
                        plan[app.Key] = existing with { UpMbps = capMbps };
                }
                else
                {
                    plan[app.Key] = new EffectiveLimit(app.Key, 0, capMbps, false, true);
                }
            }
        }
        return [.. plan.Values.OrderBy(l => l.Key, StringComparer.Ordinal)];
    }

    /// <summary>Two plans with the same signature need no change.</summary>
    public static string Signature(IEnumerable<EffectiveLimit> plan)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join("|", plan.Select(l => $"{l.Key}:{l.DownMbps.ToString("0.###", inv)}:{l.UpMbps.ToString("0.###", inv)}:{(l.Blocked ? 1 : 0)}"));
    }
}

public sealed record PingStats(int Samples, double? Last, double Average, double Jitter, double LossPercent, double Baseline)
{
    public static readonly PingStats Empty = new(0, null, 0, 0, 0, 0);

    /// <summary>null entries are lost packets.</summary>
    public static PingStats From(IReadOnlyList<double?> samples)
    {
        if (samples.Count == 0) return Empty;
        var ok = samples.Where(s => s is not null).Select(s => s!.Value).ToList();
        if (ok.Count == 0) return new PingStats(samples.Count, null, 0, 0, 100, 0);

        double jitter = 0;
        for (var i = 1; i < ok.Count; i++) jitter += Math.Abs(ok[i] - ok[i - 1]);
        if (ok.Count > 1) jitter /= ok.Count - 1;

        var sorted = ok.Order().ToList();
        return new PingStats(samples.Count, samples[^1], ok.Average(), jitter,
            100.0 * (samples.Count - ok.Count) / samples.Count, sorted[sorted.Count / 2]);
    }
}

/// <summary>Pings one host every two seconds and keeps the last couple of minutes.</summary>
public sealed class PingMeter : IDisposable
{
    public const int Keep = 60;

    private readonly Func<string> _host;
    private readonly object _gate = new();
    private readonly List<double?> _samples = [];
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;
    private string _current = "";

    public PingMeter(Func<string> host) => _host = host;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ping" };
        _thread.Start();
    }

    public IReadOnlyList<double?> Samples
    {
        get { lock (_gate) return [.. _samples]; }
    }

    private void Loop()
    {
        using var ping = new Ping();
        do
        {
            var host = _host().Trim();
            if (host != _current)
            {
                _current = host;
                lock (_gate) _samples.Clear();
            }
            if (host.Length == 0) continue;

            double? ms = null;
            try
            {
                var reply = ping.Send(host, 1500);
                if (reply.Status == IPStatus.Success) ms = Math.Max(1, reply.RoundtripTime);
            }
            catch { /* no route, DNS failure: counts as loss */ }

            lock (_gate)
            {
                _samples.Add(ms);
                if (_samples.Count > Keep) _samples.RemoveAt(0);
            }
        } while (!_stop.Wait(2000));
    }

    public void Dispose() => _stop.Set();
}
