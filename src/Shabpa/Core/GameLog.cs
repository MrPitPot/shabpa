using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Shabpa.Core;

// ---- data ---------------------------------------------------------------------------------------------------------------
// Everything here stays on this PC. The index (games.json) holds the summaries; the chart series of each session sit in
// their own small file under games-series\, read only when a session is opened, so a long history costs almost no RAM.

public static class GameMetric
{
    public const string CpuTemp = "cpuT", GpuTemp = "gpuT", CpuLoad = "cpuL", GpuLoad = "gpuL", Ram = "ram", Down = "down", Up = "up", Fps = "fps";
    public static readonly string[] All = [CpuTemp, GpuTemp, CpuLoad, GpuLoad, Ram, Down, Up, Fps];
}

/// <summary>Minimum, average and maximum of one reading over a session.</summary>
public sealed class MetricStat
{
    public double Min { get; set; }
    public double Avg { get; set; }
    public double Max { get; set; }
}

/// <summary>A program that ran next to the game: name only, with how busy it was.</summary>
public sealed class BackgroundProgram
{
    public string Name { get; set; } = "";
    public double AvgCpu { get; set; }
    public double MaxCpu { get; set; }
    public double MaxRamMb { get; set; }
    /// <summary>How many of the session's process looks it was seen in.</summary>
    public int Seen { get; set; }
}

public sealed class GameSessionRecord
{
    public string Id { get; set; } = "";
    public DateTime Start { get; set; }
    public int Seconds { get; set; }
    public long DownBytes { get; set; }
    public long UpBytes { get; set; }
    /// <summary>True when the bytes are the whole PC's traffic during the game (per-app counting needs administrator rights).</summary>
    public bool NetApprox { get; set; }
    public Dictionary<string, MetricStat> Stats { get; set; } = [];
    public int ProcLooks { get; set; }
    public List<BackgroundProgram> Programs { get; set; } = [];

    public TimeSpan Length => TimeSpan.FromSeconds(Seconds);
    public MetricStat? Stat(string metric) => Stats.TryGetValue(metric, out var s) ? s : null;
}

public sealed class GameEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public List<GameSessionRecord> Sessions { get; set; } = [];
}

public sealed class GameLogData
{
    public int Version { get; set; } = 1;
    public List<GameEntry> Games { get; set; } = [];
}

/// <summary>The downsampled curves of one session (at most <see cref="GameSessionRecorder.MaxPoints"/> points each).</summary>
public sealed class SessionSeries
{
    public Dictionary<string, double[]> Series { get; set; } = [];
}

public sealed record FinishedSession(string Key, string Name, GameSessionRecord Record, SessionSeries Series);

// ---- recorder -----------------------------------------------------------------------------------------------------------

/// <summary>One look at the machine while a game runs. Key is "" when no game is running.</summary>
public sealed record RecorderInput(DateTime Time, string Key, string Name, double CpuLoad, double GpuLoad, double? CpuTemp, double? GpuTemp,
    double Ram, double DownMbps, double UpMbps, double Fps, long AppDown = 0, long AppUp = 0, bool AppKnown = false);

/// <summary>One program (all its processes together) at one moment.</summary>
public sealed record ProcUse(string Name, double CpuPercent, double RamMb);

/// <summary>Turns the stream of looks into finished session records. Pure (no timers, no disk): feed it and it hands back a
/// session when the game ends. Memory is bounded: when the raw buffer is full neighbours are merged and the cadence doubles.</summary>
public sealed class GameSessionRecorder
{
    public const int MaxPoints = 120;
    private const int BufferLimit = 240;
    public static readonly TimeSpan Cadence = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(1);
    private const int MaxProcLooks = 40;

    private sealed class Acc
    {
        public double Min = double.MaxValue, Max = double.MinValue, Sum;
        public int N;
        public void Add(double v) { if (v < Min) Min = v; if (v > Max) Max = v; Sum += v; N++; }
    }

    private readonly object _gate = new();
    private string _key = "", _name = "";
    private DateTime _start, _last, _takenAt;
    private readonly Dictionary<string, Acc> _acc = [];
    private readonly Dictionary<string, List<double>> _raw = [];
    private long _downAcc, _upAcc, _downLast, _upLast;
    private double _downApprox, _upApprox;
    private bool _appKnown;
    private int _merge = 1, _tick;
    private int _looks;
    private readonly Dictionary<string, (double cpuSum, double cpuMax, double ramMax, int seen)> _procs = [];

