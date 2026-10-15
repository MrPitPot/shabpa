using System.IO;
using System.Xml.Linq;

namespace Shabpa.Tests;

public class FaTests
{
    [Fact]
    public void Digits_are_converted_to_persian() => Assert.Equal("۰۷:۳۰", Fa.Clock(7, 30));

    [Fact]
    public void Gregorian_date_maps_to_jalali() => Assert.Equal("۲۲ مهر", Fa.Date(new DateTime(2026, 10, 14)));

    [Theory]
    [InlineData(40, "۴۰ ثانیه")]
    [InlineData(300, "۵ دقیقه")]
    [InlineData(7500, "۲ ساعت و ۵ دقیقه")]
    [InlineData(90000, "۱ روز و ۱ ساعت")]
    public void Durations_read_naturally(int seconds, string expected) =>
        Assert.Equal(expected, Fa.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Empty_or_full_week_is_every_day()
    {
        Assert.Equal("هر روز", Fa.Days([]));
        Assert.Equal("هر روز", Fa.Days(Fa.Week));
        Assert.Equal("شنبه، جمعه", Fa.Days([DayOfWeek.Friday, DayOfWeek.Saturday]));
    }
}

public class ScheduleTests
{
    private static readonly DateTime Wed = new(2026, 10, 14, 22, 0, 0); // a Wednesday

    [Fact]
    public void Later_today_stays_today() => Assert.Equal(new DateTime(2026, 10, 14, 23, 30, 0), Schedule.Next(23, 30, [], Wed));

    [Fact]
    public void Earlier_time_rolls_to_tomorrow() => Assert.Equal(new DateTime(2026, 10, 15, 1, 30, 0), Schedule.Next(1, 30, [], Wed));

    [Fact]
    public void The_exact_current_minute_is_not_now() => Assert.Equal(Wed.AddDays(1), Schedule.Next(22, 0, [], Wed));

    [Fact]
    public void Weekday_filter_skips_ahead() =>
        Assert.Equal(new DateTime(2026, 10, 17, 7, 0, 0), Schedule.Next(7, 0, [DayOfWeek.Saturday], Wed));

    [Fact]
    public void Same_weekday_earlier_time_waits_a_week() =>
        Assert.Equal(new DateTime(2026, 10, 21, 7, 0, 0), Schedule.Next(7, 0, [DayOfWeek.Wednesday], Wed));

    [Fact]
    public void No_day_picked_follows_the_time() => Assert.Equal(new DateTime(2026, 10, 15, 3, 0, 0), Schedule.On(null, 3, 0, Wed));

    [Fact]
    public void Picked_day_is_used_as_is() =>
        Assert.Equal(new DateTime(2026, 10, 25, 3, 0, 0), Schedule.On(new DateTime(2026, 10, 25), 3, 0, Wed));

    [Fact]
    public void Picked_day_in_the_past_is_not_moved() =>
        Assert.True(Schedule.On(Wed.Date, 3, 0, Wed) < Wed);
}

public class ClockFieldTests
{
    [Theory]
    [InlineData("", 23, true)]
    [InlineData("2", 23, true)]
    [InlineData("23", 23, true)]
    [InlineData("24", 23, false)]
    [InlineData("230", 23, false)]
    [InlineData("59", 59, true)]
    [InlineData("60", 59, false)]
    [InlineData("1a", 59, false)]
    public void Accepts(string text, int max, bool ok) => Assert.Equal(ok, ClockField.Accepts(text, max));

