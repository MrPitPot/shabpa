using System.Xml.Linq;

namespace Shabpa.Tests;

public class TrafficMeterTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 12, 0, 0);

    private static TrafficMeter Meter() => new(pid => pid switch
    {
        10 or 11 => new ProcInfo("chrome", "Google Chrome", @"C:\Apps\chrome.exe"),
        20 => new ProcInfo("steam", "Steam", @"C:\Apps\steam.exe"),
        _ => null,
    });

    [Fact]
    public void Bytes_become_megabits_per_second()
    {
        var meter = Meter();
        meter.Record(20, 1_250_000, upload: false); // 10 megabits
        meter.Record(20, 125_000, upload: true);    // 1 megabit
        var steam = Assert.Single(meter.Snapshot(2, T0));
        Assert.Equal(5, steam.DownMbps, 6);
        Assert.Equal(0.5, steam.UpMbps, 6);
        Assert.Equal(1_250_000, steam.DownBytes);
        Assert.Equal(T0, steam.LastUpload);
    }

    [Fact]
    public void Processes_of_one_app_are_merged()
    {
        var meter = Meter();
        meter.Record(10, 1000, false);
        meter.Record(11, 500, false);
        var chrome = Assert.Single(meter.Snapshot(1, T0));
        Assert.Equal("chrome", chrome.Key);
        Assert.Equal("Google Chrome", chrome.Title);
        Assert.Equal(1500, chrome.DownBytes);
    }

    [Fact]
    public void Rates_drop_to_zero_but_totals_stay()
    {
        var meter = Meter();
        meter.Record(20, 4000, true);
        meter.Snapshot(1, T0);
        var later = Assert.Single(meter.Snapshot(3, T0.AddSeconds(3)));
        Assert.Equal(0, later.UpMbps);
        Assert.Equal(4000, later.UpBytes);
        Assert.Equal(T0, later.LastUpload);
    }

    [Fact]
    public void Traffic_of_vanished_processes_is_not_lost()
    {
        var meter = Meter();
        meter.Record(999, 700, false);
        var other = Assert.Single(meter.Snapshot(1, T0));
        Assert.Equal(TrafficMeter.Other, other.Key);
        Assert.Equal(700, other.DownBytes);
    }

    [Fact]
    public void Totals_reset_at_midnight()
    {
        var meter = Meter();
        meter.Load(null, T0);
        meter.Record(20, 4000, false);
        meter.Snapshot(1, T0);
        Assert.Empty(meter.Snapshot(1, T0.AddDays(1)));
    }

    [Fact]
    public void Todays_totals_survive_a_restart_but_yesterdays_do_not()
    {
        var meter = Meter();
        meter.Load(null, T0);
        meter.Record(20, 4000, false);
        meter.Snapshot(1, T0);
        var saved = meter.Export();

        var sameDay = Meter();
        sameDay.Load(saved, T0.AddHours(3));
        sameDay.Record(20, 1000, false);
        Assert.Equal(5000, Assert.Single(sameDay.Snapshot(1, T0.AddHours(3))).DownBytes);

        var nextDay = Meter();
        nextDay.Load(saved, T0.AddDays(1));
        Assert.Empty(nextDay.Snapshot(1, T0.AddDays(1)));
    }
}

public class LimitPlannerTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 21, 0, 0);

    private static AppTraffic App(string key, int uploadedMinutesAgo = 0) =>
        new(key, key, $@"C:\Apps\{key}.exe", 0, 0, 1000, 1000, Now.AddMinutes(-uploadedMinutesAgo));

    [Fact]
    public void Without_a_game_only_manual_limits_apply()
    {
        var plan = LimitPlanner.Plan([new AppLimit { Key = "onedrive", UpMbps = 2 }], guard: true, 1, gameKey: "", [App("onedrive"), App("chrome")], Now);
        var only = Assert.Single(plan);
        Assert.Equal(("onedrive", 2.0, false), (only.Key, only.UpMbps, only.Automatic));
    }

    [Fact]
    public void In_game_every_recent_uploader_is_capped_except_the_game()
    {
        var plan = LimitPlanner.Plan([], true, 1, "cs2", [App("cs2"), App("chrome"), App("onedrive")], Now);
        Assert.Equal(["chrome", "onedrive"], plan.Select(l => l.Key));
        Assert.All(plan, l => Assert.True(l.Automatic && l.UpMbps == 1 && !l.Blocked));
    }

    [Fact]
    public void Guard_off_means_no_automatic_caps()
    {
        Assert.Empty(LimitPlanner.Plan([], guard: false, 1, "cs2", [App("chrome")], Now));
    }

    [Fact]
    public void Exempt_apps_and_windows_itself_are_left_alone()
    {
        var plan = LimitPlanner.Plan([new AppLimit { Key = "flclashcore", Exempt = true }], true, 1, "cs2",
            [App("flclashcore"), App("system"), App("shabpa"), App(TrafficMeter.Other), App("chrome")], Now);
        Assert.Equal("chrome", Assert.Single(plan).Key);
    }

    [Fact]
    public void Apps_that_stopped_uploading_are_not_capped()
    {
        Assert.Empty(LimitPlanner.Plan([], true, 1, "cs2", [App("chrome", uploadedMinutesAgo: 10)], Now));
    }

    [Fact]
    public void The_stricter_of_manual_and_game_cap_wins()
    {
        var manual = new[] { new AppLimit { Key = "loose", UpMbps = 5 }, new AppLimit { Key = "tight", UpMbps = 0.5 } };
        var plan = LimitPlanner.Plan(manual, true, 1, "cs2", [App("loose"), App("tight")], Now).ToDictionary(l => l.Key);
        Assert.Equal(1, plan["loose"].UpMbps);
        Assert.Equal(0.5, plan["tight"].UpMbps);
    }

    [Fact]
    public void A_blocked_app_stays_blocked_in_game()
    {
        var plan = LimitPlanner.Plan([new AppLimit { Key = "torrent", Path = @"C:\t\torrent.exe", Blocked = true }], true, 1, "cs2", [App("torrent")], Now);
        Assert.True(Assert.Single(plan).Blocked);
    }

    [Fact]
    public void Signature_changes_only_when_windows_needs_a_change()
    {
        var a = LimitPlanner.Plan([new AppLimit { Key = "x", UpMbps = 2 }], false, 1, "", [], Now);
        var same = LimitPlanner.Plan([new AppLimit { Key = "x", UpMbps = 2, Exempt = true, Title = "X" }], false, 1, "", [], Now);
        var different = LimitPlanner.Plan([new AppLimit { Key = "x", UpMbps = 3 }], false, 1, "", [], Now);
        Assert.Equal(LimitPlanner.Signature(a), LimitPlanner.Signature(same));
        Assert.NotEqual(LimitPlanner.Signature(a), LimitPlanner.Signature(different));
        Assert.Equal("", LimitPlanner.Signature([]));
    }
}

