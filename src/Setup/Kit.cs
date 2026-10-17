using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Shabpa.Setup;

// The installer is a separate net48 project and cannot reference the app's Kit, so this file holds the small slice of the
// "Glass Depth" kit the installer needs (spring easing, springy hover/press, layered shadow, glass card, stroke icons,
// sliding segmented control, switch, glass buttons, animated bar and ring). Same numbers as src/Shabpa/Kit.
// Rules kept: transform/opacity/gradient only, no Effect, nothing keeps running at idle, everything honours Motion.Enabled.

/// <summary>Motion on/off. Off when Windows' "animation effects" switch is off (reduced motion) or SHABPA_REDUCED_MOTION=1.</summary>
internal static class Motion
{
    private static readonly bool Forced = Environment.GetEnvironmentVariable("SHABPA_REDUCED_MOTION") == "1";

    public static bool Enabled => !Forced && SystemParameters.ClientAreaAnimation;
}

/// <summary>The design's damped spring (k=170, c=14, 0.8 s) as an easing function: a formula the animation clock evaluates.</summary>
internal sealed class SpringEase : EasingFunctionBase
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

    public static readonly SpringEase I = Make();
    private static SpringEase Make() { var e = new SpringEase(); e.Freeze(); return e; }
}

/// <summary>Resource helpers.</summary>
internal static class K
{
    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    public static Color C(string key) => (Color)Application.Current.FindResource(key);
    public static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Max(0, Math.Min(1, a)) * 255), c.R, c.G, c.B);
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}

/// <summary>Springy hover / press feedback (transform only) and one-shot animation helpers. The real value is always written
/// first and animations use FillBehavior.Stop, so an idle control has no clock running.</summary>
internal static class Fx
{
    public static bool Enabled => Motion.Enabled;

    public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached("Lift", typeof(double), typeof(Fx), new PropertyMetadata(0.0, Changed));
    public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached("Press", typeof(double), typeof(Fx), new PropertyMetadata(1.0, Changed));
    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached("Grow", typeof(double), typeof(Fx), new PropertyMetadata(1.0, Changed));
    public static double GetLift(DependencyObject o) => (double)o.GetValue(LiftProperty);
    public static void SetLift(DependencyObject o, double v) => o.SetValue(LiftProperty, v);
    public static double GetPress(DependencyObject o) => (double)o.GetValue(PressProperty);
    public static void SetPress(DependencyObject o, double v) => o.SetValue(PressProperty, v);
    public static double GetGrow(DependencyObject o) => (double)o.GetValue(GrowProperty);
    public static void SetGrow(DependencyObject o, double v) => o.SetValue(GrowProperty, v);