    public bool Active => _key.Length > 0;
    public string Key => _key;
    public string Name => _name;
    public DateTime Started => _start;
    public int ProcessLooks => _looks;

    /// <summary>Takes one look. Returns a finished session when the game just ended (or was replaced) and it lasted long enough.</summary>
    public FinishedSession? Feed(RecorderInput input)
    {
        lock (_gate)
        {
            FinishedSession? done = null;
            if (Active && (input.Key.Length == 0 || !string.Equals(input.Key, _key, StringComparison.OrdinalIgnoreCase)))
                done = Finish(input.Time);
            if (input.Key.Length == 0) return done;
            if (!Active) Begin(input);
            _last = input.Time;
            if (input.Time - _takenAt >= Cadence || _tick == 0) Take(input);
            return done;
        }
    }

    /// <summary>The program is being closed: keep what there is.</summary>
    public FinishedSession? Flush(DateTime now)
    {
        lock (_gate) return Active ? Finish(now) : null;
    }

    private DateTime _lastAsk;
    private int _asked;

    /// <summary>True (once) when the caller should measure the other programs now: the first time ~40 s into the game, then every 6 minutes.</summary>
    public bool TryBeginProcessLook(DateTime now)
    {
        lock (_gate)
        {
            if (!Active || _asked >= MaxProcLooks) return false;
            var due = _asked == 0 ? TimeSpan.FromSeconds(40) : TimeSpan.FromMinutes(6);
            if (now - (_asked == 0 ? _start : _lastAsk) < due) return false;
            _asked++;
            _lastAsk = now;
            return true;
        }
    }

    /// <summary>True when a new reading is due (the caller can skip gathering one otherwise).</summary>
    public bool Wants(DateTime now)
    {
        lock (_gate) return !Active || _tick == 0 || now - _takenAt >= Cadence;
    }

    public void AddProcessLook(string key, IReadOnlyList<ProcUse> look)
    {
        lock (_gate)
        {
            if (!Active || !string.Equals(key, _key, StringComparison.OrdinalIgnoreCase)) return;
            _looks++;
            foreach (var p in look)
            {
                _procs.TryGetValue(p.Name, out var e);
                _procs[p.Name] = (e.cpuSum + p.CpuPercent, Math.Max(e.cpuMax, p.CpuPercent), Math.Max(e.ramMax, p.RamMb), e.seen + 1);
            }
        }
    }

    private void Begin(RecorderInput i)
    {
        _key = i.Key;
        _name = i.Name.Length > 0 ? i.Name : i.Key;
        _start = _last = i.Time;
        _takenAt = DateTime.MinValue;
        _acc.Clear();
        _raw.Clear();
        _procs.Clear();
        _merge = 1;
        _tick = 0;
        _looks = 0;
        _asked = 0;
        _lastAsk = i.Time;
        _downApprox = _upApprox = 0;
        _appKnown = i.AppKnown;
        _downAcc = _upAcc = 0;
        _downLast = i.AppDown;
        _upLast = i.AppUp;
    }

    private void Take(RecorderInput i)
    {
        var dt = _tick == 0 ? 0 : Math.Min(60, (i.Time - _takenAt).TotalSeconds);
        _takenAt = i.Time;
        _tick++;
        _downApprox += i.DownMbps * dt / 8 * 1_000_000;
        _upApprox += i.UpMbps * dt / 8 * 1_000_000;
        if (i.AppKnown)
        {
            if (!_appKnown) { _appKnown = true; _downLast = i.AppDown; _upLast = i.AppUp; }
            // the counters are "today so far": after midnight they start again from zero
            _downAcc += i.AppDown >= _downLast ? i.AppDown - _downLast : i.AppDown;
            _upAcc += i.AppUp >= _upLast ? i.AppUp - _upLast : i.AppUp;
            _downLast = i.AppDown;
            _upLast = i.AppUp;
        }

        Put(GameMetric.CpuTemp, i.CpuTemp);
        Put(GameMetric.GpuTemp, i.GpuTemp);
        Put(GameMetric.CpuLoad, i.CpuLoad);
        Put(GameMetric.GpuLoad, i.GpuLoad);
        Put(GameMetric.Ram, i.Ram);
        Put(GameMetric.Down, i.DownMbps);
        Put(GameMetric.Up, i.UpMbps);
        Put(GameMetric.Fps, i.Fps > 0 ? i.Fps : null);

        // buffer full: merge neighbours (keeps every metric aligned) so a ten-hour session never grows the buffer
        if (_raw.Values.Any(l => l.Count >= BufferLimit))
        {
            foreach (var key in _raw.Keys.ToList())
            {
                var l = _raw[key];
                var merged = new List<double>(l.Count / 2 + 1);
                for (var k = 0; k < l.Count; k += 2) merged.Add(Mean(l, k, Math.Min(l.Count, k + 2)));
                _raw[key] = merged;
            }
            _merge *= 2;
        }
    }

