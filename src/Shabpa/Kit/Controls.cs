using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>Small helpers shared by the kit controls and by pages that build their UI in code.</summary>
public static class K
{
    public static Brush Brush(string key) => Ui.Live(key);
    public static Style Style(string key) => (Style)Application.Current.Resources[key];
    public static Color ColorOf(string key) => Application.Current.Resources[key] switch { Color c => c, SolidColorBrush b => b.Color, _ => Colors.Gray };

    public static TextBlock Text(string text, string style = "Label", double? size = null, FontWeight? weight = null, string? brush = null)
    {
        var t = new TextBlock { Text = text, Style = Style(style) };
        if (size is { } s) t.FontSize = s;
        if (weight is { } w) t.FontWeight = w;
        if (brush is not null) t.Foreground = Brush(brush);
        return t;
    }

    /// <summary>Card with a section title (and optional subtitle) and a body panel. Same shape as Ui2.Card.</summary>
    public static (GlassCard card, StackPanel body) Card(string? title = null, string? subtitle = null, bool lift = false)
    {
        var body = new StackPanel();
        if (title is not null) body.Children.Add(new TextBlock { Text = title, Style = Style("H2") });
        if (subtitle is not null)
            body.Children.Add(new TextBlock { Text = subtitle, Style = Style("Body"), Margin = new Thickness(0, 3, 0, 0) });
        var card = new GlassCard { Margin = new Thickness(0, 12, 0, 0), Child = body, Lift = lift };
        return (card, body);
    }

    /// <summary>One 44 px settings row: label at the start, the control at the end, hairline above (except the first).</summary>
    public static Grid SettingRow(string label, UIElement control, bool first = false, string? hint = null)
    {
        var grid = new Grid { MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 14, 6) };
        texts.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
        if (hint is not null) texts.Children.Add(new TextBlock { Text = hint, Style = Style("Label"), Margin = new Thickness(0, 1, 0, 0) });
        grid.Children.Add(texts);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (first) return grid;
        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Brush("Line") };
        Grid.SetColumnSpan(line, 2);
        grid.Children.Add(line);
        return grid;
    }
}

/// <summary>Round-cornered square that holds an icon in a tint ("HueA".."HueE", "Accent", "Green", "Gold", "Red"):
/// soft tinted background, tinted stroke. On hover of its card it springs to 1.12x and tilts -6 degrees.</summary>
public sealed class IconBadge : Border
{
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _rotate = new(0);

    public IconBadge(string icon, string tint = "Accent", double size = 34, double iconSize = 18, double radius = 11)
    {
        Width = Height = size;
        CornerRadius = new CornerRadius(radius);
        VerticalAlignment = VerticalAlignment.Center;
        Background = K.Brush(tint + "Soft");
        Child = new Icon(icon, iconSize, K.Brush(tint)) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new TransformGroup { Children = { _scale, _rotate } };
    }

    /// <summary>Hover pose (true) or rest (false).</summary>
    public void Pose(bool on)
    {
        Fx.Anim(_scale, ScaleTransform.ScaleXProperty, on ? 1.12 : 1);
        Fx.Anim(_scale, ScaleTransform.ScaleYProperty, on ? 1.12 : 1);
        Fx.Anim(_rotate, RotateTransform.AngleProperty, on ? -6 : 0);
    }

    public void Tint(string tint)
    {
        Background = K.Brush(tint + "Soft");
        if (Child is Icon i) i.Foreground = K.Brush(tint);
    }
}

/// <summary>Segmented control: a track with ONE thumb that springs to the chosen option (design: .seg).
/// <c>new Segmented(["خاموش", "خواب"], 0)</c>; read <see cref="Index"/>, listen to <see cref="Changed"/>.</summary>
public sealed class Segmented : Border
{
    private readonly Grid _thumb = new();
    private readonly TranslateTransform _tx = new();
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _host = new() { FlowDirection = FlowDirection.LeftToRight };
    private readonly List<TextBlock> _items = [];
    private readonly bool _compact;

    public int Index { get; private set; }
    public event Action<int>? Changed;

