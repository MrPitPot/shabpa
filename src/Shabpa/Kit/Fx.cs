using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Shabpa.Kit;

/// <summary>The design's real damped spring (k=170, c=14, 0.8 s, about 9% overshoot) as a WPF easing function. It is only a
/// formula the animation clock evaluates: no timer of our own, nothing runs once an animation has ended.</summary>
public sealed class SpringEase : EasingFunctionBase
{
    private const double K = 170, C = 14, T = 0.8;
    private static readonly double W = Math.Sqrt(K), Z = C / (2 * W), Wd = W * Math.Sqrt(1 - Z * Z);

    public SpringEase() { EasingMode = EasingMode.EaseIn; }

    protected override double EaseInCore(double t)
    {
        if (t >= 1) return 1;
        var s = t * T;
        return 1 - Math.Exp(-Z * W * s) * (Math.Cos(Wd * s) + Z * W / Wd * Math.Sin(Wd * s));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();

    /// <summary>Shared frozen instance.</summary>
    public static readonly SpringEase I = Make();
    private static SpringEase Make() { var e = new SpringEase(); e.Freeze(); return e; }
}

/// <summary>Springy hover / press feedback (transform only) and one-shot animation helpers.
/// Rule: the real value is always written first and every animation uses FillBehavior.Stop, so once it ends the clock is
/// gone and an idle control costs nothing. Everything honours <see cref="Motion.Enabled"/> (Settings, low-memory mode,
/// Windows' animation switch).
/// <para>XAML: <c>kit:Fx.Lift="2" kit:Fx.Press="0.96"</c> on any element (done for you by the Btn/Chip/Nav styles).</para></summary>
public static class Fx
{
    public static bool Enabled => Motion.Enabled;

    public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached("Lift", typeof(double), typeof(Fx), new PropertyMetadata(0.0, Changed));
    public static readonly DependencyProperty NudgeProperty = DependencyProperty.RegisterAttached("Nudge", typeof(double), typeof(Fx), new PropertyMetadata(0.0, Changed));
    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached("Grow", typeof(double), typeof(Fx), new PropertyMetadata(1.0, Changed));
    public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached("Press", typeof(double), typeof(Fx), new PropertyMetadata(1.0, Changed));

    /// <summary>Pixels the element rises on hover.</summary>
    public static double GetLift(DependencyObject o) => (double)o.GetValue(LiftProperty);
    public static void SetLift(DependencyObject o, double v) => o.SetValue(LiftProperty, v);
    /// <summary>Pixels it moves along the reading direction on hover (towards the content; mirrors itself in RTL).</summary>
    public static double GetNudge(DependencyObject o) => (double)o.GetValue(NudgeProperty);
    public static void SetNudge(DependencyObject o, double v) => o.SetValue(NudgeProperty, v);
    /// <summary>Scale on hover (1 = none).</summary>
    public static double GetGrow(DependencyObject o) => (double)o.GetValue(GrowProperty);
    public static void SetGrow(DependencyObject o, double v) => o.SetValue(GrowProperty, v);
    /// <summary>Scale while pressed (1 = none; the design uses .94 to .97).</summary>
    public static double GetPress(DependencyObject o) => (double)o.GetValue(PressProperty);
    public static void SetPress(DependencyObject o, double v) => o.SetValue(PressProperty, v);

    private sealed class State
    {
        public readonly ScaleTransform S = new(1, 1);
        public readonly TranslateTransform T = new();
        public bool Over, Down, Hooked;
        public Action<bool>? OnHover;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement el) Hook(el, null);
    }

    /// <summary>Code version of the attached properties. <paramref name="onHover"/> is called with true/false on enter/leave.</summary>
    public static void Attach(FrameworkElement e, double lift = 0, double nudge = 0, double grow = 1, double press = 0.96, Action<bool>? onHover = null)
    {
        SetLift(e, lift); SetNudge(e, nudge); SetGrow(e, grow); SetPress(e, press);
        Hook(e, onHover);
    }

    private static void Hook(FrameworkElement e, Action<bool>? onHover)
    {
        var st = States.GetOrCreateValue(e);
        if (onHover is not null) st.OnHover = onHover;
        if (st.Hooked) return;
        st.Hooked = true;
        e.MouseEnter += (_, _) => { st.Over = true; Apply(e, st); st.OnHover?.Invoke(true); };
        e.MouseLeave += (_, _) => { st.Over = false; st.Down = false; Apply(e, st); st.OnHover?.Invoke(false); };
        e.PreviewMouseLeftButtonDown += (_, _) => { st.Down = true; Apply(e, st); };
        e.PreviewMouseLeftButtonUp += (_, _) => { st.Down = false; Apply(e, st); };
    }