    [Theory]
    [InlineData("1", 23, false)]
    [InlineData("2", 23, false)]
    [InlineData("3", 23, true)]
    [InlineData("09", 23, true)]
    [InlineData("5", 59, false)]
    [InlineData("6", 59, true)]
    public void Complete(string text, int max, bool done) => Assert.Equal(done, ClockField.Complete(text, max));
}

public class CommandQueueTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 22, 0, 0);

    [Fact]
    public void Nothing_is_due_early()
    {
        var queue = new CommandQueue([]);
        queue.Add(PowerAction.Shutdown, Now.AddMinutes(1));
        Assert.Empty(queue.TakeDue(Now));
        Assert.Single(queue.Items);
    }

    [Fact]
    public void One_shot_runs_once_and_leaves_the_queue()
    {
        var queue = new CommandQueue([]);
        queue.Add(PowerAction.Sleep, Now);
        Assert.Equal(PowerAction.Sleep, Assert.Single(queue.TakeDue(Now.AddSeconds(1))).Action);
        Assert.Empty(queue.Items);
        Assert.Empty(queue.TakeDue(Now.AddSeconds(2)));
    }

    [Fact]
    public void A_command_missed_while_the_app_was_closed_is_dropped_not_run()
    {
        var queue = new CommandQueue([]);
        queue.Add(PowerAction.Shutdown, Now.AddHours(-3));
        var missed = new List<ScheduledCommand>();
        Assert.Empty(queue.TakeDue(Now, missed));
        Assert.Single(missed);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public void Repeating_command_moves_to_its_next_day()
    {
        var queue = new CommandQueue([]);
        queue.Add(PowerAction.Shutdown, new DateTime(2026, 10, 14, 22, 0, 0), [DayOfWeek.Wednesday, DayOfWeek.Friday]);
        Assert.Single(queue.TakeDue(Now));
        Assert.Equal(new DateTime(2026, 10, 16, 22, 0, 0), Assert.Single(queue.Items).DueAt);
    }

    [Fact]
    public void Missed_repeating_command_is_skipped_but_kept()
    {
        var queue = new CommandQueue([]);
        queue.Add(PowerAction.Shutdown, Now.AddDays(-2), Fa.Week);
        Assert.Empty(queue.TakeDue(Now));
        Assert.Equal(Now.AddDays(1), Assert.Single(queue.Items).DueAt);
    }
}

