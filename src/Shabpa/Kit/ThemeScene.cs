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
    private readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly DispatcherTimer _idleWatch = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _still;

    /// <summary>After a minute without a key or a mouse move anywhere the backdrop stops moving (it is drawn once, as its still picture) and starts again at the next touch.
    /// A backdrop nobody is touching is the most wasteful thing a weak graphics card can be asked to draw.</summary>
    private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(60);

    private void CheckIdle()
    {
        if (!HostActive || !IsVisible) return;
        var idle = UserIdle.Since() > IdleAfter;
        if (idle == _still) return;
        _still = idle;
        Build();
    }
    private Random _rng = new(7);

    public Scene Kind => _scene;

    public ThemeScene(Scene scene)
    {
        _scene = scene;
        IsHitTestVisible = false;
        ClipToBounds = true;
        SnapsToDevicePixels = false;
        _rebuild.Tick += (_, _) => { _rebuild.Stop(); Build(); };
        SizeChanged += (_, _) => { _rebuild.Stop(); _rebuild.Start(); };
        _idleWatch.Tick += (_, _) => CheckIdle();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) { Build(); _idleWatch.Start(); } else { Clear(); _idleWatch.Stop(); } };
        Loaded += (_, _) => { HookHost(); Build(); _idleWatch.Start(); };
    }

    private void Clear()
    {
        foreach (UIElement child in Children) StopAll(child);
        Children.Clear();
    }

    private static void StopAll(UIElement e)
    {
        e.BeginAnimation(OpacityProperty, null);
        if (e is Panel panel) foreach (UIElement child in panel.Children) StopAll(child);
        if (e.RenderTransform is TransformGroup g) foreach (var t in g.Children) Stop(t);
        else Stop(e.RenderTransform);
        static void Stop(Transform t)
        {
            switch (t)
            {
                case TranslateTransform tt: tt.BeginAnimation(TranslateTransform.XProperty, null); tt.BeginAnimation(TranslateTransform.YProperty, null); break;
                case RotateTransform rt: rt.BeginAnimation(RotateTransform.AngleProperty, null); break;
                case ScaleTransform st: st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); break;
            }
        }
    }

    /// <summary>A slow drifting backdrop needs only 10 to 18 frames a second (a 144 Hz screen would otherwise redraw it 144 times, and every frame also redraws what is on top of it).</summary>
    private static int SceneFps = 10;

    /// <summary>Fast things (rain, meteors, sparks) need a few more frames than slow ones (lanterns, snow, mist, ink) to look smooth.</summary>
    internal static int FpsFor(Scene scene) => scene switch { Scene.Rain or Scene.Storm => 15, Scene.Matrix or Scene.Embers or Scene.Pixels or Scene.Invaders or Scene.Shards or Scene.Synth or Scene.Tactical => 12, _ => 10 };

    private Window? _host;

    /// <summary>The backdrop only moves while its window is the one in front: behind a game or another program it is not drawn at all.</summary>
    private bool HostActive => _host is null || _host.IsActive;

    private void HookHost()
    {
        if (_host is not null || Window.GetWindow(this) is not { } window) return;
        _host = window;
        window.Activated += (_, _) => { if (IsVisible) Build(); };
        window.Deactivated += (_, _) => Clear();
    }

    private bool Animated => Motion.Enabled && IsVisible && !_still;

    private void Build()
    {
        Clear();
        if (!HostActive) return;
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 80 || !IsVisible) return;
        _rng = new Random(7);
        SceneFps = FpsFor(_scene);
        if (!Art(w, h)) return;   // every scene is a painting now (Assets/themes); a missing one just leaves the plain window background
        if (Animated) { Opacity = 0; BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new QuadraticEase() }); }
        else Opacity = 1;
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private double R(double a, double b) => a + _rng.NextDouble() * (b - a);

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    private static Duration D(double seconds) => new(TimeSpan.FromSeconds(seconds));

    /// <summary>From → to again and again, starting part-way through so the sprites never move in step.</summary>
    private static void Loop(IAnimatable target, DependencyProperty dp, double from, double to, double seconds, double phase, bool reverse = false, bool smooth = false, int? fps = null)
    {
        var a = new DoubleAnimation(from, to, D(seconds))
        {
            RepeatBehavior = RepeatBehavior.Forever, AutoReverse = reverse, BeginTime = TimeSpan.FromSeconds(-phase * seconds * (reverse ? 2 : 1)),
            EasingFunction = smooth ? new SineEase { EasingMode = EasingMode.EaseInOut } : null,
        };
        Timeline.SetDesiredFrameRate(a, fps ?? SceneFps);
        target.BeginAnimation(dp, a);
    }
}
