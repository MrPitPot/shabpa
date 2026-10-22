using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shabpa.Kit;
using Xunit;
using static Shabpa.Kit.SmoothScrollPhysics;

namespace Shabpa.Tests;

public class SmoothScrollTests
{
    private static State Run(State s, double target, double seconds, double dt = 1.0 / 60)
    {
        for (var t = 0.0; t < seconds; t += dt) s = Step(s, target, dt);
        return s;
    }

    [Fact]
    public void Glide_reaches_the_target_without_overshoot_and_comes_to_rest()
    {
        var s = new State(0, 0);
        var max = 0.0;
        for (var i = 0; i < 120; i++) { s = Step(s, 300, 1.0 / 60); max = Math.Max(max, s.Position); }
        Assert.True(max <= 300 + 1e-9);
        Assert.True(AtRest(s, 300), $"still at {s.Position} v={s.Velocity}");
    }

    [Fact]
    public void Glide_eases_in_and_decelerates_gradually_instead_of_stopping_dead()
    {
        var s = new State(0, 0);
        var speeds = new List<double>();
        for (var i = 0; i < 60; i++) { var n = Step(s, 300, 1.0 / 60); speeds.Add(n.Position - s.Position); s = n; }
        var peak = speeds.IndexOf(speeds.Max());
        Assert.InRange(peak, 3, 25);                               // starts gently, not at full speed
        for (var i = peak + 1; i < 40; i++) Assert.True(speeds[i] < speeds[i - 1]);   // then only slows down
        Assert.True(speeds[30] > 0.05);                            // still moving half a second in: no sudden stop
    }

    [Theory]
    [InlineData(30), InlineData(60), InlineData(144)]
    public void Result_barely_depends_on_the_frame_rate(int fps)
    {
        var s = Run(new State(0, 0), 500, 0.3, 1.0 / fps);
        var reference = Run(new State(0, 0), 500, 0.3, 1.0 / 240);
        Assert.InRange(s.Position, reference.Position - 12, reference.Position + 12);
    }

    [Fact]
    public void A_stalled_frame_is_cut_and_never_produces_nan_or_a_jump_past_the_target()
    {
        var s = Step(new State(0, 0), 100, 30);
        Assert.False(double.IsNaN(s.Position));
        Assert.InRange(s.Position, 0, 100);
    }

    [Fact]
    public void Wheel_notches_add_up_and_stay_in_range()
    {
        Assert.Equal(PixelsPerNotch * 2, AddWheel(0, -240, 1000), 6);
        Assert.Equal(0, AddWheel(10, 600, 1000));
        Assert.Equal(1000, AddWheel(990, -600, 1000));
        Assert.Equal(0, AddWheel(5, -120, 0));
    }

    [Theory]
    [InlineData(120, false), InlineData(-120, false), InlineData(360, false), InlineData(30, true), InlineData(-47, true), InlineData(150, true)]
    public void Precision_touchpad_deltas_are_told_apart_from_wheel_notches(int delta, bool precise) => Assert.Equal(precise, IsPrecise(delta));

    [Fact]
    public void Handoff_to_the_parent_only_happens_at_the_end_when_nothing_is_gliding()
    {
        Assert.True(ShouldHandle(50, 100, -120, false));
        Assert.False(ShouldHandle(100, 100, -120, false));   // at the bottom, idle: parent takes it
        Assert.True(ShouldHandle(100, 100, -120, true));     // still gliding into the end: swallow
        Assert.False(ShouldHandle(0, 100, 120, false));
        Assert.False(ShouldHandle(0, 0, -120, true));        // nothing to scroll at all
    }

