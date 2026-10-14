using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Shabpa.Core;

public sealed class UsageApp
{
    public string Title { get; set; } = "";
    public long Down { get; set; }
    public long Up { get; set; }
    public long Total => Down + Up;
}

public sealed class UsageDay
{
    public DateOnly Date { get; set; }
    public Dictionary<string, UsageApp> Apps { get; set; } = [];
    /// <summary>Bytes received per hour of the day (all apps).</summary>
    public long[] HourDown { get; set; } = new long[24];
    public long[] HourUp { get; set; } = new long[24];

    public long Down => Apps.Values.Sum(a => a.Down);
    public long Up => Apps.Values.Sum(a => a.Up);
}

public sealed record UsageRow(string Key, string Title, long Down, long Up)
{
    public long Total => Down + Up;
}

/// <summary>Per-application internet use, kept day by day (and hour by hour for totals) so it can be
/// looked at over any range. Fed with the running totals the traffic meter already keeps.</summary>
public sealed class UsageHistory
{
    /// <summary>Days kept on disk.</summary>
    public const int KeepDays = 400;
    /// <summary>Apps that moved less than this in a finished day are folded into "other" when saving.</summary>
    private const long TinyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly Dictionary<DateOnly, UsageDay> _days = [];
    private readonly Dictionary<string, (long down, long up)> _seen = [];
    private DateOnly _seenDate;

    public int Days
    {
        get { lock (_gate) return _days.Count; }
    }

    /// <summary>Adds what each app transferred since the previous call. <paramref name="apps"/> carries
    /// the running per-day totals of the traffic meter, which restart at midnight.</summary>
    public void Update(DateTime now, IEnumerable<AppTraffic> apps)
    {
        var today = DateOnly.FromDateTime(now);
        lock (_gate)
        {
            if (today != _seenDate)
            {
                _seen.Clear();
                _seenDate = today;
            }
            if (!_days.TryGetValue(today, out var day))
                _days[today] = day = new UsageDay { Date = today };

            foreach (var app in apps)
            {
                _seen.TryGetValue(app.Key, out var last);
                // A smaller total than last time means the meter restarted: everything it has now is new.
                var down = app.DownBytes >= last.down ? app.DownBytes - last.down : app.DownBytes;
                var up = app.UpBytes >= last.up ? app.UpBytes - last.up : app.UpBytes;
                _seen[app.Key] = (app.DownBytes, app.UpBytes);
                if (down == 0 && up == 0) continue;

                if (!day.Apps.TryGetValue(app.Key, out var entry)) day.Apps[app.Key] = entry = new UsageApp();
                if (app.Title.Length > 0) entry.Title = app.Title;
                entry.Down += down;
                entry.Up += up;
                day.HourDown[now.Hour] += down;
                day.HourUp[now.Hour] += up;
            }
        }
    }

    /// <summary>Apps ranked by use over [from, to], inclusive.</summary>
    public List<UsageRow> Range(DateOnly from, DateOnly to)
    {
        lock (_gate)
        {
            var rows = new Dictionary<string, (string title, long down, long up)>();
            foreach (var (date, day) in _days)
            {
                if (date < from || date > to) continue;
                foreach (var (key, app) in day.Apps)
                {
                    rows.TryGetValue(key, out var r);
                    rows[key] = (app.Title.Length > 0 ? app.Title : r.title, r.down + app.Down, r.up + app.Up);
                }
            }
            return rows.Select(r => new UsageRow(r.Key, r.Value.title.Length > 0 ? r.Value.title : r.Key, r.Value.down, r.Value.up))
                .OrderByDescending(r => r.Total).ToList();
        }
    }

