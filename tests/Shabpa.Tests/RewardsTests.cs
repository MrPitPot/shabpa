using System.IO;
using Shabpa.Core;

namespace Shabpa.Tests;

public class RewardsTests
{
    private static Hub NewHub(bool demo = true) =>
        new(new Store(Path.Combine(Path.GetTempPath(), "shabpa-rw-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: demo);

    /// <summary>A fresh rewards state on a fresh hub, driven by a clock the test controls.</summary>
    private static (Hub hub, RewardsEngine engine, Func<DateTime> clock, Action<DateTime> set) Engine(DateTime start)
    {
        var hub = NewHub();
        hub.State.Rewards = new RewardsState();
        var now = start;
        var engine = new RewardsEngine(hub, () => now);
        return (hub, engine, () => now, t => now = t);
    }

    [Fact]
    public void The_catalog_has_the_sheet_medals_and_the_upper_rungs_five_hidden_with_unique_ids()
    {
        var m = RewardsCatalog.Medals;
        Assert.True(m.Count >= 80); // the 30 of the sheet, the upper rungs and the later ones
        Assert.Equal(m.Count, m.Select(x => x.Id).Distinct().Count());
        Assert.All(new[] { MedalTier.Platinum, MedalTier.Diamond, MedalTier.Legendary }, t => Assert.Contains(m, x => x.Tier == t));
        Assert.Equal(8, m.Count(x => x.Hidden));
        Assert.All(m.Where(x => x.Hidden), x => Assert.Equal(MedalTier.Special, x.Tier));
        Assert.All(m, x => { Assert.NotEmpty(x.NameFa); Assert.NotEmpty(x.NameEn); Assert.NotEmpty(x.DescFa); Assert.NotEmpty(x.DescEn); });
    }

    [Fact]
    public void Every_medal_icon_exists_in_the_icon_set()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shabpa", "Kit", "Icons.xaml"));
        foreach (var name in RewardsCatalog.Medals.Select(m => m.Icon).Append("lock").Append("medal").Append("feather").Append("flame").Distinct())
            Assert.Contains($"x:Key=\"I.{name}\"", xaml);
    }

    [Fact]
    public void Game_medals_follow_the_game_reports()
    {
        var (_, e, _, _) = Engine(new DateTime(2026, 11, 2, 12, 0, 0));
        Assert.DoesNotContain("firstgame", e.State.Medals.Keys);
        e.GameFinished("Dota 2", 200, new DateTime(2026, 11, 2, 2, 40, 0)); // longer than 3 hours, started at 02:40
        Assert.Contains("firstgame", e.State.Medals.Keys);
        Assert.Contains("marathon", e.State.Medals.Keys);
        Assert.Contains("realowl", e.State.Medals.Keys);
        Assert.DoesNotContain("allrounder", e.State.Medals.Keys);
        foreach (var g in new[] { "CS2", "Valorant", "Minecraft", "Elden Ring" }) e.GameFinished(g, 30, new DateTime(2026, 11, 2, 20, 0, 0));
        Assert.Contains("allrounder", e.State.Medals.Keys);
        Assert.Equal(5, e.State.Counters["gameSessions"]);
    }

    [Fact]
    public void A_game_that_starts_in_the_evening_is_not_the_night_owl_medal()
    {
        var (_, e, _, _) = Engine(new DateTime(2026, 11, 2, 21, 0, 0));
        e.GameFinished("CS2", 90, new DateTime(2026, 11, 2, 19, 30, 0));
        Assert.DoesNotContain("realowl", e.State.Medals.Keys);
    }

    [Fact]
    public void A_medal_is_paid_once_and_its_feathers_follow_the_tier()
    {
        var (_, e, _, _) = Engine(new DateTime(2026, 11, 2, 12, 0, 0));
        var before = e.State.Feathers;
        e.GameFinished("Dota 2", 200, new DateTime(2026, 11, 2, 9, 0, 0));
        var gained = e.State.Feathers - before;
        Assert.True(gained >= 2 + 10); // firstgame (bronze) + marathon (gold), plus whatever else came with them
        var again = e.State.Feathers;
        e.Evaluate();
        e.Evaluate();
        Assert.Equal(again, e.State.Feathers); // nothing is paid twice
        Assert.Equal(e.State.TotalFeathers, e.State.Feathers);
    }

    [Fact]
    public void The_date_medals_wait_for_their_date()
    {
        var (_, e, _, set) = Engine(new DateTime(2026, 11, 2, 12, 0, 0));
        Assert.DoesNotContain("pumpkin", e.State.Medals.Keys);
        set(new DateTime(2026, 10, 31, 20, 0, 0));
        e.Evaluate();
        Assert.Contains("pumpkin", e.State.Medals.Keys);
        Assert.DoesNotContain("midnight", e.State.Medals.Keys);
        set(new DateTime(2026, 11, 1, 0, 1, 0));
        e.Evaluate();
        Assert.Contains("midnight", e.State.Medals.Keys);
    }

    [Fact]
    public void The_birthday_medal_uses_the_calendar_of_the_birthday_settings()
    {
        var (hub, e, _, set) = Engine(new DateTime(2026, 5, 3, 12, 0, 0));
        hub.Settings.Birthday = new BirthdaySettings { Jalali = false, Month = 5, Day = 4 };
        e.Evaluate();
        Assert.DoesNotContain("birthday", e.State.Medals.Keys);
        set(new DateTime(2026, 5, 4, 9, 0, 0));
        e.Evaluate();
        Assert.Contains("birthday", e.State.Medals.Keys);

        // 1 Mehr 1405 is 23 September 2026
        var (hubP, p, _, setP) = Engine(new DateTime(2026, 9, 22, 12, 0, 0));
        hubP.Settings.Birthday = new BirthdaySettings { Jalali = true, Month = 7, Day = 1 };
        p.Evaluate();
        Assert.DoesNotContain("birthday", p.State.Medals.Keys);
        setP(new DateTime(2026, 9, 23, 12, 0, 0));
        p.Evaluate();
        Assert.Contains("birthday", p.State.Medals.Keys);
    }

    [Fact]
    public void Days_and_pages_unlock_the_loyalty_and_exploring_medals()
    {
        var (_, e, _, set) = Engine(new DateTime(2026, 11, 1, 12, 0, 0));
        foreach (var p in new[] { "home", "system", "gaming", "tasks" }) e.Visit(p);
        Assert.DoesNotContain("curious", e.State.Medals.Keys);
        e.Visit("settings:general");
        Assert.Contains("curious", e.State.Medals.Keys);
        Assert.Contains("tinker", e.State.Medals.Keys);
        Assert.Contains("overlay", e.State.Medals.Keys);
        e.Visit("kit"); // the hidden design kit never counts
        Assert.DoesNotContain("kit", e.State.Pages);

        for (var d = 1; d <= 7; d++) { set(new DateTime(2026, 11, 1, 12, 0, 0).AddDays(d)); e.Evaluate(); }
        Assert.Equal(8, e.State.Days.Count);
        Assert.Contains("sevennights", e.State.Medals.Keys);
        Assert.DoesNotContain("companion", e.State.Medals.Keys);
    }

    [Fact]
    public void The_collector_medal_comes_after_fifteen_others()
    {
        var (_, e, _, _) = Engine(new DateTime(2026, 11, 2, 12, 0, 0));
        foreach (var m in RewardsCatalog.Medals.Where(m => m.Id != "collector").Take(14)) e.State.Medals[m.Id] = DateTime.Now;
        e.Evaluate();
        Assert.DoesNotContain("collector", e.State.Medals.Keys);
        e.State.Medals[RewardsCatalog.Medals.Where(m => m.Id != "collector").ElementAt(14).Id] = DateTime.Now;
        e.Evaluate();
        Assert.Contains("collector", e.State.Medals.Keys);
    }

    [Fact]
    public void Daily_quests_are_three_different_ones_that_stay_the_same_all_day()
    {
        var day = new DateTime(2026, 11, 2, 8, 0, 0);
        var a = RewardsCatalog.DailyFor(day);
        Assert.Equal(3, a.Count);
        Assert.Equal(3, a.Select(q => q.Id).Distinct().Count());
        Assert.Equal(a.Select(q => q.Id), RewardsCatalog.DailyFor(day.AddHours(10)).Select(q => q.Id));
        var seen = Enumerable.Range(0, 30).SelectMany(i => RewardsCatalog.DailyFor(day.AddDays(i))).Select(q => q.Id).Distinct().Count();
        Assert.True(seen >= 5, "the quests rotate through the pool");
    }

    [Fact]
    public void Daily_quests_never_pair_two_of_one_family()
    {
        var day = new DateTime(2026, 11, 2);
        for (var i = 0; i < 120; i++)
        {
            var fams = RewardsCatalog.DailyFor(day.AddDays(i)).Select(q => q.Family).Where(f => f.Length > 0).ToList();
            Assert.Equal(fams.Count, fams.Distinct().Count());
        }
    }

    [Fact]
    public void Each_week_has_one_easy_one_medium_and_one_hard_quest()
    {
        var day = new DateTime(2026, 11, 2);
        for (var i = 0; i < 20; i++)
            Assert.Equal([0, 1, 2], RewardsCatalog.WeeklyFor(day.AddDays(i * 7)).Select(q => q.Tier));
        var seen = Enumerable.Range(0, 20).SelectMany(i => RewardsCatalog.WeeklyFor(day.AddDays(i * 7))).Select(q => q.Id).Distinct().Count();
        Assert.Equal(9, seen);
    }

    [Fact]
    public void The_quest_tables_match_the_economy_plan()
    {
        Assert.Equal(25, RewardsCatalog.Once.Count);
        Assert.Equal(427, RewardsCatalog.Once.Sum(q => q.Reward));
        Assert.Equal(("o.profile", 200), (RewardsCatalog.Once[0].Id, RewardsCatalog.Once[0].Reward));
        Assert.Equal(4, RewardsCatalog.DailyBonus);
        Assert.Equal(15, RewardsCatalog.WeeklyBonus);
        Assert.All(RewardsCatalog.Once, q => Assert.Equal(q.Id, RewardsCatalog.ClaimKey(q, DateTime.Now))); // one-time quests are paid once, ever
    }

    [Fact]
    public void A_name_and_a_birthday_in_settings_pay_the_200_feather_welcome_gift_once()
    {
        var (hub, e, _, _) = Engine(new DateTime(2026, 11, 2, 12, 0, 0));
        e.Evaluate();
        Assert.DoesNotContain("o.profile", e.State.Claimed);
        hub.Settings.Birthday.Name = "Sara";                                  // a name alone is not enough
        e.Evaluate();
        Assert.DoesNotContain("o.profile", e.State.Claimed);
        hub.Settings.Birthday.Name = "   ";
        hub.Settings.Birthday.Month = 5; hub.Settings.Birthday.Day = 17;     // nor is a date alone
        e.Evaluate();
        Assert.DoesNotContain("o.profile", e.State.Claimed);
        var before = e.State.Feathers;
        hub.Settings.Birthday.Name = "Sara";
        e.Evaluate();
        Assert.Contains("o.profile", e.State.Claimed);
        Assert.True(e.State.Feathers - before >= 200);
        var after = e.State.Feathers;
        e.Evaluate(); e.Evaluate();
        Assert.Equal(after, e.State.Feathers);                                // never paid twice
    }

    [Fact]
    public void Every_full_hour_with_shabpa_open_pays_three_feathers_up_to_eight_hours_a_day()
    {
        var (_, e, _, set) = Engine(new DateTime(2026, 11, 2, 9, 0, 0));
        var start = e.State.Feathers;
        for (var m = 1; m <= 59; m++) e.ActiveMinute(new DateTime(2026, 11, 2, 9, 0, 0).AddMinutes(m));
        Assert.Equal(start, e.State.Feathers);                                // 59 minutes: nothing yet
        e.ActiveMinute(new DateTime(2026, 11, 2, 10, 0, 0));
        Assert.Equal(start + 3, e.State.Feathers);
        for (var m = 61; m <= 20 * 60; m++) e.ActiveMinute(new DateTime(2026, 11, 2, 9, 0, 0).AddMinutes(m));
        Assert.Equal(start + 3 * 8, e.State.Feathers);                        // the day's cap is 8 hours
        set(new DateTime(2026, 11, 3, 9, 1, 0));                              // the clock reaches the next day
        e.ActiveMinute(new DateTime(2026, 11, 3, 9, 1, 0));                   // and the count starts again
        Assert.Equal(1, e.State.DayCounters["present"]);
    }

    [Fact]
    public void A_finished_quest_pays_once_and_the_next_day_starts_fresh()
    {
        var (hub, e, _, set) = Engine(new DateTime(2026, 11, 2, 12, 0, 0));
        var quest = RewardsCatalog.DailyFor(new DateTime(2026, 11, 2)).First();
        void Do(string id)
        {
            switch (id)
            {
                case "d.game": e.GameFinished("X", 61, DateTime.Now.AddHours(-2)); break;
                case "d.task": e.Count("tasksDone", day: "task"); break;
                case "d.ram": e.Count("ramFreed", day: "ram"); break;
                case "d.power": e.Count("powerScheduled", day: "power"); break;
                case "d.explore": foreach (var p in new[] { "home", "system", "gaming", "tasks" }) e.Visit(p); break;
                case "d.game2": e.GameFinished("X", 125, DateTime.Now.AddHours(-3)); break;
                case "d.task3": e.Count("tasksDone", 3, day: "task"); break;
                case "d.chat": e.Count("bidar", 3, day: "bidar"); break;
                case "d.net": e.Count("dnsTest", day: "net"); break;
                case "d.night": e.Count("nightMinTest", 60, day: "nightMin"); break;
                case "d.focus2": for (var i = 0; i < 2; i++) hub.State.FocusLog.Add(new FocusSession { Start = new DateTime(2026, 11, 2, 9 + i, 0, 0), Minutes = 25, Completed = true }); e.Evaluate(); break;
                case "d.focus": hub.State.FocusLog.Add(new FocusSession { Start = new DateTime(2026, 11, 2, 9, 0, 0), Minutes = 25, Completed = true }); e.Evaluate(); break;
            }
        }
        var before = e.State.Feathers;
        Do(quest.Id);
        var key = RewardsCatalog.ClaimKey(quest, new DateTime(2026, 11, 2));
        Assert.Contains(key, e.State.Claimed);
        var paid = e.State.Feathers;
        Assert.True(paid >= before + quest.Reward);
        e.Evaluate();
        Assert.Equal(paid, e.State.Feathers);

        set(new DateTime(2026, 11, 3, 12, 0, 0));
        e.Evaluate();
        Assert.Equal(0, e.State.DayCounters.GetValueOrDefault("gameMin")); // the day counters were reset
    }

    [Fact]
    public void Level_follows_total_feathers_and_the_streak_counts_active_days()
    {
        Assert.Equal(1, RewardsCatalog.LevelFor(0).Level);
        Assert.Equal(1, RewardsCatalog.LevelFor(19).Level);
        Assert.Equal(2, RewardsCatalog.LevelFor(20).Level);
        Assert.Equal(100, RewardsCatalog.LevelFor(10_000_000).Level);
        // every level costs more than the one before, and a month of ordinary use (about 35 a day) is level 10, not level 50
        for (var n = 3; n <= 100; n++)
            Assert.True(RewardsCatalog.FeathersFor(n) - RewardsCatalog.FeathersFor(n - 1) > RewardsCatalog.FeathersFor(n - 1) - RewardsCatalog.FeathersFor(n - 2));
        Assert.InRange(RewardsCatalog.FeathersFor(10), 900, 1200);
        Assert.True(RewardsCatalog.FeathersFor(50) > 15_000);
        Assert.True(RewardsCatalog.FeathersFor(100) > 60_000);

        var (_, e, _, _) = Engine(new DateTime(2026, 11, 8, 12, 0, 0));
        foreach (var d in new[] { 8, 7, 6, 4 }) e.State.Active[$"2026-11-{d:00}"] = 5; // the 5th is missing
        Assert.Equal(3, e.Streak);
    }

    [Fact]
    public void Seven_nights_in_a_row_pay_the_bonus_once()
    {
        var (_, e, _, _) = Engine(new DateTime(2026, 11, 8, 12, 0, 0));
        for (var d = 2; d <= 8; d++) e.State.Active[$"2026-11-{d:00}"] = 10;
        Assert.Equal(7, e.Streak);
        var before = e.State.Feathers;
        for (var i = 0; i < 4; i++) e.Evaluate(); // the streak medals follow the streak on later passes
        var after = e.State.Feathers;
        Assert.True(after - before >= 10);
        Assert.Contains("streak:2026-11-08", e.State.Claimed);
        e.Evaluate();
        e.Evaluate();
        Assert.Equal(after, e.State.Feathers);
    }

    [Fact]
    public void The_rewards_state_survives_a_save_and_load()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-rw-" + Guid.NewGuid().ToString("N"));
        var store = new Store(folder);
        store.State.Rewards.Medals["hello"] = new DateTime(2026, 11, 2);
        store.State.Rewards.Counters["ramFreed"] = 7;
        store.Save();
        var again = new Store(folder);
        again.Load();
        Assert.Equal(7, again.State.Rewards.Counters["ramFreed"]);
        Assert.True(again.State.Rewards.Medals.ContainsKey("hello"));
        // an older file that has no Rewards section still loads
        File.WriteAllText(Path.Combine(folder, "state.json"), "{\"Settings\":{}}");
        var old = new Store(folder);
        old.Load();
        Assert.Empty(old.State.Rewards.Medals);
    }
}

public class LegacyNightTests
{
    private static Hub NewHub() =>
        new(new Store(Path.Combine(Path.GetTempPath(), "shabpa-night-" + Guid.NewGuid().ToString("N"))) { ReadOnly = true }, dryRun: true, demo: true);

