namespace Shabpa.Core;

/// <summary>One thing to do. Kept on this PC in state.json; nothing here is ever sent anywhere.</summary>
public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>Local date and time, or null for a task without a deadline.</summary>
    public DateTime? Due { get; set; }
    /// <summary>Minutes before <see cref="Due"/> to remind; -1 for no reminder, 0 for at the deadline.</summary>
    public int RemindBefore { get; set; } = -1;
    /// <summary>Set when "remind me later" was used: the reminder fires at this time instead.</summary>
    public DateTime? SnoozedUntil { get; set; }
    public DateTime? RemindedAt { get; set; }
    public bool Important { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public bool Done { get; set; }
    public DateTime? DoneAt { get; set; }

    /// <summary>When the reminder is due, or null if there is none.</summary>
    public DateTime? ReminderTime => SnoozedUntil ?? (Due is { } due && RemindBefore >= 0 ? due.AddMinutes(-RemindBefore) : null);
}

public enum TaskView { Today, Upcoming, NoDate, Done }

/// <summary>The task list and the rules around it. Pure logic over a list, so it is easy to test; the page and the
/// hub only call it.</summary>
public sealed class TaskBook
{
    public const int MaxTitle = 200;
    public const int MaxNotes = 2000;
    public const int MaxTasks = 5000;

    /// <summary>A reminder that is this old when it is first noticed (the PC was off) is not announced as if it were
    /// fresh; the task just shows as overdue.</summary>
    public static readonly TimeSpan StaleReminder = TimeSpan.FromHours(6);

    private readonly List<TodoItem> _items;

    public TaskBook(List<TodoItem> items) => _items = items;

    public IReadOnlyList<TodoItem> All => _items;

    public event Action? Changed;

    public static string Clean(string? text, int max)
    {
        if (text is null) return "";
        var cleaned = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray()).Trim();
        return cleaned.Length > max ? cleaned[..max] : cleaned;
    }

    /// <summary>Adds a task; null if the title is empty or the list is full.</summary>
    public TodoItem? Add(string title, DateTime? due = null, int remindBefore = -1, string category = "", string notes = "", bool important = false, DateTime? now = null)
    {
        title = Clean(title, MaxTitle).Replace('\n', ' ').Replace('\r', ' ');
        if (title.Length == 0 || _items.Count >= MaxTasks) return null;
        var item = new TodoItem
        {
            Title = title, Due = due, RemindBefore = due is null ? -1 : Math.Clamp(remindBefore, -1, 7 * 24 * 60),
            Category = Clean(category, 40).Replace('\n', ' '), Notes = Clean(notes, MaxNotes), Important = important, Created = now ?? DateTime.Now,
        };
        _items.Add(item);
        Changed?.Invoke();
        return item;
    }

    public bool Update(Guid id, string title, DateTime? due, int remindBefore, string category, string notes, bool important)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        title = Clean(title, MaxTitle).Replace('\n', ' ').Replace('\r', ' ');
        if (item is null || title.Length == 0) return false;
        var dueChanged = item.Due != due || item.RemindBefore != remindBefore;
        item.Title = title;
        item.Due = due;
        item.RemindBefore = due is null ? -1 : Math.Clamp(remindBefore, -1, 7 * 24 * 60);
        item.Category = Clean(category, 40).Replace('\n', ' ');
        item.Notes = Clean(notes, MaxNotes);
        item.Important = important;
        if (dueChanged)
        {
            item.RemindedAt = null;
            item.SnoozedUntil = null;
        }
        Changed?.Invoke();
        return true;
    }

    public bool Complete(Guid id, DateTime? now = null)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.Done) return false;
        item.Done = true;
        item.DoneAt = now ?? DateTime.Now;
        Changed?.Invoke();
        return true;
    }

    public bool Reopen(Guid id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null || !item.Done) return false;
        item.Done = false;
        item.DoneAt = null;
        item.RemindedAt = null; // its reminder, if still in the future, works again
        item.SnoozedUntil = null;
        Changed?.Invoke();
        return true;
    }

    public bool Remove(Guid id)
    {
        var removed = _items.RemoveAll(i => i.Id == id) > 0;
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>Removes finished tasks completed before <paramref name="before"/>; returns how many.</summary>
    public int ClearDone(DateTime before)
    {
        var n = _items.RemoveAll(i => i.Done && (i.DoneAt ?? i.Created) < before);
        if (n > 0) Changed?.Invoke();
        return n;
    }

    public bool Snooze(Guid id, int minutes, DateTime now)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.Done) return false;
        item.SnoozedUntil = now.AddMinutes(Math.Clamp(minutes, 1, 24 * 60));
        item.RemindedAt = null;
        Changed?.Invoke();
        return true;
    }

    // ---- views ----------------------------------------------------------------------------------

    public static bool IsOverdue(TodoItem item, DateTime now) => !item.Done && item.Due is { } due && due < now;

    public IEnumerable<TodoItem> View(TaskView view, DateTime now, string? category = null)
    {
        var today = now.Date;
        IEnumerable<TodoItem> q = _items;
        if (!string.IsNullOrEmpty(category)) q = q.Where(i => i.Category == category);
        return view switch
        {
            // today: everything due today and everything already overdue, earliest first
            TaskView.Today => q.Where(i => !i.Done && i.Due is { } d && d.Date <= today).OrderBy(i => i.Due).ThenByDescending(i => i.Important),
            TaskView.Upcoming => q.Where(i => !i.Done && i.Due is { } d && d.Date > today).OrderBy(i => i.Due),
            TaskView.NoDate => q.Where(i => !i.Done && i.Due is null).OrderByDescending(i => i.Important).ThenBy(i => i.Created),
            _ => q.Where(i => i.Done).OrderByDescending(i => i.DoneAt ?? i.Created),
        };
    }

    public IReadOnlyList<string> Categories() =>
        [.. _items.Select(i => i.Category).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.CurrentCulture)];

    // ---- reminders --------------------------------------------------------------------------------

    /// <summary>Tasks whose reminder time has come and that were not reminded yet. Each is marked, so it is returned
    /// once. Stale ones (the PC was off) are marked but not returned.</summary>
    public List<TodoItem> TakeDueReminders(DateTime now)
    {
        var due = new List<TodoItem>();
        var changed = false;
        foreach (var item in _items)
        {
            if (item.Done || item.RemindedAt is not null || item.ReminderTime is not { } at || at > now) continue;
            item.RemindedAt = now;
            item.SnoozedUntil = null;
            changed = true;
            if (now - at <= StaleReminder) due.Add(item);
        }
        if (changed) Changed?.Invoke();
        return due;
    }
}