    public Segmented(IReadOnlyList<string> labels, int selected = 0, bool compact = false)
    {
        _compact = compact;
        Background = K.Brush("Line");
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(4);
        HorizontalAlignment = HorizontalAlignment.Left;
        Focusable = true;
        FocusVisualStyle = null;
        Index = Math.Clamp(selected, 0, Math.Max(0, labels.Count - 1));
        _row.FlowDirection = Ui.Flow;
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.VerticalAlignment = VerticalAlignment.Top;
        _thumb.RenderTransform = _tx;
        _thumb.IsHitTestVisible = false;
        _thumb.Children.Add(new Shadow { Radius = 9, Blur = 10, Spread = -4, OffsetY = 4, Layers = 4, Color = K.ColorOf("ShadowSoftC") });
        _thumb.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Background = K.Brush("Card2Fill"), BorderBrush = K.Brush("CardRim") });
        _host.Children.Add(_thumb);
        for (var i = 0; i < labels.Count; i++)
        {
            var idx = i;
            var t = new TextBlock
            {
                Text = labels[i], FontWeight = FontWeights.SemiBold, FontSize = 12.5, Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Padding = new Thickness(compact ? 12 : 16, 6, compact ? 12 : 16, 6), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            t.MouseLeftButtonUp += (_, _) => Select(idx);
            // hover: the label warms up and rises a pixel; press: it dips (the thumb itself springs on Select)
            Fx.Attach(t, lift: 1, press: 0.95, onHover: on => { if (idx != Index) t.Foreground = K.Brush(on ? "Text" : "Muted"); });
            _items.Add(t);
            _row.Children.Add(t);
        }
        _host.Children.Add(_row);
        Child = _host;
        Paint();
        Loaded += (_, _) => Place(false);
        _row.SizeChanged += (_, _) => Place(false);
        PreviewKeyDown += (_, e) =>
        {
            var step = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0;
            if (step == 0) return;
            if (Ui.Flow == FlowDirection.RightToLeft) step = -step;
            Select(Math.Clamp(Index + step, 0, _items.Count - 1));
            e.Handled = true;
        };
    }

    /// <summary>Changes the text of one option (for example a count); the thumb re-fits by itself.</summary>
    public void SetLabel(int i, string text)
    {
        if (i >= 0 && i < _items.Count) _items[i].Text = text;
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
        for (var i = 0; i < _items.Count; i++) _items[i].Foreground = K.Brush(i == Index ? "Text" : "Muted");
    }

    private void Place(bool animate)
    {
        if (_items.Count == 0 || !_items[Index].IsLoaded || _items[Index].ActualWidth <= 0) return;
        var o = _items[Index];
        var p = new Point(Fx.Slot(_row, o).X, 0);
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
        _thumb.BeginAnimation(WidthProperty, new DoubleAnimation(from, w, TimeSpan.FromMilliseconds(520)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop });
        Fx.Anim(_tx, TranslateTransform.XProperty, p.X);
    }
}

/// <summary>On/off switch (design: .tg). Knob springs across and stretches while pressed. Put text in <c>Content</c> to get a
/// label at the start and the switch at the end. Style key "Switch" (set by default).</summary>
public sealed class Switch : CheckBox
{
    private readonly TranslateTransform _move = new();
    private FrameworkElement? _knob;

    public Switch()
    {
        Style = K.Style("Switch");
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _knob = GetTemplateChild("PART_Knob") as FrameworkElement;
        if (_knob is null) return;
        _knob.RenderTransform = _move;
        _move.X = Target();
        _fill = GetTemplateChild("fill") as FrameworkElement;
        // hover: the whole switch swells a touch (transform only)
        if (_knob.Parent is FrameworkElement body) Fx.Attach(body, grow: 1.06, press: 1);
    }

    private FrameworkElement? _fill;

    private double Target() => IsChecked == true ? (IsPressed ? 14 : 20) : 0;

    /// <summary>The accent fill fades in and out with the knob (the template trigger alone would flip it at once).</summary>
    private void Fade()
    {
        if (_fill is not null) Fx.Anim(_fill, OpacityProperty, IsChecked == true ? 1 : 0, null, 240);
    }

    protected override void OnToggle()
    {
        base.OnToggle();
        Fx.Anim(_move, TranslateTransform.XProperty, Target());
        Fade();
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        if (_knob is null) return;
        Fx.Anim(_knob, WidthProperty, IsPressed ? 20 : 14, null, 200);
        Fx.Anim(_move, TranslateTransform.XProperty, Target());
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        Fx.Anim(_move, TranslateTransform.XProperty, Target());
        Fade();
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        Fx.Anim(_move, TranslateTransform.XProperty, Target());
        Fade();
    }
}

