using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Shabpa.Core;

/// <summary>Frame pacing figures worked out from the times (in milliseconds) at which frames were presented.</summary>
public sealed record FrameStats(double Fps, double AverageFrameMs, double WorstFrameMs, double OnePercentLowFps, int Frames)
{
    public static readonly FrameStats None = new(0, 0, 0, 0, 0);

    /// <summary>Needs at least two presents. "1% low" is the frame rate of the slowest one percent of frames.</summary>
    public static FrameStats From(IReadOnlyList<double> presentTimesMs)
    {
        if (presentTimesMs.Count < 2) return None;
        var gaps = new List<double>(presentTimesMs.Count - 1);
        for (var i = 1; i < presentTimesMs.Count; i++)
        {
            var gap = presentTimesMs[i] - presentTimesMs[i - 1];
            if (gap > 0) gaps.Add(gap);
        }
        if (gaps.Count == 0) return None;

        var span = presentTimesMs[^1] - presentTimesMs[0];
        var average = span / gaps.Count;
        var sorted = gaps.Order().ToList();
        var worst = sorted[^1];
        // The frame time that 99 % of frames beat; its reciprocal is the 1 % low.
        var p99 = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.99) - 1)];
        return new FrameStats(1000.0 / average, average, worst, 1000.0 / p99, gaps.Count + 1);
    }
}

/// <summary>Counts frames per process from the Windows graphics trace events (the same source PresentMon uses).
/// Nothing is injected into any game, so anti-cheat has nothing to object to. Needs admin.</summary>
public sealed class FpsMeter : IDisposable
{
    private const string SessionName = "Shabpa-Fps";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    /// <summary>Present::Start of the DXGI provider.</summary>
    private const int DxgiPresentStart = 42;
    private const int D3D9Present = 1;
    private readonly TraceEventSession _session;
    private readonly PresentTracks _tracks = new();

    private FpsMeter(TraceEventSession session) => _session = session;

    public static FpsMeter? TryStart(out string? error)
    {
        error = null;
        if (!Elevation.IsElevated)
        {
            error = "needs-admin";
            return null;
        }
        TraceEventSession? session = null;
        try
        {
            session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(Dxgi, TraceEventLevel.Verbose, ulong.MaxValue);
            session.EnableProvider(D3D9, TraceEventLevel.Verbose, ulong.MaxValue);
            var meter = new FpsMeter(session);
            var source = session.Source;
            // AllEvents can deliver an undecoded event. Ask TraceEvent to resolve the installed provider manifests.
            source.Dynamic.AddCallbackForProviderEvents(
                (provider, _) => provider is "Microsoft-Windows-DXGI" or "Microsoft-Windows-D3D9"
                    ? EventFilterResponse.AcceptEvent : EventFilterResponse.RejectProvider,
                meter.OnEvent);
            new Thread(() =>
            {
                try { source.Process(); }
                catch (Exception ex) { Log.Write($"fps trace stopped: {ex.Message}"); }
            }) { IsBackground = true, Name = "fps-trace", Priority = ThreadPriority.BelowNormal }.Start();
            return meter;
        }
        catch (Exception ex)
        {
            try { session?.Dispose(); }
            catch (Exception cleanup) { Log.Write($"fps trace cleanup: {cleanup.Message}"); }
            Log.Write($"fps trace failed: {ex.Message}");
            error = ex.Message;
            return null;
        }
    }

    private void OnEvent(TraceEvent e)
    {
        var dxgi = e.ProviderGuid == Dxgi;
        var isPresent = dxgi ? (int)e.ID == DxgiPresentStart
            : e.ProviderGuid == D3D9 && (int)e.ID == D3D9Present;
        if (!isPresent || e.ProcessID == Environment.ProcessId) return;

        _tracks.Record(e.ProcessID, dxgi, e.PayloadByName(dxgi ? "pIDXGISwapChain" : "pSwapchain"),
            e.PayloadByName("Flags"), e.TimeStampRelativeMSec);
    }

    public FrameStats Stats(int pid, double windowSeconds = 4) => _tracks.Stats(pid, windowSeconds);

    public (int pid, FrameStats stats)? Busiest(Func<int, bool>? allow = null) => _tracks.Busiest(allow);

    public void Dispose()
    {
        try { _session.Dispose(); }
        catch { /* already stopped */ }
    }
}

/// <summary>Recent application presents grouped by process, runtime and swap chain.</summary>
internal sealed class PresentTracks
{
    private const int KeepFrames = 400;
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(3);

    private sealed class Track
    {
        public readonly Queue<double> Times = new();
        public long LastSeen;
    }

    private readonly object _gate = new();
    // One track per (process, swap chain): a game also has overlay swap chains (chat, store, capture) that present on their own
    // schedule and would otherwise be added on top of the game's own frames.
    private readonly Dictionary<(int pid, bool dxgi, ulong chain), Track> _tracks = [];

    public void Record(int pid, bool dxgi, object? swapChain, object? flags, double timeMs)
    {
        // TraceEvent can expose an ETW pointer as IntPtr or an unsigned address, depending on the manifest.
        var chain = swapChain switch
        {
            IntPtr address => unchecked((ulong)address.ToInt64()),
            UIntPtr address => address.ToUInt64(),
            ulong address => address,
            long address => unchecked((ulong)address),
            uint address => address,
            int address => unchecked((uint)address),
            _ => 0UL,
        };
        if (chain == 0) return;
        if (dxgi)
        {
            // DXGI_PRESENT_TEST only probes whether a present would succeed.
            if (flags is not (uint or int)) return;
            if ((Convert.ToInt64(flags) & 0x1) != 0) return;
        }

        lock (_gate)
        {
            var key = (pid, dxgi, chain);
            if (!_tracks.TryGetValue(key, out var track)) _tracks[key] = track = new Track();
            var now = Environment.TickCount64;
            if (now - track.LastSeen > Stale.TotalMilliseconds) track.Times.Clear();
            track.Times.Enqueue(timeMs);
            while (track.Times.Count > KeepFrames) track.Times.Dequeue();
            track.LastSeen = now;
        }
    }

    /// <summary>Frame figures from the swap chain with the largest sample count in the recent window.</summary>
    public FrameStats Stats(int pid, double windowSeconds = 4)
    {
        lock (_gate)
        {
            var best = FrameStats.None;
            foreach (var (key, track) in _tracks)
            {
                if (key.pid != pid || Environment.TickCount64 - track.LastSeen > Stale.TotalMilliseconds || track.Times.Count == 0) continue;
                var newest = track.Times.Last();
                var stats = FrameStats.From(track.Times.Where(t => newest - t <= windowSeconds * 1000).ToList());
                if (stats.Frames > best.Frames) best = stats;
            }
            return best;
        }
    }

    /// <summary>The highest-rate process that passes the caller's filter.</summary>
    public (int pid, FrameStats stats)? Busiest(Func<int, bool>? allow = null)
    {
        List<int> pids;
        lock (_gate)
        {
            var now = Environment.TickCount64;
            foreach (var old in _tracks.Where(t => now - t.Value.LastSeen > 30_000).Select(t => t.Key).ToList()) _tracks.Remove(old);
            pids = [.. _tracks.Keys.Select(k => k.pid).Distinct()];
        }
        (int, FrameStats)? best = null;
        foreach (var pid in pids)
        {
            if (allow is not null && !allow(pid)) continue;
            var stats = Stats(pid);
            if (stats.Frames < 5) continue;
            if (best is null || stats.Fps > best.Value.Item2.Fps) best = (pid, stats);
        }
        return best;
    }
}
