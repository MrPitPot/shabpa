using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Helpers private to the Tasks / History / Usage / Updates group of the Glass Depth port.</summary>
internal static class GroupC
{
    private static readonly (string name, string path)[] ExtraIcons =
    [
        ("warn", "M12 3.5l9.5 16.5h-19zM12 10v4.5M12 17.4h.01"),
        ("pause", "M8 5v14M16 5v14"),
        ("skip", "M5 5l10 7-10 7zM19 5v14"),
        ("stop", "M7 7h10v10H7z"),
        ("ul", "M12 20V6m0 0l-4 4m4-4l4 4M5 4h14"),
        ("ext", "M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"),
    ];

    private static bool _icons;

    /// <summary>Registers the few extra 24x24 stroke icons this group needs, unless the shared set already has them.</summary>
    public static void EnsureIcons()
    {
        if (_icons || Application.Current is null) return;
        _icons = true;
        foreach (var (name, path) in ExtraIcons)
        {
            if (Application.Current.Resources.Contains("I." + name)) continue;
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            Application.Current.Resources["I." + name] = geometry;
        }
    }
}

/// <summary>Cheap motion for the Tasks / History / Usage / Updates group: staggered entrances, list items that spring in and out,
/// numbers that count, bars that grow from their base. Everything is transform/opacity (or one attached number), runs once, leaves
/// nothing behind and is skipped when <see cref="Motion.Enabled"/> is off (Settings, low-memory mode, Windows' animation switch).</summary>
internal static class CMo
{
    // ---- entrances ------------------------------------------------------------------------------------