    [Fact]
    public void An_upgrade_removes_exactly_what_the_old_night_page_made()
    {
        var hub = NewHub();
        var mine = hub.Queue.Add(PowerAction.Shutdown, DateTime.Now.AddHours(3), source: "mine");
        var myPlan = new DownloadPlan { LauncherKey = "steam", LauncherName = "Steam", ExePath = @"C:\steam.exe" };
        hub.State.DownloadPlans.Add(myPlan);
        var myWake = new WakeSchedule { Hour = 7, Minute = 30 };
        hub.State.WakeSchedules.Add(myWake);

        var s = hub.Settings.Overnight;
        var plan = new DownloadPlan { LauncherKey = "epic", LauncherName = "Epic", ExePath = @"C:\epic.exe" };
        var wake = new WakeSchedule { Hour = 1, Minute = 0 };
        hub.State.DownloadPlans.Add(plan);
        hub.State.WakeSchedules.Add(wake);
        var end = hub.Queue.Add(PowerAction.Sleep, DateTime.Now.AddHours(5), source: "night");
        var slot = new OutageSlot { Hour = 14 };
        hub.Queue.Add(PowerAction.Hibernate, DateTime.Now.AddHours(2), source: slot.Source);
        (s.InternetOn, s.WakeId) = (true, wake.Id);
        s.PlanIds.Add(plan.Id);
        s.CommandIds.Add(end.Id);
        s.Outages.Add(slot);

        Assert.True(LegacyNight.HasLeftovers(s));
        Assert.True(LegacyNight.Cleanup(hub));

        Assert.False(LegacyNight.HasLeftovers(hub.Settings.Overnight));
        Assert.Equal(mine.Id, Assert.Single(hub.State.Queue).Id);
        Assert.Equal(myPlan.Id, Assert.Single(hub.State.DownloadPlans).Id);
        Assert.Equal(myWake.Id, Assert.Single(hub.State.WakeSchedules).Id);
        Assert.False(LegacyNight.Cleanup(hub)); // nothing left to do the second time
    }

    [Fact]
    public void A_state_file_from_3_13_0_with_night_settings_still_loads()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-night-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "state.json"), "{\"Settings\":{\"Overnight\":{\"InternetOn\":true,\"Outages\":[{\"Hour\":14}]}}}");
        var store = new Store(folder);
        store.Load();
        Assert.True(store.State.Settings.Overnight.InternetOn);
        Assert.Equal(14, store.State.Settings.Overnight.Outages[0].Hour);
    }
}
