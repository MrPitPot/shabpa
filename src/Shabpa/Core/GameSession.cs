namespace Shabpa.Core;

/// <summary>How a gaming session went, worked out from the telemetry samples taken during it.</summary>
public sealed record GameReport(string Game, DateTime Start, TimeSpan Duration, double AvgFps, double Low1Fps, double AvgPing, double MaxPing,
    double MaxGpuTemp, double MaxHotspot, double AvgGpuLoad, long DownBytes, long UpBytes)
{
    public bool HasFrames => AvgFps > 0;
}

/// <summary>Watches the samples for a game starting and stopping. Feed it every sample; it hands back a
/// <see cref="GameReport"/> the moment a session that lasted long enough ends.</summary>
public sealed class GameSessionTracker
{
    /// <summary>Shorter sessions (a launcher flashing full screen, a video) are not worth a report.</summary>
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(3);

    private string _game = "";
    private DateTime _start;
    private int _count, _fpsCount, _pingCount;
    private double _fpsSum, _lowMin = double.MaxValue, _pingSum, _pingMax, _tempMax, _hotMax, _loadSum;
    private long _downStart, _upStart, _downLast, _upLast;

    public bool InSession => _game.Length > 0;

    /// <param name="downBytes">Total bytes the game process has received so far today (a running total).</param>
    /// <param name="upBytes">Total bytes it has sent so far today.</param>
    public GameReport? Update(TelemetrySample sample, long downBytes, long upBytes)
    {
        if (sample.InGame && sample.Game.Length > 0)
        {
            if (!InSession || sample.Game != _game)
            {
                var finished = InSession ? Finish(sample.Time) : null;
                Begin(sample, downBytes, upBytes);
                return finished;
            }
            Add(sample, downBytes, upBytes);
            return null;
        }
        return InSession ? Finish(sample.Time) : null;
    }

    private void Begin(TelemetrySample s, long down, long up)
    {
        _game = s.Game;
        _start = s.Time;
        _count = _fpsCount = _pingCount = 0;
        _fpsSum = _pingSum = _pingMax = _tempMax = _hotMax = _loadSum = 0;
        _lowMin = double.MaxValue;
        _downStart = _downLast = down;
        _upStart = _upLast = up;
        Add(s, down, up);
    }

    private void Add(TelemetrySample s, long down, long up)
    {
        _count++;
        if (s.Fps > 0)
        {
            _fpsCount++;
            _fpsSum += s.Fps;
            if (s.Low1Fps > 0) _lowMin = Math.Min(_lowMin, s.Low1Fps);
        }
        if (s.PingMs is { } ping)
        {
            _pingCount++;
            _pingSum += ping;
            _pingMax = Math.Max(_pingMax, ping);
        }
        if (s.Gpu.TempC is { } t) _tempMax = Math.Max(_tempMax, t);
        if (s.Gpu.HotspotC is { } h) _hotMax = Math.Max(_hotMax, h);
        _loadSum += s.Gpu.LoadPercent ?? 0;
        if (down >= _downLast) _downLast = down;
        if (up >= _upLast) _upLast = up;
    }

    private GameReport? Finish(DateTime end)
    {
        var report = new GameReport(_game, _start, end - _start,
            _fpsCount > 0 ? _fpsSum / _fpsCount : 0, _lowMin == double.MaxValue ? 0 : _lowMin,
            _pingCount > 0 ? _pingSum / _pingCount : 0, _pingMax, _tempMax, _hotMax,
            _count > 0 ? _loadSum / _count : 0, Math.Max(0, _downLast - _downStart), Math.Max(0, _upLast - _upStart));
        _game = "";
        return report.Duration >= MinimumLength ? report : null;
    }
}

/// <summary>Tracks the month's data use against a limit the user set, and says when 80 % and 100 % are crossed.</summary>
public static class DataBudget
{
    public sealed record Check(double UsedGb, double Fraction, int Level);

    /// <summary>Level 0 below 80 %, 1 from 80 %, 2 from 100 %.</summary>
    public static Check Evaluate(long usedBytes, double limitGb)
    {
        var usedGb = usedBytes / 1073741824.0;
        if (limitGb <= 0) return new Check(usedGb, 0, 0);
        var fraction = usedGb / limitGb;
        return new Check(usedGb, fraction, fraction >= 1 ? 2 : fraction >= 0.8 ? 1 : 0);
    }
}
