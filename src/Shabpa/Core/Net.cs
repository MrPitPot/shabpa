using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Principal;

namespace Shabpa.Core;

public static class Elevation
{
    private static readonly bool Real = Check();
    /// <summary>Test aid: pretend to be (not) elevated so both layouts of the permission pages can be built.</summary>
    public static bool? TestOverride { get; set; }
    public static bool IsElevated => TestOverride ?? Real;

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

public sealed record AppTraffic(string Key, string Title, string Path, double DownMbps, double UpMbps, long DownBytes, long UpBytes, DateTime LastUpload,
    DateTime LastDownload = default);

/// <summary>Turns a stream of "pid sent/received n bytes" into per-application rates and daily totals.</summary>
public sealed class TrafficMeter
{
    public const string Other = "other";

    private sealed class Entry
    {
        public string Title = "";
        public string Path = "";
        public long Down, Up, StepDown, StepUp;
        public DateTime LastUpload, LastDownload;
    }

    private readonly object _gate = new();
    private readonly Func<int, ProcInfo?> _resolve;
    private readonly Dictionary<string, Entry> _apps = [];
    private Dictionary<int, (long down, long up)> _pending = [];
    private DateOnly _date;
    private readonly Dictionary<int, int> _waited = [];
    private const int MaxWaitRounds = 4;

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
        Dictionary<int, (long down, long up)>? carry = null;
        foreach (var (pid, (down, up)) in pending)
        {
            var found = _resolve(pid);
            if (ReferenceEquals(found, ProcessResolver.Pending))
            {
                // The name is still being looked up in the background (it can take a while for a busy or protected process).
                // The bytes wait for the next round instead of making this one wait; after a few rounds they go to "other".
                _waited.TryGetValue(pid, out var rounds);
                if (rounds < MaxWaitRounds)
                {
                    (carry ??= [])[pid] = (down, up);
                    _waited[pid] = rounds + 1;
                    continue;
                }
                found = null;
            }
            _waited.Remove(pid);
            var info = found ?? new ProcInfo(Other, L.T("سایر", "Other"), "");
            if (!_apps.TryGetValue(info.Key, out var entry)) _apps[info.Key] = entry = new Entry();
            if (info.Title.Length > 0) entry.Title = info.Title;
            if (info.Path.Length > 0) entry.Path = info.Path;
            entry.StepDown += down;
            entry.StepUp += up;
            entry.Down += down;
            entry.Up += up;
            if (up > 0) entry.LastUpload = now;
            if (down > 0) entry.LastDownload = now;
        }

        if (carry is not null)
            lock (_gate)
                foreach (var (pid, (down, up)) in carry)
                {
                    _pending.TryGetValue(pid, out var value);
                    _pending[pid] = (value.down + down, value.up + up);
                }

        seconds = Math.Max(0.5, seconds);
        return _apps.Select(a => new AppTraffic(a.Key, a.Value.Title.Length > 0 ? a.Value.Title : a.Key, a.Value.Path,
                a.Value.StepDown * 8 / 1e6 / seconds, a.Value.StepUp * 8 / 1e6 / seconds, a.Value.Down, a.Value.Up, a.Value.LastUpload, a.Value.LastDownload))
            .ToList();
    }
}

/// <summary>Maps process ids to applications, with a short cache because ids are looked up constantly. Looking a process up
/// (its name, its file, the file's description from disk) can take tens of milliseconds, much longer behind a virus scanner,
/// so it is never done for the caller: an unknown process answers <see cref="Pending"/> at once and is resolved on a worker.</summary>
public sealed class ProcessResolver
{
    /// <summary>The answer for a process that is still being looked up (compare by reference).</summary>
    public static readonly ProcInfo Pending = new("pending", "", "");

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);
    private readonly object _gate = new();
    private readonly Dictionary<int, (ProcInfo? info, long at)> _cache = [];
    private readonly HashSet<int> _queued = [];
    private readonly Queue<int> _queue = new();
    private bool _working;
    private readonly Func<int, ProcInfo?> _lookup;

    public ProcessResolver() : this(Lookup) { }

    internal ProcessResolver(Func<int, ProcInfo?> lookup) => _lookup = lookup;

    public ProcInfo? Resolve(int pid)
    {
        if (pid is 0 or 4) return new ProcInfo("system", L.T("ویندوز", "Windows"), "");
        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (_cache.TryGetValue(pid, out var hit))
            {
                if (now - hit.at >= Ttl.TotalMilliseconds) Enqueue(pid); // out of date: the old answer serves meanwhile
                return hit.info;
            }
            Enqueue(pid);
            return Pending;
        }
    }

    // called with the lock held
    private void Enqueue(int pid)
    {
        if (!_queued.Add(pid)) return;
        _queue.Enqueue(pid);
        if (_working) return;
        _working = true;
        _ = Task.Run(Work);
    }

    private void Work()
    {
        while (true)
        {
            int pid;
            lock (_gate)
            {
                if (_queue.Count == 0) { _working = false; return; }
                pid = _queue.Dequeue();
            }
            ProcInfo? info = null;
            try { info = _lookup(pid); }
            catch { /* already exited */ }
            lock (_gate)
            {
                _queued.Remove(pid);
                if (_cache.Count > 2000) _cache.Clear();
                _cache[pid] = (info, Environment.TickCount64);
            }
        }
    }

    private static ProcInfo? Lookup(int pid)
    {
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
            return new ProcInfo(key, title, path);
        }
        catch
        {
            return null; // already exited
        }
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

    /// <param name="capMbps">In-game upload cap per app; 0 = leave uploads alone.</param>
    /// <param name="downCapMbps">In-game download cap per app; 0 = leave downloads alone.</param>
    public static List<EffectiveLimit> Plan(IEnumerable<AppLimit> manual, bool guard, double capMbps, string gameKey,
        IEnumerable<AppTraffic> apps, DateTime now, double downCapMbps = 0)
    {
        var limits = manual.ToList();
        var plan = new Dictionary<string, EffectiveLimit>();
        foreach (var limit in limits.Where(l => l.DownMbps > 0 || l.UpMbps > 0 || l.Blocked))
            plan[limit.Key] = new EffectiveLimit(limit.Key, Math.Max(0, limit.DownMbps), Math.Max(0, limit.UpMbps), limit.Blocked, false);

        if (guard && (capMbps > 0 || downCapMbps > 0) && gameKey.Length > 0)
        {
            var exempt = limits.Where(l => l.Exempt).Select(l => l.Key).ToHashSet();
            foreach (var app in apps)
            {
                if (app.Key == gameKey || NeverTouch.Contains(app.Key) || exempt.Contains(app.Key)) continue;
                // Only apps that were actually busy in that direction a moment ago are touched.
                var up = capMbps > 0 && now - app.LastUpload <= RecentUpload ? capMbps : 0;
                var down = downCapMbps > 0 && app.LastDownload != default && now - app.LastDownload <= RecentUpload ? downCapMbps : 0;
                if (up == 0 && down == 0) continue;

                if (plan.TryGetValue(app.Key, out var existing))
                {
                    if (existing.Blocked) continue;
                    // The stricter of the user's own cap and the in-game cap wins, per direction.
                    var newUp = up > 0 && (existing.UpMbps <= 0 || existing.UpMbps > up) ? up : existing.UpMbps;
                    var newDown = down > 0 && (existing.DownMbps <= 0 || existing.DownMbps > down) ? down : existing.DownMbps;
                    plan[app.Key] = existing with { UpMbps = newUp, DownMbps = newDown };
                }
                else
                {
                    plan[app.Key] = new EffectiveLimit(app.Key, down, up, false, true);
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