public class PingStatsTests
{
    [Fact]
    public void No_samples_is_empty() => Assert.Equal(PingStats.Empty, PingStats.From([]));

    [Fact]
    public void Steady_line_has_no_jitter()
    {
        var stats = PingStats.From([40, 40, 40, 40]);
        Assert.Equal((40.0, 40.0, 0.0, 0.0, 40.0), (stats.Last!.Value, stats.Average, stats.Jitter, stats.LossPercent, stats.Baseline));
    }

    [Fact]
    public void Jitter_is_the_average_step_between_replies()
    {
        var stats = PingStats.From([40, 60, 40, 60]);
        Assert.Equal(20, stats.Jitter);
        Assert.Equal(50, stats.Average);
    }

    [Fact]
    public void Lost_packets_count_as_loss_not_as_zero_ms()
    {
        var stats = PingStats.From([50, null, 50, null]);
        Assert.Equal(50, stats.LossPercent);
        Assert.Equal(50, stats.Average);
        Assert.Null(stats.Last);
    }

    [Fact]
    public void A_single_spike_does_not_move_the_baseline()
    {
        Assert.Equal(40, PingStats.From([40, 41, 40, 39, 400, 40, 40]).Baseline);
    }

    [Fact]
    public void Total_outage_is_reported_as_such()
    {
        var stats = PingStats.From([null, null]);
        Assert.Equal(100, stats.LossPercent);
        Assert.Null(stats.Last);
    }
}

public class NetTextTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(11 * 1024, "11.0 KB")]
    [InlineData(144179200, "138 MB")]
    [InlineData(1395864371, "1.3 GB")]
    public void Bytes_are_readable(long bytes, string expected) => Assert.Equal(expected, Fa.Bytes(bytes));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(0.41, "410 Kb/s")]
    [InlineData(8.54, "8.5 Mb/s")]
    [InlineData(250, "250 Mb/s")]
    public void Rates_are_readable(double mbps, string expected) => Assert.Equal(expected, Fa.Rate(mbps));

    [Fact]
    public void Elevated_autostart_task_runs_at_logon_with_highest_rights()
    {
        var xml = XDocument.Parse(Autostart.BuildTaskXml(@"C:\Apps\R&D\Shabpa.exe", @"PC\Ali"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal("HighestAvailable", xml.Descendants(ns + "RunLevel").Single().Value);
        Assert.Single(xml.Descendants(ns + "LogonTrigger"));
        Assert.Equal(@"C:\Apps\R&D\Shabpa.exe", xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--minimized", xml.Descendants(ns + "Arguments").Single().Value);
        Assert.All(xml.Descendants(ns + "UserId"), u => Assert.Equal(@"PC\Ali", u.Value));
    }
}

public class GameDownloadCapTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 20, 0, 0);

    private static AppTraffic App(string key, DateTime up, DateTime down) => new(key, key, "", 0, 0, 0, 0, up, down);

    [Fact]
    public void Download_cap_applies_only_to_apps_that_were_downloading()
    {
        var apps = new[] { App("steam", default, Now), App("chrome", default, Now.AddMinutes(-30)), App("cs2", default, Now) };
        var plan = LimitPlanner.Plan([], true, 0, "cs2", apps, Now, downCapMbps: 5);
        var steam = Assert.Single(plan);
        Assert.Equal("steam", steam.Key);
        Assert.Equal(5, steam.DownMbps);
        Assert.Equal(0, steam.UpMbps);
    }

    [Fact]
    public void Upload_and_download_caps_combine_and_the_stricter_user_limit_wins()
    {
        var manual = new[] { new AppLimit { Key = "steam", DownMbps = 2 } };
        var apps = new[] { App("steam", Now, Now) };
        var steam = Assert.Single(LimitPlanner.Plan(manual, true, 1, "cs2", apps, Now, downCapMbps: 10));
        Assert.Equal(2, steam.DownMbps);
        Assert.Equal(1, steam.UpMbps);
    }

    [Fact]
    public void Nothing_is_capped_without_a_game_or_when_the_guard_is_off()
    {
        var apps = new[] { App("steam", Now, Now) };
        Assert.Empty(LimitPlanner.Plan([], true, 1, "", apps, Now, downCapMbps: 5));
        Assert.Empty(LimitPlanner.Plan([], false, 1, "cs2", apps, Now, downCapMbps: 5));
    }
}
