using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

public class TaskBookTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 10, 0, 0);

    private static TaskBook Book(out List<TodoItem> items)
    {
        items = [];
        return new TaskBook(items);
    }

    [Fact]
    public void Adding_trims_and_refuses_empty_titles()
    {
        var book = Book(out var items);
        Assert.Null(book.Add("   ", now: Now));
        var item = book.Add("  buy bread \n", now: Now)!;
        Assert.Equal("buy bread", item.Title);
        Assert.Single(items);
    }

    [Fact]
    public void Control_characters_and_huge_text_are_cleaned()
    {
        var book = Book(out _);
        var item = book.Add("a\u0000b\u0007c" + new string('x', 500), notes: new string('n', 5000), now: Now)!;
        Assert.DoesNotContain('\0', item.Title);
        Assert.Equal(TaskBook.MaxTitle, item.Title.Length);
        Assert.Equal(TaskBook.MaxNotes, item.Notes.Length);
    }

    [Fact]
    public void Views_split_today_upcoming_undated_and_done()
    {
        var book = Book(out _);
        var overdue = book.Add("overdue", Now.AddDays(-1), now: Now)!;
        var today = book.Add("today", Now.AddHours(3), now: Now)!;
        var later = book.Add("later", Now.AddDays(2), now: Now)!;
        var none = book.Add("none", now: Now)!;
        var finished = book.Add("finished", Now.AddHours(1), now: Now)!;
        book.Complete(finished.Id, Now);

        Assert.Equal(["overdue", "today"], book.View(TaskView.Today, Now).Select(i => i.Title));
        Assert.Equal(["later"], book.View(TaskView.Upcoming, Now).Select(i => i.Title));
        Assert.Equal(["none"], book.View(TaskView.NoDate, Now).Select(i => i.Title));
        Assert.Equal(["finished"], book.View(TaskView.Done, Now).Select(i => i.Title));
        Assert.True(TaskBook.IsOverdue(overdue, Now));
        Assert.False(TaskBook.IsOverdue(today, Now));
        Assert.False(TaskBook.IsOverdue(later, Now));
        Assert.False(TaskBook.IsOverdue(none, Now));
    }

    [Fact]
    public void Category_filter_and_list_work()
    {
        var book = Book(out _);
        book.Add("a", category: "work", now: Now);
        book.Add("b", category: "home", now: Now);
        book.Add("c", category: "work", now: Now);
        Assert.Equal(["home", "work"], book.Categories());
        Assert.Equal(2, book.View(TaskView.NoDate, Now, "work").Count());
    }

    [Fact]
    public void A_reminder_fires_once_at_its_time()
    {
        var book = Book(out _);
        var item = book.Add("call", Now.AddMinutes(30), remindBefore: 10, now: Now)!;
        Assert.Equal(Now.AddMinutes(20), item.ReminderTime);
        Assert.Empty(book.TakeDueReminders(Now.AddMinutes(19)));
        Assert.Single(book.TakeDueReminders(Now.AddMinutes(20)));
        Assert.Empty(book.TakeDueReminders(Now.AddMinutes(21))); // not again
    }

    [Fact]
    public void A_reminder_from_hours_ago_is_not_announced_as_fresh()
    {
        var book = Book(out _);
        var item = book.Add("old", Now, remindBefore: 0, now: Now.AddDays(-1))!;
        // the PC was off all day: the task shows as overdue but no notice pops up now
        Assert.Empty(book.TakeDueReminders(Now.AddHours(8)));
        Assert.NotNull(item.RemindedAt);
    }

    [Fact]
    public void Finished_tasks_do_not_remind_and_snooze_moves_the_reminder()
    {
        var book = Book(out _);
        var done = book.Add("done", Now.AddMinutes(5), remindBefore: 0, now: Now)!;
        book.Complete(done.Id, Now);
        Assert.Empty(book.TakeDueReminders(Now.AddMinutes(6)));

        var item = book.Add("later", Now.AddMinutes(5), remindBefore: 0, now: Now)!;
        Assert.Single(book.TakeDueReminders(Now.AddMinutes(5)));
        Assert.True(book.Snooze(item.Id, 60, Now.AddMinutes(5)));
        Assert.Empty(book.TakeDueReminders(Now.AddMinutes(30)));
        Assert.Single(book.TakeDueReminders(Now.AddMinutes(66)));
    }

    [Fact]
    public void Editing_the_deadline_re_arms_the_reminder_and_reopen_restores_it()
    {
        var book = Book(out _);
        var item = book.Add("x", Now.AddMinutes(5), remindBefore: 0, now: Now)!;
        book.TakeDueReminders(Now.AddMinutes(5));
        Assert.NotNull(item.RemindedAt);
        Assert.True(book.Update(item.Id, "x", Now.AddHours(2), 0, "", "", false));
        Assert.Null(item.RemindedAt);

        book.Complete(item.Id, Now);
        Assert.True(book.Reopen(item.Id));
        Assert.False(item.Done);
        Assert.Null(item.DoneAt);
    }

    [Fact]
    public void Remove_and_clear_old_done()
    {
        var book = Book(out var items);
        var a = book.Add("a", now: Now)!;
        var b = book.Add("b", now: Now)!;
        book.Complete(b.Id, Now.AddDays(-40));
        Assert.Equal(1, book.ClearDone(Now.AddMonths(-1)));
        Assert.True(book.Remove(a.Id));
        Assert.Empty(items);
    }

    [Fact]
    public void Tasks_survive_a_save_and_load()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(folder);
            var book = new TaskBook(store.State.Tasks);
            var item = book.Add("persist me", Now.AddDays(1), remindBefore: 60, category: "home", important: true, now: Now)!;
            store.State.Settings.Focus.FocusMinutes = 40;
            store.State.FocusLog.Add(new FocusSession { Start = Now, Minutes = 25, Completed = true, Task = "write" });
            store.Save();

            var loaded = new Store(folder);
            loaded.Load();
            var back = Assert.Single(loaded.State.Tasks);
            Assert.Equal(item.Id, back.Id);
            Assert.Equal("persist me", back.Title);
            Assert.Equal(Now.AddDays(1), back.Due);
            Assert.Equal(60, back.RemindBefore);
            Assert.True(back.Important);
            Assert.Equal(40, loaded.State.Settings.Focus.FocusMinutes);
            Assert.Equal("write", Assert.Single(loaded.State.FocusLog).Task);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}