public class RuleEngineTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 22, 0, 0);

    private static SensorSnapshot Snap(double down = 0, double up = 0, double cpu = 0, double gpu = 0, double idle = 0,
        bool game = false, bool recording = false, params string[] processes) => new()
    {
        DownMbps = down, UpMbps = up, CpuLoad = cpu, GpuLoad = gpu, IdleSeconds = idle,
        GameRunning = game, Recording = recording, Processes = new HashSet<string>(processes),
    };

    private static RuleEngine Engine(Rule rule)
    {
        var engine = new RuleEngine();
        engine.Activate(rule);
        return engine;
    }

    [Fact]
    public void No_rule_means_off() => Assert.Equal(RuleState.Off, new RuleEngine().Evaluate(Snap(), T0).State);

    [Fact]
    public void Download_rule_waits_for_the_download_to_start()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Network, ThresholdMbps = 1, HoldMinutes = 5, WaitForStart = true });
        Assert.Equal(RuleState.Waiting, engine.Evaluate(Snap(down: 0.1), T0).State);
        // Even an hour of silence does not fire before a transfer was seen.
        Assert.Equal(RuleState.Waiting, engine.Evaluate(Snap(down: 0.1), T0.AddSeconds(20)).State);
        Assert.Equal(RuleState.NotMet, engine.Evaluate(Snap(down: 40), T0.AddSeconds(40)).State);
        Assert.Equal(RuleState.Counting, engine.Evaluate(Snap(down: 0.2), T0.AddSeconds(43)).State);
    }

    [Fact]
    public void Fires_only_after_the_condition_held_continuously()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Network, ThresholdMbps = 1, HoldMinutes = 1, WaitForStart = false });
        var t = T0;
        for (var i = 0; i < 19; i++, t = t.AddSeconds(3))
            Assert.Equal(RuleState.Counting, engine.Evaluate(Snap(down: 0.3), t).State);

        // A burst resets the clock...
        Assert.Equal(RuleState.NotMet, engine.Evaluate(Snap(down: 12), t).State);
        t = t.AddSeconds(3);
        var status = engine.Evaluate(Snap(down: 0.3), t);
        Assert.Equal(RuleState.Counting, status.State);
        Assert.Equal(TimeSpan.Zero, status.Elapsed);

        // ...and a full minute later it fires.
        for (var i = 0; i < 19; i++)
        {
            t = t.AddSeconds(3);
            Assert.Equal(RuleState.Counting, engine.Evaluate(Snap(down: 0.3), t).State);
        }
        Assert.Equal(RuleState.Fire, engine.Evaluate(Snap(down: 0.3), t.AddSeconds(3)).State);
    }

    [Fact]
    public void Upload_rule_ignores_download_speed()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Network, Upload = true, ThresholdMbps = 1, HoldMinutes = 1, WaitForStart = false });
        Assert.Equal(RuleState.Counting, engine.Evaluate(Snap(down: 90, up: 0.1), T0).State);
        Assert.Equal(RuleState.NotMet, engine.Evaluate(Snap(down: 0, up: 5), T0.AddSeconds(3)).State);
    }

    [Fact]
    public void Recording_blocks_and_resets_the_count()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Network, HoldMinutes = 1, WaitForStart = false, ExceptRecording = true });
        engine.Evaluate(Snap(), T0);
        Assert.Equal(RuleState.Blocked, engine.Evaluate(Snap(recording: true), T0.AddSeconds(30)).State);
        var after = engine.Evaluate(Snap(), T0.AddSeconds(33));
        Assert.Equal(RuleState.Counting, after.State);
        Assert.Equal(TimeSpan.Zero, after.Elapsed);
    }

    [Fact]
    public void Game_exception_is_opt_in()
    {
        var rule = new Rule { Kind = RuleKind.Load, HoldMinutes = 1, ExceptGame = false, ExceptRecording = false };
        Assert.Equal(RuleState.Counting, Engine(rule).Evaluate(Snap(game: true), T0).State);
        rule.ExceptGame = true;
        Assert.Equal(RuleState.Blocked, Engine(rule).Evaluate(Snap(game: true), T0).State);
    }

    [Fact]
    public void Time_spent_asleep_does_not_count()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Network, HoldMinutes = 5, WaitForStart = false });
        engine.Evaluate(Snap(), T0);
        engine.Evaluate(Snap(), T0.AddSeconds(3));
        // The machine slept for eight hours: without the gap check this would fire the moment it wakes.
        var woke = engine.Evaluate(Snap(), T0.AddHours(8));
        Assert.Equal(RuleState.Counting, woke.State);
        Assert.Equal(TimeSpan.Zero, woke.Elapsed);
    }

    [Fact]
    public void Load_rule_needs_both_cpu_and_gpu_quiet()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Load, LoadPercent = 15, HoldMinutes = 1 });
        Assert.Equal(RuleState.NotMet, engine.Evaluate(Snap(cpu: 5, gpu: 80), T0).State);
        Assert.Equal(RuleState.NotMet, engine.Evaluate(Snap(cpu: 60, gpu: 2), T0.AddSeconds(3)).State);
        Assert.Equal(RuleState.Counting, engine.Evaluate(Snap(cpu: 5, gpu: 2), T0.AddSeconds(6)).State);
    }

    [Fact]
    public void Idle_rule_uses_windows_idle_time()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Idle, HoldMinutes = 10 });
        Assert.Equal(RuleState.NotMet, engine.Evaluate(Snap(idle: 1), T0).State);
        Assert.Equal(RuleState.Counting, engine.Evaluate(Snap(idle: 300), T0).State);
        Assert.Equal(RuleState.Fire, engine.Evaluate(Snap(idle: 600), T0).State);
    }

    [Fact]
    public void Process_rule_waits_for_the_program_then_for_it_to_close()
    {
        var engine = Engine(new Rule { Kind = RuleKind.ProcessExit, ProcessName = "Blender.exe", HoldMinutes = 0 });
        Assert.Equal(RuleState.Waiting, engine.Evaluate(Snap(processes: "chrome"), T0).State);
        Assert.Equal(RuleState.NotMet, engine.Evaluate(Snap(processes: ["chrome", "blender"]), T0.AddSeconds(3)).State);
        Assert.Equal(RuleState.Fire, engine.Evaluate(Snap(processes: "chrome"), T0.AddSeconds(6)).State);
    }

    [Fact]
    public void Deactivate_stops_everything()
    {
        var engine = Engine(new Rule { Kind = RuleKind.Idle, HoldMinutes = 1 });
        engine.Deactivate();
        Assert.Equal(RuleState.Off, engine.Evaluate(Snap(idle: 9999), T0).State);
    }
}

