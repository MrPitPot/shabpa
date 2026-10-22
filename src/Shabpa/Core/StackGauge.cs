using System.Diagnostics;

namespace Shabpa.Core;

/// <summary>A measuring aid for the navigation probe: at a few chosen places on the window thread it can record how many call
/// frames are on the stack, so "how deep does a page switch go" is a number and not a guess. Off by default (then a call costs
/// one boolean read); only <c>--nav-stress</c> and the tests switch it on.</summary>
public static class StackGauge
{
    public static volatile bool Enabled;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Peaks = [];

    public static void Sample(string site)
    {
        if (!Enabled) return;
        var frames = new StackTrace(false).FrameCount;
        lock (Gate) { if (!Peaks.TryGetValue(site, out var old) || frames > old) Peaks[site] = frames; }
    }

    public static int Peak(string site) { lock (Gate) return Peaks.TryGetValue(site, out var v) ? v : 0; }

    public static int PeakAll() { lock (Gate) return Peaks.Count == 0 ? 0 : Peaks.Values.Max(); }

    public static string Report() { lock (Gate) return string.Join(", ", Peaks.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}")); }

    public static void Reset() { lock (Gate) Peaks.Clear(); }
}
