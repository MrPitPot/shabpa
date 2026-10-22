using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Kit;

namespace Shabpa.Tests;

// ---- the brain: what Bidar chooses, when he must keep still, how he reacts ----------------------------------------------------

public class BidarBrainTests
{
    private static readonly DateTime Noon = new(2026, 10, 22, 14, 0, 0);

    private static BrainContext Free(DateTime? now = null) => new() { Now = now ?? Noon, IdleFor = TimeSpan.FromSeconds(10), CanFly = true };

    private static List<BidarAct?> Run(int seed, int steps, BrainContext ctx)
    {
        var brain = new BidarBrain(seed);
        var list = new List<BidarAct?>();
        var now = ctx.Now;
        for (var i = 0; i < steps; i++)
        {
            now += brain.NextGap();
            list.Add(brain.Pick(ctx with { Now = now }));
        }
        return list;
    }

    [Fact]
    public void The_same_seed_gives_the_same_life()
    {
        Assert.Equal(Run(7, 80, Free()), Run(7, 80, Free()));
        Assert.NotEqual(Run(7, 80, Free()), Run(8, 80, Free()));
    }

    [Fact]
    public void Gaps_are_between_four_and_fifteen_seconds_and_really_vary()
    {
        var brain = new BidarBrain(1);
        var gaps = Enumerable.Range(0, 300).Select(_ => brain.NextGap()).ToList();
        Assert.All(gaps, g => Assert.InRange(g.TotalSeconds, 4, 15));
        Assert.True(gaps.Max() - gaps.Min() > TimeSpan.FromSeconds(6));
    }

    [Fact]
    public void An_act_does_not_repeat_inside_its_cooldown()
    {
        var brain = new BidarBrain(3);
        var seen = new List<BidarAct>();
        for (var i = 0; i < 40; i++)
            if (brain.Pick(Free()) is { } a) seen.Add(a); // the clock never moves: every act is on cooldown after its first use
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.True(seen.Count >= 5);
    }

    [Fact]
    public void An_act_can_come_again_once_its_cooldown_is_over()
    {
        var brain = new BidarBrain(3);
        var first = Enumerable.Range(0, 40).Select(_ => brain.Pick(Free())).OfType<BidarAct>().ToList();
        var later = Enumerable.Range(0, 40).Select(_ => brain.Pick(Free(Noon.AddHours(1)))).OfType<BidarAct>().ToList();
        Assert.Contains(later, a => first.Contains(a));
    }

    [Theory]
    [InlineData("life", PauseReason.Disabled)]
    [InlineData("motion", PauseReason.NoMotion)]
    [InlineData("memory", PauseReason.LowMemory)]
    [InlineData("game", PauseReason.GameRunning)]
    [InlineData("hidden", PauseReason.Hidden)]
    [InlineData("inactive", PauseReason.Inactive)]
    [InlineData("popup", PauseReason.PopupOpen)]
    [InlineData("typing", PauseReason.UserBusy)]
    [InlineData("asleep", PauseReason.Asleep)]
    [InlineData("busy", PauseReason.Busy)]
    public void Every_pause_condition_keeps_him_still(string what, PauseReason reason)
    {
        var ctx = what switch
        {
            "life" => Free() with { LifeEnabled = false },
            "motion" => Free() with { MotionOn = false },
            "memory" => Free() with { LowMemory = true },
            "game" => Free() with { GameRunning = true },
            "hidden" => Free() with { WindowVisible = false },
            "inactive" => Free() with { WindowActive = false },
            "popup" => Free() with { PopupOpen = true },
            "typing" => Free() with { UserBusy = true },
            "asleep" => Free() with { Asleep = true },
            _ => Free() with { Busy = true },
        };
        Assert.Equal(reason, BidarBrain.Pause(ctx));
        var brain = new BidarBrain(5);
        for (var i = 0; i < 50; i++) Assert.Null(brain.Pick(ctx));
        Assert.Equal(PauseReason.None, BidarBrain.Pause(Free()));
    }

    [Fact]
    public void He_flies_only_when_there_is_somewhere_safe_to_land()
    {
        for (var seed = 0; seed < 40; seed++)
        {
            Assert.DoesNotContain(BidarAct.Fly, Run(seed, 60, Free() with { CanFly = false }));
            Assert.DoesNotContain(null, Run(seed, 5, Free() with { CanFly = false }));
        }
        Assert.Contains(Enumerable.Range(0, 40).SelectMany(s => Run(s, 60, Free())), a => a == BidarAct.Fly);
    }