public class EnergyTrackerTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 12, 0, 0);

    [Fact]
    public void A_hundred_watts_for_an_hour_is_a_hundred_watt_hours()
    {
        var days = new List<EnergyDay>();
        var tracker = new EnergyTracker(days);
        for (var i = 0; i <= 1200; i++)
            tracker.Add(new SensorSnapshot { Time = T0.AddSeconds(3 * i), TotalWatts = 100 });
        Assert.Equal(100, Assert.Single(days).ActiveWh, 3);
        Assert.Equal(3600, days[0].MeasuredSeconds, 3);
    }

    [Fact]
    public void Energy_is_split_by_how_the_pc_is_used()
    {
        var days = new List<EnergyDay>();
        var tracker = new EnergyTracker(days);
        tracker.Add(new SensorSnapshot { Time = T0, TotalWatts = 360 });
        tracker.Add(new SensorSnapshot { Time = T0.AddSeconds(10), TotalWatts = 360, GameRunning = true });
        tracker.Add(new SensorSnapshot { Time = T0.AddSeconds(20), TotalWatts = 360, IdleSeconds = 1 });
        tracker.Add(new SensorSnapshot { Time = T0.AddSeconds(30), TotalWatts = 360, IdleSeconds = 900 });
        Assert.Equal(1, days[0].GamingWh, 6);
        Assert.Equal(1, days[0].ActiveWh, 6);
        Assert.Equal(1, days[0].IdleWh, 6);
    }

    [Fact]
    public void A_sleep_gap_is_not_counted_as_consumption()
    {
        var days = new List<EnergyDay>();
        var tracker = new EnergyTracker(days);
        tracker.Add(new SensorSnapshot { Time = T0, TotalWatts = 500 });
        tracker.Add(new SensorSnapshot { Time = T0.AddHours(7), TotalWatts = 500 });
        Assert.Empty(days);
    }

    [Fact]
    public void Summary_always_covers_thirty_days_oldest_first()
    {
        var days = new List<EnergyDay> { new() { Date = DateOnly.FromDateTime(T0).AddDays(-2), ActiveWh = 2000, MeasuredSeconds = 86400 } };
        var summary = new EnergyTracker(days).Summarize(T0);
        Assert.Equal(30, summary.Days.Count);
        Assert.Equal(DateOnly.FromDateTime(T0), summary.Days[^1].Date);
        Assert.Equal(2, summary.TotalKwh, 6);
        Assert.Equal(1.0 / 30, summary.Coverage, 6);
    }
}

public class PowerHistoryTests
{
    private static readonly DateTime Day = new(2026, 10, 13);
    private static DateTime At(int h, int m) => Day.AddHours(h).AddMinutes(m);
    private static PowerEvent Ev(int h, int m, PowerEventKind kind, string detail = "") => new(At(h, m), kind, detail);

    [Fact]
    public void Restart_collapses_to_one_timeline_entry()
    {
        var tidy = PowerHistory.Tidy([
            Ev(15, 18, PowerEventKind.Restart, "ویندوز"),
            Ev(15, 18, PowerEventKind.Shutdown),
            Ev(15, 19, PowerEventKind.Boot),
        ]);
        var shown = tidy.Where(e => !e.Hidden).ToList();
        Assert.Equal(PowerEventKind.Restart, Assert.Single(shown).Kind);
        Assert.Equal("ویندوز", shown[0].Detail);
    }

    [Fact]
    public void Fast_startup_resume_after_shutdown_is_shown_as_boot()
    {
        var tidy = PowerHistory.Tidy([
            Ev(0, 34, PowerEventKind.Shutdown, "از منوی استارت"),
            Ev(0, 34, PowerEventKind.Hibernate),
            Ev(13, 10, PowerEventKind.Wake),
            Ev(13, 10, PowerEventKind.Wake, "Power Button"),
        ]);
        Assert.Equal([PowerEventKind.Shutdown, PowerEventKind.Boot], tidy.Select(e => e.Kind));
    }

    [Fact]
    public void Crash_is_placed_before_the_boot_that_reported_it()
    {
        var tidy = PowerHistory.Tidy([
            Ev(9, 0, PowerEventKind.Boot),
            Ev(9, 0, PowerEventKind.Unexpected),
            Ev(9, 1, PowerEventKind.Unexpected),
        ]);
        Assert.Equal([PowerEventKind.Unexpected, PowerEventKind.Boot], tidy.Select(e => e.Kind));
    }

    [Fact]
    public void Sleep_and_wake_keep_the_wake_source()
    {
        var tidy = PowerHistory.Tidy([
            Ev(1, 0, PowerEventKind.Sleep),
            Ev(7, 30, PowerEventKind.Wake),
            Ev(7, 30, PowerEventKind.Wake, "بیدارباش شب‌پا"),
        ]);
        Assert.Equal(2, tidy.Count);
        Assert.Equal("بیدارباش شب‌پا", tidy[1].Detail);
    }

