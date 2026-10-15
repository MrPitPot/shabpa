using Shabpa.Core;

namespace Shabpa.Tests;

public class ComfortTests
{
    private static readonly DateTime T0 = new(2026, 10, 15, 21, 0, 0);

    [Theory]
    [InlineData("22:00", "07:00", 23, 0, true)]
    [InlineData("22:00", "07:00", 3, 0, true)]
    [InlineData("22:00", "07:00", 12, 0, false)]
    [InlineData("08:00", "18:00", 12, 0, true)]
    [InlineData("08:00", "18:00", 19, 0, false)]
    [InlineData("08:00", "08:00", 8, 0, false)]
    [InlineData("nope", "07:00", 3, 0, false)]
    public void Night_window_handles_midnight(string from, string to, int hour, int minute, bool expected) =>
        Assert.Equal(expected, ComfortTime.InWindow(new DateTime(2026, 10, 15, hour, minute, 0), from, to));

    [Fact]
    public void Persian_digits_are_read_in_times() => Assert.Equal(new TimeSpan(22, 30, 0), ComfortTime.Parse("۲۲:۳۰"));

    [Fact]
    public void Break_comes_after_the_active_time_and_not_before()
    {
        var plan = new BreakPlanner();
        var s = new ComfortSettings { BreakOn = true, BreakEveryMinutes = 15 };
        string? first = null;
        var minutes = 0;
        for (var i = 0; i < 20 * 60 && first is null; i++)
        {
            first = plan.Tick(T0.AddSeconds(i), true, false, s, false);
            if (first is null) minutes = i / 60;
        }
        Assert.NotNull(first);
        Assert.InRange(minutes, 14, 15);
    }

    [Fact]
    public void Being_away_five_minutes_counts_as_a_break_and_off_means_no_reminders()
    {
        var plan = new BreakPlanner();
        var s = new ComfortSettings { BreakOn = true, BreakEveryMinutes = 15 };
        var now = T0;
        for (var i = 0; i < 10 * 60; i++) plan.Tick(now = now.AddSeconds(1), true, false, s, false);
        for (var i = 0; i < 6 * 60; i++) plan.Tick(now = now.AddSeconds(1), false, false, s, false);
        Assert.True(plan.ActiveMinutes < 1);
        var off = new ComfortSettings { BreakOn = false };
        for (var i = 0; i < 3 * 3600; i++) Assert.Null(plan.Tick(now = now.AddSeconds(1), true, false, off, false));
    }

    [Fact]
    public void Reminders_wait_for_the_game_to_end_unless_asked()
    {
        var plan = new BreakPlanner();
        var s = new ComfortSettings { BreakOn = true, BreakEveryMinutes = 15, BreakInGame = false };
        var now = T0;
        string? during = null;
        for (var i = 0; i < 20 * 60; i++) during ??= plan.Tick(now = now.AddSeconds(1), true, true, s, false);
        Assert.Null(during);
        Assert.NotNull(plan.Tick(now.AddSeconds(1), true, false, s, false));
    }

    [Fact]
    public void Sleep_ramp_fades_volume_and_screen_evenly()
    {
        var length = TimeSpan.FromMinutes(30);
        Assert.Equal(0, SleepRamp.Progress(T0, length, T0));
        Assert.Equal(0.5, SleepRamp.Progress(T0, length, T0.AddMinutes(15)), 3);
        Assert.Equal(1, SleepRamp.Progress(T0, length, T0.AddHours(2)));
        Assert.Equal(0.3, SleepRamp.Volume(0.6, 0.5), 3);
        Assert.Equal(0, SleepRamp.Volume(0.6, 1), 3);
        Assert.Equal(SleepRamp.DimTarget, SleepRamp.Dim(0.1, 1), 3);
        Assert.Equal(0.1, SleepRamp.Dim(0.1, 0), 3);
    }

    [Fact]
    public void A_games_own_profile_replaces_the_general_choices()
    {
        var o = new OverlaySettings { Layout = "card", Items = ["fps"] };
        o.Profiles.Add(new OverlayProfile { Game = "CS2", Layout = "bar", Items = ["fps", "gpu", "ping"], Scale = 1.4 });
        var effective = o.WithProfile("cs2");
        Assert.Equal("bar", effective.Layout);
        Assert.Equal(["fps", "gpu", "ping"], effective.Items);
        Assert.Equal(1.4, effective.Scale);
        Assert.Same(o, o.WithProfile("other"));
        Assert.Equal("card", o.Layout); // the general settings are untouched
    }

    [Fact]
    public void A_snapshot_remembers_the_current_overlay()
    {
        var o = new OverlaySettings { Layout = "rings", Look = "cyber", Items = ["gpu", "cpu"] };
        var p = o.Snapshot("dota2");
        Assert.Equal(("dota2", "rings", "cyber"), (p.Game, p.Layout, p.Look));
        Assert.Equal(["gpu", "cpu"], p.Items);
    }

    [Fact]
    public void Wake_timer_can_be_set_and_cleared()
    {
        using var timer = new WakeTimer();
        var at = DateTime.Now.AddHours(6);
        Assert.True(timer.Set(at));
        Assert.Equal(at, timer.At);
        timer.Clear();
        Assert.Null(timer.At);
    }

    [Fact]
    public void Master_volume_can_be_read_and_set_back()
    {
        var now = MasterVolume.Get();
        if (now is null) return; // no audio device on this machine
        Assert.InRange(now.Value, 0, 1);
        Assert.True(MasterVolume.Set(now.Value));
        Assert.Equal(now.Value, MasterVolume.Get()!.Value, 2);
    }

    [Fact]
    public void Every_break_message_exists_in_both_languages() => Assert.Equal(BreakPlanner.Fa.Length, BreakPlanner.En.Length);
}