    private sealed class State
    {
        public readonly ScaleTransform S = new ScaleTransform(1, 1);
        public readonly TranslateTransform T = new TranslateTransform();
        public bool Over, Down, Hooked;
        public Action<bool>? OnHover;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new ConditionalWeakTable<FrameworkElement, State>();

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement el) Hook(el, null);
    }

    public static void Attach(FrameworkElement e, double lift = 0, double grow = 1, double press = 0.96, Action<bool>? onHover = null)
    {
        SetLift(e, lift); SetGrow(e, grow); SetPress(e, press);
        Hook(e, onHover);
    }

    private static void Hook(FrameworkElement e, Action<bool>? onHover)
    {
        var st = States.GetOrCreateValue(e);
        if (onHover != null) st.OnHover = onHover;
        if (st.Hooked) return;
        st.Hooked = true;
        e.MouseEnter += (_, __) => { st.Over = true; Apply(e, st); st.OnHover?.Invoke(true); };
        e.MouseLeave += (_, __) => { st.Over = false; st.Down = false; Apply(e, st); st.OnHover?.Invoke(false); };
        e.PreviewMouseLeftButtonDown += (_, __) => { st.Down = true; Apply(e, st); };
        e.PreviewMouseLeftButtonUp += (_, __) => { st.Down = false; Apply(e, st); };
    }

    private static void Ensure(FrameworkElement e, State st)
    {
        if (e.RenderTransform is TransformGroup g && g.Children.Contains(st.S)) return;
        var group = new TransformGroup();
        group.Children.Add(st.S);
        group.Children.Add(st.T);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = group;
    }

    private static void Apply(FrameworkElement e, State st)
    {
        var lift = GetLift(e); var grow = GetGrow(e); var press = GetPress(e);
        var scale = st.Down ? press : st.Over ? grow : 1;
        var moving = st.Over && !st.Down;
        if (!Enabled && scale == 1 && !moving && !(e.RenderTransform is TransformGroup)) return;
        Ensure(e, st);
        Anim(st.S, ScaleTransform.ScaleXProperty, scale);
        Anim(st.S, ScaleTransform.ScaleYProperty, scale);
        Anim(st.T, TranslateTransform.YProperty, moving ? -lift : 0);
    }

    /// <summary>Sets <paramref name="to"/> immediately and plays the spring from the old value (nothing is held afterwards).</summary>
    public static void Anim(Animatable target, DependencyProperty p, double to, IEasingFunction? ease = null, int ms = 520)
    {
        var from = (double)target.GetValue(p);
        target.SetValue(p, to);
        if (!Enabled || Math.Abs(from - to) < 0.001) return;
        target.BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? SpringEase.I, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Same for an element's own dependency property (Opacity, Value...).</summary>
    public static void Anim(UIElement target, DependencyProperty p, double to, IEasingFunction? ease = null, int ms = 300)
    {
        var from = (double)target.GetValue(p);
        target.SetValue(p, to);
        if (!Enabled || Math.Abs(from - to) < 0.001) return;
        target.BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    public static readonly IEasingFunction Out = Frozen(new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 });

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>A transform group (scale about the centre + translate) for entrance animations; created once per element.</summary>
    public static (ScaleTransform scale, TranslateTransform move) Rig(UIElement e)
    {
        if (e.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t && !g.IsFrozen)
            return (s, t);
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(move);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = group;
        return (scale, move);
    }

    /// <summary>The design's "rise": fade in while sliding up from <paramref name="dy"/> px and growing from .97, after a delay.
    /// The start state is applied first, so nothing flashes during the delay; the end state is written when the clock ends.</summary>
    public static void Rise(UIElement e, int delayMs, double dy = 22, int ms = 800, double fromScale = 0.97)
    {
        var (scale, move) = Rig(e);
        if (!Enabled)
        {
            e.Opacity = 1; scale.ScaleX = scale.ScaleY = 1; move.Y = 0;
            return;
        }
        e.Opacity = 0; scale.ScaleX = scale.ScaleY = fromScale; move.Y = dy;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var dur = TimeSpan.FromMilliseconds(ms);
        void Play(Animatable target, DependencyProperty p, double from, double to, int dms, EasingFunctionBase? ease, bool last)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(dms)) { BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            a.Completed += (_, __) =>
            {
                target.BeginAnimation(p, null);
                target.SetValue(p, to);
            };
            target.BeginAnimation(p, a);
        }
        var easeOut = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        Play(move, TranslateTransform.YProperty, dy, 0, ms, easeOut, false);
        Play(scale, ScaleTransform.ScaleXProperty, fromScale, 1, ms, easeOut, false);
        Play(scale, ScaleTransform.ScaleYProperty, fromScale, 1, ms, easeOut, false);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Min(ms, 420))) { BeginTime = begin, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, __) => { e.BeginAnimation(UIElement.OpacityProperty, null); e.Opacity = 1; };
        e.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Spring "pop": scale from <paramref name="from"/> to 1 with the design's overshoot.</summary>
    public static void Pop(UIElement e, double from = 0.6, int ms = 700, int delayMs = 0)
    {
        var (scale, _) = Rig(e);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
        if (!Enabled) return;
        foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            scale.BeginAnimation(p, new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = SpringEase.I, BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.Stop });
    }
}