    [Fact]
    public void On_time_is_split_across_days_and_excludes_sleep()
    {
        var from = Day;
        var now = Day.AddDays(1).AddHours(2); // 02:00 the next day
        List<PowerEvent> events =
        [
            Ev(1, 0, PowerEventKind.Sleep),     // on 00:00-01:00 (was already on)
            Ev(8, 0, PowerEventKind.Wake),      // on 08:00 -> now
        ];
        var onTime = PowerHistory.OnTime(events, from, now);
        Assert.Equal(TimeSpan.FromHours(17), onTime[DateOnly.FromDateTime(Day)]);
        Assert.Equal(TimeSpan.FromHours(2), onTime[DateOnly.FromDateTime(Day.AddDays(1))]);
    }

    [Fact]
    public void Restart_does_not_lose_on_time()
    {
        var tidy = PowerHistory.Tidy([
            Ev(10, 0, PowerEventKind.Boot),
            Ev(15, 0, PowerEventKind.Restart),
            Ev(15, 1, PowerEventKind.Boot),
        ]);
        var onTime = PowerHistory.OnTime(tidy, Day, At(20, 0));
        Assert.Equal(TimeSpan.FromHours(10) - TimeSpan.FromMinutes(1), onTime[DateOnly.FromDateTime(Day)]);
    }

    [Fact]
    public void Events_are_mapped_from_the_windows_log()
    {
        var none = () => new Dictionary<string, string>();
        Assert.Equal(PowerEventKind.Boot, PowerHistory.Map("Microsoft-Windows-Kernel-General", 12, Day, none)!.Kind);
        Assert.Equal(PowerEventKind.Unexpected, PowerHistory.Map("Microsoft-Windows-Kernel-Power", 41, Day, none)!.Kind);
        Assert.Equal(PowerEventKind.Hibernate,
            PowerHistory.Map("Microsoft-Windows-Kernel-Power", 42, Day, () => new() { ["TargetState"] = "5" })!.Kind);
        Assert.Equal(PowerEventKind.Sleep,
            PowerHistory.Map("Microsoft-Windows-Kernel-Power", 42, Day, () => new() { ["TargetState"] = "4" })!.Kind);

        var restart = PowerHistory.Map("User32", 1074, Day, () => new() { ["param1"] = @"C:\Windows\Explorer.EXE (PC)", ["param5"] = "restart" })!;
        Assert.Equal(PowerEventKind.Restart, restart.Kind);
        Assert.Equal("از منوی استارت", restart.Detail);

        // Event id 1 exists in many providers; only the power troubleshooter's is a wake.
        Assert.Null(PowerHistory.Map("Microsoft-Windows-Kernel-General", 1, Day, none));
    }
}

public class SystemToolsTests
{
    [Fact]
    public void Powercfg_requests_are_parsed()
    {
        const string output = """
            DISPLAY:
            [PROCESS] \Device\HarddiskVolume3\Program Files\Google\Chrome\Application\chrome.exe
            Video Wake Lock

            SYSTEM:
            [DRIVER] Realtek High Definition Audio (HDAUDIO\FUNC_01&VEN_10EC)
            An audio stream is currently in use.
            [PROCESS] \Device\HarddiskVolume3\Program Files\Google\Chrome\Application\chrome.exe
            Video Wake Lock

            AWAYMODE:
            None.

            EXECUTION:
            None.

            PERFBOOST:
            None.

            ACTIVELOCKSCREEN:
            None.
            """;
        var items = SleepBlockers.Parse(output);
        Assert.Equal(3, items.Count);
        Assert.Equal(new SleepBlocker("DISPLAY", "PROCESS", "chrome.exe", "Video Wake Lock"), items[0]);
        Assert.Equal("Realtek High Definition Audio", items[1].Name);
        Assert.Equal("SYSTEM", items[1].Category);
        Assert.Equal("An audio stream is currently in use.", items[1].Reason);
    }

    [Fact]
    public void Nothing_blocking_gives_an_empty_list() =>
        Assert.Empty(SleepBlockers.Parse("DISPLAY:\nNone.\n\nSYSTEM:\nNone.\n"));