    [Fact]
    public void Hours_of_night_and_long_idle_make_him_sleepy()
    {
        int Dozes(DateTime when, TimeSpan idle) =>
            Enumerable.Range(0, 60).SelectMany(s => Run(s, 40, Free(when) with { IdleFor = idle })).Count(a => a == BidarAct.Doze);
        var day = Dozes(Noon, TimeSpan.FromSeconds(40));
        var night = Dozes(new DateTime(2026, 10, 22, 23, 30, 0), TimeSpan.FromSeconds(40));
        var longIdle = Dozes(Noon, TimeSpan.FromMinutes(5));
        Assert.True(night > day, $"night {night} vs day {day}");
        Assert.True(longIdle > day * 2, $"long idle {longIdle} vs day {day}");
        Assert.True(BidarBrain.IsNight(new DateTime(2026, 1, 1, 3, 0, 0)));
        Assert.False(BidarBrain.IsNight(new DateTime(2026, 1, 1, 12, 0, 0)));
        Assert.DoesNotContain(Enumerable.Range(0, 30).SelectMany(s => Run(s, 40, Free() with { IdleFor = TimeSpan.FromSeconds(2) })), a => a is BidarAct.Doze or BidarAct.Yawn or BidarAct.Stretch);
    }

    [Theory]
    [InlineData(BidarCue.DownloadDone, BidarAct.Celebrate)]
    [InlineData(BidarCue.BadPing, BidarAct.Worry)]
    [InlineData(BidarCue.ShutdownSoon, BidarAct.Alert)]
    [InlineData(BidarCue.Error, BidarAct.Dizzy)]
    public void App_events_get_their_own_reaction(BidarCue cue, BidarAct expected)
    {
        var brain = new BidarBrain(1);
        Assert.Equal(expected, brain.OnCue(cue, Free()));
        Assert.Null(brain.OnCue(cue, Free(Noon.AddSeconds(1)))); // not twice in a row
        Assert.Equal(expected, brain.OnCue(cue, Free(Noon.AddMinutes(30))));
    }

    [Fact]
    public void Reactions_interrupt_typing_but_stay_quiet_in_games_hidden_windows_and_static_mode()
    {
        var brain = new BidarBrain(1);
        Assert.NotNull(brain.OnCue(BidarCue.Error, Free() with { UserBusy = true, Busy = true, Asleep = true }));
        foreach (var quiet in new[]
                 {
                     Free() with { GameRunning = true }, Free() with { WindowVisible = false }, Free() with { MotionOn = false },
                     Free() with { LowMemory = true }, Free() with { LifeEnabled = false }, Free() with { PopupOpen = true },
                 })
            Assert.Null(new BidarBrain(1).OnCue(BidarCue.DownloadDone, quiet));
    }

    [Fact]
    public void A_bad_line_makes_him_less_playful_for_a_while()
    {
        int Playful(bool worried) => Enumerable.Range(0, 80).SelectMany(s =>
        {
            var brain = new BidarBrain(s);
            if (worried) brain.OnCue(BidarCue.BadPing, Free());
            return Enumerable.Range(0, 30).Select(i => brain.Pick(Free(Noon.AddSeconds(5 + i * 200 % 60 + i))));
        }).Count(a => a is BidarAct.Hop or BidarAct.Hello or BidarAct.WingWave);
        Assert.True(Playful(true) < Playful(false));
    }

    [Fact]
    public void Being_clicked_makes_him_fonder_and_it_fades()
    {
        var brain = new BidarBrain(1);
        Assert.Equal(0, brain.Fondness(Noon));
        for (var i = 0; i < 4; i++) brain.RegisterClick(Noon.AddSeconds(i * 3));
        var now = brain.Fondness(Noon.AddSeconds(10));
        Assert.InRange(now, 0.4, 1);
        Assert.True(brain.Fondness(Noon.AddMinutes(30)) < 0.05);
    }

    [Fact]
    public void Seven_gentle_clicks_in_a_row_are_the_easter_egg_but_a_frantic_burst_is_not()
    {
        var gentle = new BidarBrain(1);
        var results = Enumerable.Range(0, 7).Select(i => gentle.RegisterClick(Noon.AddMilliseconds(i * 900))).ToList();
        Assert.Equal([false, false, false, false, false, false, true], results);

        var burst = new BidarBrain(1);
        Assert.DoesNotContain(true, Enumerable.Range(0, 7).Select(i => burst.RegisterClick(Noon.AddMilliseconds(i * 200))));

        var slow = new BidarBrain(1);
        Assert.DoesNotContain(true, Enumerable.Range(0, 7).Select(i => slow.RegisterClick(Noon.AddSeconds(i * 3)))); // too far apart
    }

