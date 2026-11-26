using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>The slow animated backdrop of a premium theme: a painted plate with a few dozen small sprites over it (see ThemeScene.Art.cs
/// and tools/theme-art). It lies behind the pages, never takes the mouse, moves its sprites only by transforms and opacity (no effects,
/// no per-frame code) and stands still when animations are off or the window is hidden. Layout is rebuilt (after a short pause) when the window is resized.</summary>
public sealed partial class ThemeScene : Canvas
{
    private readonly Scene _scene;
    private readonly DispatcherTimer _rebuild = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _idleWatch = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
    private readonly List<(IAnimatable Target, DependencyProperty Property, ClockController Controller)> _animations = [];
    private bool _animateArt;
    private bool _playing;
    private bool _needsLayout = true;

    /// <summary>After a minute without a key or a mouse move anywhere the backdrop stops moving (it is drawn once, as its still picture) and starts again at the next touch.
    /// A backdrop nobody is touching is the most wasteful thing a weak graphics card can be asked to draw.</summary>
    private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(60);

    private void CheckIdle()
    {
        if (!CanDraw) return;
        if (Motion.Enabled != _animateArt) QueueBuild();
        else SetPlaying(ShouldAnimate);
    }
    private Random _rng = new(7);

    public Scene Kind => _scene;

    /// <summary>True for the backdrop of the real window, where the sidebar, the page title and the footer put text straight on the painting: a veil keeps it readable.
    /// The small store preview shows the bare painting.</summary>
    public bool Veiled { get; init; }

    public ThemeScene(Scene scene)
    {
        _scene = scene;
        // a painting is never mirrored: in the Persian layout (right to left) WPF would flip the picture and every sprite, so the koi swam tail first and the scenes no longer matched their art
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = false;
        ClipToBounds = true;
        SnapsToDevicePixels = false;
        _rebuild.Tick += (_, _) => { _rebuild.Stop(); Build(); };
        SizeChanged += (_, _) =>
        {
            _needsLayout = true;
            QueueBuild(260);
        };
        _idleWatch.Tick += (_, _) => CheckIdle();
        IsVisibleChanged += (_, _) => Refresh();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void Clear()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        foreach (var (target, property, controller) in _animations)
        {
            target.ApplyAnimationClock(property, null);
            controller.Remove();
        }
        _animations.Clear();
        _playing = false;
        Children.Clear();
    }

    private void Animate(IAnimatable target, DependencyProperty property, AnimationTimeline timeline)
    {
        var clock = (AnimationClock)timeline.CreateClock(true);
        target.ApplyAnimationClock(property, clock);
        _animations.Add((target, property, clock.Controller!));
    }

    private void SetPlaying(bool playing)
    {
        if (_playing == playing) return;
        _playing = playing;
        foreach (var (_, _, controller) in _animations)
        {
            if (playing) controller.Resume();
            else controller.Pause();
        }
    }

    /// <summary>A slow drifting backdrop needs only 10 to 18 frames a second (a 144 Hz screen would otherwise redraw it 144 times, and every frame also redraws what is on top of it).</summary>
    private int SceneFps => FpsFor(_scene);

    /// <summary>Fast things (rain, meteors, sparks) need a few more frames than slow ones (lanterns, snow, mist, ink) to look smooth.</summary>
    internal static int FpsFor(Scene scene) => scene switch { Scene.Rain or Scene.Storm => 15, Scene.Matrix or Scene.Embers or Scene.Pixels or Scene.Invaders or Scene.Shards or Scene.Synth or Scene.Tactical => 12, _ => 10 };

    private Window? _host;

    /// <summary>The backdrop only moves while its window is visible and active.</summary>
    private bool CanDraw => IsLoaded && IsVisible && (_host is null || (_host.IsActive && _host.WindowState != WindowState.Minimized));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = Window.GetWindow(this);
        if (_host is { } window)
        {
            window.Activated += OnHostChanged;
            window.Deactivated += OnHostChanged;
            window.StateChanged += OnHostChanged;
        }
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_host is { } window)
        {
            window.Activated -= OnHostChanged;
            window.Deactivated -= OnHostChanged;
            window.StateChanged -= OnHostChanged;
            _host = null;
        }
        Suspend();
        Clear();
        _needsLayout = true;
    }

    private void OnHostChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (!CanDraw)
        {
            Suspend();
            return;
        }
        if (_needsLayout || Motion.Enabled != _animateArt) QueueBuild();
        else SetPlaying(ShouldAnimate);
        _idleWatch.Start();
    }

    private void Suspend()
    {
        _rebuild.Stop();
        _idleWatch.Stop();
        // Keep the existing visuals and their current positions across hide and minimize.
        // Rebuilding them while the WPF window is restoring competes with its first layout and draw.
        SetPlaying(false);
        BeginAnimation(OpacityProperty, null);
    }

    private void QueueBuild(int delayMs = 1)
    {
        if (!CanDraw) return;
        _rebuild.Stop();
        _rebuild.Interval = TimeSpan.FromMilliseconds(delayMs);
        _rebuild.Start();
    }

    private static bool ShouldAnimate => Motion.Enabled && UserIdle.Since() <= IdleAfter;

    private void Build()
    {
        if (!CanDraw) return;
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 80) return;
        var appearing = Children.Count == 0;
        Clear();
        _needsLayout = false;
        _animateArt = Motion.Enabled;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var built = Art(w, h);
        _playing = _animations.Count > 0;
        SetPlaying(ShouldAnimate);
        var took = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        PageTiming.Record("scene:" + _scene, took);          // decoding the painting and laying out its sprites runs on the window thread: a slow one is written to the log
        if (Environment.GetEnvironmentVariable("SHABPA_SCENE_LOG") is { Length: > 0 } sceneLog) System.IO.File.AppendAllText(sceneLog, $"{_scene} {took:F0} ms, {Children.Count} elements{Environment.NewLine}");   // test aid
        if (!built) return;   // every scene is a painting now (Assets/themes); a missing one just leaves the plain window background
        if (_playing && appearing)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new QuadraticEase(), FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(fade, SceneFps);
            BeginAnimation(OpacityProperty, fade);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private double R(double a, double b) => a + _rng.NextDouble() * (b - a);

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    private static Duration D(double seconds) => new(TimeSpan.FromSeconds(seconds));

    /// <summary>From → to again and again, starting part-way through so the sprites never move in step.</summary>
    private void Loop(IAnimatable target, DependencyProperty dp, double from, double to, double seconds, double phase, bool reverse = false, bool smooth = false, int? fps = null)
    {
        var a = new DoubleAnimation(from, to, D(seconds))
        {
            RepeatBehavior = RepeatBehavior.Forever, AutoReverse = reverse, BeginTime = TimeSpan.FromSeconds(-phase * seconds * (reverse ? 2 : 1)),
            EasingFunction = smooth ? new SineEase { EasingMode = EasingMode.EaseInOut } : null,
        };
        Timeline.SetDesiredFrameRate(a, fps ?? SceneFps);
        Animate(target, dp, a);
    }
}