    private void Put(string metric, double? value)
    {
        if (!_raw.TryGetValue(metric, out var list)) _raw[metric] = list = [];
        if (value is { } v && !double.IsNaN(v))
        {
            if (!_acc.TryGetValue(metric, out var a)) _acc[metric] = a = new Acc();
            a.Add(v);
            list.Add(v);
        }
        else list.Add(double.NaN);
    }

    private static double Mean(List<double> l, int from, int to)
    {
        double sum = 0;
        var n = 0;
        for (var k = from; k < to; k++)
            if (!double.IsNaN(l[k])) { sum += l[k]; n++; }
        return n == 0 ? double.NaN : sum / n;
    }

    /// <summary>At most <paramref name="max"/> points: each is the average of its slice (NaN slices are skipped).</summary>
    public static double[] Downsample(IReadOnlyList<double> values, int max = MaxPoints)
    {
        var list = values.Where(v => !double.IsNaN(v)).ToList();
        if (list.Count <= max) return [.. list.Select(v => Math.Round(v, 1))];
        var result = new double[max];
        for (var i = 0; i < max; i++)
        {
            int from = (int)((long)i * list.Count / max), to = Math.Max(from + 1, (int)((long)(i + 1) * list.Count / max));
            double sum = 0;
            for (var k = from; k < to; k++) sum += list[k];
            result[i] = Math.Round(sum / (to - from), 1);
        }
        return result;
    }

    private FinishedSession? Finish(DateTime end)
    {
        var key = _key;
        var name = _name;
        _key = "";
        var length = (_last > end ? _last : end) - _start;
        if (length < MinimumLength || _tick == 0) return null;
        // the end is the last real look, not the moment the loop noticed (the game closed up to a few seconds earlier)
        var record = new GameSessionRecord
        {
            Id = _start.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Math.Abs(key.GetHashCode()) % 10000,
            Start = _start,
            Seconds = (int)Math.Round(Math.Min(length.TotalSeconds, (_last - _start).TotalSeconds + Cadence.TotalSeconds)),
        };
        if (_appKnown)
        {
            record.DownBytes = _downAcc;
            record.UpBytes = _upAcc;
        }
        else
        {
            record.DownBytes = (long)_downApprox;
            record.UpBytes = (long)_upApprox;
            record.NetApprox = true;
        }
        var series = new SessionSeries();
        foreach (var (metric, a) in _acc)
        {
            if (a.N == 0) continue;
            record.Stats[metric] = new MetricStat { Min = Math.Round(a.Min, 1), Avg = Math.Round(a.Sum / a.N, 1), Max = Math.Round(a.Max, 1) };
            series.Series[metric] = Downsample(_raw[metric]);
        }
        record.ProcLooks = _looks;
        record.Programs = [.. _procs
            .Select(p => new BackgroundProgram { Name = p.Key, AvgCpu = Math.Round(p.Value.cpuSum / Math.Max(1, _looks), 1), MaxCpu = Math.Round(p.Value.cpuMax, 1), MaxRamMb = Math.Round(p.Value.ramMax), Seen = p.Value.seen })
            .OrderByDescending(p => p.AvgCpu + p.MaxRamMb / 256.0)
            .Take(8)];
        _acc.Clear();
        _raw.Clear();
        _procs.Clear();
        return new FinishedSession(key, name, record, series);
    }
}

// ---- other programs -----------------------------------------------------------------------------------------------------