    /// <summary>The visible children of a page's top-level panel rise one after another (the design's staggered cards).</summary>
    public static void Cascade(Panel host, int first = 0)
    {
        if (!Motion.Enabled) return;
        var i = first;
        foreach (var child in host.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible))
            Motion.Rise(child, i++, 16, 0.62);
    }

    /// <summary>Runs <see cref="Cascade"/> every time the page is attached to a window (first visit and every tab switch).</summary>
    public static void CascadeOnLoad(FrameworkElement page, Panel host)
    {
        page.Loaded += (_, _) => Cascade(host);
    }

    /// <summary>A short rise for one item: opacity, 12 px up, scale .97 to 1 (the stagger index is capped).</summary>
    public static void Appear(FrameworkElement element, int index = 0, double dy = 12, double seconds = 0.5)
    {
        if (Motion.Enabled) Motion.Rise(element, Math.Min(index, 8), dy, seconds);
    }

    /// <summary>A note, status line or result that just changed: fades in and drifts up 6 px.</summary>
    public static void Flash(FrameworkElement element)
    {
        if (Motion.Enabled && element.IsLoaded) Motion.Rise(element, 0, 6, 0.4);
    }

    /// <summary>Sets <paramref name="block"/>'s text and flashes it when it really changed.</summary>
    public static void Say(TextBlock block, string text)
    {
        var changed = block.Text != text;
        block.Text = text;
        if (changed && text.Length > 0) Flash(block);
    }

    /// <summary>Remembers which list items were already shown, so a rebuilt list only animates the new ones. <see cref="Reset"/>
    /// makes everything new again (the list switched to another view).</summary>
    public sealed class Memo(Panel panel)
    {
        private HashSet<string> _seen = [];
        private HashSet<string> _next = [];
        private int _index;

        public void Reset() => _seen = [];

        public void Begin()
        {
            _next = [];
            _index = 0;
        }

        public T Item<T>(string key, T element) where T : FrameworkElement
        {
            _next.Add(key);
            if (!_seen.Contains(key) && panel.IsLoaded) Appear(element, _index++);
            return element;
        }

        public void End() => _seen = _next;
    }

    /// <summary>The exit of an item (fade, small shrink, drift towards the end side), then <paramref name="done"/> runs once.</summary>
    public static void Leave(FrameworkElement? element, Action done)
    {
        if (element is null || !Motion.Enabled || !element.IsLoaded)
        {
            done();
            return;
        }
        element.IsHitTestVisible = false;
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, move } };
        var span = TimeSpan.FromMilliseconds(190);
        var finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            done();
        }
        var fade = new DoubleAnimation(1, 0, span) { FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => Finish();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Ui.Flow == FlowDirection.RightToLeft ? -26 : 26, span) { EasingFunction = Fx.Out });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, span));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, span));
    }

    // ---- numbers --------------------------------------------------------------------------------------

    private static readonly DependencyProperty CountProperty = DependencyProperty.RegisterAttached("Count", typeof(double), typeof(CMo),
        new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is TextBlock block && block.GetValue(FormatProperty) is Func<double, string> format) block.Text = format((double)e.NewValue);
        }));

    private static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached("Format", typeof(Func<double, string>), typeof(CMo));

    /// <summary>Counts the number in <paramref name="block"/> from its last value (zero the first time) to <paramref name="to"/>.
    /// The final text is written first, so a page nobody sees still shows the right number.</summary>
    public static void Count(TextBlock block, double to, Func<double, string> format, int ms = 800)
    {
        block.SetValue(FormatProperty, format);
        var from = (double)block.GetValue(CountProperty);
        block.BeginAnimation(CountProperty, null);
        block.SetValue(CountProperty, to);
        block.Text = format(to);
        if (!Motion.Enabled || Math.Abs(from - to) < 0.5) return;
        block.BeginAnimation(CountProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Puts a counter back to zero so the next <see cref="Count"/> counts up from nothing (page entrance).</summary>
    public static void ResetCount(TextBlock block)
    {
        block.BeginAnimation(CountProperty, null);
        block.SetValue(CountProperty, 0.0);
    }

    /// <summary>Counts a whole number (Persian digits in Persian mode).</summary>
    public static void CountInt(TextBlock block, int to, int ms = 800) =>
        Count(block, to, v => Fa.Digits(((int)Math.Round(v)).ToString()), ms);

    private static readonly DependencyProperty BytesProperty = DependencyProperty.RegisterAttached("Bytes", typeof(double), typeof(CMo),
        new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is StatCard card) ShowBytes(card, (long)Math.Round((double)e.NewValue), null);
        }));

    /// <summary>"1.3 GB" becomes the big number and its small unit.</summary>
    private static void ShowBytes(StatCard card, long bytes, IReadOnlyList<double>? history)
    {
        var text = Fa.Bytes(bytes);
        var space = text.LastIndexOf(' ');
        card.Set(space < 0 ? text : text[..space], space < 0 ? "" : text[(space + 1)..], history);
    }

    /// <summary>Shows a size on a stat card. With <paramref name="animate"/> it counts from the last value (the unit changes along
    /// the way, like a real counter); without, it just sets the numbers (a live refresh must not animate).</summary>
    public static void CountBytes(StatCard card, long bytes, IReadOnlyList<double> history, bool animate = true)
    {
        var from = (double)card.GetValue(BytesProperty);
        card.BeginAnimation(BytesProperty, null);
        card.SetValue(BytesProperty, (double)bytes);
        ShowBytes(card, bytes, history);
        if (!animate || !Motion.Enabled || Math.Abs(from - bytes) < 1) return;
        card.BeginAnimation(BytesProperty, new DoubleAnimation(from, bytes, TimeSpan.FromMilliseconds(800)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Puts the size counter back to zero so the next <see cref="CountBytes"/> counts up from nothing (page entrance).</summary>
    public static void ResetBytes(StatCard card)
    {
        card.BeginAnimation(BytesProperty, null);
        card.SetValue(BytesProperty, 0.0);
    }

    /// <summary>Counts a whole number on a stat card (value, unit and an optional sparkline history stay as <see cref="StatCard.Set"/>).</summary>
    public static void CountCard(StatCard card, int value, string unit)
    {
        card.Set(Fa.Digits(value.ToString()), unit);
        CountInt(card.Value, value);
    }

    // ---- bars -----------------------------------------------------------------------------------------

    /// <summary>A bar that grows from its base: scaleY 0 to 1 (<paramref name="vertical"/>) or scaleX from its start edge.
    /// The final size is already laid out; only the transform plays, then it is removed.</summary>
    public static void Grow(FrameworkElement bar, int index, bool vertical = true, double seconds = 0.7, int delayMs = 0)
    {
        if (!Motion.Enabled) return;
        var scale = vertical ? new ScaleTransform(1, 0) : new ScaleTransform(0, 1);
        bar.RenderTransformOrigin = vertical ? new Point(0.5, 1) : new Point(0, 0.5);
        bar.RenderTransform = scale;
        var begin = TimeSpan.FromMilliseconds(delayMs + Math.Min(index, 14) * 38);
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(seconds)) { BeginTime = begin, EasingFunction = Fx.Out, FillBehavior = FillBehavior.HoldEnd };
        anim.Completed += (_, _) =>
        {
            scale.BeginAnimation(vertical ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty, null);
            if (ReferenceEquals(bar.RenderTransform, scale)) bar.RenderTransform = Transform.Identity;
        };
        scale.BeginAnimation(vertical ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty, anim);
    }

    /// <summary>Sweeps a progress bar from <paramref name="from"/> to its (already set) value.</summary>
    public static void Fill(System.Windows.Controls.ProgressBar bar, double from, int ms = 650)
    {
        if (!Motion.Enabled || Math.Abs(from - bar.Value) < 0.0005) return;
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new DoubleAnimation(from, bar.Value, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop,
        });
    }

    // ---- icons ----------------------------------------------------------------------------------------

    /// <summary>A full turn (a refresh icon on click).</summary>
    public static void Spin(FrameworkElement icon)
    {
        if (!Motion.Enabled) return;
        var rotate = new RotateTransform(0);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = rotate;
        var turn = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(700)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop };
        turn.Completed += (_, _) =>
        {
            if (ReferenceEquals(icon.RenderTransform, rotate)) icon.RenderTransform = Transform.Identity;
        };
        rotate.BeginAnimation(RotateTransform.AngleProperty, turn);
    }

    /// <summary>Keeps an icon turning while something is working (a check, a search); <c>false</c> stops it. Nothing runs when idle.</summary>
    public static void Busy(FrameworkElement icon, bool on)
    {
        if (!on)
        {
            if (icon.RenderTransform is RotateTransform { IsFrozen: false } current)
            {
                current.BeginAnimation(RotateTransform.AngleProperty, null);
                icon.RenderTransform = Transform.Identity;
            }
            return;
        }
        if (!Motion.Enabled) return;
        var rotate = new RotateTransform(0);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    /// <summary>The icon of a button built by <c>ListUi.Btn</c> or by hand (first <see cref="Icon"/> inside its content row).</summary>
    public static Icon? IconOf(Button button) => button.Content is Panel p ? p.Children.OfType<Icon>().FirstOrDefault() : null;
}

