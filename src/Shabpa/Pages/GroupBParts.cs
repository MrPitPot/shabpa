using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>A 12-column grid of page blocks like Home's: wide layouts give every block a row, column and span; below the
/// breakpoint all blocks stack full width in the order they were added. Blocks get the 8 px margin of the design's 16 px gap.</summary>
internal sealed class BlockGrid : Grid
{
    private readonly List<(FrameworkElement el, int row, int col, int span)> _blocks = [];
    private bool? _wide;

    /// <summary>Page content width (px) above which the wide placement is used.</summary>
    public double Breakpoint { get; set; } = 900;

    public BlockGrid()
    {
        Margin = new Thickness(-8, 0, -8, 0);
        for (var i = 0; i < 12; i++) ColumnDefinitions.Add(new ColumnDefinition());
        SizeChanged += (_, e) => Apply(e.NewSize.Width - 16 > Breakpoint);
    }

    public void Add(FrameworkElement block, int row, int col, int span)
    {
        block.Margin = new Thickness(8);
        _blocks.Add((block, row, col, span));
        Children.Add(block);
        while (RowDefinitions.Count < Math.Max(_blocks.Count, row + 1)) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _wide = null;
        Apply(true);
    }

    private void Apply(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        for (var i = 0; i < _blocks.Count; i++)
        {
            var (el, row, col, span) = _blocks[i];
            SetRow(el, wide ? row : i);
            SetColumn(el, wide ? col : 0);
            SetColumnSpan(el, wide ? span : 12);
        }
    }
}

/// <summary>Equal-width columns that fill row by row; the number of columns follows the available width. Children of one
/// row share the tallest height, so cards line up. Layout mirrors itself in right-to-left pages.</summary>
internal sealed class AutoColumns : Panel
{
    public double MinColumnWidth { get; set; } = 460;

    private int Count(double width) => Math.Max(1, (int)Math.Floor(width / MinColumnWidth));

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? MinColumnWidth : available.Width;
        var n = Count(width);
        var cw = width / n;
        var heights = new List<double>();
        var i = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cw, double.PositiveInfinity));
            var row = i / n;
            if (heights.Count <= row) heights.Add(0);
            heights[row] = Math.Max(heights[row], child.DesiredSize.Height);
            i++;
        }
        return new Size(width, heights.Sum());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var n = Count(finalSize.Width);
        var cw = finalSize.Width / n;
        var children = InternalChildren.Cast<UIElement>().ToList();
        double y = 0;
        for (var start = 0; start < children.Count; start += n)
        {
            var h = children.Skip(start).Take(n).Max(c => c.DesiredSize.Height);
            for (var k = 0; k < n && start + k < children.Count; k++)
                children[start + k].Arrange(new Rect(k * cw, y, cw, h));
            y += h;
        }
        return finalSize;
    }
}

/// <summary>The little game scene used behind the overlay preview and the zone editor: a dusk sky, a sun and two ridges.
/// It stands for the game, not for the app's chrome, so it keeps its own colours in every theme.</summary>
internal static class SceneArt
{
    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static readonly Brush Sky = Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(34, 22, 86), 0), new GradientStop(Color.FromRgb(104, 40, 120), 0.5), new GradientStop(Color.FromRgb(255, 138, 92), 1)],
        new Point(0.5, 0), new Point(0.5, 1)));
    private static readonly Brush Ridge = Frozen(new SolidColorBrush(Color.FromRgb(30, 17, 58)));
    private static readonly Brush Sun = Frozen(new RadialGradientBrush(Color.FromRgb(255, 226, 160), Color.FromRgb(255, 190, 120)));

    /// <summary>Zone outline and drop highlight on top of the scene (always light on dark art).</summary>
    public static readonly Brush ZoneLine = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)));
    public static readonly Brush ZoneHot = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)));
    public static readonly Brush ZoneHotFill = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)));
    public static readonly Brush ZoneShade = Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0, 0, 0)));
    public static readonly Brush ZoneText = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)));

    /// <summary>The scene as a grid clipped to rounded corners. Put the content on top of it.</summary>
    public static Grid Build(double radius)
    {
        var grid = new Grid { FlowDirection = FlowDirection.LeftToRight, IsHitTestVisible = false };
        var canvas = new Canvas { Width = 640, Height = 280 };
        canvas.Children.Add(new System.Windows.Shapes.Rectangle { Width = 640, Height = 280, Fill = Sky });
        var sun = new System.Windows.Shapes.Ellipse { Width = 74, Height = 74, Fill = Sun };
        Canvas.SetLeft(sun, 470);
        Canvas.SetTop(sun, 66);
        canvas.Children.Add(sun);
        canvas.Children.Add(new System.Windows.Shapes.Path { Fill = Ridge, Data = Geometry.Parse("M0,280 L0,214 L110,150 L225,226 L330,170 L450,252 L560,200 L640,236 L640,280 Z") });
        canvas.Children.Add(new System.Windows.Shapes.Path { Fill = Ridge, Opacity = 0.7, Data = Geometry.Parse("M0,280 L0,250 L140,206 L300,262 L430,226 L640,270 L640,280 Z") });
        grid.Children.Add(new Viewbox { Stretch = Stretch.UniformToFill, Child = canvas });
        grid.SizeChanged += (_, e) => grid.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
        return grid;
    }
}