    [Fact]
    public void The_cursor_makes_him_curious_only_once_in_a_while_and_only_when_free()
    {
        var brain = new BidarBrain(1);
        Assert.Equal(BidarAct.Curious, brain.OnCursorNear(Free()));
        Assert.Null(brain.OnCursorNear(Free(Noon.AddSeconds(5))));
        Assert.Equal(BidarAct.Curious, brain.OnCursorNear(Free(Noon.AddSeconds(30))));
        Assert.Null(new BidarBrain(1).OnCursorNear(Free() with { GameRunning = true }));
        Assert.Null(new BidarBrain(1).OnCursorNear(Free() with { Asleep = true }));
    }

    [Fact]
    public void Ping_and_deadline_cues_follow_their_thresholds()
    {
        double?[] good = [20, 22, 21, 25, 20, 19];
        double?[] lossy = [20, null, null, null, 20, 19];
        double?[] slow = [300, 280, 310, 260, 400, 290];
        Assert.False(BidarCues.PingIsBad(good));
        Assert.True(BidarCues.PingIsBad(lossy));
        Assert.True(BidarCues.PingIsBad(slow));
        Assert.False(BidarCues.PingIsBad([null, null, null])); // not enough samples yet
        var now = Noon;
        Assert.True(BidarCues.SomethingDueSoon([now.AddMinutes(5)], now));
        Assert.False(BidarCues.SomethingDueSoon([now.AddMinutes(30), now.AddMinutes(-2)], now));
        Assert.Equal(BidarCue.BadPing, BidarCues.Next(lossy, [now.AddMinutes(5)], now));
        Assert.Equal(BidarCue.ShutdownSoon, BidarCues.Next(good, [now.AddMinutes(5)], now));
        Assert.Null(BidarCues.Next(good, [now.AddHours(2)], now));
    }

    [Fact]
    public void Every_pose_is_sane_and_the_sleepy_wings_are_folded_not_turned_into_the_belly()
    {
        foreach (var mood in Enum.GetValues<BidarMood>())
        {
            var p = BidarPoses.For(mood);
            Assert.InRange(p.WingL, -45, 160);
            Assert.InRange(p.WingR, -45, 160);
            Assert.InRange(p.Squash, 0.88, 1.1);
        }
        var sleepy = BidarPoses.For(BidarMood.Sleepy);
        Assert.InRange(sleepy.WingL, 0, 20);
        Assert.InRange(sleepy.WingR, 0, 20);
        var props = new HashSet<string> { "star", "stars", "heart", "z", "?", "!", "magnifier", "clock", "plane", "power", "down", "signal", "shield", "sweat", "confetti" };
        foreach (var mood in Enum.GetValues<BidarMood>())
            if (BidarPoses.For(mood).Fx is { } fx) Assert.Contains(fx, props);
    }
}

// ---- flight paths: stay inside, avoid what must not be covered, mirror in Persian ------------------------------------------

public class BidarFlightTests
{
    private const double W = 50, H = 60;
    private static readonly Box Area = new(0, 0, 900, 560);

