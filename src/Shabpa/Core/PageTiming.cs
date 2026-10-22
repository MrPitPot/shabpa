using System.Text;

namespace Shabpa.Core;

/// <summary>How long the steps that run on the window thread take (a page's Refresh, OnTick and OnSecond, the page swap, the hub tick).
/// A step that is slow enough to be felt is written to the log once with its name, so a stall in a real log names its page and step;
/// the probe (<c>--page-timing</c>) prints the worst and average of every step.</summary>
public static class PageTiming
{
    /// <summary>A step slower than this is logged (at most once per 30 s per step).</summary>
    public const double SlowMs = 250;

    public readonly record struct Stat(int Count, double TotalMs, double MaxMs)
    {
        public double AvgMs => Count == 0 ? 0 : TotalMs / Count;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Stat> Stats = [];
    private static readonly Dictionary<string, long> LoggedAt = [];

    public static void Record(string step, double ms)
    {
        var slow = false;
        lock (Gate)
        {
            Stats.TryGetValue(step, out var s);
            Stats[step] = new Stat(s.Count + 1, s.TotalMs + ms, Math.Max(s.MaxMs, ms));
            if (ms >= SlowMs && (!LoggedAt.TryGetValue(step, out var last) || Environment.TickCount64 - last > 30000))
            {
                LoggedAt[step] = Environment.TickCount64;
                slow = true;
            }
        }
        if (slow) Log.Write($"slow ui step: {step} took {ms:0} ms");
    }

    public static void Reset()
    {
        lock (Gate) { Stats.Clear(); LoggedAt.Clear(); }
    }

    public static IReadOnlyDictionary<string, Stat> Snapshot()
    {
        lock (Gate) return new Dictionary<string, Stat>(Stats);
    }

    /// <summary>Steps whose slowest run passed <paramref name="overMs"/>, worst first.</summary>
    public static string Report(double overMs = 50)
    {
        var sb = new StringBuilder();
        foreach (var (step, s) in Snapshot().Where(p => p.Value.MaxMs >= overMs).OrderByDescending(p => p.Value.MaxMs))
            sb.Append($"{step} max {s.MaxMs:0} ms avg {s.AvgMs:0.0} ms x{s.Count}; ");
        return sb.ToString();
    }
}
