namespace Shabpa.Core;

/// <summary>One expected power cut: the days it happens on (none = every day), when it starts and how long it lasts.</summary>
public sealed class OutageWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<DayOfWeek> Days { get; set; } = [];
    /// <summary>Minutes after midnight.</summary>
    public int StartMinute { get; set; } = 14 * 60;
    public int DurationMinutes { get; set; } = 120;
}

/// <summary>The power-cut guard: the person types the schedule of the cuts in their area; shortly before one Shabpa warns, then
/// hibernates the PC (through the usual cancellable countdown) so open work is saved and the disk is not cut mid-write.
/// Everything stays on this PC; the schedule is typed in by hand.</summary>
public sealed class OutageSettings
{
    public bool Enabled { get; set; }
    public int WarnMinutes { get; set; } = 10;
    /// <summary>Hibernation is requested this many minutes before the cut.</summary>
    public int HibernateMinutes { get; set; } = 2;
    /// <summary>Do not hibernate while a game is running (the warning still comes).</summary>
    public bool SkipDuringGame { get; set; } = true;
    public List<OutageWindow> Windows { get; set; } = [];
    /// <summary>Start of the cut the warning was already given for.</summary>
    public DateTime? WarnedStart { get; set; }
    /// <summary>Start of the cut the hibernation was already handled for (done, or skipped for a game).</summary>
    public DateTime? ActedStart { get; set; }
    /// <summary>When hibernation was really requested for <see cref="ActedStart"/>; null if it was skipped.</summary>
    public DateTime? ActedAt { get; set; }
    /// <summary>Start of the cut the "you are back" report was already shown for.</summary>
    public DateTime? ReportedStart { get; set; }
}

public enum OutageStep { None, Warn, Hibernate, SkippedForGame }

public sealed record OutageDue(OutageStep Step, DateTime Start, OutageWindow Window);

public static class OutageGuard
{
    /// <summary>How long after its start a cut still counts as "now" when picking the next one, so a check a second late still sees it.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(45);

    /// <summary>The first start of this window at or after <paramref name="from"/>.</summary>
    public static DateTime? NextStart(OutageWindow w, DateTime from)
    {
        for (var add = 0; add <= 8; add++)
        {
            var start = from.Date.AddDays(add).AddMinutes(w.StartMinute);
            if (start < from) continue;
            if (w.Days.Count == 0 || w.Days.Contains(start.DayOfWeek)) return start;
        }
        return null;
    }

    /// <summary>What to do now, if anything: the warning, the hibernation, or a note that a game kept it from hibernating.
    /// The same cut never triggers a step twice (the caller records <see cref="OutageSettings.WarnedStart"/> and
    /// <see cref="OutageSettings.ActedStart"/>).</summary>
    public static OutageDue? Check(OutageSettings s, DateTime now, bool gameRunning)
    {
        if (!s.Enabled || s.Windows.Count == 0) return null;
        OutageDue? best = null;
        foreach (var w in s.Windows)
        {
            if (w.DurationMinutes <= 0 || NextStart(w, now - Grace) is not { } start) continue;
            var left = start - now;
            OutageStep step;
            if (left <= TimeSpan.FromMinutes(s.HibernateMinutes) && s.ActedStart != start)
                step = gameRunning && s.SkipDuringGame ? OutageStep.SkippedForGame : OutageStep.Hibernate;
            else if (left <= TimeSpan.FromMinutes(s.WarnMinutes) && s.WarnedStart != start && s.ActedStart != start)
                step = OutageStep.Warn;
            else continue;
            if (best is null || start < best.Start) best = new OutageDue(step, start, w);
        }
        return best;
    }

    /// <summary>The next cut coming up, for the page header.</summary>
    public static (DateTime Start, OutageWindow Window)? Upcoming(OutageSettings s, DateTime now)
    {
        (DateTime, OutageWindow)? best = null;
        foreach (var w in s.Windows)
            if (NextStart(w, now) is { } start && (best is null || start < best.Value.Item1)) best = (start, w);
        return best;
    }

    /// <summary>True when the PC was hibernated for a cut and has not been told about its return yet. The clock jumping forward
    /// by a minute or more between two checks (<paramref name="gap"/>) after the request means the PC slept and is back; a countdown
    /// the person cancelled leaves no such jump.</summary>
    public static bool ReturnReportDue(OutageSettings s, DateTime now, TimeSpan gap) =>
        s.ActedAt is { } at && s.ActedStart is { } start && s.ReportedStart != start && gap >= TimeSpan.FromSeconds(60) && now > at;

    /// <summary>How long the PC was away since the hibernation was requested.</summary>
    public static TimeSpan Away(OutageSettings s, DateTime now) => s.ActedAt is { } at ? now - at : TimeSpan.Zero;
}