/// <summary>A soft drop shadow made of nested semi-transparent rounded rectangles (no DropShadowEffect). Never paints under
/// its own box, like CSS box-shadow. Put it as the first child behind the thing that casts it.</summary>
internal sealed class Shadow : FrameworkElement
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(Shadow),
        new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Radius { get; set; } = 18;
    public double Blur { get; set; } = 30;
    public double Spread { get; set; } = -14;
    public double OffsetY { get; set; } = 14;
    public int Layers { get; set; } = 9;

    protected override Size MeasureOverride(Size availableSize) => new Size(0, 0);

    private static readonly Dictionary<(Color, int), Brush> Cache = new Dictionary<(Color, int), Brush>();

    public static void Draw(DrawingContext dc, double w, double h, Color color, double radius, double blur, double spread, double offsetY, int layers)
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
        var big = new RectangleGeometry(new Rect(-160, -160, w + 320, h + 400));
        var hole = new CombinedGeometry(GeometryCombineMode.Exclude, big, new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
        hole.Freeze();
        dc.PushClip(hole);
        for (var k = 0; k < layers; k++)
        {
            var t = layers == 1 ? 0.5 : k / (double)(layers - 1);
            var grow = spread + blur * (0.5 - t);
            var r = new Rect(-grow, offsetY - grow, Math.Max(0, w + 2 * grow), Math.Max(0, h + 2 * grow));
            if (r.Width <= 0 || r.Height <= 0) continue;
            var rad = Math.Max(0, Math.Min(radius + grow, Math.Min(r.Width, r.Height) / 2));
            dc.DrawRoundedRectangle(brush, null, r, rad, rad);
        }
        dc.Pop();
    }

    protected override void OnRender(DrawingContext dc) =>
        Draw(dc, ActualWidth, ActualHeight, Color, Radius, Blur, Spread, OffsetY, Layers);
}

/// <summary>Layer 2 of the design: an acrylic card (translucent fill, rim light, layered soft shadow).</summary>
internal sealed class GlassCard : Border
{
    public GlassCard()
    {
        CornerRadius = new CornerRadius(18);
        Background = K.B("CardFill");
        BorderBrush = K.B("CardRim");
        BorderThickness = new Thickness(1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            var radius = CornerRadius.TopLeft;
            Shadow.Draw(dc, ActualWidth, ActualHeight, K.C("ShadowC"), radius, 30, -14, 14, 9);
            Shadow.Draw(dc, ActualWidth, ActualHeight, K.C("ShadowTightC"), radius, 3, 0, 1, 3);
        }
        base.OnRender(dc);
    }
}

