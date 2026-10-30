using System.Windows;
using System.Windows.Controls;
using Shabpa.Kit;

namespace Shabpa.Tests;

// ---- walking: the pure planner and frames, the brain's choice, and the controller on a real (hidden) stage ------------------

public class BidarWalkTests
{
    private const double W = 50, H = 60;
    private static readonly Box Area = new(0, 0, 900, 560);
    private static readonly double Ground = Area.Bottom - H - Perches.BottomMargin;

    [Fact]
    public void A_walk_stays_on_one_row_within_a_sensible_distance()
    {
        var from = new Pt(600, Ground);
        Assert.NotNull(WalkPlanner.Plan(from, new Pt(400, Ground), W, H, Area, []));
        Assert.Null(WalkPlanner.Plan(from, new Pt(400, Ground - 80), W, H, Area, [])); // another row: that is a flight
        Assert.Null(WalkPlanner.Plan(from, new Pt(580, Ground), W, H, Area, []));      // a shuffle, not a walk
        Assert.Null(WalkPlanner.Plan(from, new Pt(100, Ground), W, H, Area, []));      // too far to waddle
        Assert.Null(WalkPlanner.Plan(new Pt(880, Ground), new Pt(700, Ground), W, H, Area, [])); // starts outside the window
    }

    [Fact]
    public void He_never_walks_through_buttons_or_text()
    {
        var from = new Pt(600, Ground);
        var to = new Pt(400, Ground);
        Assert.Null(WalkPlanner.Plan(from, to, W, H, Area, [new Box(500, Ground + 10, 30, 20)])); // a button on the floor between
        Assert.Null(WalkPlanner.Plan(from, to, W, H, Area, [new Box(452, Ground - 3, 10, 10)]));   // even one that only grazes the strip
        Assert.NotNull(WalkPlanner.Plan(from, to, W, H, Area, [new Box(500, Ground - 40, 30, 20)])); // above his head is fine
        Assert.NotNull(WalkPlanner.Plan(from, to, W, H, Area, [new Box(100, Ground, 60, 60)]));      // and so is anything beyond the target
    }

    [Fact]
    public void Options_keep_only_reachable_spots()
    {
        var valid = Perches.Valid(Area, W, H, false, []);
        var home = Perches.Home(Area, W, H, false);
        var options = WalkPlanner.Options(home, valid, W, H, Area, []).ToList();
        Assert.NotEmpty(options);
        Assert.All(options, o =>
        {
            Assert.Equal(home.Y, o.To.Y);
            Assert.InRange(Math.Abs(o.To.X - home.X), WalkPlanner.MinDistance, WalkPlanner.MaxDistance);
        });
        Assert.Empty(WalkPlanner.Options(home, valid, W, H, Area, [new Box(0, Ground - 5, 820, 80)])); // the whole floor is busy
    }

    [Theory]
    [InlineData(600, 400)]
    [InlineData(300, 640)]
    public void Frames_start_and_end_on_the_spots_never_overshoot_and_end_with_both_feet_down(double fromX, double toX)
    {
        var plan = WalkPlanner.Plan(new Pt(fromX, Ground), new Pt(toX, Ground), W, H, Area, [])!;
        Assert.Equal(Math.Sign(toX - fromX), plan.Direction);
        Assert.Equal(plan.From, WalkMotion.Frame(plan, 0).Pos);
        var last = WalkMotion.Frame(plan, plan.Seconds);
        Assert.True(last.Done);
        Assert.Equal(plan.To, last.Pos);
        Assert.Equal((0.0, 0.0), (last.StepL, last.StepR));
        var lo = Math.Min(fromX, toX);
        var hi = Math.Max(fromX, toX);
        var prev = fromX;
        double maxL = 0, maxR = 0;
        for (var t = 0.0; t < plan.Seconds; t += 0.01)
        {
            var f = WalkMotion.Frame(plan, t);
            Assert.False(f.Done);
            Assert.Equal(Ground, f.Pos.Y);
            Assert.InRange(f.Pos.X, lo - 1e-9, hi + 1e-9);
            Assert.True((f.Pos.X - prev) * plan.Direction >= -1e-9, $"t={t}: he stepped backwards"); // always forward
            Assert.True(Math.Abs(f.Pos.X - prev) < 3, $"t={t}: a jump of {f.Pos.X - prev}");        // and smoothly
            Assert.False(f.StepL > 0 && f.StepR > 0, "both feet in the air");
            Assert.InRange(Math.Abs(f.Sway), 0, WalkMotion.SwayDegrees + 1e-9);
            maxL = Math.Max(maxL, f.StepL);
            maxR = Math.Max(maxR, f.StepR);
            prev = f.Pos.X;
        }
        Assert.True(maxL > WalkMotion.Lift * 0.9 && maxR > WalkMotion.Lift * 0.9, "both feet take steps");
        var mid = WalkMotion.Frame(plan, plan.Seconds / 2);
        Assert.Equal((double)plan.Direction, mid.Look); // eyes on the way ahead
    }