    [Fact]
    public void Home_is_the_end_corner_and_mirrors_in_Persian()
    {
        var ltr = Perches.Home(Area, W, H, rtl: false);
        var rtl = Perches.Home(Area, W, H, rtl: true);
        Assert.True(ltr.X > Area.W / 2);
        Assert.True(rtl.X < Area.W / 2);
        Assert.Equal(ltr.Y, rtl.Y);
        Assert.Equal(Area.W - W - ltr.X, rtl.X, 6); // exact mirror images
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_candidate_stays_inside_the_window(bool rtl)
    {
        var all = Perches.Candidates(Area, W, H, rtl);
        Assert.True(all.Count >= 8);
        Assert.All(all, p => Assert.True(Area.Contains(new Box(p.X, p.Y, W, H)), p.ToString()));
        Assert.Equal(Perches.Home(Area, W, H, rtl), all[0]);
    }

    [Fact]
    public void A_resting_place_is_valid_only_when_nothing_is_under_it()
    {
        var home = Perches.Home(Area, W, H, false);
        Assert.True(Perches.IsClear(home, W, H, Area, []));
        Assert.False(Perches.IsClear(home, W, H, Area, [new Box(home.X + 10, home.Y + 10, 30, 12)])); // a button under him
        Assert.False(Perches.IsClear(home, W, H, Area, [new Box(home.X - 2, home.Y + 20, 4, 4)])); // even the padding counts
        Assert.False(Perches.IsClear(new Pt(-5, 100), W, H, Area, [])); // half outside
        Assert.False(Perches.IsClear(new Pt(Area.W - 20, 100), W, H, Area, []));
        var valid = Perches.Valid(Area, W, H, false, [new Box(home.X, home.Y, W, H)]);
        Assert.DoesNotContain(home, valid);
        Assert.NotEmpty(valid);
    }

    [Fact]
    public void After_a_resize_or_page_change_he_moves_to_the_nearest_free_place_or_is_pulled_home_inside()
    {
        var current = new Pt(400, 480);
        Assert.Equal(current, Perches.Revalidate(current, Area, W, H, false, [])); // still free: stays
        var blocked = new Box(380, 470, 100, 80);
        var moved = Perches.Revalidate(current, Area, W, H, false, [blocked]);
        Assert.NotEqual(current, moved);
        Assert.True(Perches.IsClear(moved, W, H, Area, [blocked]));
        // nothing free at all: home, clamped into the area (never outside the window)
        var everything = new Box(-10, -10, 2000, 2000);
        var fallback = Perches.Revalidate(current, new Box(0, 0, 120, 90), W, H, true, [everything]);
        Assert.True(new Box(0, 0, 120, 90).Contains(new Box(fallback.X, fallback.Y, W, H)));
    }

    [Fact]
    public void Flights_stay_inside_the_window_whatever_the_size_or_the_spots()
    {
        var rng = new Random(11);
        for (var i = 0; i < 300; i++)
        {
            var area = new Box(0, 0, 300 + rng.Next(900), 200 + rng.Next(500));
            var from = new Pt(rng.NextDouble() * (area.W - W), rng.NextDouble() * (area.H - H));
            var to = new Pt(rng.NextDouble() * (area.W - W), rng.NextDouble() * (area.H - H));
            var plan = FlightPlanner.Plan(from, to, W, H, area, [], rng);
            if (plan is null) { Assert.True(Pt.Distance(from, to) < 1); continue; }
            for (var s = 0; s <= 100; s++)
            {
                var p = plan.At(s / 100.0);
                Assert.True(area.Contains(new Box(p.X, p.Y, W, H)), $"{p} outside {area}");
            }
        }
    }

    [Fact]
    public void Flights_go_around_buttons_and_text()
    {
        var rng = new Random(5);
        var from = new Pt(60, 480);
        var to = new Pt(780, 480);
        // a wall of controls across the middle of the bottom area, leaving the top free
        var noGo = new List<Box> { new(300, 300, 300, 260) };
        var plan = FlightPlanner.Plan(from, to, W, H, Area, noGo, rng);
        Assert.NotNull(plan);
        for (var s = 4; s <= 36; s++)
        {
            var p = plan!.At(s / 40.0);
            Assert.False(new Box(p.X, p.Y, W, H).Intersects(noGo[0]), $"step {s}: {p}");
        }
        Assert.True(Enumerable.Range(0, 100).Select(s => plan!.At(s / 100.0).Y).Min() < 300); // it went over the top
    }

    [Fact]
    public void When_every_way_is_blocked_there_is_no_flight()
    {
        var wall = new Box(300, 0, 60, 560); // floor to ceiling
        Assert.Null(FlightPlanner.Plan(new Pt(60, 480), new Pt(780, 480), W, H, Area, [wall], new Random(1)));
        Assert.Null(FlightPlanner.Plan(new Pt(60, 480), new Pt(60, 480), W, H, Area, [], new Random(1))); // already there
    }

    [Fact]
    public void The_same_seed_plans_the_same_flight()
    {
        FlightPlan? Make(int seed) => FlightPlanner.Plan(new Pt(60, 480), new Pt(780, 100), W, H, Area, [], new Random(seed));
        Assert.Equal(Make(3), Make(3));
        Assert.NotEqual(Make(3)!.C1, Make(4)!.C1);
    }

    [Fact]
    public void Choosing_picks_a_safe_far_spot_or_nothing()
    {
        var valid = Perches.Valid(Area, W, H, false, []);
        var current = Perches.Home(Area, W, H, false);
        for (var seed = 0; seed < 30; seed++)
        {
            var pick = Perches.Choose(valid, current, new Random(seed));
            Assert.NotNull(pick);
            Assert.True(Pt.Distance(pick!.Value, current) >= 120);
            Assert.Contains(pick.Value, valid);
        }
        Assert.Null(Perches.Choose([current], current, new Random(1)));
    }

    [Fact]
    public void The_flight_frames_start_and_end_on_the_spots_and_keep_the_body_in_range()
    {
        var plan = FlightPlanner.Plan(new Pt(60, 480), new Pt(780, 100), W, H, Area, [], new Random(2))!;
        var total = FlightMotion.Total(plan);
        Assert.Equal(plan.From, FlightMotion.Frame(plan, 0).Pos);
        Assert.Equal(plan.To, FlightMotion.Frame(plan, total).Pos);
        Assert.True(FlightMotion.Frame(plan, total).Done);
        Assert.False(FlightMotion.Frame(plan, total - 0.01).Done);
        var sawAir = false;
        var minSquash = 9.0;
        for (var t = 0.0; t <= total; t += 0.01)
        {
            var f = FlightMotion.Frame(plan, t);
            Assert.InRange(f.WingL, -45, 160);
            Assert.InRange(f.WingR, -45, 160);
            Assert.InRange(f.Squash, 0.84, 1.12);
            Assert.InRange(f.Tilt, -14, 14);
            Assert.True(Area.Contains(new Box(f.Pos.X, f.Pos.Y, W, H)));
            sawAir |= f.Airborne;
            minSquash = Math.Min(minSquash, f.Squash);
        }
        Assert.True(sawAir);
        Assert.True(minSquash < 0.92); // he crouches before the jump and squashes on landing
    }

    [Fact]
    public void While_flying_the_wings_flap()
    {
        var plan = FlightPlanner.Plan(new Pt(60, 480), new Pt(780, 100), W, H, Area, [], new Random(2))!;
        var wings = Enumerable.Range(0, 60).Select(i => FlightMotion.Frame(plan, FlightMotion.Crouch + 0.2 + i * 0.01).WingL).ToList();
        Assert.True(wings.Max() - wings.Min() > 60);
    }
}

// ---- the setting ------------------------------------------------------------------------------------------------------------

public class BidarSettingTests
{
    [Fact]
    public void Idle_life_is_on_by_default_and_survives_a_backup_round_trip()
    {
        Assert.True(new Settings().BidarIdleLife);
        var source = new AppState();
        source.Settings.BidarIdleLife = false;
        var json = SettingsBackup.Export(source, includeProfiles: false, includeTasks: false, DateTime.Now);
        Assert.Contains("BidarIdleLife", json);
        var target = new AppState();
        Assert.True(target.Settings.BidarIdleLife);
        SettingsBackup.Apply(SettingsBackup.Read(json)!, target);
        Assert.False(target.Settings.BidarIdleLife);

        source.Settings.BidarIdleLife = true;
        SettingsBackup.Apply(SettingsBackup.Read(SettingsBackup.Export(source, false, false, DateTime.Now))!, target);
        Assert.True(target.Settings.BidarIdleLife);
    }