public sealed class FocusSettings
{
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int RoundsBeforeLongBreak { get; set; } = 4;
    /// <summary>Start the next phase by itself instead of waiting for the person.</summary>
    public bool AutoContinue { get; set; }
}

public sealed class FocusSession
{
    public DateTime Start { get; set; }
    public double Minutes { get; set; }
    public bool Completed { get; set; }
    public string Task { get; set; } = "";
}

public enum FocusPhase { Idle, Focus, ShortBreak, LongBreak }

/// <summary>A focus and break timer (the Pomodoro rhythm). Driven by a clock the caller passes in, so it is testable
/// and survives sleep: if the PC slept, the phase simply ended while it was away.</summary>
public sealed class FocusTimer
{
    private readonly Func<FocusSettings> _settings;
    private readonly List<FocusSession> _log;
    private DateTime _phaseStart;
    private DateTime _phaseEnd;
    private DateTime? _pausedAt;
    private int _round;
    private string _task = "";

    public FocusTimer(Func<FocusSettings> settings, List<FocusSession> log)
    {
        _settings = settings;
        _log = log;
    }

    public FocusPhase Phase { get; private set; } = FocusPhase.Idle;
    public bool Paused => _pausedAt is not null;
    public int Round => _round;
    public string Task => _task;
    public event Action<FocusPhase, FocusPhase>? PhaseChanged;