/// <summary>Looks at what else is running: two process listings a few seconds apart give each program's CPU share. Runs on a
/// pool thread a handful of times per session; nothing stays loaded in between.</summary>
public static class ProcessProbe
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "system", "registry", "memory compression", "secure system", "dwm", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "svchost",
        "fontdrvhost", "conhost", "runtimebroker", "searchhost", "sihost", "ctfmon", "audiodg", "shabpa", "msmpeng", "taskhostw", "startmenuexperiencehost",
        "textinputhost", "applicationframehost", "shellexperiencehost", "system idle process",
    };

    private static Dictionary<int, (string name, double cpu, double ram)> Snap()
    {
        var map = new Dictionary<int, (string, double, double)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                map[p.Id] = (p.ProcessName, p.TotalProcessorTime.TotalSeconds, p.WorkingSet64 / 1048576.0);
            }
            catch
            {
                // protected process: not readable, not interesting
            }
            finally
            {
                p.Dispose();
            }
        }
        return map;
    }

    /// <summary>Picks the busiest programs from two listings (pure, so it can be tested).</summary>
    public static List<ProcUse> Pick(Dictionary<int, (string name, double cpu, double ram)> a, Dictionary<int, (string name, double cpu, double ram)> b,
        double seconds, int cores, string gameProcess, int top = 4)
    {
        var groups = new Dictionary<string, (double cpu, double ram)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pid, now) in b)
        {
            if (Skip.Contains(now.name) || string.Equals(now.name, gameProcess, StringComparison.OrdinalIgnoreCase)) continue;
            var cpu = a.TryGetValue(pid, out var before) && before.name == now.name ? Math.Max(0, now.cpu - before.cpu) / Math.Max(0.1, seconds) / Math.Max(1, cores) * 100 : 0;
            groups.TryGetValue(now.name, out var g);
            groups[now.name] = (g.cpu + cpu, g.ram + now.ram);
        }
        var byCpu = groups.Where(g => g.Value.cpu >= 0.3).OrderByDescending(g => g.Value.cpu).Take(top);
        var byRam = groups.Where(g => g.Value.ram >= 200).OrderByDescending(g => g.Value.ram).Take(top);
        return [.. byCpu.Concat(byRam).DistinctBy(g => g.Key).Select(g => new ProcUse(g.Key.ToLowerInvariant(), Math.Round(g.Value.cpu, 1), Math.Round(g.Value.ram)))];
    }

    public static async Task<List<ProcUse>> Measure(string gameProcess, int gapMs = 8000)
    {
        var started = Stopwatch.GetTimestamp();
        var first = Snap();
        await Task.Delay(gapMs).ConfigureAwait(false);
        var second = Snap();
        return Pick(first, second, Stopwatch.GetElapsedTime(started).TotalSeconds, Environment.ProcessorCount, gameProcess);
    }
}

// ---- store --------------------------------------------------------------------------------------------------------------

/// <summary>games.json (index) plus games-series\*.json (curves). Atomic writes, capped, and a damaged file never stops the app.</summary>
public sealed class GameLogStore
{
    public const int PerGameCap = 200, TotalCap = 1000;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly string _file, _seriesFolder;
    private GameLogData _data = new();

    public bool ReadOnly { get; }
    public event Action? Changed;

    public GameLogStore(string folder, bool readOnly = false)
    {
        _file = Path.Combine(folder, "games.json");
        _seriesFolder = Path.Combine(folder, "games-series");
        ReadOnly = readOnly;
    }

    public string File => _file;

    public void Load()
    {
        lock (_gate)
        {
            _data = new GameLogData();
            if (!System.IO.File.Exists(_file)) return;
            foreach (var candidate in new[] { _file, _file + ".bak" })
            {
                try
                {
                    if (!System.IO.File.Exists(candidate)) continue;
                    var data = JsonSerializer.Deserialize<GameLogData>(System.IO.File.ReadAllText(candidate), Json);
                    if (data is null) continue;
                    _data = Sanitize(data);
                    if (candidate != _file) Log.Write("game log restored from backup");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Write($"game log unreadable ({Path.GetFileName(candidate)}): {ex.Message}");
                    if (candidate == _file && !ReadOnly)
                        try { System.IO.File.Copy(_file, _file + ".broken", true); } catch { /* best effort */ }
                }
            }
        }
    }

    private static GameLogData Sanitize(GameLogData data)
    {
        data.Games = [.. data.Games.Where(g => g is { Key.Length: > 0 }).Select(g =>
        {
            g.Sessions = [.. (g.Sessions ?? []).Where(s => s is { Seconds: > 0 }).OrderBy(s => s.Start)];
            foreach (var s in g.Sessions) { s.Stats ??= []; s.Programs ??= []; }
            return g;
        }).Where(g => g.Sessions.Count > 0)];
        return data;
    }

    public IReadOnlyList<GameEntry> Games
    {
        get { lock (_gate) return [.. _data.Games.Select(g => new GameEntry { Key = g.Key, Name = g.Name, Sessions = [.. g.Sessions] })]; }
    }

    public GameEntry? Game(string key) => Games.FirstOrDefault(g => g.Key == key);

