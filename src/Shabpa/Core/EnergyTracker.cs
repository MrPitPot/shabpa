namespace Shabpa.Core;

public sealed record EnergySummary(
    IReadOnlyList<EnergyDay> Days,
    double TotalKwh,
    double GamingKwh,
    double ActiveKwh,
    double IdleKwh,
    double Coverage);

/// <summary>Integrates power samples into per-day energy, split by how the PC was being used.</summary>
public sealed class EnergyTracker(List<EnergyDay> days)
{
    public const int Window = 30;
    /// <summary>The user counts as away after this long without input.</summary>
    public static readonly TimeSpan ActiveWithin = TimeSpan.FromMinutes(5);
    /// <summary>Samples further apart than this are not joined: the machine was asleep or the app was stuck.</summary>
    private static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(20);

    private readonly object _gate = new();
    private DateTime? _last;

    public void Add(SensorSnapshot s)
    {
        lock (_gate)
        {
            var prev = _last;
            _last = s.Time;
            if (prev is null) return;
            var step = s.Time - prev.Value;
            if (step <= TimeSpan.Zero || step > MaxStep) return;

            var date = DateOnly.FromDateTime(s.Time);
            var day = days.Find(d => d.Date == date);
            if (day is null)
            {
                day = new EnergyDay { Date = date };
                days.Add(day);
                days.RemoveAll(d => d.Date < date.AddDays(-90));
            }

            var wh = s.TotalWatts * step.TotalHours;
            if (s.GameRunning) day.GamingWh += wh;
            else if (s.IdleSeconds < ActiveWithin.TotalSeconds) day.ActiveWh += wh;
            else day.IdleWh += wh;
            day.MeasuredSeconds += step.TotalSeconds;
        }
    }

    /// <summary>The last 30 days, oldest first, with empty days filled in.</summary>
    public EnergySummary Summarize(DateTime now)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(now);
            var window = Enumerable.Range(0, Window)
                .Select(i => today.AddDays(i - Window + 1))
                .Select(date => days.Find(d => d.Date == date) ?? new EnergyDay { Date = date })
                .ToList();
            return new EnergySummary(
                window,
                window.Sum(d => d.TotalWh) / 1000,
                window.Sum(d => d.GamingWh) / 1000,
                window.Sum(d => d.ActiveWh) / 1000,
                window.Sum(d => d.IdleWh) / 1000,
                Math.Clamp(window.Sum(d => d.MeasuredSeconds) / TimeSpan.FromDays(Window).TotalSeconds, 0, 1));
        }
    }
}