    [Fact]
    public void Wake_task_xml_is_valid_and_wakes_the_machine()
    {
        var schedule = new WakeSchedule { Hour = 7, Minute = 30, Days = [DayOfWeek.Saturday, DayOfWeek.Monday] };
        var xml = XDocument.Parse(WakeScheduler.BuildXml(schedule, @"C:\Apps\R&D\Shabpa.exe", new DateTime(2026, 10, 14, 22, 0, 0)));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal("true", xml.Descendants(ns + "WakeToRun").Single().Value);
        Assert.Equal("2026-10-14T07:30:00", xml.Descendants(ns + "StartBoundary").Single().Value);
        Assert.Equal(["Monday", "Saturday"], xml.Descendants(ns + "DaysOfWeek").Single().Elements().Select(e => e.Name.LocalName));
        Assert.Equal(@"C:\Apps\R&D\Shabpa.exe", xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--wake", xml.Descendants(ns + "Arguments").Single().Value);
    }

    [Fact]
    public void Wake_task_without_days_runs_daily()
    {
        var xml = WakeScheduler.BuildXml(new WakeSchedule(), "Shabpa.exe", DateTime.Now);
        Assert.Contains("<ScheduleByDay>", xml);
        Assert.DoesNotContain("ScheduleByWeek", xml);
    }

    [Theory]
    [InlineData("0x00000001", "0x00000001", true)]
    [InlineData("0x00000000", "0x00000001", false)]
    [InlineData("0x00000002", "0x00000000", false)]
    public void Wake_timer_setting_is_read_from_powercfg(string ac, string dc, bool expected)
    {
        var output = $"""
            Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
              Subgroup GUID: 238c9fa8-0aad-41ed-83f4-97be242c8f20  (Sleep)
                Power Setting GUID: bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d  (Allow wake timers)
                  Possible Setting Index: 000
                  Possible Setting Friendly Name: Disable
                  Possible Setting Index: 001
                  Possible Setting Friendly Name: Enable
                Current AC Power Setting Index: {ac}
                Current DC Power Setting Index: {dc}
            """;
        Assert.Equal(expected, WakeScheduler.ParseWakeTimers(output));
    }

    [Fact]
    public void Recorder_list_accepts_messy_input() =>
        Assert.Equal(["obs64", "bdcam", "action"], SensorHub.RecorderNames(" OBS64.exe ,bdcam؛ ; Action\n"));
}

public class TelegramBotTests
{
    private readonly Settings _settings = new();
    private readonly List<PowerAction> _requested = [];
    private int _saves;

    private TelegramBot Bot() => new(() => _settings, () => _saves++, new BotHost
    {
        Status = () => "STATUS",
        Request = a => { _requested.Add(a); return 15; },
        Cancel = () => "CANCELLED",
    });

    [Theory]
    [InlineData("/shutdown", BotCommand.Shutdown)]
    [InlineData("/restart@MyShabpaBot", BotCommand.Restart)]
    [InlineData("خاموش", BotCommand.Shutdown)]
    [InlineData(" خواب ", BotCommand.Sleep)]
    [InlineData("قفل صفحه", BotCommand.Lock)]
    [InlineData("وضعیت", BotCommand.Status)]
    [InlineData("لغو", BotCommand.Cancel)]
    [InlineData("سلام", BotCommand.Unknown)]
    [InlineData("/format_c", BotCommand.Unknown)]
    public void Commands_are_recognised(string text, BotCommand expected) => Assert.Equal(expected, TelegramBot.Parse(text));

    [Fact]
    public void A_stranger_cannot_run_anything()
    {
        var bot = Bot();
        bot.Handle(111, "خاموش", "x", false);
        bot.Handle(111, "/shutdown", "x", false);
        Assert.Empty(_requested);
        Assert.Equal(0, _settings.TelegramChatId);
    }

    [Fact]
    public void Pairing_needs_the_code_and_then_locks_to_that_chat()
    {
        var bot = Bot();
        var code = bot.PairCode;
        Assert.Contains("وصل شد", bot.Handle(111, code, "Amir", false));
        Assert.Equal(111, _settings.TelegramChatId);
        Assert.Equal(BotState.Connected, bot.State);
        Assert.NotEqual(code, bot.PairCode);
        Assert.Equal(1, _saves);

        // Someone else, even with the old code, gets nowhere.
        bot.Handle(222, code, "Eve", false);
        bot.Handle(222, "خاموش", "Eve", false);
        Assert.Equal(111, _settings.TelegramChatId);
        Assert.Empty(_requested);

        bot.Handle(111, "خاموش", "Amir", false);
        Assert.Equal([PowerAction.Shutdown], _requested);
    }