    [Fact]
    public void Walking_pace_is_steady_in_the_middle_and_gentle_at_the_ends()
    {
        var plan = WalkPlanner.Plan(new Pt(700, Ground), new Pt(400, Ground), W, H, Area, [])!;
        double Speed(double t) => Math.Abs(WalkMotion.Frame(plan, t + 0.01).Pos.X - WalkMotion.Frame(plan, t).Pos.X) / 0.01;
        var start = WalkMotion.Start;
        var moving = plan.Seconds - WalkMotion.Start - WalkMotion.Stop;
        Assert.True(Speed(start + 0.02) < Speed(start + moving / 2) / 3);
        Assert.InRange(Speed(start + moving / 2), WalkPlanner.Speed * 0.9, WalkPlanner.Speed * 1.6);
    }
}

public class BidarWalkBrainTests
{
    private static readonly DateTime Noon = new(2026, 10, 22, 14, 0, 0);

    private static BrainContext Free(bool canWalk) => new() { Now = Noon, IdleFor = TimeSpan.FromSeconds(10), CanFly = true, CanWalk = canWalk };

    private static IEnumerable<BidarAct?> Run(int seed, int steps, BrainContext ctx)
    {
        var brain = new BidarBrain(seed);
        var now = ctx.Now;
        for (var i = 0; i < steps; i++)
        {
            now += brain.NextGap();
            yield return brain.Pick(ctx with { Now = now });
        }
    }

    [Fact]
    public void He_walks_only_when_his_row_has_a_clear_way_and_then_he_walks_often()
    {
        var blocked = Enumerable.Range(0, 40).SelectMany(s => Run(s, 60, Free(false))).ToList();
        var open = Enumerable.Range(0, 40).SelectMany(s => Run(s, 60, Free(true))).ToList();
        Assert.DoesNotContain(BidarAct.Walk, blocked);
        var walks = open.Count(a => a == BidarAct.Walk);
        var flights = open.Count(a => a == BidarAct.Fly);
        Assert.True(walks > flights, $"walks {walks} vs flights {flights}"); // a stroll is his everyday way about; a flight is the treat
    }

    [Fact]
    public void Coming_back_after_a_break_gets_a_hello_but_not_every_time()
    {
        var brain = new BidarBrain(1);
        Assert.Equal(BidarAct.Hello, brain.OnCue(BidarCue.Welcome, Free(false)));
        Assert.Null(brain.OnCue(BidarCue.Welcome, Free(false) with { Now = Noon.AddMinutes(5) }));
        Assert.Equal(BidarAct.Hello, brain.OnCue(BidarCue.Welcome, Free(false) with { Now = Noon.AddMinutes(11) }));
        Assert.Null(new BidarBrain(1).OnCue(BidarCue.Welcome, Free(false) with { GameRunning = true }));
    }
}

[Collection("Wpf")]
public class BidarWalkWpfTests
{
    private sealed class Rig
    {
        public readonly BidarOwl Owl = new() { Width = 50, Height = 60 };
        public readonly Canvas Stage = new() { Width = 900, Height = 560 };
        public readonly BidarLife Life;
        public List<Box> NoGo = [];
        private readonly System.Windows.Interop.HwndSource _source;

