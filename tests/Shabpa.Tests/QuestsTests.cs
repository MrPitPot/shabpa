using System.IO;
namespace Shabpa.Tests;

public class QuestsTests
{
    private static readonly DateTime Sat = new(2026, 10, 31, 20, 0, 0); // a Saturday

    [Fact]
    public void Weeks_start_on_saturday()
    {
        var sat = new DateOnly(2026, 10, 31);
        Assert.Equal(DayOfWeek.Saturday, sat.DayOfWeek);
        Assert.Equal(sat, Quests.WeekStart(sat));
        Assert.Equal(sat, Quests.WeekStart(sat.AddDays(6))); // Friday
        Assert.Equal(sat.AddDays(7), Quests.WeekStart(sat.AddDays(7)));
        Assert.Equal(sat, Quests.WeekStart(sat.AddDays(1))); // Sunday
    }

    [Fact]
    public void Three_daily_quests_never_share_an_event_and_stay_the_same_all_day()
    {
        for (var i = 0; i < 60; i++)
        {
            var day = new DateOnly(2026, 11, 1).AddDays(i);
            var today = Quests.Today(day);
            Assert.Equal(3, today.Count);
            Assert.Equal(3, today.Select(q => q.Event).Distinct().Count());
            Assert.Equal(today.Select(q => q.Id), Quests.Today(day).Select(q => q.Id));
        }
    }

    [Fact]
    public void Daily_quests_change_from_day_to_day()
    {
        var sets = Enumerable.Range(0, 30).Select(i => string.Join(",", Quests.Today(new DateOnly(2026, 11, 1).AddDays(i)).Select(q => q.Id))).Distinct().Count();
        Assert.True(sets > 5);
    }

    [Fact]
    public void A_quest_pays_once_when_its_target_is_reached()
    {
        var s = new QuestState();
        var day = DateOnly.FromDateTime(Sat);
        var quest = Quests.Today(day)[0];
        var first = Quests.Record(s, quest.Event, quest.Target - 1, Sat);
        Assert.DoesNotContain(quest, first.Completed);
        var second = Quests.Record(s, quest.Event, 1, Sat);
        Assert.Contains(quest, second.Completed);
        var feathersAfter = s.Feathers;
        var third = Quests.Record(s, quest.Event, 100, Sat);
        Assert.DoesNotContain(quest, third.Completed);
        Assert.Equal(feathersAfter, s.Feathers);
    }

    [Fact]
    public void Daily_progress_and_finished_daily_quests_reset_the_next_day_but_lifetime_does_not()
    {
        var s = new QuestState();
        Quests.Record(s, QuestEvent.GameMinutes, 45, Sat);
        Assert.Equal(45, s.DayProgress["GameMinutes"]);
        Quests.Record(s, QuestEvent.GameMinutes, 10, Sat.AddDays(1));
        Assert.Equal(10, s.DayProgress["GameMinutes"]);
        Assert.Equal(55, s.Lifetime["GameMinutes"]);
        Assert.DoesNotContain(s.Done, id => id.StartsWith("d-") && !Quests.Today(DateOnly.FromDateTime(Sat.AddDays(1))).Any(q => q.Id == id));
    }

    [Fact]
    public void One_time_quests_stay_done_for_good()
    {
        var s = new QuestState();
        var r = Quests.Record(s, QuestEvent.BotLinked, 1, Sat);
        Assert.Contains(r.Completed, q => q.Id == "o-bot");
        var later = Quests.Record(s, QuestEvent.BotLinked, 1, Sat.AddDays(40));
        Assert.DoesNotContain(later.Completed, q => q.Id == "o-bot");
        Assert.Contains("o-bot", s.Done);
    }

    [Fact]
    public void The_streak_grows_on_consecutive_days_and_breaks_on_a_gap()
    {
        var s = new QuestState();
        Quests.Record(s, QuestEvent.AssistantCommand, 1, Sat);
        Quests.Record(s, QuestEvent.AssistantCommand, 1, Sat.AddHours(1)); // same day: no change
        Assert.Equal(1, s.Streak);
        Quests.Record(s, QuestEvent.AssistantCommand, 1, Sat.AddDays(1));
        Quests.Record(s, QuestEvent.AssistantCommand, 1, Sat.AddDays(2));
        Assert.Equal(3, s.Streak);
        Quests.Record(s, QuestEvent.AssistantCommand, 1, Sat.AddDays(5));
        Assert.Equal(1, s.Streak);
        Assert.Equal(3, s.BestStreak);
    }

