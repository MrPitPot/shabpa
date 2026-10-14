namespace Shabpa.Core;

public static class Schedule
{
    /// <summary>The first moment strictly after <paramref name="after"/> that falls on one of
    /// <paramref name="days"/> (empty = any day) at the given time of day.</summary>
    public static DateTime Next(int hour, int minute, IReadOnlyCollection<DayOfWeek> days, DateTime after)
    {
        var candidate = after.Date.AddHours(hour).AddMinutes(minute);
        for (var i = 0; i < 8; i++)
        {
            if (candidate > after && (days.Count == 0 || days.Contains(candidate.DayOfWeek)))
                return candidate;
            candidate = candidate.AddDays(1);
        }
        return candidate;
    }
}

/// <summary>The list of pending power commands. Pure logic; the caller supplies the clock.</summary>
public sealed class CommandQueue(List<ScheduledCommand> items)
{
    /// <summary>A command that should have run while the app was not running is dropped rather than
    /// fired late: nobody wants the PC to shut down the moment they log in.</summary>
    public static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(2);

    public IReadOnlyList<ScheduledCommand> Items => items;

    public ScheduledCommand? Next => items.MinBy(c => c.DueAt);

    public ScheduledCommand Add(PowerAction action, DateTime dueAt, IEnumerable<DayOfWeek>? repeat = null, string source = "")
    {
        var cmd = new ScheduledCommand { Action = action, DueAt = dueAt, RepeatDays = repeat?.Distinct().ToList() ?? [], Source = source };
        items.Add(cmd);
        return cmd;
    }

    public bool Remove(Guid id) => items.RemoveAll(c => c.Id == id) > 0;

    public int Clear()
    {
        var n = items.Count;
        items.Clear();
        return n;
    }

    /// <summary>Returns the commands to run now and updates the list (one-shots are removed, repeating
    /// ones move to their next occurrence). <paramref name="missed"/> receives commands that were skipped.</summary>
    public List<ScheduledCommand> TakeDue(DateTime now, List<ScheduledCommand>? missed = null)
    {
        var due = new List<ScheduledCommand>();
        foreach (var cmd in items.Where(c => c.DueAt <= now).OrderBy(c => c.DueAt).ToList())
        {
            if (now - cmd.DueAt > MissedAfter) missed?.Add(cmd);
            else due.Add(cmd);

            if (cmd.Repeats)
                cmd.DueAt = Schedule.Next(cmd.DueAt.Hour, cmd.DueAt.Minute, cmd.RepeatDays, now);
            else
                items.Remove(cmd);
        }
        return due;
    }

    /// <summary>Pushes one command back, e.g. from the countdown's "later" button.</summary>
    public ScheduledCommand Postpone(PowerAction action, DateTime now, TimeSpan by, string source) =>
        Add(action, now + by, null, source);
}