    /// <summary>One value per calendar day in [from, to] (zero for days with no data).</summary>
    public List<(DateOnly date, long down, long up)> Daily(DateOnly from, DateOnly to, string? appKey = null)
    {
        lock (_gate)
        {
            var result = new List<(DateOnly, long, long)>();
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!_days.TryGetValue(d, out var day)) { result.Add((d, 0, 0)); continue; }
                if (appKey is null) result.Add((d, day.Down, day.Up));
                else if (day.Apps.TryGetValue(appKey, out var app)) result.Add((d, app.Down, app.Up));
                else result.Add((d, 0, 0));
            }
            return result;
        }
    }

    /// <summary>Per-hour totals for one day.</summary>
    public (long[] down, long[] up) Hourly(DateOnly date)
    {
        lock (_gate)
        {
            return _days.TryGetValue(date, out var day) ? ([.. day.HourDown], [.. day.HourUp]) : (new long[24], new long[24]);
        }
    }

    public (long down, long up) Total(DateOnly from, DateOnly to)
    {
        lock (_gate)
        {
            long down = 0, up = 0;
            foreach (var (date, day) in _days)
            {
                if (date < from || date > to) continue;
                down += day.Down;
                up += day.Up;
            }
            return (down, up);
        }
    }

    public DateOnly? FirstDay
    {
        get { lock (_gate) return _days.Count == 0 ? null : _days.Keys.Min(); }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _days.Clear();
            _seen.Clear();
        }
    }

    // ---- persistence ------------------------------------------------------------------------------

    public void Save(string file)
    {
        string json;
        lock (_gate)
        {
            var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-KeepDays);
            foreach (var old in _days.Keys.Where(d => d < cutoff).ToList()) _days.Remove(old);
            var export = _days.Values.OrderBy(d => d.Date).Select(Compact).ToList();
            json = JsonSerializer.Serialize(export, Json);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, file, true);
        }
        catch (Exception ex)
        {
            Log.Write($"usage save failed: {ex.Message}");
        }
    }

    /// <summary>Small apps of a finished day are merged into "other"; today's data is kept as is.</summary>
    private static UsageDay Compact(UsageDay day)
    {
        if (day.Date >= DateOnly.FromDateTime(DateTime.Now)) return day;
        var copy = new UsageDay { Date = day.Date, HourDown = day.HourDown, HourUp = day.HourUp };
        var other = new UsageApp { Title = L.T("سایر", "Other") };
        foreach (var (key, app) in day.Apps)
        {
            if (key != TrafficMeter.Other && app.Total >= TinyBytes)
            {
                copy.Apps[key] = app;
            }
            else
            {
                other.Down += app.Down;
                other.Up += app.Up;
            }
        }
        if (other.Total > 0) copy.Apps[TrafficMeter.Other] = other;
        return copy;
    }

    public void Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            var days = JsonSerializer.Deserialize<List<UsageDay>>(File.ReadAllText(file), Json) ?? [];
            lock (_gate)
            {
                foreach (var day in days)
                {
                    if (day.HourDown.Length != 24) day.HourDown = new long[24];
                    if (day.HourUp.Length != 24) day.HourUp = new long[24];
                    _days[day.Date] = day;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"usage load failed: {ex.Message}");
        }
    }

    /// <summary>Plausible month and a half of data for --demo runs and screenshots.</summary>
    public void FillDemo(DateTime now)
    {
        var random = new Random(11);
        (string key, string title, double gb)[] apps =
        [
            ("steam", "Steam Client", 6.5), ("chrome", "Google Chrome", 2.1), ("discord", "Discord", 0.7), ("onedrive", "Microsoft OneDrive", 0.9),
            ("spotify", "Spotify", 0.45), ("telegram", "Telegram Desktop", 0.3), ("svchost", "Host Process for Windows Services", 0.25),
            ("cs2", "Counter-Strike 2", 0.15),
        ];
        lock (_gate)
        {
            _days.Clear();
            for (var i = 0; i < 45; i++)
            {
                var date = DateOnly.FromDateTime(now).AddDays(-i);
                var day = new UsageDay { Date = date };
                foreach (var (key, title, gb) in apps)
                {
                    var wobble = 0.15 + 1.5 * random.NextDouble();
                    var total = (long)(gb * wobble * (1 << 30) / 3);
                    day.Apps[key] = new UsageApp { Title = title, Down = (long)(total * 0.9), Up = (long)(total * 0.1) };
                }
                double down = day.Down, up = day.Up;
                var weights = new double[24];
                for (var h = 0; h < 24; h++) weights[h] = h >= 18 ? 3.0 : h >= 8 ? 1.5 : 0.2;
                var sum = weights.Sum();
                for (var h = 0; h < 24; h++)
                {
                    day.HourDown[h] = (long)(down * weights[h] / sum);
                    day.HourUp[h] = (long)(up * weights[h] / sum);
                }
                _days[date] = day;
            }
        }
    }
}