/// <summary>Tiny polyline chart (design: stat-card sparkline). One frozen geometry per <see cref="Set"/>; nothing animates.
/// <c>Fill</c> adds the soft gradient area under the line (health chart).</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(nameof(Line), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Line { get => (Brush)GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public bool Fill { get; set; }
    public double Thickness { get; set; } = 2;
    private double[] _v = [];

    public Sparkline() { FlowDirection = FlowDirection.LeftToRight; }

    public void Set(IReadOnlyList<double> values)
    {
        _v = [.. values];
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_v.Length < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;
        double mn = _v.Min(), mx = _v.Max(), span = mx - mn < 1e-9 ? 1 : mx - mn;
        double h = ActualHeight, w = ActualWidth;
        var pts = new Point[_v.Length];
        for (var i = 0; i < _v.Length; i++)
            pts[i] = new Point(1 + i * (w - 2) / (_v.Length - 1), 2 + (1 - (_v[i] - mn) / span) * (h - 4));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            for (var i = 1; i < pts.Length; i++) c.LineTo(pts[i], true, true);
        }
        g.Freeze();
        if (Fill && Line is SolidColorBrush sb)
        {
            var area = new StreamGeometry();
            using (var c = area.Open())
            {
                c.BeginFigure(new Point(pts[0].X, h), true, true);
                foreach (var p in pts) c.LineTo(p, true, false);
                c.LineTo(new Point(pts[^1].X, h), true, false);
            }
            area.Freeze();
            var fill = new LinearGradientBrush(Color.FromArgb(0x80, sb.Color.R, sb.Color.G, sb.Color.B), Color.FromArgb(0, sb.Color.R, sb.Color.G, sb.Color.B), 90);
            fill.Freeze();
            dc.DrawGeometry(fill, null, area);
        }
        var pen = new Pen(Line, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (pen.CanFreeze) pen.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>The countdown / score ring of the design: static track (and 60 ticks), a gradient arc, an optional knob.
/// Redrawn only when <see cref="Fraction"/> changes. <see cref="AnimateTo"/> sweeps it in 1.6 s (ease out).
/// Hero ring: defaults (300 box). Health score ring: <c>ViewSize=200, RingRadius=84, Thickness=12, Ticks=false, Knob=false</c>.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(nameof(ArcBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    /// <summary>Optional solid colour for the arc (e.g. green after confirming); null = accent gradient.</summary>
    public Brush? ArcBrush { get => (Brush?)GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public double ViewSize { get; set; } = 300;
    public double RingRadius { get; set; } = 118;
    public double Thickness { get; set; } = 14;
    public bool Ticks { get; set; } = true;
    public bool Knob { get; set; } = true;

    /// <summary>Clockwise from the top in both reading directions (like the design's SVG, which is not mirrored).</summary>
    public RingGauge() { FlowDirection = FlowDirection.LeftToRight; }

    public void AnimateTo(double fraction, int ms = 1600)
    {
        var from = Fraction;
        Fraction = fraction;
        if (!Fx.Enabled || Math.Abs(from - fraction) < 0.0005) return;
        BeginAnimation(FractionProperty, new DoubleAnimation(from, fraction, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }

    private static readonly StreamGeometry TickGeometry = BuildTicks();

    private static StreamGeometry BuildTicks()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
            for (var i = 0; i < 60; i++)
            {
                var a = i * 6 * Math.PI / 180;
                var r1 = i % 5 != 0 ? 138 : 134;
                const double r2 = 146;
                c.BeginFigure(new Point(150 + Math.Cos(a) * r1, 150 + Math.Sin(a) * r1), false, false);
                c.LineTo(new Point(150 + Math.Cos(a) * r2, 150 + Math.Sin(a) * r2), true, false);
            }
        g.Freeze();
        return g;
    }

    // The pens and the accent gradient do not change while the app runs (the theme is fixed per process): build and freeze them once,
    // not on every frame of the 1.6 s sweep.
    private Pen? _tickPen, _trackPen, _arcPen;
    private static Pen FrozenPen(Brush brush, double thickness, bool round = true)
    {
        var pen = new Pen(brush, thickness);
        if (round) { pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round; }
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
    private static Brush FrozenGradient()
    {
        var g = new LinearGradientBrush(K.ColorOf("Accent1"), K.ColorOf("Accent2"), new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight) / ViewSize;
        if (s <= 0) return;
        var c0 = ViewSize / 2;
        dc.PushTransform(new ScaleTransform(s, s));
        if (Ticks)
        {
            dc.PushOpacity(0.35);
            dc.DrawGeometry(null, _tickPen ??= FrozenPen(K.Brush("Faint"), 2), TickGeometry);
            dc.Pop();
        }
        dc.DrawEllipse(null, _trackPen ??= FrozenPen(K.Brush("Line"), Thickness, false), new Point(c0, c0), RingRadius, RingRadius);
        var f = Math.Clamp(Fraction, 0, 1);
        if (f > 0.0005)
        {
            Pen pen;
            if (ArcBrush is null) pen = _arcPen ??= FrozenPen(FrozenGradient(), Thickness);
            else pen = new Pen(ArcBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var ang = f * 2 * Math.PI;
            var end = new Point(c0 + RingRadius * Math.Sin(ang), c0 - RingRadius * Math.Cos(ang));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(c0, c0 - RingRadius), false, false);
                if (f >= 0.9999)
                {
                    c.ArcTo(new Point(c0, c0 + RingRadius), new Size(RingRadius, RingRadius), 0, true, SweepDirection.Clockwise, true, false);
                    c.ArcTo(new Point(c0, c0 - RingRadius), new Size(RingRadius, RingRadius), 0, true, SweepDirection.Clockwise, true, false);
                }
                else c.ArcTo(end, new Size(RingRadius, RingRadius), 0, f > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
            if (Knob) dc.DrawEllipse(Brushes.White, new Pen(K.Brush("Accent"), 4), end, 11, 11);
        }
        dc.Pop();
    }
}

/// <summary>Design stat card: tinted icon badge + label, big value with a small unit, and a sparkline (or a meter bar).
/// <c>var c = new StatCard("پردازنده", "cpu", "HueA"); c.Set("۱۸", "٪", history);</c></summary>
public sealed class StatCard : GlassCard
{
    public TextBlock Label { get; } = new() { FontSize = 12.5, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    public TextBlock Value { get; } = new() { FontSize = 32, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, LineHeight = 38, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    public TextBlock Unit { get; } = new() { FontSize = 15, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 6) };
    public Sparkline? Spark { get; }
    public ProgressBar? Meter { get; }
    public IconBadge Badge { get; }

    public StatCard(string label, string icon, string hue = "HueA", bool meter = false)
    {
        Lift = true;
        Padding = new Thickness(20, 18, 20, meter ? 20 : 12);
        Label.Text = label;
        Label.Foreground = K.Brush("Muted");
        Value.Foreground = K.Brush("Text");
        Unit.Foreground = K.Brush("Muted");
        Badge = new IconBadge(icon, hue);
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Badge);
        Label.Margin = new Thickness(10, 0, 0, 0);
        head.Children.Add(Label);
        var val = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        val.Children.Add(Value);
        val.Children.Add(Unit);
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(val);
        if (meter)
        {
            Meter = new ProgressBar { Style = K.Style("Bar"), Maximum = 100, Margin = new Thickness(0, 14, 0, 0) };
            body.Children.Add(Meter);
        }
        else
        {
            Spark = new Sparkline { Height = 34, Margin = new Thickness(0, 6, 0, 0), Line = K.Brush(hue), Opacity = 0.65 };
            body.Children.Add(Spark);
        }
        Child = body;
        MouseEnter += (_, _) => Hover(true);
        MouseLeave += (_, _) => Hover(false);
    }

    private void Hover(bool on)
    {
        Badge.Pose(on);
        if (Spark is not null) Fx.Anim(Spark, OpacityProperty, on ? 1 : 0.65, null, 300);
    }

    private long _lastFlash, _lastMeter;

    /// <summary>Value text, optional unit and sparkline history (any length, oldest first) or meter fill (0-100).</summary>
    public void Set(string value, string? unit = null, IReadOnlyList<double>? history = null, double? meter = null)
    {
        var changed = Value.Text != value && Value.Text.Length > 0 && IsLoaded;
        Value.Text = value;
        // A live reading changes every second or two. Lighting up on every change would keep the render thread busy all the time
        // (0.4% CPU / 2% GPU on an idle Home page), so it lights up at most once every 15 s, at 30 frames a second.
        var now = Environment.TickCount64;
        if (changed && Fx.Enabled && now - _lastFlash > 15000)
        {
            _lastFlash = now;
            Value.BeginAnimation(OpacityProperty, new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop }.Slow()); // a new reading lights up
        }
        if (unit is not null)
        {
            Unit.Text = unit;
            // Latin units (°C, Mb/s) stay one left-to-right run; Persian units follow the page
            Unit.FlowDirection = unit.Any(c => c is >= '؀' and <= 'ۿ') ? Ui.Flow : FlowDirection.LeftToRight;
        }
        if (history is not null) Spark?.Set(history);
        if (meter is { } m && Meter is not null)
        {
            var from = Meter.Value;
            Meter.Value = m;
            // the bar glides to a clearly different value, but not on every small wobble of a live reading (same idle-cost reason)
            if (Fx.Enabled && IsLoaded && Math.Abs(from - m) >= 3 && now - _lastMeter > 4000)
            {
                _lastMeter = now;
                Meter.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new DoubleAnimation(from, m, TimeSpan.FromMilliseconds(600)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop }.Slow());
            }
        }
    }
}

/// <summary>Quick-action tile: tinted icon + bold title + small muted subtitle. A clickable lifting card.</summary>
public sealed class QuickCard : GlassCard
{
    public TextBlock Title { get; } = new() { FontSize = 13.5, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    public TextBlock Sub { get; } = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
    public IconBadge Badge { get; }

    public QuickCard(string icon, string hue, string title, string sub, Action? onClick = null)
    {
        Lift = true;
        Padding = new Thickness(16);
        Title.Text = title;
        Title.Foreground = K.Brush("Text");
        Sub.Text = sub;
        Sub.Foreground = K.Brush("Muted");
        Badge = new IconBadge(icon, hue);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        texts.Children.Add(Title);
        texts.Children.Add(Sub);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(texts, 1);
        row.Children.Add(Badge);
        row.Children.Add(texts);
        Child = row;
        if (onClick is not null) OnClick(onClick);
        MouseEnter += (_, _) => Badge.Pose(true);
        MouseLeave += (_, _) => Badge.Pose(false);
    }
}

/// <summary>Soft status pill: "all healthy" badge, "active" badge. Tint: Green, Gold, Red, Accent.</summary>
public sealed class StatusBadge : Border
{
    public TextBlock Label { get; } = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };

    public StatusBadge(string text, string tint = "Green", string? icon = "chk")
    {
        Height = 32;
        CornerRadius = new CornerRadius(16);
        Padding = new Thickness(14, 0, 14, 0);
        VerticalAlignment = VerticalAlignment.Bottom;
        Child = _row;
        Set(text, tint, icon);
    }

    public void Set(string text, string tint = "Green", string? icon = "chk")
    {
        Background = K.Brush(tint + "Soft");
        Label.Text = text;
        Label.Foreground = K.Brush(tint);
        _row.Children.Clear();
        if (icon is not null) _row.Children.Add(new Icon(icon, 16, K.Brush(tint), 2) { Margin = new Thickness(0, 0, 8, 0) });
        _row.Children.Add(Label);
    }
}

/// <summary>Keyboard key cap for hotkeys (design: kbd). Digits/Latin stay left-to-right.</summary>
public sealed class KeyCap : Border
{
    public KeyCap(string key)
    {
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(9, 3, 9, 3);
        Margin = new Thickness(0, 0, 6, 0);
        Background = K.Brush("Card2Fill");
        BorderBrush = K.Brush("Line");
        BorderThickness = new Thickness(1, 1, 1, 2);
        FlowDirection = FlowDirection.LeftToRight;
        Child = new TextBlock { Text = key, FontSize = 12, FontWeight = FontWeights.SemiBold };
    }

    /// <summary>"Ctrl+Shift+O" as a row of key caps.</summary>
    public static StackPanel Row(string combo)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) p.Children.Add(new KeyCap(part));
        return p;
    }
}

