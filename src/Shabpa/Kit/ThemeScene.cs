using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>The slow animated backdrop of a premium theme. It lies behind the pages, never takes the mouse, uses only a few dozen
/// plain shapes moved by transforms and opacity (no effects, no per-frame code) and stands still when animations are off or the
/// window is hidden. Layout is rebuilt (after a short pause) when the window is resized.</summary>
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

    private readonly Palette? _palette;

    /// <summary>The palette the scene takes its few theme-dependent colours from (the running theme unless a preview gives its own).</summary>
    private Palette Pal => _palette ?? Ui.Theme;

    public ThemeScene(Scene scene, Palette? palette = null)
    {
        _scene = scene;
        _palette = palette;
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

    /// <summary>A meteor crosses the sky in a fraction of a second, so it gets its own, higher rate; the stars between meteors stay slow.</summary>
    private const int StreakFps = 24;

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
        switch (_scene)
        {
            case Scene.Lanterns: Lanterns(w, h); break;
            case Scene.Rain: Rain(w, h); break;
            case Scene.Dunes: Dunes(w, h); break;
            case Scene.Snow: Snow(w, h); break;
            case Scene.Embers: Embers(w, h); break;
            case Scene.Tiles: Tiles(w, h); break;
            case Scene.Ink: Ink(w, h); break;
            case Scene.Prism: Prism(w, h); break;
            case Scene.Mist: Mist(w, h); break;
            case Scene.Meteors: Meteors(w, h); break;
            default: More(w, h); break;
        }
        if (Animated) { Opacity = 0; BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new QuadraticEase() }); }
        else Opacity = 1;
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private double R(double a, double b) => a + _rng.NextDouble() * (b - a);

    private static Color C(string hex, byte alpha = 255) =>
        Color.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));

    private Color Accent(byte alpha) { var (r, g, b) = Pal.Rgb("Accent"); return Color.FromArgb(alpha, r, g, b); }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    private static Brush Radial(Color center, Color edge) => Freeze(new RadialGradientBrush(center, edge));

    private static Brush Linear(Color a, Color b) => Grad(new Point(0, 0), new Point(0, 1), (a, 0), (b, 1));

    private static Brush Grad(Point start, Point end, params (Color color, double at)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, at) in stops) g.GradientStops.Add(new GradientStop(color, at));
        g.Freeze();
        return g;
    }

    private static TranslateTransform Move(UIElement e)
    {
        var t = new TranslateTransform();
        e.RenderTransform = t;
        return t;
    }

    private static Duration D(double seconds) => new(TimeSpan.FromSeconds(seconds));

    /// <summary>From → to again and again, starting part-way through so the shapes never move in step.</summary>
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

    /// <summary>A shape that moves or fades is drawn once into a small texture and then only moved, instead of being tessellated and filled again every frame
    /// (this is what made the busy backdrops cost 6-15 % of the graphics card).</summary>
    private static readonly BitmapCache Cached = MakeCache();

    private static BitmapCache MakeCache() { var c = new BitmapCache(1.0); c.Freeze(); return c; }

    private void Add(UIElement e, double x, double y)
    {
        SetLeft(e, x);
        SetTop(e, y);
        if (Animated && !(e is FrameworkElement { Width: > 480 } or FrameworkElement { Height: > 480 })) e.CacheMode = Cached;   // the big static washes are left alone
        Children.Add(e);
    }

    private Ellipse Dot(double size, Brush fill, double x, double y, double opacity = 1)
    {
        var e = new Ellipse { Width = size, Height = size, Fill = fill, Opacity = opacity, IsHitTestVisible = false };
        Add(e, x, y);
        return e;
    }

    private static void Twinkle(UIElement e, double low, double high, double seconds, double phase) =>
        Loop(e, OpacityProperty, low, high, seconds, phase, reverse: true, smooth: true);

    // ---- the ten scenes ---------------------------------------------------------------------------------------

    private void Lanterns(double w, double h)
    {
        var glow = Radial(C("FFB347", 120), C("FF5A36", 0));
        for (var i = 0; i < 9; i++)
        {
            var scale = R(.7, 1.35);
            var grid = new Grid { Width = 40 * scale, Height = 56 * scale, IsHitTestVisible = false };
            grid.Children.Add(new Ellipse { Width = 96 * scale, Height = 96 * scale, Fill = glow, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10 * scale), Margin = new Thickness(5 * scale, 6 * scale, 5 * scale, 6 * scale),
                Background = Linear(C("FFD27A", 235), C("FF6A2B", 215)), BorderBrush = Freeze(new SolidColorBrush(C("7A2412", 200))), BorderThickness = new Thickness(1),
            });
            grid.Children.Add(new Rectangle { Height = 3 * scale, Margin = new Thickness(9 * scale, 4 * scale, 9 * scale, 0), VerticalAlignment = VerticalAlignment.Top, Fill = Freeze(new SolidColorBrush(C("5A1A0C", 230))) });
            grid.Children.Add(new Rectangle { Height = 3 * scale, Margin = new Thickness(9 * scale, 0, 9 * scale, 4 * scale), VerticalAlignment = VerticalAlignment.Bottom, Fill = Freeze(new SolidColorBrush(C("5A1A0C", 230))) });
            var rise = new TranslateTransform();
            var sway = new TranslateTransform();
            grid.RenderTransform = new TransformGroup { Children = { sway, rise } };
            var x = R(.04, .96) * w;
            Add(grid, x, 0);
            grid.Opacity = R(.55, .95);
            if (Animated)
            {
                var seconds = R(24, 42);
                Loop(rise, TranslateTransform.YProperty, h + 90, -120, seconds, i / 9.0);
                Loop(sway, TranslateTransform.XProperty, -18, 18, R(5, 9), R(0, 1), reverse: true, smooth: true);
                Twinkle(grid, .45, .95, R(2.5, 4.5), R(0, 1));
            }
            else SetTop(grid, R(.05, .9) * h);
        }
    }

    private void Rain(double w, double h)
    {
        var streak = Linear(C("BFD9F0", 0), C("BFD9F0", 120));
        var n = (int)Math.Clamp(w / 40, 14, 40);
        for (var i = 0; i < n; i++)
        {
            var len = R(16, 34);
            var line = new Rectangle { Width = 1.3, Height = len, Fill = streak, IsHitTestVisible = false, Opacity = R(.35, .8) };
            var t = Move(line);
            Add(line, R(0, 1) * (w + 80) - 40, -40);
            line.RenderTransformOrigin = new Point(.5, 0);
            if (Animated) Loop(t, TranslateTransform.YProperty, 0, h + 80, R(.9, 1.7), R(0, 1));
            else SetTop(line, R(0, 1) * h);
        }
        // drops that run down the glass, slowly, with a pause between
        var drop = Radial(C("DDEBF7", 150), C("9CC4E4", 30));
        for (var i = 0; i < 9; i++)
        {
            var d = Dot(R(4, 8), drop, R(.03, .97) * w, R(.02, .5) * h, R(.5, .9));
            d.Height = d.Width * 1.5;
            var t = Move(d);
            if (Animated)
            {
                var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-R(0, 9)) }; Timeline.SetDesiredFrameRate(a, SceneFps);
                a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                a.KeyFrames.Add(new LinearDoubleKeyFrame(R(40, 90), KeyTime.FromPercent(.25)));
                a.KeyFrames.Add(new LinearDoubleKeyFrame(R(110, 200), KeyTime.FromPercent(.6)));
                a.KeyFrames.Add(new LinearDoubleKeyFrame(R(260, 420), KeyTime.FromPercent(1)));
                a.Duration = D(R(8, 13));
                t.BeginAnimation(TranslateTransform.YProperty, a);
            }
        }
        var haze = new Rectangle { Width = w, Height = h * .45, Fill = Linear(C("9CC4E4", 0), C("9CC4E4", 38)), IsHitTestVisible = false };
        Add(haze, 0, h * .55);
    }

    private void Dunes(double w, double h)
    {
        var star = Freeze(new SolidColorBrush(C("FFF3D6")));
        for (var i = 0; i < 46; i++)
            Twinkle(Dot(R(1.2, 2.8), star, R(0, 1) * w, R(0, .55) * h, R(.2, .9)), .12, .95, R(1.8, 5), R(0, 1));
        var moon = Dot(Math.Min(w, h) * .16, Radial(C("FFF1CC", 230), C("E8B86B", 0)), w * .78, h * .08, .9);
        moon.Width = moon.Height = Math.Min(w, h) * .2;
        string[] cols = ["3A2314", "2D1A10", "21130B"];
        for (var layer = 0; layer < 3; layer++)
        {
            var baseY = h * (.62 + layer * .12);
            var amp = h * (.07 - layer * .012);
            var path = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(-60, h + 10), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(-60, baseY), true));
            var steps = 5;
            for (var s = 0; s < steps; s++)
            {
                var x0 = -60 + (w + 120) * s / steps;
                var x1 = -60 + (w + 120) * (s + 1) / steps;
                fig.Segments.Add(new BezierSegment(new Point(x0 + (x1 - x0) * .35, baseY - amp * R(.6, 1.4)), new Point(x0 + (x1 - x0) * .65, baseY + amp * R(-.4, .8)), new Point(x1, baseY + amp * R(-.3, .5)), true));
            }
            fig.Segments.Add(new LineSegment(new Point(w + 60, h + 10), true));
            path.Figures.Add(fig);
            path.Freeze();
            var dune = new Path { Data = path, Fill = Linear(C(cols[layer], (byte)(150 + layer * 30)), C("0E0805", 235)), IsHitTestVisible = false };
            var t = Move(dune);
            Add(dune, 0, 0);
            if (Animated) Loop(t, TranslateTransform.XProperty, -14 - layer * 8, 14 + layer * 8, 22 + layer * 7, 0, reverse: true, smooth: true);
        }
    }

    private void Snow(double w, double h)
    {
        var n = (int)Math.Clamp(w / 26, 24, 60);
        for (var i = 0; i < n; i++)
        {
            var size = R(2, 5.5);
            var flake = Dot(size, Freeze(new SolidColorBrush(C("F2F8FF", (byte)R(150, 235)))), R(0, 1) * w, -12);
            var fall = new TranslateTransform();
            var sway = new TranslateTransform();
            flake.RenderTransform = new TransformGroup { Children = { sway, fall } };
            if (Animated)
            {
                Loop(fall, TranslateTransform.YProperty, 0, h + 30, R(9, 20) * (size > 4 ? .8 : 1.1), R(0, 1));
                Loop(sway, TranslateTransform.XProperty, -R(14, 34), R(14, 34), R(3.5, 7), R(0, 1), reverse: true, smooth: true);
            }
            else SetTop(flake, R(0, 1) * h);
        }
        Add(new Rectangle { Width = w, Height = h * .3, IsHitTestVisible = false, Fill = Linear(C("CFE8FF", 0), C("CFE8FF", 34)) }, 0, h * .7);
    }

    private void Embers(double w, double h)
    {
        Add(new Rectangle { Width = w, Height = h * .5, IsHitTestVisible = false, Fill = Linear(C("FF5A1F", 0), C("FF5A1F", 70)) }, 0, h * .5);
        var n = (int)Math.Clamp(w / 30, 20, 48);
        for (var i = 0; i < n; i++)
        {
            var size = R(2, 5);
            var spark = Dot(size, Radial(C("FFE08A", 255), C("FF5A1F", 40)), R(0, 1) * w, h);
            var rise = new TranslateTransform();
            var sway = new TranslateTransform();
            spark.RenderTransform = new TransformGroup { Children = { sway, rise } };
            if (Animated)
            {
                var seconds = R(5, 11);
                var phase = R(0, 1);
                Loop(rise, TranslateTransform.YProperty, 0, -(h * R(.55, 1.05)), seconds, phase);
                Loop(sway, TranslateTransform.XProperty, -R(10, 30), R(10, 30), R(2, 4.5), R(0, 1), reverse: true, smooth: true);
                var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(seconds), BeginTime = TimeSpan.FromSeconds(-phase * seconds) }; Timeline.SetDesiredFrameRate(fade, SceneFps);
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(.95, KeyTime.FromPercent(.12)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(.5, KeyTime.FromPercent(.6)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                spark.BeginAnimation(OpacityProperty, fade);
            }
            else { SetTop(spark, R(.1, 1) * h); spark.Opacity = R(.3, .9); }
        }
    }

    private static Geometry Star8(double r)
    {
        // an eight-pointed star: two squares, one turned an eighth of a turn
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var k = 0; k < 2; k++)
        {
            var fig = new PathFigure { IsClosed = true };
            for (var i = 0; i < 4; i++)
            {
                var a = (i * 90 + k * 45 + 45) * Math.PI / 180;
                var p = new Point(r * Math.Cos(a), r * Math.Sin(a));
                if (i == 0) fig.StartPoint = p; else fig.Segments.Add(new LineSegment(p, true));
            }
            g.Children.Add(new PathGeometry { Figures = { fig } });
        }
        g.Freeze();
        return g;
    }

    private void Tiles(double w, double h)
    {
        var gold = Freeze(new SolidColorBrush(C("E3BE62", 255)));
        var teal = Freeze(new SolidColorBrush(C("2BB5B0", 255)));
        var cell = 118.0;
        var cols = (int)Math.Ceiling(w / cell) + 1;
        var rows = (int)Math.Ceiling(h / cell) + 1;
        var geo = Star8(cell * .36);
        for (var gy = 0; gy < rows; gy++)
            for (var gx = 0; gx < cols; gx++)
            {
                var cx = gx * cell + cell / 2 - 20;
                var cy = gy * cell + cell / 2 - 20;
                var star = new Path { Data = geo, Stroke = (gx + gy) % 2 == 0 ? gold : teal, StrokeThickness = 1.1, Opacity = .16, IsHitTestVisible = false, RenderTransform = new TranslateTransform(cx, cy) };
                Add(star, 0, 0);
                var knot = Dot(5, gold, cx - 2.5, cy - 2.5, .16);
                if (Animated && (gx + gy) % 3 == 0) // a third of the stars shimmer (a few dozen animations, not hundreds)
                {
                    var phase = (gx + gy) / (double)(cols + rows); // a slow shimmer sweeping from the top corner
                    Twinkle(star, .06, .5, 5, phase);
                    Twinkle(knot, .06, .55, 5, phase);
                }
            }
        // two large rosettes turning in opposite corners
        foreach (var (px, py, dir) in new[] { (w * .96, h * .06, 1), (w * .04, h * .94, -1) })
        {
            var rosette = new Canvas { Width = 0, Height = 0, IsHitTestVisible = false, Opacity = .34 };
            var rot = new RotateTransform();
            rosette.RenderTransform = new TransformGroup { Children = { rot, new TranslateTransform(px, py) } };
            var big = Star8(Math.Min(w, h) * .3);
            rosette.Children.Add(new Path { Data = big, Stroke = gold, StrokeThickness = 1.4 });
            rosette.Children.Add(new Path { Data = Star8(Math.Min(w, h) * .21), Stroke = teal, StrokeThickness = 1.2, RenderTransform = new RotateTransform(22.5) });
            rosette.Children.Add(new Ellipse { Width = Math.Min(w, h) * .5, Height = Math.Min(w, h) * .5, Stroke = gold, StrokeThickness = .8, RenderTransform = new TranslateTransform(-Math.Min(w, h) * .25, -Math.Min(w, h) * .25), Opacity = .6 });
            Add(rosette, 0, 0);
            if (Animated) Loop(rot, RotateTransform.AngleProperty, 0, 360 * dir, 140, 0, fps: 5);   // 2.6 degrees a second: five steps a second look smooth
        }
    }

    private void Ink(double w, double h)
    {
        var n = 7;
        for (var i = 0; i < n; i++)
        {
            var size = R(180, 400);
            var red = i == 3;
            var blob = new Ellipse { Width = size, Height = size * R(.7, 1), Fill = Radial(red ? C("8C2F2B", 120) : C("1F1B16", 105), red ? C("8C2F2B", 0) : C("1F1B16", 0)), IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
            var scale = new ScaleTransform(.3, .3);
            blob.RenderTransform = scale;
            Add(blob, R(-.05, .95) * w - size / 2, R(-.05, .95) * h - size / 2);
            if (Animated)
            {
                var seconds = R(16, 26);
                var phase = i / (double)n;
                Loop(scale, ScaleTransform.ScaleXProperty, .25, 1.15, seconds, phase);
                Loop(scale, ScaleTransform.ScaleYProperty, .25, 1.15, seconds, phase);
                var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(seconds), BeginTime = TimeSpan.FromSeconds(-phase * seconds) }; Timeline.SetDesiredFrameRate(fade, SceneFps);
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(.55, KeyTime.FromPercent(.3)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                blob.BeginAnimation(OpacityProperty, fade);
            }
            else { scale.ScaleX = scale.ScaleY = .8; blob.Opacity = .28; }
        }
    }

    private void Prism(double w, double h)
    {
        string[][] pairs = [["A78BFA", "5EEAD4"], ["5EEAD4", "FF8AD0"], ["FF8AD0", "FFD27A"], ["8B7CF6", "5EEAD4"]];
        for (var i = 0; i < 14; i++)
        {
            var size = R(110, 260);
            var pair = pairs[i % pairs.Length];
            var tri = new Polygon
            {
                Points = [new Point(0, size * .87), new Point(size / 2, 0), new Point(size, size * .87)],
                Fill = Grad(new Point(0, 0), new Point(1, 1), (C(pair[0], 120), 0), (C(pair[1], 40), 1)),
                Stroke = Freeze(new SolidColorBrush(C(pair[1], 90))), StrokeThickness = 1, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5), Opacity = .3,
            };
            var rot = new RotateTransform(R(0, 360));
            tri.RenderTransform = rot;
            Add(tri, R(-.05, .95) * w - size / 2, R(-.05, .95) * h - size / 2);
            if (Animated)
            {
                Twinkle(tri, .08, .55, R(4, 9), R(0, 1));
                Loop(rot, RotateTransform.AngleProperty, rot.Angle - 8, rot.Angle + 8, R(16, 28), R(0, 1), reverse: true, smooth: true);
            }
        }
    }

    private void Mist(double w, double h)
    {
        Add(new Ellipse { Width = w * .5, Height = w * .5, IsHitTestVisible = false, Fill = Radial(C("F2B48A", 110), C("F2B48A", 0)) }, -w * .1, -w * .22);
        for (var i = 0; i < 6; i++)
        {
            var band = new Rectangle
            {
                Width = w * R(.7, 1.2), Height = R(90, 200), IsHitTestVisible = false,
                Fill = Grad(new Point(0, .5), new Point(1, .5), (C("FFFFFF", 0), 0), (C("FFFFFF", (byte)R(110, 170)), .5), (C("FFFFFF", 0), 1)),
                RadiusX = 90, RadiusY = 90,
            };
            var t = Move(band);
            Add(band, R(-.2, .5) * w, R(.05, .9) * h);
            if (Animated) Loop(t, TranslateTransform.XProperty, -R(80, 160), R(80, 200), R(26, 48), R(0, 1), reverse: true, smooth: true);
        }
    }

    private void Meteors(double w, double h)
    {
        var star = Freeze(new SolidColorBrush(C("EEF0FF")));
        for (var i = 0; i < 24; i++)
            Twinkle(Dot(R(1, 2.6), star, R(0, 1) * w, R(0, 1) * h, R(.2, .8)), .1, .95, R(2, 6), R(0, 1));
        var tail = Grad(new Point(0, .5), new Point(1, .5), (C("FFE08A", 0), 0), (C("FFE08A", 200), .8), (C("FFFFFF", 255), 1));
        const double angle = 32;
        for (var i = 0; i < 5; i++)
        {
            var len = R(110, 200);
            var streak = new Rectangle { Width = len, Height = 2.2, Fill = tail, RadiusX = 1, RadiusY = 1, IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(0, .5) };
            var move = new TranslateTransform();
            streak.RenderTransform = new TransformGroup { Children = { move, new RotateTransform(angle) } };
            Add(streak, R(0, .8) * w, R(0, .45) * h);
            if (!Animated) { streak.Opacity = 0; continue; }
            var cycle = R(6, 13);
            var start = TimeSpan.FromSeconds(R(0, cycle));
            var travel = R(380, 620);
            var dx = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(cycle), BeginTime = start }; Timeline.SetDesiredFrameRate(dx, StreakFps);
            dx.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            dx.KeyFrames.Add(new LinearDoubleKeyFrame(travel, KeyTime.FromPercent(.12)));
            dx.KeyFrames.Add(new LinearDoubleKeyFrame(travel, KeyTime.FromPercent(1)));
            move.BeginAnimation(TranslateTransform.XProperty, dx);
            var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = D(cycle), BeginTime = start }; Timeline.SetDesiredFrameRate(fade, StreakFps);
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(.95, KeyTime.FromPercent(.02)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(.12)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            streak.BeginAnimation(OpacityProperty, fade);
        }
    }
}