        public Rig(int seed = 4)
        {
            _source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("bidar-walk") { ParentWindow = new IntPtr(-3), Width = 900, Height = 560 }) { RootVisual = Stage };
            Stage.Children.Add(Owl);
            Stage.Measure(new Size(900, 560));
            Stage.Arrange(new Rect(0, 0, 900, 560));
            Life = new BidarLife(Owl, Stage, () => new BrainContext(), () => NoGo, () => false, null, seed, () => new DateTime(2026, 10, 22, 14, 0, 0));
        }
    }

    private static void Sta(bool motion, Action test)
    {
        var before = Motion.TestOverride;
        Motion.TestOverride = motion;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Motion.TestOverride = before;
        if (error is not null) throw new Exception("STA test failed", error);
    }

    [Fact]
    public void A_walk_hooks_the_render_clock_moves_along_the_floor_and_unhooks_on_arrival()
    {
        Sta(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            var from = r.Life.Rest;
            Assert.True(r.Life.WalkNow());
            Assert.True(r.Life.Walking);
            Assert.True(r.Life.Moving);
            Assert.False(r.Life.Flying);
            Assert.True(r.Life.RenderingHooked);
            Assert.Equal(BidarAct.Walk, r.Life.CurrentAct);
            var plan = r.Life.Route!;
            Assert.Equal(from, plan.From);
            var landed = 0;
            r.Life.Landed += () => landed++; // that event is for flights
            for (var t = 0.0; t < plan.Seconds - 0.02; t += 0.05)
            {
                r.Life.StepWalk(t);
                Assert.Equal(from.Y, r.Life.Position.Y);
                Assert.False(DependencyPropertyHelper.GetValueSource(r.Owl, BidarOwl.TiltProperty).IsAnimated); // frames are set directly
            }
            r.Life.StepWalk(plan.Seconds + 0.01);
            Assert.False(r.Life.Walking);
            Assert.False(r.Life.RenderingHooked);
            Assert.Equal(0, landed);
            Assert.Equal(plan.To, r.Life.Rest);
            Assert.Equal(plan.To.X, Canvas.GetLeft(r.Owl));
            Assert.Equal(plan.To, r.Life.Position);
            Assert.True(r.Life.TimerEnabled);
            r.Life.Stop();
        });
    }

    [Fact]
    public void A_click_mid_walk_stops_him_on_the_spot_and_hiding_does_too()
    {
        Sta(true, () =>
        {
            var r = new Rig();
            r.Life.Start();
            Assert.True(r.Life.WalkNow());
            var plan = r.Life.Route!;
            r.Life.StepWalk(plan.Seconds / 2);
            var here = r.Life.Position;
            Assert.NotEqual(plan.From, here);
            Assert.NotEqual(plan.To, here);
            r.Owl.React();
            Assert.False(r.Life.Walking);
            Assert.False(r.Life.RenderingHooked);
            Assert.Equal(here, r.Life.Rest);
            Assert.Equal(here, r.Life.Position);

            Assert.True(r.Life.WalkNow());
            r.Life.StepWalk(0.8);
            r.Owl.Visibility = Visibility.Collapsed;
            Assert.False(r.Life.Walking);
            Assert.False(r.Life.RenderingHooked);
            r.Life.Stop();
        });
    }

    [Fact]
    public void No_walk_when_the_floor_is_busy_or_motion_is_off()
    {
        Sta(true, () =>
        {
            var r = new Rig { NoGo = [new Box(0, 440, 820, 120)] };
            r.Life.Start();
            Assert.False(r.Life.WalkNow());
            Assert.False(r.Life.RenderingHooked);
            r.Life.Play(BidarAct.Walk); // the brain never asks for it here, but if it did he would only look about
            Assert.False(r.Life.Walking);
            r.Life.Stop();
        });
        Sta(false, () =>
        {
            var r = new Rig();
            r.Life.Start();
            Assert.False(r.Life.WalkNow());
            Assert.False(r.Life.Walking);
            r.Life.Stop();
        });
    }
}