/// <summary>One status line under a card's actions: a small tinted icon and the text. New text fades in; while something is
/// working the icon keeps turning. Hidden while empty.</summary>
internal sealed class StatusLine : StackPanel
{
    private readonly Icon _icon = new("chk", 16) { Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top };

    public TextBlock Text { get; } = new() { Style = K.Style("Label"), FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    public StatusLine()
    {
        Orientation = Orientation.Horizontal;
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 14, 0, 0);
        Children.Add(_icon);
        Children.Add(Text);
    }

    public void Set(string text, string tint = "Muted", string icon = "chk", bool busy = false)
    {
        var changed = Text.Text != text;
        Text.Text = text;
        Text.Foreground = K.Brush(tint);
        _icon.Glyph = icon;
        _icon.Foreground = K.Brush(tint);
        Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CMo.Busy(_icon, busy);
        if (changed && text.Length > 0) CMo.Flash(this);
    }
}

/// <summary>A 12-column grid of page blocks (same idea as the Home page): each block says where it sits on a wide page and
/// where on a narrow one (under ~900 px). Blocks get an 8 px margin and the grid compensates, so cards line up with the header.</summary>
internal sealed class Blocks : Grid
{
    public readonly record struct Spot(int Row, int Column, int Span);

    private readonly List<(FrameworkElement element, Spot wide, Spot? narrow)> _items = [];
    private bool? _wide;

    public double Breakpoint { get; init; } = 900;

    public Blocks()
    {
        Margin = new Thickness(-8, 0, -8, 0);
        for (var i = 0; i < 12; i++) ColumnDefinitions.Add(new ColumnDefinition());
        SizeChanged += (_, e) => Apply(e.NewSize.Width > Breakpoint);
    }

    public static Spot At(int row, int column, int span) => new(row, column, span);

    public T Add<T>(T element, Spot wide, Spot? narrow = null) where T : FrameworkElement
    {
        element.Margin = new Thickness(8);
        _items.Add((element, wide, narrow));
        Children.Add(element);
        _wide = null;
        Apply(_wide ?? ActualWidth is 0 or > 900);
        return element;
    }

    public void Reset()
    {
        _items.Clear();
        Children.Clear();
        RowDefinitions.Clear();
        _wide = null;
    }

    private void Apply(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        var rows = 0;
        var next = 0;
        foreach (var (element, w, n) in _items)
        {
            // narrow page without an explicit spot: one block per row, in order
            var spot = wide ? w : n ?? new Spot(next, 0, 12);
            if (!wide) next = Math.Max(next, spot.Row + 1);
            SetRow(element, spot.Row);
            SetColumn(element, spot.Column);
            SetColumnSpan(element, spot.Span);
            rows = Math.Max(rows, spot.Row + 1);
        }
        while (RowDefinitions.Count < rows) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (RowDefinitions.Count > rows) RowDefinitions.RemoveAt(RowDefinitions.Count - 1);
    }
}