    /// <summary>The element's own transform can be replaced by page entrances; re-attach ours whenever it went missing.</summary>
    private static void Ensure(FrameworkElement e, State st)
    {
        if (e.RenderTransform is TransformGroup g && g.Children.Contains(st.S)) return;
        var group = new TransformGroup();
        if (e.RenderTransform is { } existing && existing != Transform.Identity && !existing.IsFrozen) group.Children.Add(existing);
        group.Children.Add(st.S);
        group.Children.Add(st.T);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = group;
    }

    private static void Apply(FrameworkElement e, State st)
    {
        var lift = GetLift(e); var nudge = GetNudge(e); var grow = GetGrow(e); var press = GetPress(e);
        var scale = st.Down ? press : st.Over ? grow : 1;
        var moving = st.Over && !st.Down;
        if (!Enabled && scale == 1 && !moving && e.RenderTransform is not TransformGroup) return;
        Ensure(e, st);
        Anim(st.S, ScaleTransform.ScaleXProperty, scale);
        Anim(st.S, ScaleTransform.ScaleYProperty, scale);
        Anim(st.T, TranslateTransform.XProperty, moving ? nudge : 0);
        Anim(st.T, TranslateTransform.YProperty, moving ? -lift : 0);
    }

    /// <summary>Sets <paramref name="to"/> immediately and plays the spring from the old value (nothing is held afterwards).</summary>
    public static void Anim(Animatable target, DependencyProperty p, double to, IEasingFunction? ease = null, int ms = 520)
    {
        var from = (double)target.GetValue(p);
        target.SetValue(p, to);
        if (!Enabled || Math.Abs(from - to) < 0.001)
        {
            target.BeginAnimation(p, null); // never leave an older animation running towards a stale target
            return;
        }
        target.BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? SpringEase.I, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Where a child sits inside a horizontal StackPanel, from layout sizes alone (left edge, width, height in the panel's left-to-right space).
    /// Independent of render transforms (press scale, entrance motion) and of the mirroring of right-to-left flow, which made transform-based measuring unreliable.</summary>
    public static (double X, double W, double H) Slot(StackPanel row, FrameworkElement child)
    {
        var x = 0.0;
        var total = row.Children.OfType<FrameworkElement>().Sum(c => c.ActualWidth);
        foreach (var c in row.Children.OfType<FrameworkElement>())
        {
            x += c.ActualWidth;
            if (ReferenceEquals(c, child)) break;
        }
        var w = child.ActualWidth;
        return (row.FlowDirection == FlowDirection.RightToLeft ? total - x : x - w, w, child.ActualHeight);
    }

    /// <summary>Same for an element's own dependency property (Opacity, Width...).</summary>
    public static void Anim(UIElement target, DependencyProperty p, double to, IEasingFunction? ease = null, int ms = 300)
    {
        var from = (double)target.GetValue(p);
        target.SetValue(p, to);
        if (!Enabled || Math.Abs(from - to) < 0.001)
        {
            target.BeginAnimation(p, null);
            return;
        }
        target.BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>For small, repeating live-data touches (a reading lighting up, a meter gliding): 30 frames a second is plenty and halves the work of the render thread.</summary>
    public static T Slow<T>(this T animation) where T : Timeline { Timeline.SetDesiredFrameRate(animation, 30); return animation; }

    public static readonly IEasingFunction Out = Frozen(new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 });

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

/// <summary>A soft drop shadow made of a handful of nested, semi-transparent rounded rectangles in one retained drawing.
/// No DropShadowEffect or pixel shader: plain vector geometry. It never paints under its own box (like CSS box-shadow).
/// Put it as the first child of a Grid behind the thing that casts it.</summary>
public sealed class Shadow : FrameworkElement
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(Shadow),
        new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Radius { get; set; } = 18;
    public double Blur { get; set; } = 30;
    public double Spread { get; set; } = -14;
    public double OffsetY { get; set; } = 14;
    public int Layers { get; set; } = 9;
    public bool Knockout { get; set; } = true;

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    private static readonly Dictionary<(Color, int), Brush> Cache = [];

    /// <summary>Draws the shadow of a rounded box of the given size (also used by <see cref="GlassCard"/>). Large boxes with the knock-out use the shared 9-slice bitmaps (<see cref="ShadowBake"/>); <paramref name="dpiScale"/> is the screen's scale (1 = 96 dpi).</summary>
    public static void Draw(DrawingContext dc, double w, double h, Color color, double radius, double blur, double spread, double offsetY, int layers, bool knockout = true, double dpiScale = 1)
    {
        if (w <= 0 || h <= 0 || color.A == 0) return;
        if (knockout && ShadowBake.TryDraw(dc, w, h, color, radius, blur, spread, offsetY, layers, dpiScale)) return;
        DrawVector(dc, w, h, color, radius, blur, spread, offsetY, layers, knockout);
    }

    /// <summary>The plain vector version: nested translucent rounded rectangles (and the knock-out clip).</summary>
    internal static void DrawVector(DrawingContext dc, double w, double h, Color color, double radius, double blur, double spread, double offsetY, int layers, bool knockout = true)
    {
        if (w <= 0 || h <= 0 || color.A == 0) return;
        var key = (color, layers);
        if (!Cache.TryGetValue(key, out var brush))
        {
            var a = 1 - Math.Pow(1 - color.A / 255.0, 1.0 / layers);
            brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), color.R, color.G, color.B));
            brush.Freeze();
            Cache[key] = brush;
        }
        if (knockout)
        {
            var big = new RectangleGeometry(new Rect(-160, -160, w + 320, h + 400));
            var hole = new CombinedGeometry(GeometryCombineMode.Exclude, big, new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
            hole.Freeze();
            dc.PushClip(hole);
        }
        for (var k = 0; k < layers; k++)
        {
            var t = layers == 1 ? 0.5 : k / (double)(layers - 1);
            var grow = spread + blur * (0.5 - t);
            var r = new Rect(-grow, offsetY - grow, Math.Max(0, w + 2 * grow), Math.Max(0, h + 2 * grow));
            if (r.Width <= 0 || r.Height <= 0) continue;
            var rad = Math.Max(0, Math.Min(radius + grow, Math.Min(r.Width, r.Height) / 2));
            dc.DrawRoundedRectangle(brush, null, r, rad, rad);
        }
        if (knockout) dc.Pop();
    }