    public TimeSpan Remaining(DateTime now)
    {
        if (Phase == FocusPhase.Idle) return TimeSpan.Zero;
        var left = _phaseEnd - (_pausedAt ?? now);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public double Progress(DateTime now)
    {
        if (Phase == FocusPhase.Idle) return 0;
        var total = (_phaseEnd - _phaseStart).TotalSeconds;
        return total <= 0 ? 1 : Math.Clamp(1 - Remaining(now).TotalSeconds / total, 0, 1);
    }

    private static int Clamp(int minutes, int min, int max) => Math.Clamp(minutes, min, max);

    private void Begin(FocusPhase phase, DateTime now)
    {
        var s = _settings();
        var minutes = phase switch
        {
            FocusPhase.Focus => Clamp(s.FocusMinutes, 1, 240),
            FocusPhase.ShortBreak => Clamp(s.ShortBreakMinutes, 1, 60),
            _ => Clamp(s.LongBreakMinutes, 1, 120),
        };
        var old = Phase;
        Phase = phase;
        _phaseStart = now;
        _phaseEnd = now.AddMinutes(minutes);
        _pausedAt = null;
        PhaseChanged?.Invoke(old, phase);
    }

    public void Start(DateTime now, string task = "")
    {
        _task = TaskBook.Clean(task, TaskBook.MaxTitle);
        _round = 1;
        Begin(FocusPhase.Focus, now);
    }

    public void Pause(DateTime now)
    {
        if (Phase != FocusPhase.Idle && _pausedAt is null) _pausedAt = now;
    }

    public void Resume(DateTime now)
    {
        if (_pausedAt is not { } p) return;
        var shift = now - p;
        _phaseStart += shift;
        _phaseEnd += shift;
        _pausedAt = null;
    }

    /// <summary>Stops the timer. A focus phase that was already under way is logged as not completed if it ran at
    /// least a minute.</summary>
    public void Stop(DateTime now)
    {
        if (Phase == FocusPhase.Focus) Log(now, completed: false);
        var old = Phase;
        Phase = FocusPhase.Idle;
        _pausedAt = null;
        _round = 0;
        if (old != FocusPhase.Idle) PhaseChanged?.Invoke(old, FocusPhase.Idle);
    }

    private void Log(DateTime now, bool completed)
    {
        var end = completed ? _phaseEnd : (_pausedAt ?? now);
        var minutes = Math.Max(0, (end - _phaseStart).TotalMinutes);
        if (!completed && minutes < 1) return;
        _log.Add(new FocusSession { Start = _phaseStart, Minutes = Math.Round(minutes, 1), Completed = completed, Task = _task });
        if (_log.Count > 2000) _log.RemoveRange(0, _log.Count - 2000);
    }

    /// <summary>Call about once a second. Returns the phase that just ended, or null.</summary>
    public FocusPhase? Tick(DateTime now)
    {
        if (Phase == FocusPhase.Idle || _pausedAt is not null || now < _phaseEnd) return null;
        var ended = Phase;
        if (ended == FocusPhase.Focus)
        {
            Log(now, completed: true);
            var s = _settings();
            var long_ = _round % Math.Clamp(s.RoundsBeforeLongBreak, 1, 12) == 0;
            Begin(long_ ? FocusPhase.LongBreak : FocusPhase.ShortBreak, now);
        }
        else
        {
            _round++;
            if (_settings().AutoContinue) Begin(FocusPhase.Focus, now);
            else
            {
                var old = Phase;
                Phase = FocusPhase.Idle;
                _round = 0;
                PhaseChanged?.Invoke(old, FocusPhase.Idle);
            }
        }
        return ended;
    }

    /// <summary>Skips what is running now (a break, or a focus phase counted as done up to this moment).</summary>
    public void Skip(DateTime now)
    {
        if (Phase == FocusPhase.Idle) return;
        if (Phase == FocusPhase.Focus)
        {
            Log(now, completed: false);
            Begin(FocusPhase.ShortBreak, now);
        }
        else
        {
            _round++;
            Begin(FocusPhase.Focus, now);
        }
    }
}

public static class WeekMath
{
    /// <summary>The first day of the week that contains <paramref name="date"/>: Saturday for the Persian calendar,
    /// Monday otherwise.</summary>
    public static DateOnly StartOf(DateTime date, bool persianWeek)
    {
        var first = persianWeek ? DayOfWeek.Saturday : DayOfWeek.Monday;
        var back = ((int)date.DayOfWeek - (int)first + 7) % 7;
        return DateOnly.FromDateTime(date.Date.AddDays(-back));
    }
}

/// <summary>One week of what was done, for the weekly report.</summary>
public sealed record WeekReport(
    DateOnly Start, int Created, int Completed, int CompletedOnTime, int OverdueNow, int Open,
    double FocusMinutes, int FocusSessions, double[] FocusPerDay, int[] CompletedPerDay, int Streak, DateOnly? BestDay)
{
    public static WeekReport Build(IEnumerable<TodoItem> tasks, IEnumerable<FocusSession> sessions, DateOnly weekStart, DateTime now)
    {
        var from = weekStart.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(7);
        var all = tasks.ToList();
        var done = all.Where(t => t.Done && t.DoneAt is { } d && d >= from && d < to).ToList();
        var perDay = new int[7];
        foreach (var t in done) perDay[(int)(t.DoneAt!.Value.Date - from).TotalDays]++;
        var focusPerDay = new double[7];
        var sessionList = sessions.Where(s => s.Start >= from && s.Start < to && s.Minutes > 0).ToList();
        foreach (var s in sessionList) focusPerDay[(int)(s.Start.Date - from).TotalDays] += s.Minutes;

        // streak: consecutive days, ending today (or yesterday), on which something was finished or a focus session ran
        var activeDays = new HashSet<DateOnly>(
            all.Where(t => t.Done && t.DoneAt is not null).Select(t => DateOnly.FromDateTime(t.DoneAt!.Value))
               .Concat(sessions.Where(s => s.Minutes >= 1).Select(s => DateOnly.FromDateTime(s.Start))));
        var day = DateOnly.FromDateTime(now);
        if (!activeDays.Contains(day)) day = day.AddDays(-1);
        var streak = 0;
        while (activeDays.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        DateOnly? best = null;
        var bestScore = 0.0;
        for (var i = 0; i < 7; i++)
        {
            var score = perDay[i] * 10 + focusPerDay[i];
            if (score > bestScore)
            {
                bestScore = score;
                best = weekStart.AddDays(i);
            }
        }
        return new WeekReport(
            weekStart,
            all.Count(t => t.Created >= from && t.Created < to),
            done.Count,
            done.Count(t => t.Due is null || t.DoneAt <= t.Due),
            all.Count(t => TaskBook.IsOverdue(t, now)),
            all.Count(t => !t.Done),
            focusPerDay.Sum(), sessionList.Count(s => s.Completed), focusPerDay, perDay, streak, best);
    }
}