/// <summary>24x24 stroke icons (same geometry as the app's Icons.xaml); stroke = inherited Foreground.</summary>
internal sealed class StrokeIcon : FrameworkElement
{
    private static readonly Dictionary<string, Geometry> Geometries = new Dictionary<string, Geometry>();
    private static readonly Dictionary<string, string> Data = new Dictionary<string, string>
    {
        ["dl"] = "M12 4v11m0 0l-4-4m4 4l4-4M5 20h14",
        ["refresh"] = "M20 11a8 8 0 0 0-14.5-4M4 4v4h4M4 13a8 8 0 0 0 14.5 4M20 20v-4h-4",
        ["trash"] = "M4 7h16M10 3h4M6 7l1 13h10l1-13M10 11v6M14 11v6",
        ["chk"] = "M5 12.5l4.5 4.5L19 7",
        ["folder"] = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        ["cloud"] = "M7 18a4 4 0 1 1 .9-7.9A5.5 5.5 0 0 1 18.5 11 3.5 3.5 0 0 1 18 18z",
        ["alert"] = "M12 4l9.5 16.5h-19zM12 10v4.5M12 17.6h.01",
        ["x"] = "M6 6l12 12M18 6L6 18",
        ["min"] = "M6 12h12",
        ["moon"] = "M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z",
    };

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StrokeIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(StrokeIcon),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty WeightProperty = DependencyProperty.Register(nameof(Weight), typeof(double), typeof(StrokeIcon),
        new FrameworkPropertyMetadata(1.7, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Weight { get => (double)GetValue(WeightProperty); set => SetValue(WeightProperty, value); }

    static StrokeIcon()
    {
        WidthProperty.OverrideMetadata(typeof(StrokeIcon), new FrameworkPropertyMetadata(20.0));
        HeightProperty.OverrideMetadata(typeof(StrokeIcon), new FrameworkPropertyMetadata(20.0));
        FlowDirectionProperty.OverrideMetadata(typeof(StrokeIcon), new FrameworkPropertyMetadata(FlowDirection.LeftToRight));
    }

    public StrokeIcon() { }

    public StrokeIcon(string glyph, double size = 20, Brush? foreground = null, double weight = 1.7)
    {
        Glyph = glyph; Width = Height = size; Weight = weight;
        if (foreground != null) Foreground = foreground;
    }

    public static Geometry? Get(string name)
    {
        if (Geometries.TryGetValue(name, out var g)) return g;
        if (!Data.TryGetValue(name, out var data)) return null;
        g = Geometry.Parse(data);
        g.Freeze();
        Geometries[name] = g;
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var geometry = Get(Glyph);
        if (geometry == null) return;
        var s = Math.Min(ActualWidth, ActualHeight) / 24.0;
        var pen = new Pen(Foreground, Weight) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (pen.CanFreeze) pen.Freeze();
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }
}

/// <summary>Segmented control: a track with ONE thumb that springs to the chosen option (design: .seg).</summary>
internal sealed class Segmented : Border
{
    private readonly Grid _thumb = new Grid();
    private readonly TranslateTransform _tx = new TranslateTransform();
    private readonly StackPanel _row = new StackPanel { Orientation = Orientation.Horizontal };
    private readonly Grid _host = new Grid { FlowDirection = FlowDirection.LeftToRight };
    private readonly List<TextBlock> _items = new List<TextBlock>();

    public int Index { get; private set; }
    public event Action<int>? Changed;

    public Segmented(IList<string> labels, int selected = 0)
    {
        Background = K.B("Line");
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(4);
        Focusable = true;
        FocusVisualStyle = null;
        Index = (int)K.Clamp(selected, 0, Math.Max(0, labels.Count - 1));
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.VerticalAlignment = VerticalAlignment.Top;
        _thumb.RenderTransform = _tx;
        _thumb.IsHitTestVisible = false;
        _thumb.Children.Add(new Shadow { Radius = 9, Blur = 10, Spread = -4, OffsetY = 4, Layers = 4, Color = K.C("ShadowSoftC") });
        _thumb.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Background = K.B("Card2Fill"), BorderBrush = K.B("CardRim") });
        _host.Children.Add(_thumb);
        for (var i = 0; i < labels.Count; i++)
        {
            var idx = i;
            var t = new TextBlock
            {
                Text = labels[i], FontWeight = FontWeights.SemiBold, FontSize = 12.5, Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Padding = new Thickness(16, 6, 16, 6), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            t.MouseLeftButtonUp += (_, __) => Select(idx);
            Fx.SetPress(t, 0.95);
            _items.Add(t);
            _row.Children.Add(t);
        }
        _host.Children.Add(_row);
        Child = _host;
        Paint();
        Loaded += (_, __) => Place(false);
        _row.SizeChanged += (_, __) => Place(false);
        PreviewKeyDown += (_, e) =>
        {
            var step = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0;
            if (step == 0) return;
            Select((int)K.Clamp(Index + step, 0, _items.Count - 1));
            e.Handled = true;
        };
    }

    public void Select(int i, bool raise = true)
    {
        if (i == Index || i < 0 || i >= _items.Count) return;
        Index = i;
        Paint();
        Place(true);
        if (raise) Changed?.Invoke(i);
    }

    private void Paint()
    {
        for (var i = 0; i < _items.Count; i++) _items[i].Foreground = K.B(i == Index ? "Text" : "Muted");
    }

    private void Place(bool animate)
    {
        if (_items.Count == 0 || !_items[Index].IsLoaded || _items[Index].ActualWidth <= 0) return;
        var o = _items[Index];
        var p = o.TransformToAncestor(_host).TransformBounds(new Rect(0, 0, o.ActualWidth, o.ActualHeight)).TopLeft;
        var w = o.ActualWidth;
        _thumb.Height = o.ActualHeight;
        if (!animate || !Fx.Enabled || double.IsNaN(_thumb.Width))
        {
            _thumb.Width = w;
            _tx.BeginAnimation(TranslateTransform.XProperty, null);
            _tx.X = p.X;
            return;
        }
        var from = _thumb.Width;
        _thumb.Width = w;
        _thumb.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(from, w, TimeSpan.FromMilliseconds(520)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop });
        Fx.Anim(_tx, TranslateTransform.XProperty, p.X);
    }
}

/// <summary>On/off switch (design: .tg). Knob springs across and squashes while pressed. Content = label at the start,
/// switch at the end. Template: implicit style in App.xaml.</summary>
internal sealed class Switch : CheckBox
{
    private readonly TranslateTransform _move = new TranslateTransform();
    private readonly ScaleTransform _squash = new ScaleTransform(1, 1);
    private FrameworkElement? _knob;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _knob = GetTemplateChild("PART_Knob") as FrameworkElement;
        if (_knob == null) return;
        var group = new TransformGroup();
        group.Children.Add(_squash);
        group.Children.Add(_move);
        _knob.RenderTransformOrigin = new Point(0.5, 0.5);
        _knob.RenderTransform = group;
        _move.X = Target();
    }

    private double Target() => IsChecked == true ? 20 : 0;

    protected override void OnToggle()
    {
        base.OnToggle();
        Fx.Anim(_move, TranslateTransform.XProperty, Target());
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        if (_knob != null) Fx.Anim(_squash, ScaleTransform.ScaleXProperty, IsPressed ? 1.35 : 1.0, null, 300);
    }
}