    [Fact]
    public void Seven_days_in_a_row_pay_the_streak_bonus_once()
    {
        var s = new QuestState();
        QuestResult? last = null;
        for (var i = 0; i < 7; i++) last = Quests.Record(s, QuestEvent.AssistantCommand, 1, Sat.AddDays(i));
        Assert.True(last!.StreakBonus);
        Assert.True(last.Earned >= Quests.StreakBonusFeathers);
        var eighth = Quests.Record(s, QuestEvent.AssistantCommand, 1, Sat.AddDays(7));
        Assert.False(eighth.StreakBonus);
    }

    [Fact]
    public void Levels_follow_the_thresholds_and_stop_at_the_top()
    {
        Assert.Equal(1, Quests.Level(0));
        Assert.Equal(1, Quests.Level(59));
        Assert.Equal(2, Quests.Level(60));
        Assert.Equal(Quests.LevelAt.Length, Quests.Level(1_000_000));
        Assert.Equal(1, Quests.LevelProgress(1_000_000));
        Assert.Equal(60, Quests.ToNext(0));
        Assert.Null(Quests.ToNext(1_000_000));
        Assert.Equal(Quests.LevelAt.Length, Quests.LevelNames.Length);
    }

    [Fact]
    public void A_level_up_is_reported()
    {
        var s = new QuestState { Feathers = 55 };
        var r = Quests.Record(s, QuestEvent.BotLinked, 1, Sat); // +50
        Assert.True(r.LeveledUp);
        Assert.Equal(2, r.Level);
    }

    [Fact]
    public void Badges_come_from_lifetime_totals_once()
    {
        var s = new QuestState();
        var r = Quests.Record(s, QuestEvent.GameMinutes, 6000, Sat);
        Assert.Contains(r.NewBadges, b => b.Id == "night-owl");
        var again = Quests.Record(s, QuestEvent.GameMinutes, 10, Sat);
        Assert.Empty(again.NewBadges);
    }

    [Fact]
    public void Switched_off_counts_nothing()
    {
        var s = new QuestState { Off = true };
        var r = Quests.Record(s, QuestEvent.BotLinked, 1, Sat);
        Assert.False(r.Any);
        Assert.Equal(0, s.Feathers);
        Assert.Empty(s.Lifetime);
    }

    [Fact]
    public void Every_quest_id_is_unique_and_prefixed_by_its_scope()
    {
        var all = Quests.All.ToList();
        Assert.Equal(all.Count, all.Select(q => q.Id).Distinct().Count());
        Assert.All(all, q => Assert.StartsWith(q.Scope switch { QuestScope.Daily => "d-", QuestScope.Weekly => "w-", _ => "o-" }, q.Id));
        Assert.All(all, q => { Assert.True(q.Target > 0); Assert.True(q.Feathers > 0); });
    }