/// <summary>Round colour swatch picker (Settings accent): the chosen one gets the double ring.</summary>
public sealed class Swatches : StackPanel
{
    public int Index { get; private set; }
    public event Action<int>? Changed;
    private readonly List<Border> _rings = [];

    public Swatches(IReadOnlyList<Color> colors, int selected = 0)
    {
        Orientation = Orientation.Horizontal;
        Index = selected;
        for (var i = 0; i < colors.Count; i++)
        {
            var idx = i;
            var brush = new SolidColorBrush(colors[i]);
            brush.Freeze();
            var ring = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), BorderBrush = brush, BorderThickness = new Thickness(2), Opacity = 0 };
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = brush };
            var host = new Grid { Width = 36, Height = 36, Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 4, 0) };
            host.Children.Add(ring);
            host.Children.Add(dot);
            Fx.Attach(host, grow: 1.15, press: 0.85);
            host.MouseLeftButtonUp += (_, _) => { Index = idx; Paint(); Changed?.Invoke(idx); };
            _rings.Add(ring);
            Children.Add(host);
        }
        Paint();
    }

    private void Paint()
    {
        for (var i = 0; i < _rings.Count; i++) Fx.Anim(_rings[i], OpacityProperty, i == Index ? 1 : 0, null, 250);
    }
}