    private static void Sta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
    }

    private static ScrollViewer Viewer(double content, double view = 100)
    {
        var sv = new ScrollViewer { Width = 200, Height = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = content, Width = 100 } };
        sv.Measure(new Size(200, view));
        sv.Arrange(new Rect(0, 0, 200, view));
        sv.UpdateLayout();
        return sv;
    }

    private static MouseWheelEventArgs Wheel(UIElement source, int delta)
    {
        var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = source };
        source.RaiseEvent(e);
        return e;
    }

    [Fact]
    public void Wheel_over_a_scrollable_viewer_is_taken_and_starts_a_glide_that_is_released_when_done()
    {
        Sta(() =>
        {
            SmoothScroll.Install();
            var sv = Viewer(1000);
            Assert.True(Wheel(sv, -120).Handled);
            Assert.True(SmoothScroll.IsRunning);
            Assert.Equal(0, sv.VerticalOffset);                    // the offset is moved by frames, not jumped
        });
    }

    [Fact]
    public void Viewer_that_cannot_scroll_and_one_at_its_end_pass_the_wheel_to_the_parent()
    {
        Sta(() =>
        {
            SmoothScroll.Install();
            // a viewer with nothing to scroll, and one already at its end, both give the wheel to the parent
            foreach (var (content, atEnd) in new[] { (50.0, false), (1000.0, true) })
            {
                var inner = Viewer(content);
                if (atEnd) { inner.ScrollToVerticalOffset(inner.ScrollableHeight); inner.UpdateLayout(); }
                var seen = 0;
                var host = new Border { Child = inner };
                host.MouseWheel += (_, _) => seen++;
                host.Measure(new Size(200, 100)); host.Arrange(new Rect(0, 0, 200, 100)); host.UpdateLayout();
                Wheel(inner, -120);
                Assert.Equal(1, seen);
            }
            // in the middle it keeps the wheel for itself
            var sv = Viewer(1000);
            var leaked = 0;
            var wrap = new Border { Child = sv };
            wrap.MouseWheel += (_, _) => leaked++;
            wrap.Measure(new Size(200, 100)); wrap.Arrange(new Rect(0, 0, 200, 100)); wrap.UpdateLayout();
            Assert.True(Wheel(sv, -120).Handled);
            Assert.Equal(0, leaked);
        });
    }

    [Fact]
    public void Precision_deltas_move_directly()
    {
        Sta(() =>
        {
            SmoothScroll.Install();
            var sv = Viewer(1000);
            Assert.True(Wheel(sv, -30).Handled);
            sv.UpdateLayout();
            Assert.True(sv.VerticalOffset > 0);
        });
    }

    private static void Pump(double seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => { if (DateTime.UtcNow >= end) frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    /// <summary>Measured on a real (off-screen, never activated, no taskbar button) window: the glide really moves the offset over
    /// many frames, keeps going after the last notch, ends exactly on the target, and releases the frame callback.</summary>
    [Fact]
    public void Real_window_glide_runs_over_many_frames_continues_after_the_wheel_stops_and_goes_idle()
    {
        Sta(() =>
        {
            SmoothScroll.Install();
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = 5000, Width = 100 } };
            var win = new Window
            {
                Left = -32000, Top = -32000, Width = 300, Height = 300, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
                ShowActivated = false, Content = sv,
            };
            win.Show();
            try
            {
                Pump(0.3);
                var frames0 = SmoothScroll.FramesRendered;
                for (var i = 0; i < 4; i++) Wheel(sv, -120);          // four notches = 384 px target
                var samples = new List<double>();
                var stop = DateTime.UtcNow.AddSeconds(1.5);
                var lastMove = DateTime.UtcNow;
                var prev = 0.0;
                while (DateTime.UtcNow < stop)
                {
                    Pump(0.016);
                    samples.Add(sv.VerticalOffset);
                    if (sv.VerticalOffset != prev) { lastMove = DateTime.UtcNow; prev = sv.VerticalOffset; }
                }
                var frames = SmoothScroll.FramesRendered - frames0;
                Assert.Equal(4 * PixelsPerNotch, sv.VerticalOffset, 3);   // ends exactly on the target
                Assert.True(frames >= 15, $"only {frames} frames");         // a glide, not a jump
                Assert.True(samples.Count(v => v > 0 && v < 384) >= 8);   // many distinct in-between positions
                Assert.False(SmoothScroll.IsRunning);                      // released: zero cost while idle
                var pause = (DateTime.UtcNow - lastMove).TotalSeconds;
                Assert.True(pause > 0.2);
                Console.WriteLine($"smooth-scroll probe: {frames} frames, {samples.Count(v => v > 0 && v < 384)} in-between positions, idle={!SmoothScroll.IsRunning}");
            }
            finally { win.Close(); }
        });
    }
}