    [Fact]
    public void The_file_round_trips_and_a_broken_file_starts_over()
    {
        var dir = Path.Combine(Path.GetTempPath(), "shabpa-quests-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "quests.json");
        try
        {
            var s = new QuestState();
            Quests.Record(s, QuestEvent.BotLinked, 1, Sat);
            Quests.Save(file, s);
            var back = Quests.Load(file);
            Assert.Equal(s.Feathers, back.Feathers);
            Assert.Equal(s.Done, back.Done);
            File.WriteAllText(file, "{ not json");
            Assert.Equal(0, Quests.Load(file).Feathers);
            Assert.Equal(0, Quests.Load(Path.Combine(dir, "missing.json")).Feathers);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

public class OutageGuardTests
{
    private static OutageSettings Setup(params (int hour, int minute, int minutes)[] windows) => new()
    {
        Enabled = true,
        Windows = [.. windows.Select(w => new OutageWindow { StartMinute = w.hour * 60 + w.minute, DurationMinutes = w.minutes })],
    };

    [Fact]
    public void Next_start_respects_the_days()
    {
        var w = new OutageWindow { StartMinute = 14 * 60, Days = [DayOfWeek.Monday] };
        var sat = new DateTime(2026, 10, 31, 10, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 2, 14, 0, 0), OutageGuard.NextStart(w, sat));
        var every = new OutageWindow { StartMinute = 14 * 60 };
        Assert.Equal(new DateTime(2026, 10, 31, 14, 0, 0), OutageGuard.NextStart(every, sat));
        Assert.Equal(new DateTime(2026, 11, 1, 14, 0, 0), OutageGuard.NextStart(every, new DateTime(2026, 10, 31, 14, 0, 1)));
    }

    [Fact]
    public void Nothing_happens_far_from_a_cut_or_when_off()
    {
        var s = Setup((14, 0, 120));
        Assert.Null(OutageGuard.Check(s, new DateTime(2026, 10, 31, 12, 0, 0), false));
        s.Enabled = false;
        Assert.Null(OutageGuard.Check(s, new DateTime(2026, 10, 31, 13, 59, 0), false));
        Assert.Null(OutageGuard.Check(new OutageSettings { Enabled = true }, new DateTime(2026, 10, 31, 13, 59, 0), false));
    }

    [Fact]
    public void The_warning_comes_once_then_the_hibernation()
    {
        var s = Setup((14, 0, 120));
        var warn = OutageGuard.Check(s, new DateTime(2026, 10, 31, 13, 50, 0), false);
        Assert.Equal(OutageStep.Warn, warn!.Step);
        s.WarnedStart = warn.Start;
        Assert.Null(OutageGuard.Check(s, new DateTime(2026, 10, 31, 13, 52, 0), false));
        var hib = OutageGuard.Check(s, new DateTime(2026, 10, 31, 13, 58, 0), false);
        Assert.Equal(OutageStep.Hibernate, hib!.Step);
        s.ActedStart = hib.Start;
        Assert.Null(OutageGuard.Check(s, new DateTime(2026, 10, 31, 13, 59, 0), false));
        Assert.Null(OutageGuard.Check(s, new DateTime(2026, 10, 31, 14, 0, 10), false));
    }

    [Fact]
    public void Opening_the_app_late_still_hibernates_and_skips_a_stale_warning()
    {
        var s = Setup((14, 0, 120));
        var due = OutageGuard.Check(s, new DateTime(2026, 10, 31, 13, 59, 0), false);
        Assert.Equal(OutageStep.Hibernate, due!.Step);
    }

    [Fact]
    public void A_game_keeps_it_from_hibernating_unless_allowed()
    {
        var s = Setup((14, 0, 120));
        var now = new DateTime(2026, 10, 31, 13, 58, 0);
        Assert.Equal(OutageStep.SkippedForGame, OutageGuard.Check(s, now, true)!.Step);
        s.SkipDuringGame = false;
        Assert.Equal(OutageStep.Hibernate, OutageGuard.Check(s, now, true)!.Step);
    }

    [Fact]
    public void The_nearest_of_several_windows_wins()
    {
        var s = Setup((20, 0, 60), (14, 0, 120));
        var due = OutageGuard.Check(s, new DateTime(2026, 10, 31, 13, 55, 0), false);
        Assert.Equal(new DateTime(2026, 10, 31, 14, 0, 0), due!.Start);
    }

    [Fact]
    public void Upcoming_reports_the_next_cut()
    {
        var s = Setup((14, 0, 120), (9, 0, 60));
        var (start, _) = OutageGuard.Upcoming(s, new DateTime(2026, 10, 31, 10, 0, 0))!.Value;
        Assert.Equal(new DateTime(2026, 10, 31, 14, 0, 0), start);
        Assert.Null(OutageGuard.Upcoming(new OutageSettings(), DateTime.Now));
    }

    [Fact]
    public void The_return_report_needs_a_time_jump_after_the_request_and_comes_once()
    {
        var start = new DateTime(2026, 10, 31, 14, 0, 0);
        var s = new OutageSettings { ActedStart = start, ActedAt = start.AddMinutes(-2) };
        Assert.False(OutageGuard.ReturnReportDue(s, start.AddMinutes(-1), TimeSpan.FromSeconds(1)));  // countdown cancelled: no jump
        Assert.True(OutageGuard.ReturnReportDue(s, start.AddHours(3), TimeSpan.FromHours(3)));
        s.ReportedStart = start;
        Assert.False(OutageGuard.ReturnReportDue(s, start.AddHours(3), TimeSpan.FromHours(3)));
        Assert.False(OutageGuard.ReturnReportDue(new OutageSettings(), start, TimeSpan.FromHours(3)));
    }
}

public class NightWindowTests
{
    private static NightWindow.Hourly Usage(Dictionary<DateOnly, long[]> down) => day =>
        (down.TryGetValue(day, out var d) ? d : new long[24], new long[24]);

    private static long[] Hours(params (int hour, long bytes)[] cells)
    {
        var a = new long[24];
        foreach (var (h, b) in cells) a[h] = b;
        return a;
    }

    [Fact]
    public void An_hour_counts_when_its_middle_is_inside_the_window()
    {
        Assert.True(NightWindow.IsNightHour(0, 0, 360));
        Assert.True(NightWindow.IsNightHour(5, 0, 360));
        Assert.False(NightWindow.IsNightHour(6, 0, 360));
        // 23:00 to 06:00 crosses midnight
        Assert.True(NightWindow.IsNightHour(23, 1380, 360));
        Assert.True(NightWindow.IsNightHour(2, 1380, 360));
        Assert.False(NightWindow.IsNightHour(12, 1380, 360));
        // 00:30 to 06:30: the 00:00 hour has its middle exactly on the start
        Assert.True(NightWindow.IsNightHour(0, 30, 390));
    }

    [Fact]
    public void A_window_across_midnight_takes_the_evening_before_and_the_morning_after()
    {
        var evening = new DateOnly(2026, 10, 30);
        var morning = new DateOnly(2026, 10, 31);
        var usage = Usage(new()
        {
            [evening] = Hours((22, 1000), (23, 500)),
            [morning] = Hours((0, 200), (5, 300), (6, 9999)),
        });
        var (down, up) = NightWindow.Night(usage, morning, 1380, 360);
        Assert.Equal(500 + 200 + 300, down);
        Assert.Equal(0, up);
    }

    [Fact]
    public void A_window_inside_one_day_takes_only_that_day()
    {
        var morning = new DateOnly(2026, 10, 31);
        var usage = Usage(new() { [morning] = Hours((1, 100), (3, 50), (7, 7)), [morning.AddDays(-1)] = Hours((23, 5000)) });
        Assert.Equal(150, NightWindow.Night(usage, morning, 0, 360).down);
    }

    [Fact]
    public void A_day_splits_into_night_and_the_rest()
    {
        var day = new DateOnly(2026, 10, 31);
        var usage = Usage(new() { [day] = Hours((2, 10), (12, 20), (23, 30)) });
        var (night, rest) = NightWindow.Split(usage, day, 1380, 360);
        Assert.Equal(40, night);
        Assert.Equal(20, rest);
    }

    [Fact]
    public void Missing_days_count_as_zero()
    {
        var (down, up) = NightWindow.Night(Usage([]), new DateOnly(2026, 10, 31), 1380, 360);
        Assert.Equal(0, down + up);
    }
}

public class HealthWatchTests
{
    private static HealthCheck C(string id, CheckState st) => new(id, id, st, "");

    [Fact]
    public void First_look_only_records_a_baseline()
    {
        var seen = new Dictionary<string, CheckState>();
        Assert.Equal(0, HealthWatch.Fixed(seen, [C("a", CheckState.Good), C("b", CheckState.Problem)]));
        Assert.Equal(CheckState.Problem, seen["b"]);
    }

    [Fact]
    public void A_warning_that_turns_good_counts_once()
    {
        var seen = new Dictionary<string, CheckState>();
        HealthWatch.Fixed(seen, [C("a", CheckState.Attention), C("b", CheckState.Problem), C("c", CheckState.Good)]);
        Assert.Equal(2, HealthWatch.Fixed(seen, [C("a", CheckState.Good), C("b", CheckState.Good), C("c", CheckState.Good)]));
        Assert.Equal(0, HealthWatch.Fixed(seen, [C("a", CheckState.Good), C("b", CheckState.Good)]));
    }

    [Fact]
    public void Unknown_does_not_count_as_fixed_or_reset_the_baseline()
    {
        var seen = new Dictionary<string, CheckState>();
        HealthWatch.Fixed(seen, [C("a", CheckState.Problem)]);
        Assert.Equal(0, HealthWatch.Fixed(seen, [C("a", CheckState.Unknown)]));
        Assert.Equal(1, HealthWatch.Fixed(seen, [C("a", CheckState.Good)]));
    }
}