    [Fact]
    public void An_old_backup_without_the_field_keeps_the_default_on()
    {
        var json = SettingsBackup.Export(new AppState(), false, false, DateTime.Now).Replace("\"BidarIdleLife\"", "\"OldUnknownField\"");
        Assert.DoesNotContain("BidarIdleLife", json);
        var target = new AppState();
        target.Settings.BidarIdleLife = false;
        SettingsBackup.Apply(SettingsBackup.Read(json)!, target);
        Assert.True(target.Settings.BidarIdleLife);
    }

    [Fact]
    public void The_settings_page_offers_the_switch_in_both_languages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shabpa.slnx"))) dir = dir.Parent;
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Shabpa", "Pages", "SettingsPage.cs"));
        Assert.Contains("Idle life & flights", text);
        Assert.Contains("زندگی و پرواز جغد", text);
        Assert.Contains("SetBidarIdleLife", text);
    }
}

// ---- the rig and the controller, without a window ---------------------------------------------------------------------------

[Collection("Wpf")]
public class BidarLifeWpfTests
{
    private sealed class Rig
    {
        public readonly BidarOwl Owl = new() { Width = 50, Height = 60 };
        private readonly System.Windows.Interop.HwndSource _source;
        public readonly Canvas Stage = new() { Width = 900, Height = 560 };
        public DateTime Clock = new(2026, 10, 22, 14, 0, 0);
        public BrainContext Host = new();
        public bool Rtl;
        public List<Box> NoGo = [];
        public BidarCue? Cue;
        public BidarLife Life = null!;
        public int Scans;