public class FocusTimerTests
{
    private static readonly DateTime T0 = new(2026, 10, 16, 9, 0, 0);

    private static FocusTimer Timer(out List<FocusSession> log, FocusSettings? s = null)
    {
        log = [];
        s ??= new FocusSettings { FocusMinutes = 25, ShortBreakMinutes = 5, LongBreakMinutes = 15, RoundsBeforeLongBreak = 2 };
        var settings = s;
        return new FocusTimer(() => settings, log);
    }

    [Fact]
    public void A_focus_phase_runs_then_a_break_starts_and_the_session_is_logged()
    {
        var t = Timer(out var log);
        t.Start(T0, "essay");
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining(T0));
        Assert.Null(t.Tick(T0.AddMinutes(24)));
        Assert.Equal(FocusPhase.Focus, t.Tick(T0.AddMinutes(25)));
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        var s = Assert.Single(log);
        Assert.True(s.Completed);
        Assert.Equal(25, s.Minutes);
        Assert.Equal("essay", s.Task);
    }

    [Fact]
    public void Every_second_round_gets_a_long_break_and_it_can_continue_by_itself()
    {
        var t = Timer(out _, new FocusSettings { FocusMinutes = 10, ShortBreakMinutes = 2, LongBreakMinutes = 7, RoundsBeforeLongBreak = 2, AutoContinue = true });
        var now = T0;
        t.Start(now);
        now = now.AddMinutes(10); t.Tick(now);
        Assert.Equal(FocusPhase.ShortBreak, t.Phase);
        now = now.AddMinutes(2); t.Tick(now);
        Assert.Equal(FocusPhase.Focus, t.Phase);
        Assert.Equal(2, t.Round);
        now = now.AddMinutes(10); t.Tick(now);
        Assert.Equal(FocusPhase.LongBreak, t.Phase);
    }

    [Fact]
    public void Without_auto_continue_it_goes_idle_after_a_break()
    {
        var t = Timer(out _);
        t.Start(T0);
        t.Tick(T0.AddMinutes(25));
        t.Tick(T0.AddMinutes(30));
        Assert.Equal(FocusPhase.Idle, t.Phase);
    }

    [Fact]
    public void Pause_freezes_the_clock_and_resume_continues()
    {
        var t = Timer(out _);
        t.Start(T0);
        t.Pause(T0.AddMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddHours(3)));
        Assert.Null(t.Tick(T0.AddHours(3)));
        t.Resume(T0.AddHours(3));
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddHours(3)));
        Assert.Equal(FocusPhase.Focus, t.Tick(T0.AddHours(3).AddMinutes(15)));
    }

    [Fact]
    public void Stopping_early_logs_it_as_not_completed_but_ignores_a_few_seconds()
    {
        var t = Timer(out var log);
        t.Start(T0);
        t.Stop(T0.AddSeconds(20));
        Assert.Empty(log);
        t.Start(T0);
        t.Stop(T0.AddMinutes(7));
        var s = Assert.Single(log);
        Assert.False(s.Completed);
        Assert.Equal(7, s.Minutes);
        Assert.Equal(FocusPhase.Idle, t.Phase);
    }

    [Fact]
    public void After_sleep_the_phase_simply_ended_while_away()
    {
        var t = Timer(out var log);
        t.Start(T0);
        Assert.Equal(FocusPhase.Focus, t.Tick(T0.AddHours(5)));
        Assert.Equal(25, Assert.Single(log).Minutes); // counted as the planned length, not 5 hours
    }

    [Fact]
    public void Durations_are_clamped_to_sane_values()
    {
        var t = Timer(out _, new FocusSettings { FocusMinutes = -5, ShortBreakMinutes = 9999 });
        t.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(1), t.Remaining(T0));
    }
}