/// <summary>Picks one of the six screen corners (top/bottom x left/centre/right) on a small picture of a screen.
/// The picture is never mirrored: "left" always means the left of the screen.</summary>
internal sealed class AnchorPicker : Border
{
    public static readonly string[] Keys = ["tl", "tc", "tr", "bl", "bc", "br"];

    private readonly Dictionary<string, Border> _cells = [];
    public string Value { get; private set; } = "tl";
    public event Action<string>? Picked;

    public AnchorPicker(IReadOnlyList<(string key, string name)> names, string selected)
    {
        Value = selected;
        HorizontalAlignment = HorizontalAlignment.Left;
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(8);
        Background = K.Brush("Line");
        FlowDirection = FlowDirection.LeftToRight;
        var grid = new Grid { Width = 210, Height = 112, FlowDirection = FlowDirection.LeftToRight };
        for (var c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        grid.RowDefinitions.Add(new RowDefinition());
        for (var i = 0; i < Keys.Length; i++)
        {
            var key = Keys[i];
            var cell = new Border
            {
                CornerRadius = new CornerRadius(8), Margin = new Thickness(3), Cursor = Cursors.Hand, ToolTip = names.First(n => n.key == key).name,
                BorderThickness = new Thickness(1), Background = K.Brush("Card2Fill"), BorderBrush = K.Brush("Line"),
            };
            cell.MouseLeftButtonUp += (_, _) => Pick(key, true);
            Fx.Attach(cell, grow: 1.05, press: 0.94);
            // the column is the zone's place on the SCREEN (left = 0), read from the zone itself, not from its position in a list
            Grid.SetColumn(cell, OverlayZone.Column(key));
            Grid.SetRow(cell, OverlayZone.Row(key));
            grid.Children.Add(cell);
            _cells[key] = cell;
        }
        var game = new TextBlock
        {
            Text = L.T("بازی", "Game"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = K.Brush("Faint"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        Grid.SetRow(game, 1);
        Grid.SetColumnSpan(game, 3);
        grid.Children.Add(game);
        Child = grid;
        Paint();
    }

    public void Pick(string key, bool raise)
    {
        if (!_cells.ContainsKey(key) || key == Value) return;
        Value = key;
        Paint();
        if (raise)
        {
            Motion.Pop(_cells[key]);
            Picked?.Invoke(key);
        }
    }

    private void Paint()
    {
        foreach (var (key, cell) in _cells)
        {
            var on = key == Value;
            cell.Background = K.Brush(on ? "AccentGradient" : "Card2Fill");
            cell.BorderBrush = K.Brush(on ? "Accent" : "Line");
        }
    }
}

/// <summary>Small layout helpers shared by the pages of this group.</summary>
internal static class GroupB
{
    /// <summary>A tinted hairline list: puts a divider between consecutive rows.</summary>
    public static Border Line() => new() { Height = 1, Background = K.Brush("Line"), Margin = new Thickness(0, 2, 0, 2) };

    /// <summary>A switch with its label as one row of a settings card (44 px, hairline above unless first).</summary>
    public static Grid SwitchRow(string label, CheckBox toggle, bool first = false, string? hint = null) => K.SettingRow(label, toggle, first, hint);

    /// <summary>Label above a control, for forms in cards.</summary>
    public static StackPanel Field(string label, UIElement control, double right = 12)
    {
        var cell = new StackPanel { Margin = new Thickness(0, 0, right, 6) };
        cell.Children.Add(new TextBlock { Text = label, Style = K.Style("Label") });
        if (control is FrameworkElement fe) fe.Margin = new Thickness(0, 4, 0, 0);
        cell.Children.Add(control);
        return cell;
    }

    /// <summary>Two-digit time box in the design's clock look.</summary>
    public static TextBox TimeBox(string text = "00") => new()
    {
        Style = K.Style("NumberInput"), Width = 62, Height = 46, FontSize = 20, Text = text, MaxLength = 2,
    };

    /// <summary>Hour : minute pair as a left-to-right run.</summary>
    public static StackPanel TimePair(TextBox hour, TextBox minute)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        row.Children.Add(hour);
        row.Children.Add(new TextBlock { Text = ":", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = K.Brush("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 2) });
        row.Children.Add(minute);
        return row;
    }

    /// <summary>Rise-in for something the person just added (a list row, a note): short, springy, removes itself afterwards.</summary>
    public static void Appear(FrameworkElement element, int index = 0) => Motion.Rise(element, index, 10, 0.45);

    /// <summary>Fades and shrinks an element away (160 ms), then runs <paramref name="done"/> (the real removal).</summary>
    public static void Vanish(FrameworkElement element, Action done)
    {
        if (!Motion.Enabled) { done(); return; }
        var scale = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var span = TimeSpan.FromMilliseconds(160);
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, span) { FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd };
        fade.Completed += (_, _) => done();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        var shrink = new System.Windows.Media.Animation.DoubleAnimation(1, 0.96, span);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    /// <summary>Icon followed by text, for buttons.</summary>
    public static StackPanel IconText(string icon, string text, double size = 16)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon(icon, size, null) { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