    public void Add(FinishedSession done)
    {
        lock (_gate)
        {
            var game = _data.Games.FirstOrDefault(g => g.Key == done.Key);
            if (game is null) _data.Games.Add(game = new GameEntry { Key = done.Key });
            game.Name = done.Name;
            game.Sessions.Add(done.Record);
            WriteSeries(done.Record.Id, done.Series);
            Trim();
            Save();
        }
        Changed?.Invoke();
    }

    private void Trim()
    {
        foreach (var g in _data.Games)
            while (g.Sessions.Count > PerGameCap) Evict(g, g.Sessions[0]);
        while (_data.Games.Sum(g => g.Sessions.Count) > TotalCap)
        {
            var oldest = _data.Games.SelectMany(g => g.Sessions.Select(s => (g, s))).MinBy(x => x.s.Start);
            Evict(oldest.g, oldest.s);
        }
        _data.Games.RemoveAll(g => g.Sessions.Count == 0);
    }

    private void Evict(GameEntry game, GameSessionRecord session)
    {
        game.Sessions.Remove(session);
        DeleteSeries(session.Id);
    }

    public bool DeleteSession(string key, string id)
    {
        lock (_gate)
        {
            var game = _data.Games.FirstOrDefault(g => g.Key == key);
            if (game?.Sessions.FirstOrDefault(s => s.Id == id) is not { } session) return false;
            Evict(game, session);
            _data.Games.RemoveAll(g => g.Sessions.Count == 0);
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    public bool DeleteGame(string key)
    {
        lock (_gate)
        {
            var game = _data.Games.FirstOrDefault(g => g.Key == key);
            if (game is null) return false;
            foreach (var s in game.Sessions.ToList()) DeleteSeries(s.Id);
            _data.Games.Remove(game);
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    public SessionSeries? ReadSeries(string id)
    {
        try
        {
            var path = SeriesPath(id);
            return System.IO.File.Exists(path) ? JsonSerializer.Deserialize<SessionSeries>(System.IO.File.ReadAllText(path), Json) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Replaces the contents (demo data, tests) without touching the disk.</summary>
    public void Seed(IEnumerable<GameEntry> games, Func<string, SessionSeries>? series = null)
    {
        lock (_gate)
        {
            _data = new GameLogData { Games = [.. games] };
            _seed = series;
        }
    }

    private Func<string, SessionSeries>? _seed;

    /// <summary>Writes seeded data to disk, series files included (--demo-games, only ever into a scratch folder).</summary>
    public void SaveWithSeries(Dictionary<string, SessionSeries> series)
    {
        lock (_gate)
        {
            _seed = null;
            foreach (var (id, s) in series) WriteSeries(id, s);
            Save();
        }
    }

    public SessionSeries? SeriesOf(string id) => _seed?.Invoke(id) ?? ReadSeries(id);

    private string SeriesPath(string id) => Path.Combine(_seriesFolder, new string([.. id.Where(c => char.IsLetterOrDigit(c) || c == '-')]) + ".json");

    private void WriteSeries(string id, SessionSeries series)
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(_seriesFolder);
            System.IO.File.WriteAllText(SeriesPath(id), JsonSerializer.Serialize(series, Json));
        }
        catch (Exception ex)
        {
            Log.Write($"game series write failed: {ex.Message}");
        }
    }

    private void DeleteSeries(string id)
    {
        if (ReadOnly) return;
        try { System.IO.File.Delete(SeriesPath(id)); } catch { /* already gone */ }
    }

    /// <summary>Called with the lock held.</summary>
    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
            if (System.IO.File.Exists(_file)) System.IO.File.Replace(tmp, _file, _file + ".bak");
            else System.IO.File.Move(tmp, _file);
        }
        catch (Exception ex)
        {
            Log.Write($"game log save failed: {ex.Message}");
        }
    }

    public void SaveNow()
    {
        lock (_gate) Save();
    }
}

// ---- what the page shows --------------------------------------------------------------------------------------------------

public enum PlaySpan { Today, Week, Month, All }

public sealed record GameTotal(string Key, string Name, TimeSpan Time, int Sessions, long DownBytes, long UpBytes, DateTime Last);

public static class PlayStats
{
    public static DateTime From(PlaySpan span, DateTime now) => span switch
    {
        PlaySpan.Today => now.Date,
        PlaySpan.Week => now.Date.AddDays(-6),
        PlaySpan.Month => now.Date.AddDays(-29),
        _ => DateTime.MinValue,
    };

    public static List<GameTotal> Totals(IEnumerable<GameEntry> games, PlaySpan span, DateTime now)
    {
        var from = From(span, now);
        return [.. games
            .Select(g => (g, list: g.Sessions.Where(s => s.Start >= from).ToList()))
            .Where(x => x.list.Count > 0)
            .Select(x => new GameTotal(x.g.Key, x.g.Name, TimeSpan.FromSeconds(x.list.Sum(s => (double)s.Seconds)), x.list.Count,
                x.list.Sum(s => s.DownBytes), x.list.Sum(s => s.UpBytes), x.list.Max(s => s.Start)))
            .OrderByDescending(t => t.Time)];
    }
}

// ---- demo ---------------------------------------------------------------------------------------------------------------

/// <summary>Made-up sessions for screenshots and for trying the page without playing (--demo-games, demo runs).</summary>
public static class GameLogDemo
{
    private static readonly (string key, string name, int[] hours)[] Games =
    [
        ("eldenring", "ELDEN RING", [2, 3, 1, 4]),
        ("valorant", "VALORANT", [1, 1, 2, 1, 1]),
        ("cyberpunk2077", "Cyberpunk 2077", [3, 2]),
        ("rocketleague", "Rocket League", [1, 1, 1]),
    ];

    public static (List<GameEntry> games, Dictionary<string, SessionSeries> series) Make(DateTime now)
    {
        var games = new List<GameEntry>();
        var series = new Dictionary<string, SessionSeries>();
        var rnd = new Random(7);
        var day = 0;
        foreach (var (key, name, hours) in Games)
        {
            var entry = new GameEntry { Key = key, Name = name };
            foreach (var h in hours)
            {
                var start = now.Date.AddDays(-(day % 9)).AddHours(14 + day % 6).AddMinutes(rnd.Next(0, 50));
                if (start > now) start = now.AddMinutes(-h * 60 - 20);
                day += 2;
                var seconds = h * 3600 + rnd.Next(0, 1700);
                var n = 90;
                double[] Wave(double lo, double hi, double wobble) => [.. Enumerable.Range(0, n).Select(i => Math.Round(lo + (hi - lo) * (0.5 + 0.5 * Math.Sin(i / 11.0 + day)) * (0.6 + 0.4 * i / n) + rnd.NextDouble() * wobble, 1))];
                var s = new SessionSeries();
                s.Series[GameMetric.CpuTemp] = Wave(52, 76, 3);
                s.Series[GameMetric.GpuTemp] = Wave(58, 79, 2);
                s.Series[GameMetric.CpuLoad] = Wave(22, 68, 8);
                s.Series[GameMetric.GpuLoad] = Wave(55, 97, 5);
                s.Series[GameMetric.Ram] = Wave(48, 63, 1.5);
                s.Series[GameMetric.Down] = Wave(0.1, 6, 2);
                s.Series[GameMetric.Up] = Wave(0.05, 1.2, 0.4);
                if (key != "eldenring") s.Series[GameMetric.Fps] = Wave(95, 144, 6);
                var rec = new GameSessionRecord { Id = start.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + key.Length, Start = start, Seconds = seconds, ProcLooks = Math.Max(1, seconds / 360) };
                foreach (var (metric, values) in s.Series)
                    rec.Stats[metric] = new MetricStat { Min = values.Min(), Avg = Math.Round(values.Average(), 1), Max = values.Max() };
                rec.DownBytes = (long)(rec.Stats[GameMetric.Down].Avg * seconds / 8 * 1e6 * 0.4);
                rec.UpBytes = (long)(rec.Stats[GameMetric.Up].Avg * seconds / 8 * 1e6 * 0.4);
                rec.Programs =
                [
                    new BackgroundProgram { Name = "chrome", AvgCpu = 4.2, MaxCpu = 11, MaxRamMb = 1850, Seen = rec.ProcLooks },
                    new BackgroundProgram { Name = "discord", AvgCpu = 1.6, MaxCpu = 5, MaxRamMb = 420, Seen = rec.ProcLooks },
                    new BackgroundProgram { Name = "obs64", AvgCpu = 3.1, MaxCpu = 9, MaxRamMb = 610, Seen = Math.Max(1, rec.ProcLooks - 1) },
                    new BackgroundProgram { Name = "steamwebhelper", AvgCpu = 0.9, MaxCpu = 3, MaxRamMb = 300, Seen = rec.ProcLooks },
                ];
                entry.Sessions.Add(rec);
                series[rec.Id] = s;
            }
            games.Add(entry);
        }
        return (games, series);
    }
}