/// <summary>Button with the design's spring hover lift, press scale, and (primary) glow + one sheen sweep per hover.</summary>
internal sealed class GlassButton : Button
{
    public static readonly DependencyProperty IsPrimaryProperty = DependencyProperty.Register(nameof(IsPrimary), typeof(bool), typeof(GlassButton), new PropertyMetadata(false));
    public bool IsPrimary { get => (bool)GetValue(IsPrimaryProperty); set => SetValue(IsPrimaryProperty, value); }

    private TranslateTransform? _sheen;
    private Shadow? _glow;

    public GlassButton()
    {
        Fx.Attach(this, lift: 2, press: 0.96, onHover: Hover);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _sheen = Template.FindName("sheenT", this) as TranslateTransform;
        _glow = GetTemplateChild("PART_Glow") as Shadow;
        if (_glow != null) _glow.Color = IsPrimary ? K.C("AccentGlowC") : K.C("ShadowSoftC");
    }

    private void Hover(bool on)
    {
        if (_glow != null) Fx.Anim(_glow, OpacityProperty, on ? 1.0 : (IsPrimary ? 0.8 : 0.9), null, 300);
        if (on && IsPrimary) Sweep();
    }

    /// <summary>One light sweep across the button (hover, and the completion moment).</summary>
    public void Sweep()
    {
        if (_sheen == null || !Fx.Enabled) return;
        _sheen.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1.4, 1.4, TimeSpan.FromMilliseconds(800)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }
}

/// <summary>Progress bar whose value glides (Fx.Anim) and that can shimmer while work is running.</summary>
internal sealed class GlassBar : ProgressBar
{
    private Rectangle? _sheen;
    private TranslateTransform? _sheenMove;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Indicator") is Border indicator && _sheen == null)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), SpreadMethod = GradientSpreadMethod.Pad };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.30));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 255, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.70));
            _sheenMove = new TranslateTransform(-1.2, 0);
            brush.RelativeTransform = _sheenMove;
            _sheen = new Rectangle { Fill = brush, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false };
            indicator.Child = _sheen;
        }
    }

    /// <summary>Moves the bar towards <paramref name="value"/> with an ease-out glide.</summary>
    public void GlideTo(double value) => Fx.Anim(this, ValueProperty, value, Fx.Out, 450);

    /// <summary>Light band that keeps sweeping over the filled part while work runs (one clock; stopped when work ends).</summary>
    public void Shimmer(bool on)
    {
        if (_sheenMove == null) { ApplyTemplate(); }
        if (_sheenMove == null) return;
        _sheenMove.BeginAnimation(TranslateTransform.XProperty, null);
        _sheenMove.X = -1.2;
        if (on && Fx.Enabled)
        {
            var sweep = new DoubleAnimation(-1.2, 1.4, TimeSpan.FromMilliseconds(2200)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(sweep, 15); // a soft band does not need 60 fps; keeps the installer light while it works
            _sheenMove.BeginAnimation(TranslateTransform.XProperty, sweep);
        }
    }
}

/// <summary>Circular progress: faint track + gradient arc with round caps. Redraws only when <see cref="Fraction"/> changes.</summary>
internal sealed class Ring : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((Ring)d).FractionChanged?.Invoke((double)e.NewValue)));
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double Thickness { get; set; } = 8;
    public event Action<double>? FractionChanged;

    public void GlideTo(double value) => Fx.Anim(this, FractionProperty, K.Clamp(value, 0, 1), Fx.Out, 450);

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = (size - Thickness) / 2;
        var track = new Pen(K.B("Line"), Thickness);
        track.Freeze();
        dc.DrawEllipse(null, track, c, r, r);
        var f = K.Clamp(Fraction, 0, 1);
        if (f <= 0.002) return;
        var grad = new LinearGradientBrush(K.C("Accent1C"), K.C("Accent2C"), 45);
        var pen = new Pen(grad, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (f >= 0.999)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }
        var a = -Math.PI / 2 + f * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
