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

    /// <summary>The moment a day + time picker describes. With no day chosen it is the next time the clock reads
    /// <paramref name="hour"/>:<paramref name="minute"/> (today, or tomorrow once that has passed); with a day it is
    /// exactly that day, even if the moment is already in the past (the caller rejects it).</summary>
    public static DateTime On(DateTime? day, int hour, int minute, DateTime now) =>
        day is { } d ? d.Date.AddHours(hour).AddMinutes(minute) : Next(hour, minute, [], now);
}

/// <summary>Rules for a two-digit hour or minute box: what may be typed and when the box is full.</summary>
public static class ClockField
{
    /// <summary>True when <paramref name="text"/> (Latin digits) is an acceptable value or partial value:
    /// at most two digits and not above <paramref name="max"/>.</summary>
    public static bool Accepts(string text, int max) =>
        text.Length <= 2 && text.All(char.IsAsciiDigit) && (text.Length == 0 || int.Parse(text) <= max);

    /// <summary>True once nothing more can be typed: two digits, or one digit that cannot start a valid
    /// two-digit value (e.g. «4» for an hour), so focus can move on to the next box.</summary>
    public static bool Complete(string text, int max) =>
        Accepts(text, max) && (text.Length == 2 || (text.Length == 1 && (text[0] - '0') * 10 > max));
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

    /// <summary>Two one-shot commands for the same action this close together are the same request
    /// (a double click, the same shortcut pressed twice).</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(90);

    /// <summary>Adds the command unless an equivalent one is already waiting. False = it was a duplicate.</summary>
    public bool TryAdd(PowerAction action, DateTime dueAt, IEnumerable<DayOfWeek>? repeat, string source, out ScheduledCommand command)
    {
        var days = repeat?.Distinct().OrderBy(d => d).ToList() ?? [];
        var existing = items.FirstOrDefault(c => c.Action == action
            && c.RepeatDays.OrderBy(d => d).SequenceEqual(days)
            && (days.Count > 0 ? c.DueAt.TimeOfDay == dueAt.TimeOfDay : (c.DueAt - dueAt).Duration() <= DuplicateWindow));
        if (existing is not null)
        {
            command = existing;
            return false;
        }
        command = Add(action, dueAt, days, source);
        return true;
    }

    public bool Remove(Guid id) => items.RemoveAll(c => c.Id == id) > 0;

    /// <summary>Removes every one-shot command (weekly schedules stay). Used once a power action has run:
    /// a PC that has just been turned off or put to sleep must not be "shut down again" half an hour later
    /// after the user switched it back on.</summary>
    public List<ScheduledCommand> ClearOneShots()
    {
        var gone = items.Where(c => !c.Repeats).ToList();
        items.RemoveAll(c => !c.Repeats);
        return gone;
    }

    /// <summary>Everything that came due in (<paramref name="from"/>, <paramref name="to"/>] while nobody was watching
    /// (app closed, PC off or asleep) is skipped, never fired late; weekly commands move to their next occurrence.</summary>
    public List<ScheduledCommand> DropMissed(DateTime from, DateTime to)
    {
        var missed = new List<ScheduledCommand>();
        foreach (var cmd in items.Where(c => c.DueAt > from && c.DueAt <= to).OrderBy(c => c.DueAt).ToList())
        {
            missed.Add(cmd);
            if (cmd.Repeats) cmd.DueAt = Schedule.Next(cmd.DueAt.Hour, cmd.DueAt.Minute, cmd.RepeatDays, to);
            else items.Remove(cmd);
        }
        return missed;
    }

    /// <summary>Like <see cref="DropMissed"/> for everything already overdue (used when the app starts).</summary>
    public List<ScheduledCommand> DropOverdue(DateTime now) => DropMissed(DateTime.MinValue, now);

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