        public Rig(int seed = 4)
        {
            // a message-only host window (never shown): it gives the elements a presentation source, so IsVisible is true
            _source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("bidar-test") { ParentWindow = new IntPtr(-3), Width = 900, Height = 560 }) { RootVisual = Stage };
            Stage.Children.Add(Owl);
            Stage.Measure(new Size(900, 560));
            Stage.Arrange(new Rect(0, 0, 900, 560));
            Life = new BidarLife(Owl, Stage, () => Host, () => { Scans++; return NoGo; }, () => Rtl, () => Cue, seed, () => Clock);
        }
    }

    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
    }

    private static void WithMotion(bool on, Action test)
    {
        var before = Motion.TestOverride;
        Motion.TestOverride = on;
        try { OnSta(test); }
        finally { Motion.TestOverride = before; }
    }

    private static void Pump(int ms)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>True when the property is being driven by a running animation (the render clock has to tick once first).</summary>
    private static bool Animated(DependencyObject o, DependencyProperty p)
    {
        Pump(60);
        return DependencyPropertyHelper.GetValueSource(o, p).IsAnimated;
    }

    [Fact]
    public void He_starts_in_the_home_corner_and_a_settled_owl_costs_nothing_but_one_timer()
    {
        WithMotion(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            Assert.True(r.Life.Running);
            Assert.Equal(Perches.Home(new Box(0, 0, 900, 560), 50, 60, false), r.Life.Rest);
            Assert.Equal(r.Life.Rest.X, Canvas.GetLeft(r.Owl));
            Assert.False(r.Life.RenderingHooked); // the render clock is hooked only during a flight
            Assert.False(r.Life.Flying);
            r.Life.Stop();
            Assert.False(r.Life.TimerEnabled);
        });
    }

    [Fact]
    public void In_Persian_the_home_corner_is_the_left()
    {
        WithMotion(true, () =>
        {
            var r = new Rig { Rtl = true };
            r.Life.Start();
            Assert.True(r.Life.Rest.X < 100);
            r.Life.Stop();
        });
    }

    [Fact]
    public void A_flight_hooks_the_render_clock_moves_inside_the_window_and_unhooks_on_landing()
    {
        WithMotion(true, () =>
        {
            var r = new Rig();
            r.NoGo = [new Box(300, 200, 250, 60)];
            r.Life.Start();
            var from = r.Life.Rest;
            Assert.True(r.Life.FlyNow());
            Assert.True(r.Life.Flying);
            Assert.True(r.Life.RenderingHooked);
            var plan = r.Life.Plan!;
            var area = new Box(0, 0, 900, 560);
            var total = FlightMotion.Total(plan);
            var landed = 0;
            r.Life.Landed += () => landed++;
            for (var t = 0.0; t < total - 0.05; t += 0.03)
            {
                r.Life.StepFlight(t);
                var p = r.Life.Position;
                Assert.True(area.Contains(new Box(p.X, p.Y, 50, 60)), $"t={t}: {p}");
                if (t > 0.1 && t < total - 0.1) Assert.False(DependencyPropertyHelper.GetValueSource(r.Owl, BidarOwl.WingLProperty).IsAnimated); // frames are set directly, no animation objects
            }
            Assert.True(r.Life.Flying);
            r.Life.StepFlight(total + 0.01);
            Assert.False(r.Life.Flying);
            Assert.False(r.Life.RenderingHooked);
            Assert.Equal(1, landed);
            Assert.Equal(plan.To, r.Life.Rest);
            Assert.NotEqual(from, r.Life.Rest);
            Assert.Equal(plan.To.X, Canvas.GetLeft(r.Owl));
            Assert.Equal(plan.To, r.Life.Position);
            Assert.True(r.Life.TimerEnabled); // the next decision is scheduled
            Assert.False(r.Owl.Airborne);
            Assert.True(Perches.IsClear(r.Life.Rest, 50, 60, area, r.NoGo));
            r.Life.Stop();
        });
    }

    [Fact]
    public void A_click_in_mid_air_lands_him_first()
    {
        WithMotion(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            Assert.True(r.Life.FlyNow());
            var to = r.Life.Plan!.To;
            r.Life.StepFlight(0.8);
            Assert.True(r.Life.Flying);
            r.Owl.React(); // the click: PreClick lands him before the reaction plays
            Assert.False(r.Life.Flying);
            Assert.False(r.Life.RenderingHooked);
            Assert.Equal(to, r.Life.Rest);
            Assert.Equal(to, r.Life.Position);
            r.Life.Stop();
        });
    }

    [Fact]
    public void Hiding_the_owl_or_stopping_unhooks_everything()
    {
        WithMotion(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            Assert.True(r.Life.FlyNow());
            Assert.True(r.Life.RenderingHooked);
            r.Life.Stop();
            Assert.False(r.Life.RenderingHooked);
            Assert.False(r.Life.TimerEnabled);
            Assert.False(r.Life.Running);
            r.Life.Stop(); // twice is harmless
            r.Life.Start(); // and he can come back (a theme change unloads and loads the owl again)
            Assert.True(r.Life.Running);
            r.Life.Dispose();
            Assert.False(r.Life.Running);
        });
    }

    [Fact]
    public void Every_pause_condition_keeps_the_scheduler_from_acting_even_when_it_fires()
    {
        WithMotion(true, () =>
        {
            foreach (var host in new[]
                     {
                         new BrainContext { GameRunning = true }, new BrainContext { WindowVisible = false }, new BrainContext { WindowActive = false },
                         new BrainContext { UserBusy = true }, new BrainContext { LowMemory = true }, new BrainContext { MotionOn = false },
                         new BrainContext { LifeEnabled = false }, new BrainContext { PopupOpen = true },
                     })
            {
                var r = new Rig { Host = host };
                r.Life.Start();
                for (var i = 0; i < 30; i++)
                {
                    r.Clock += TimeSpan.FromSeconds(10);
                    r.Life.Tick();
                    Assert.Null(r.Life.CurrentAct);
                    Assert.False(r.Life.Flying);
                    Assert.False(r.Life.RenderingHooked);
                }
                Assert.Equal(0, r.Scans); // no page walk while paused
                r.Life.Stop();
            }
        });
    }

    [Fact]
    public void A_free_owl_really_does_things_over_time_including_flights()
    {
        WithMotion(true, () =>
        {
            var r = new Rig(9);
            r.Life.Start();
            var acts = new HashSet<BidarAct>();
            var flights = 0;
            r.Life.Landed += () => flights++;
            for (var i = 0; i < 400; i++)
            {
                r.Clock += TimeSpan.FromSeconds(11);
                r.Life.Tick();
                if (r.Life.CurrentAct is { } a) acts.Add(a);
                if (r.Life.Flying) r.Life.LandNow();
                r.Host = r.Host with { Now = r.Clock };
            }
            Assert.True(acts.Count >= 8, string.Join(",", acts));
            Assert.True(flights >= 2, $"flights {flights}");
            Assert.Contains(BidarAct.Doze, acts);
            Assert.False(r.Life.RenderingHooked);
            r.Life.Stop();
        });
    }

    [Fact]
    public void Acts_are_real_animations_that_end_on_their_own_and_none_run_with_motion_off()
    {
        WithMotion(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            r.Life.Play(BidarAct.Breathe);
            Assert.True(Animated(r.Owl, BidarOwl.SquashProperty));
            r.Life.Play(BidarAct.LanternFlicker);
            Assert.True(Animated(r.Owl, BidarOwl.GlowProperty));
            r.Life.Play(BidarAct.LookAround);
            Assert.True(Animated(r.Owl, BidarOwl.LookXProperty));
            r.Life.Play(BidarAct.HatFidget);
            Assert.True(Animated(r.Owl, BidarOwl.HatTiltProperty));
            r.Life.Play(BidarAct.WingWave);
            Assert.True(Animated(r.Owl, BidarOwl.WingRProperty));
            r.Life.Play(BidarAct.Peek);
            Assert.True(Animated(r.Owl, BidarOwl.SlideProperty));
            r.Life.Play(BidarAct.Hello);
            Assert.Equal(BidarMood.Hello, r.Owl.Mood);
            r.Life.Play(BidarAct.Yawn);
            Assert.Equal(BidarMood.Yawn, r.Owl.Mood);
            r.Life.Stop();
        });
        WithMotion(false, () =>
        {
            var r = new Rig();
            r.Life.Start();
            r.Life.Play(BidarAct.Breathe);
            r.Life.Play(BidarAct.LookAround);
            r.Life.Play(BidarAct.HatFidget);
            r.Life.Play(BidarAct.Peek);
            r.Life.Play(BidarAct.EasterEgg);
            Assert.False(Animated(r.Owl, BidarOwl.SquashProperty));
            Assert.False(Animated(r.Owl, BidarOwl.LookXProperty));
            Assert.False(Animated(r.Owl, BidarOwl.HatTiltProperty));
            Assert.False(Animated(r.Owl, BidarOwl.SlideProperty));
            Assert.False(Animated(r.Owl, BidarOwl.SpinProperty));
            r.Life.Stop();
        });
    }

    [Fact]
    public void Polled_and_pushed_events_make_him_react_and_a_nap_ends_by_itself_or_by_an_event()
    {
        WithMotion(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            r.Cue = BidarCue.ShutdownSoon; // what the host's poll reports on the next step
            r.Life.Tick();
            Assert.Equal(BidarAct.Alert, r.Life.CurrentAct);
            Assert.Equal(BidarMood.Alert, r.Owl.Mood);
            r.Cue = null;
            // after the hold time the calm face is back
            r.Clock += TimeSpan.FromSeconds(6);
            r.Life.Tick();
            Assert.Equal(BidarMood.Idle, r.Owl.Mood);
            Assert.Null(r.Life.CurrentAct);

            r.Life.Play(BidarAct.Doze);
            Assert.True(r.Life.Asleep);
            Assert.Equal(BidarMood.Sleepy, r.Owl.Mood);
            r.Life.Tick(); // too early: still asleep
            Assert.True(r.Life.Asleep);
            r.Life.Cue(BidarCue.DownloadDone); // an event wakes him with a cheer
            Assert.False(r.Life.Asleep);
            Assert.Equal(BidarMood.Celebrate, r.Owl.Mood);

            r.Life.Play(BidarAct.Doze);
            r.Clock += TimeSpan.FromSeconds(60);
            r.Life.Tick();
            Assert.False(r.Life.Asleep);
            Assert.Equal(BidarMood.Idle, r.Owl.Mood);
            r.Life.Stop();
        });
    }

    [Fact]
    public void A_cue_in_a_game_or_with_motion_off_changes_nothing()
    {
        WithMotion(true, () =>
        {
            var r = new Rig { Host = new BrainContext { GameRunning = true } };
            r.Life.Start();
            r.Life.Cue(BidarCue.Error);
            Assert.Equal(BidarMood.Idle, r.Owl.Mood);
            r.Life.Stop();
        });
        WithMotion(false, () =>
        {
            var r = new Rig { Host = new BrainContext { MotionOn = false } };
            r.Life.Start();
            r.Life.Cue(BidarCue.DownloadDone);
            Assert.Equal(BidarMood.Idle, r.Owl.Mood);
            r.Life.Stop();
        });
    }

    [Fact]
    public void A_resize_pulls_him_back_inside_and_a_blocked_page_moves_him_to_a_free_spot()
    {
        WithMotion(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            Assert.True(r.Life.FlyNow());
            r.Life.LandNow();
            var away = r.Life.Rest;
            // the window shrinks: wherever he is, he must still be fully inside
            r.Stage.Width = 300;
            r.Stage.Height = 200;
            r.Stage.Measure(new Size(300, 200));
            r.Stage.Arrange(new Rect(0, 0, 300, 200));
            r.Life.Resized();
            var small = new Box(0, 0, 300, 200);
            Assert.True(small.Contains(new Box(r.Life.Rest.X, r.Life.Rest.Y, 50, 60)), r.Life.Rest.ToString());
            Assert.Equal(r.Life.Rest.X, Canvas.GetLeft(r.Owl));
            // back to a large window: a resting place on top of new content is given up at the next quiet moment
            r.Stage.Width = 900;
            r.Stage.Height = 560;
            r.Stage.Measure(new Size(900, 560));
            r.Stage.Arrange(new Rect(0, 0, 900, 560));
            r.Life.Resized();
            r.NoGo = [new Box(r.Life.Rest.X - 10, r.Life.Rest.Y - 10, 80, 90)];
            r.Life.PageChanged();
            r.Life.Tick();
            if (r.Life.Flying) r.Life.LandNow();
            Assert.True(Perches.IsClear(r.Life.Rest, 50, 60, new Box(0, 0, 900, 560), r.NoGo));
            Assert.NotEqual(away, default);
            r.Life.Stop();
        });
    }
}