    [Fact]
    public void Wrong_guesses_burn_the_pairing_code()
    {
        var bot = Bot();
        var code = bot.PairCode;
        for (var i = 0; i < 5; i++) bot.Handle(999, "000000", "x", false);
        Assert.NotEqual(code, bot.PairCode);
        bot.Handle(999, code, "x", false);
        Assert.Equal(0, _settings.TelegramChatId);
    }

    [Fact]
    public void Old_messages_are_not_executed_when_the_pc_comes_back()
    {
        _settings.TelegramChatId = 111;
        var bot = Bot();
        Assert.Contains("قدیمیه", bot.Handle(111, "خاموش", "Amir", stale: true));
        Assert.Empty(_requested);
        Assert.Equal("STATUS", bot.Handle(111, "وضعیت", "Amir", stale: true));
    }

    [Fact]
    public void Stale_pairing_attempt_is_ignored()
    {
        var bot = Bot();
        Assert.Null(bot.Handle(111, bot.PairCode, "Amir", stale: true));
        Assert.Equal(0, _settings.TelegramChatId);
    }

    [Fact]
    public void Token_shape_is_checked_and_token_is_encrypted_at_rest()
    {
        const string token = "123456789:FAKE-TOKEN-for-tests-only-0000000000";
        Assert.True(TelegramBot.LooksLikeToken(token));
        Assert.False(TelegramBot.LooksLikeToken("hello"));
        var stored = TelegramBot.Protect(token);
        Assert.DoesNotContain("FAKE-TOKEN", stored);
        Assert.Equal(token, TelegramBot.Unprotect(stored));
        Assert.Equal("", TelegramBot.Unprotect("not-base64!"));
    }
}

public class SharedBotTests
{
    private readonly Settings _settings = new() { Relay = { Endpoint = "https://relay.test" } };
    private readonly List<PowerAction> _requested = [];
    private readonly List<System.Text.Json.JsonElement> _out = [];

    private TelegramBot Bot()
    {
        var bot = new TelegramBot(() => _settings, () => { }, new BotHost
        {
            Status = () => "STATUS",
            Request = a => { _requested.Add(a); return 15; },
            Cancel = () => "CANCELLED",
        });
        bot.RelayTap = text => _out.Add(System.Text.Json.JsonDocument.Parse(text).RootElement.Clone());
        return bot;
    }

    private static string Msg(long chat, string text, DateTimeOffset? at = null) =>
        $$"""{"t":"msg","chat":{{chat}},"text":"{{text}}","date":{{(at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()}}}""";

    [Fact]
    public void Shared_bot_is_used_only_without_a_personal_token()
    {
        var bot = Bot();
        Assert.True(bot.Shared);
        _settings.TelegramTokenProtected = "something";
        Assert.False(bot.Shared);
        _settings.TelegramTokenProtected = "";
        _settings.Relay.Endpoint = "http://plain.test";
        Assert.False(bot.Shared);
    }

    [Fact]
    public void Nothing_runs_until_the_link_is_allowed_on_the_pc()
    {
        var bot = Bot();
        bot.OnRelay("""{"t":"pair","chat":42,"name":"Amir","username":"amir"}""");
        Assert.Equal(42, bot.Pending?.Chat);
        bot.OnRelay(Msg(42, "خاموش"));
        Assert.Empty(_requested);
        Assert.Empty(_out);

        bot.ApproveLink();
        Assert.Null(bot.Pending);
        Assert.Equal("pair-ok", _out[^1].GetProperty("t").GetString());
        Assert.Equal("Amir", Assert.Single(_settings.Relay.Chats).Name);

        bot.OnRelay(Msg(42, "خاموش"));
        Assert.Equal([PowerAction.Shutdown], _requested);
        Assert.Equal("send", _out[^1].GetProperty("t").GetString());
        Assert.Equal(42, _out[^1].GetProperty("chat").GetInt64());
    }