public class WeekReportTests
{
    [Fact]
    public void Week_starts_on_saturday_for_persian_and_monday_otherwise()
    {
        var friday = new DateTime(2026, 10, 16); // a Friday
        Assert.Equal(DayOfWeek.Saturday, WeekMath.StartOf(friday, true).DayOfWeek);
        Assert.Equal(new DateOnly(2026, 10, 10), WeekMath.StartOf(friday, true));
        Assert.Equal(DayOfWeek.Monday, WeekMath.StartOf(friday, false).DayOfWeek);
        Assert.Equal(new DateOnly(2026, 10, 12), WeekMath.StartOf(friday, false));
    }

    [Fact]
    public void The_report_counts_tasks_focus_streak_and_overdue()
    {
        var start = new DateOnly(2026, 10, 10); // Saturday
        var now = new DateTime(2026, 10, 14, 12, 0, 0);
        var tasks = new List<TodoItem>
        {
            new() { Title = "a", Created = new DateTime(2026, 10, 10, 8, 0, 0), Done = true, DoneAt = new DateTime(2026, 10, 12, 9, 0, 0), Due = new DateTime(2026, 10, 12, 18, 0, 0) },
            new() { Title = "b", Created = new DateTime(2026, 10, 11, 8, 0, 0), Done = true, DoneAt = new DateTime(2026, 10, 13, 9, 0, 0), Due = new DateTime(2026, 10, 12, 18, 0, 0) },
            new() { Title = "c", Created = new DateTime(2026, 10, 13, 8, 0, 0), Due = new DateTime(2026, 10, 13, 18, 0, 0) },
            new() { Title = "d", Created = new DateTime(2026, 10, 3, 8, 0, 0), Done = true, DoneAt = new DateTime(2026, 10, 4, 9, 0, 0) },
        };
        var sessions = new List<FocusSession>
        {
            new() { Start = new DateTime(2026, 10, 13, 10, 0, 0), Minutes = 25, Completed = true },
            new() { Start = new DateTime(2026, 10, 14, 10, 0, 0), Minutes = 25, Completed = true },
            new() { Start = new DateTime(2026, 10, 14, 11, 0, 0), Minutes = 10, Completed = false },
        };
        var r = WeekReport.Build(tasks, sessions, start, now);
        Assert.Equal(3, r.Created);
        Assert.Equal(2, r.Completed);
        Assert.Equal(1, r.CompletedOnTime); // b was finished after its deadline
        Assert.Equal(1, r.OverdueNow);      // c
        Assert.Equal(1, r.Open);            // only c is open
        Assert.Equal(60, r.FocusMinutes);
        Assert.Equal(2, r.FocusSessions);
        Assert.Equal(3, r.Streak); // 12th, 13th, 14th all had activity (a, b+session, session)
        Assert.Equal(1, r.CompletedPerDay[2]); // Monday the 12th
        Assert.Equal(new DateOnly(2026, 10, 13), r.BestDay);
    }
}