[Collection("Wpf")]
public class BidarCrowdedPageTests
{
    [Fact]
    public void On_a_crowded_page_he_still_flies_over_the_content_but_only_lands_on_free_ground()
    {
        var before = Motion.TestOverride;
        Motion.TestOverride = true;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var owl = new BidarOwl { Width = 50, Height = 60 };
                var stage = new Canvas { Width = 900, Height = 560 };
                using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("bidar-crowd") { ParentWindow = new IntPtr(-3), Width = 900, Height = 560 }) { RootVisual = stage };
                stage.Children.Add(owl);
                stage.Measure(new Size(900, 560));
                stage.Arrange(new Rect(0, 0, 900, 560));
                // everything right of x=380 is crowded (and he starts in its corner): the only free ground is on the left, across the content
                var wall = new List<Box> { new(380, 0, 520, 560) };
                var crossed = 0;
                for (var seed = 1; seed <= 12; seed++)
                {
                    var life = new BidarLife(owl, stage, () => new BrainContext(), () => wall, () => false, null, seed);
                    life.Start();
                    Assert.True(life.Rest.X > 500);
                    Assert.True(life.FlyNow());
                    var plan = life.Plan!;
                    if (plan.To.X < 330) crossed++; // a high arc over the wall of content
                    life.LandNow();
                    Assert.True(Perches.IsClear(life.Rest, 50, 60, new Box(0, 0, 900, 560), wall)); // but he always lands on free ground
                    life.Stop();
                }
                Assert.True(crossed > 0, "never flew over the content");
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Motion.TestOverride = before;
        if (error is not null) throw new Exception("STA test failed", error);
    }
}