    protected override void OnRender(DrawingContext dc) =>
        Draw(dc, ActualWidth, ActualHeight, Color, Radius, Blur, Spread, OffsetY, Layers, Knockout, VisualTreeHelper.GetDpi(this).DpiScaleX);
}

/// <summary>Stroke icon drawn from a frozen 24x24 geometry of Icons.xaml (<c>I.name</c>). The stroke is the inherited
/// Foreground, so an icon inside a button or nav item follows its text colour. <c>&lt;kit:Icon Name="home" Width="20"/&gt;</c>.</summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Icon)d)._geometry = null));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty WeightProperty = DependencyProperty.Register(nameof(Weight), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(1.7, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Icon key without the "I." prefix, see Icons.xaml. (Not called Name because FrameworkElement.Name is taken.)</summary>
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    /// <summary>Same as <see cref="Glyph"/>.</summary>
    public string IconName { get => Glyph; set => Glyph = value; }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Weight { get => (double)GetValue(WeightProperty); set => SetValue(WeightProperty, value); }

    private Geometry? _geometry;

    static Icon()
    {
        WidthProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(20.0));
        HeightProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(20.0));
        FlowDirectionProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(FlowDirection.LeftToRight));
    }

    // icons are drawings, not text: the design's SVGs are never mirrored, so a check mark or a play triangle looks the same in RTL
    public Icon() { FlowDirection = FlowDirection.LeftToRight; }

    public Icon(string name, double size = 20, Brush? foreground = null, double weight = 1.7)
    {
        FlowDirection = FlowDirection.LeftToRight;
        IconName = name; Width = Height = size; Weight = weight;
        if (foreground is not null) Foreground = foreground;
    }

    /// <summary>True if an icon of that name exists.</summary>
    public static bool Has(string? name) => !string.IsNullOrEmpty(name) && Application.Current?.TryFindResource("I." + name) is Geometry;

    protected override void OnRender(DrawingContext dc)
    {
        _geometry ??= Application.Current?.TryFindResource("I." + IconName) as Geometry;
        if (_geometry is null) return;
        var s = Math.Min(ActualWidth, ActualHeight) / 24.0;
        var pen = new Pen(Foreground, Weight) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (pen.CanFreeze) pen.Freeze();
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(null, pen, _geometry);
        dc.Pop();
    }
}

/// <summary>Skin knobs for the shared templates.</summary>
public static class Skin
{
    /// <summary>Corner radius used by the Chip/Tab/Tile/Day/Button templates (set it in a style).</summary>
    public static readonly DependencyProperty CornerProperty = DependencyProperty.RegisterAttached("Corner", typeof(CornerRadius), typeof(Skin), new PropertyMetadata(new CornerRadius(11)));
    public static CornerRadius GetCorner(DependencyObject o) => (CornerRadius)o.GetValue(CornerProperty);
    public static void SetCorner(DependencyObject o, CornerRadius v) => o.SetValue(CornerProperty, v);
}