    [Fact]
    public void Declined_and_stale_requests_do_nothing()
    {
        var bot = Bot();
        bot.OnRelay("""{"t":"pair","chat":7,"name":"Eve","username":""}""");
        bot.RejectLink();
        Assert.Equal("pair-no", _out[^1].GetProperty("t").GetString());
        Assert.Empty(_settings.Relay.Chats);

        _settings.Relay.Chats.Add(new RelayChat { Id = 42, Name = "Amir" });
        bot.OnRelay(Msg(42, "خاموش", DateTimeOffset.UtcNow.AddMinutes(-10)));
        Assert.Empty(_requested);
        Assert.Contains("قدیمیه", _out[^1].GetProperty("text").GetString());
    }

    [Fact]
    public void The_server_list_of_linked_chats_wins_and_local_names_stay()
    {
        _settings.Relay.Chats.Add(new RelayChat { Id = 1, Name = "Phone" });
        _settings.Relay.Chats.Add(new RelayChat { Id = 2, Name = "Gone" });
        var bot = Bot();
        bot.OnRelay("""{"t":"ready","bot":"ShabpaBot","chats":[1,3]}""");
        Assert.Equal([1L, 3L], _settings.Relay.Chats.Select(c => c.Id).Order());
        Assert.Equal("Phone", _settings.Relay.Chats.Single(c => c.Id == 1).Name);
        Assert.Equal(BotState.Connected, bot.State);
        Assert.Equal("ShabpaBot", bot.BotName);

        bot.OnRelay("""{"t":"unlinked","chat":1}""");
        bot.OnRelay("""{"t":"unlinked","chat":3}""");
        Assert.Equal(BotState.WaitingForPair, bot.State);
        Assert.Equal("code", _out[^1].GetProperty("t").GetString());
    }

    [Fact]
    public void Link_code_becomes_a_deep_link()
    {
        var bot = Bot();
        bot.OnRelay("""{"t":"code","code":"K7QM3XPD","ttl":600,"bot":"ShabpaBot"}""");
        Assert.Equal("K7QM-3XPD", bot.LinkCodeDisplay);
        Assert.Equal("https://t.me/ShabpaBot?start=K7QM3XPD", bot.LinkUrl);
    }
}

public class StoreTests
{
    [Fact]
    public void State_survives_a_round_trip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(folder);
            store.State.Settings.Tariff = 450;
            store.State.Queue.Add(new ScheduledCommand { Action = PowerAction.Hibernate, DueAt = new DateTime(2026, 10, 15, 1, 30, 0), RepeatDays = [DayOfWeek.Friday] });
            store.State.ActiveRule = new Rule { Kind = RuleKind.ProcessExit, ProcessName = "blender" };
            store.State.Energy.Add(new EnergyDay { Date = new DateOnly(2026, 10, 14), GamingWh = 12.5 });
            store.Save();

            var loaded = new Store(folder);
            loaded.Load();
            Assert.Equal(450, loaded.State.Settings.Tariff);
            var cmd = Assert.Single(loaded.State.Queue);
            Assert.Equal(PowerAction.Hibernate, cmd.Action);
            Assert.Equal([DayOfWeek.Friday], cmd.RepeatDays);
            Assert.Equal("blender", loaded.State.ActiveRule!.ProcessName);
            Assert.Equal(12.5, Assert.Single(loaded.State.Energy).GamingWh);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Corrupt_state_does_not_stop_the_app()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "state.json"), "{ this is not json");
            var store = new Store(folder);
            store.Load();
            Assert.Empty(store.State.Queue);
            Assert.True(File.Exists(Path.Combine(folder, "state.json.broken")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Read_only_store_never_touches_disk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N"));
        new Store(folder) { ReadOnly = true }.Save();
        Assert.False(Directory.Exists(folder));
    }
}

public class TelegramBusyTests
{
    [Fact]
    public void A_second_power_command_during_a_countdown_is_refused_clearly()
    {
        var settings = new Settings { TelegramChatId = 111 };
        var bot = new TelegramBot(() => settings, () => { }, new BotHost { Status = () => "", Request = _ => -1, Cancel = () => "" });
        Assert.Contains("شمارش معکوس دیگه", bot.Handle(111, "خاموش", "Amir", false));
    }

    [Fact]
    public void Lock_reports_done_immediately()
    {
        var settings = new Settings { TelegramChatId = 111 };
        var bot = new TelegramBot(() => settings, () => { }, new BotHost { Status = () => "", Request = _ => 0, Cancel = () => "" });
        Assert.Contains("انجام شد", bot.Handle(111, "قفل صفحه", "Amir", false));
    }
}
