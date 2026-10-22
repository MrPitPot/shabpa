using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shabpa.Core;
using WF = System.Windows.Forms;

namespace Shabpa;

/// <summary>Draws one overlay layout from a list of metrics. Used by the real overlay window and by the
/// preview in Settings, so what the user sees while editing is what lands over the game.</summary>
public static class OverlayRenderer
{
    private static readonly string[] LevelColors = ["4ADE80", "FBBF24", "F87171"];

    private sealed record Skin(Color Accent, Color Accent2, Color Text, Color Muted, Color Back);

    private static Color Hex(string hex, Color fallback = default)
    {
        if (!OverlayLook.IsHex(hex)) return fallback;
        return Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    private static Skin Resolve(OverlaySettings o)
    {
        var look = OverlayLook.Find(o.Look);
        Color accent, accent2, text, back;
        switch (look.Key)
        {
            case "theme":
                var p = Ui.BaseTheme;
                (accent, accent2, text, back) = (Rgb(p, "Accent"), Rgb(p, "Accent2"), Rgb(p, "Text"), Rgb(p, "Panel"));
                break;
            case "custom":
                accent = Hex(o.CustomAccent, Color.FromRgb(62, 230, 160));
                accent2 = accent;
                text = Hex(o.CustomText, Color.FromRgb(240, 240, 240));
                back = Hex(o.CustomBack, Color.FromRgb(10, 14, 24));
                break;
            default:
                accent = Hex(look.Accent);
                accent2 = Hex(look.Accent2);
                text = Hex(look.Text);
                back = Hex(look.Back);
                break;
        }
        var muted = Color.FromArgb(0xB0, text.R, text.G, text.B);
        return new Skin(accent, accent2, text, muted, back);
    }

    private static Color Rgb(Palette p, string key)
    {
        var (r, g, b) = p.Rgb(key);
        return Color.FromRgb(r, g, b);
    }

    private static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    /// <summary>Good / warning / bad value colours; on a light plate (white theme) darker tones keep the contrast readable.</summary>
    private static readonly string[] LevelColorsOnLight = ["15803D", "B45309", "B91C1C"];

    private static Color LevelColor(int level, Color back)
    {
        var luminance = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) / 255;
        return Hex((luminance > 0.62 ? LevelColorsOnLight : LevelColors)[Math.Clamp(level, 0, 2)]);
    }

    private static Color ValueColor(OverlaySettings o, Skin c, MetricView m) =>
        o.ColorByValue && m.HasValue && m.Level > 0 ? LevelColor(m.Level, c.Back)
        : o.ColorByValue && m.HasValue && m.Key is "fps" or "low1" && m.Level == 0 && m.Fraction is > 0 ? LevelColor(0, c.Back)
        : c.Accent;

    /// <summary>Builds the visual for the given settings. <paramref name="history"/> feeds the graph layout.</summary>
    public static FrameworkElement Build(OverlaySettings o, IReadOnlyList<MetricView> metrics, IReadOnlyList<TelemetrySample>? history = null)
    {
        var c = Resolve(o);
        var font = (FontFamily)Application.Current.Resources["Num"];
        var flat = metrics.Where(m => m.Key != "fpsgraph").ToList();
        FrameworkElement body = o.Layout switch
        {
            "bar" => Bar(o, c, font, flat),
            "compact" => Compact(o, c, font, flat),
            "minimal" => Minimal(o, c, font, flat),
            "rings" => Rings(o, c, font, flat),
            "graph" => Graph(o, c, font, flat, history),
            _ => Card(o, c, font, metrics, history),
        };

        var frame = new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(o.Rounded ? 14 : 3),
            Padding = new Thickness(o.Layout == "minimal" ? 4 : 12, o.Layout == "minimal" ? 2 : 10, o.Layout == "minimal" ? 4 : 12, o.Layout == "minimal" ? 2 : 10),
            FlowDirection = FlowDirection.LeftToRight,
            UseLayoutRounding = true,
        };
        if (o.Layout != "minimal")
        {
            frame.Background = Brush(Color.FromArgb((byte)Math.Clamp(o.BackOpacity * 255, 0, 255), c.Back.R, c.Back.G, c.Back.B));
            frame.BorderBrush = Brush(Color.FromArgb(0x55, c.Accent.R, c.Accent.G, c.Accent.B));
            frame.BorderThickness = new Thickness(1);
        }
        frame.LayoutTransform = new ScaleTransform(Math.Clamp(o.Scale, 0.5, 3), Math.Clamp(o.Scale, 0.5, 3));
        return frame;
    }

    private static TextBlock Text(string text, double size, Color color, FontFamily font, FontWeight? weight = null, bool shadow = false)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = size, Foreground = Brush(color), FontFamily = font, FontWeight = weight ?? FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight,
        };
        if (shadow)
            block.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };
        return block;
    }

    /// <summary>The number at the full size and its unit (%, °C, MHz...) a little smaller next to it, as MSI Afterburner does.
    /// It sits in a slot of fixed width (right-aligned), so a changing value never changes the size of the overlay.</summary>
    private static TextBlock Reading(MetricView m, double size, Color color, FontFamily font, bool shadow)
    {
        var block = Text(m.HasValue ? m.Value : "—", size, color, font, FontWeights.Bold, shadow);
        if (m.HasValue)
        {
            var unit = UnitText(m);
            if (unit.Length > 0) block.Inlines.Add(new System.Windows.Documents.Run(unit) { FontSize = Math.Round(size * UnitScale, 1), FontWeight = FontWeights.SemiBold });
        }
        block.Width = SlotWidth(m, size, font);
        block.TextAlignment = TextAlignment.Right;
        block.TextTrimming = TextTrimming.CharacterEllipsis;
        block.TextWrapping = TextWrapping.NoWrap;
        return block;
    }

    private static readonly Dictionary<(string key, string unit, double size), double> SlotCache = [];

    /// <summary>The width of the widest text this reading can show: its digit pattern in the widest digit, plus its widest unit,
    /// and never less than the words that stand in for a missing value.</summary>
    internal static double SlotWidth(MetricView m, double size, FontFamily font)
    {
        var unitRaw = OverlaySlots.UnitPattern(m.Key, OverlayMetrics.Find(m.Key)?.Unit ?? m.Unit);
        if (SlotCache.TryGetValue((m.Key, unitRaw, size), out var known)) return known;
        var digit = WidestDigit(font, FontWeights.Bold, size);
        var number = OverlaySlots.NumberPattern(m.Key).Replace(OverlaySlots.Digit, digit);
        var unit = UnitText(unitRaw);
        var width = Measure(number, font, FontWeights.Bold, size) + (unit.Length > 0 ? Measure(unit, font, FontWeights.SemiBold, Math.Round(size * UnitScale, 1)) : 0);
        width = Math.Max(width, Math.Max(Measure("ADMIN", font, FontWeights.Bold, size), Measure("—", font, FontWeights.Bold, size)));
        return SlotCache[(m.Key, unitRaw, size)] = Math.Ceiling(width) + 1;
    }

    private static char WidestDigit(FontFamily font, FontWeight weight, double size) =>
        "0123456789".MaxBy(d => Measure(d.ToString(), font, weight, size));

    internal const double UnitScale = 0.72;

    /// <summary>What follows the number: "%" and "°C" stick to it, other units are set apart by a space.</summary>
    internal static string UnitText(MetricView m) => UnitText(m.Unit);

    internal static string UnitText(string unit) => unit.Length == 0 ? "" : unit is "%" or "°C" ? unit : " " + unit;

    private static string Display(MetricView m) => m.Unit.Length > 0 && m.HasValue && !m.Unit.StartsWith('/') ? $"{m.Value}{(m.Unit is "%" or "°C" ? m.Unit : " " + m.Unit)}" : m.HasValue ? $"{m.Value} {m.Unit}".Trim() : "—";

    // ---- card: label on one side, value on the other -------------------------------------------

    private static FrameworkElement Card(OverlaySettings o, Skin c, FontFamily font, IReadOnlyList<MetricView> metrics, IReadOnlyList<TelemetrySample>? history = null)
    {
        var graphRow = metrics.Any(m => m.Key == "fpsgraph");
        metrics = [.. metrics.Where(m => m.Key != "fpsgraph")];
        var grid = new Grid { MinWidth = 150 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 14 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < metrics.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var m = metrics[i];
            var big = i == 0 && m.Key == "fps";
            if (o.ShowLabels)
            {
                var label = Text(m.Label, 13, c.Muted, font, FontWeights.Medium, o.Shadow);
                label.Margin = new Thickness(0, 1.5, 0, 1.5);
                Grid.SetRow(label, i);
                grid.Children.Add(label);
            }
            var value = Reading(m, big ? 25 : 16, ValueColor(o, c, m), font, o.Shadow);
            value.Margin = new Thickness(0, 1.5, 0, 1.5);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);
        }
        if (!graphRow) return grid;

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var spark = SparkLine(c, history, 150, 34);
        spark.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(spark, metrics.Count);
        Grid.SetColumnSpan(spark, 3);
        grid.Children.Add(spark);
        return grid;
    }

    /// <summary>The last minute of frame rate as a small line.</summary>
    private static FrameworkElement SparkLine(Skin c, IReadOnlyList<TelemetrySample>? history, double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        canvas.Children.Add(new Rectangle { Width = width, Height = height, Fill = Brush(Color.FromArgb(0x28, c.Text.R, c.Text.G, c.Text.B)), RadiusX = 5, RadiusY = 5 });
        var values = (history ?? []).Select(s => s.Fps).TakeLast(60).ToList();
        if (values.Count >= 2)
        {
            var top = Math.Max(30, values.Max() * 1.15);
            var points = new PointCollection();
            for (var i = 0; i < values.Count; i++)
                points.Add(new Point(width * i / (values.Count - 1), height - 2 - Math.Clamp(values[i] / top, 0, 1) * (height - 4)));
            canvas.Children.Add(new Polyline { Points = points, Stroke = Brush(c.Accent), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
        }
        return canvas;
    }

    // ---- bar: one horizontal strip --------------------------------------------------------------

    private static FrameworkElement Bar(OverlaySettings o, Skin c, FontFamily font, IReadOnlyList<MetricView> metrics)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < metrics.Count; i++)
        {
            var m = metrics[i];
            if (i > 0)
                row.Children.Add(new Border { Width = 1, Height = 16, Background = Brush(Color.FromArgb(0x55, c.Text.R, c.Text.G, c.Text.B)), Margin = new Thickness(10, 0, 10, 0) });
            var cell = new StackPanel { Orientation = Orientation.Horizontal };
            if (o.ShowLabels) cell.Children.Add(Text(Short(m), 12.5, c.Muted, font, FontWeights.Medium, o.Shadow));
            var value = Reading(m, 16, ValueColor(o, c, m), font, o.Shadow);
            value.Margin = new Thickness(o.ShowLabels ? 6 : 0, 0, 0, 0);
            cell.Children.Add(value);
            row.Children.Add(cell);
        }
        return row;
    }

    private static string Short(MetricView m) => m.Key switch
    {
        "fps" => "FPS", "low1" => "1%", "frame" => "FT", "gpu" => "GPU", "gputemp" => "GPU°", "hotspot" => "HOT", "memtemp" => "MEM°",
        "gpuclock" => "CLK", "memclock" => "VCLK", "gpupower" => "PWR", "gpufan" => "FAN", "vram" => "VRAM", "cpu" => "CPU", "cputemp" => "CPU",
        "cpuwatts" => "CPU W", "ram" => "RAM", "ping" => "PING", "down" => "↓", "up" => "↑", "clock" => "", "gametime" => "⏱",
        "gpuvolt" => "mV", "gpufanrpm" => "FAN", "cpufreq" => "CLK", "cpufan" => "FAN%", "cpufanrpm" => "FAN", "ramused" => "RAM",
        "fpsgraph" => "",
        _ => m.Label,
    };

    // ---- compact: values only, two short rows --------------------------------------------------

    private static FrameworkElement Compact(OverlaySettings o, Skin c, FontFamily font, IReadOnlyList<MetricView> metrics)
    {
        var panel = new WrapPanel { MaxWidth = 300 };
        foreach (var m in metrics)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 12, 1) };
            if (o.ShowLabels) cell.Children.Add(Text(Short(m), 11.5, c.Muted, font, FontWeights.Medium, o.Shadow));
            var value = Reading(m, 15, ValueColor(o, c, m), font, o.Shadow);
            value.Margin = new Thickness(o.ShowLabels ? 4 : 0, 0, 0, 0);
            cell.Children.Add(value);
            panel.Children.Add(cell);
        }
        return panel;
    }

    // ---- minimal: no background, only text ------------------------------------------------------

    private static FrameworkElement Minimal(OverlaySettings o, Skin c, FontFamily font, IReadOnlyList<MetricView> metrics)
    {
        var panel = new StackPanel();
        foreach (var m in metrics)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            if (o.ShowLabels) line.Children.Add(new TextBlock
            {
                Text = Short(m) + " ", FontSize = 13, FontFamily = font, FontWeight = FontWeights.Medium, Foreground = Brush(c.Text), Opacity = 0.75,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 1 },
            });
            var reading = Reading(m, m.Key == "fps" ? 23 : 16, ValueColor(o, c, m), font, false);
            reading.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 1 };
            line.Children.Add(reading);
            panel.Children.Add(line);
        }
        return panel;
    }

    // ---- rings: load metrics as gauges ----------------------------------------------------------

    private static FrameworkElement Rings(OverlaySettings o, Skin c, FontFamily font, IReadOnlyList<MetricView> metrics)
    {
        // chosen by key, never by whether a value is there right now: a reading that drops out for a moment must not change the layout
        var rings = metrics.Where(m => m.Key is "gpu" or "cpu" or "ram" or "vram" or "gputemp" or "hotspot" or "fps" or "gpufan").ToList();
        var rest = metrics.Except(rings).ToList();
        var root = new StackPanel();
        // many gauges wrap into balanced rows (7 become 4 + 3) instead of one endless strip
        var perRow = rings.Count <= 4 ? Math.Max(1, rings.Count) : (int)Math.Ceiling(rings.Count / Math.Ceiling(rings.Count / 4.0));
        for (var start = 0; start < rings.Count; start += perRow)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, start == 0 ? 0 : 6, 0, 0) };
            foreach (var m in rings.Skip(start).Take(perRow)) row.Children.Add(RingCell(o, c, font, m));
            root.Children.Add(row);
        }
        if (rest.Count > 0) root.Children.Add(Compact(o, c, font, rest));
        return root;
    }

    // Gauge geometry (everything follows the size slider because the whole overlay is scaled as one piece):
    // the ring's stroke and radius come from the diameter, and the number is fitted into the free circle inside it.
    private const double RingSize = 72, RingStroke = 5.5, RingGap = 3.5;

    private static FrameworkElement RingCell(OverlaySettings o, Skin c, FontFamily font, MetricView m)
    {
        var r = (RingSize - RingStroke) / 2 - 0.5;          // centre line of the ring
        var free = r - RingStroke / 2 - RingGap;            // radius of the empty disc inside the ring
        var gauge = new Grid { Width = RingSize, Height = RingSize };
        var track = Brush(Color.FromArgb(0x40, c.Text.R, c.Text.G, c.Text.B));
        gauge.Children.Add(new Path { Stroke = track, StrokeThickness = RingStroke, Data = new EllipseGeometry(new Point(RingSize / 2, RingSize / 2), r, r) });
        var fraction = m.Fraction ?? 0;
        gauge.Children.Add(new Path
        {
            Stroke = Brush(ValueColor(o, c, m)), StrokeThickness = RingStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = fraction >= 0.995 ? new EllipseGeometry(new Point(RingSize / 2, RingSize / 2), r, r) : Arc(RingSize, r, fraction),
        });
        var value = m.HasValue ? m.Value : "—";
        var number = Text(value, FitInCircle(value, font, FontWeights.Bold, free), c.Text, font, FontWeights.Bold, o.Shadow);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.TextAlignment = TextAlignment.Center;
        gauge.Children.Add(number);

        var label = o.ShowLabels ? Short(m) : "";
        var labelSize = 11.5;
        var cellWidth = RingSize + 6;
        if (label.Length > 0)
        {
            var wide = Measure(label, font, FontWeights.Medium, labelSize);
            if (wide > cellWidth) labelSize = Math.Max(7.5, labelSize * cellWidth / wide);
        }
        var cell = new StackPanel { Width = cellWidth, Margin = new Thickness(2, 0, 2, 0) };
        cell.Children.Add(gauge);
        if (label.Length > 0)
        {
            var text = Text(label, labelSize, c.Muted, font, FontWeights.Medium, o.Shadow);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.Margin = new Thickness(0, 2, 0, 0);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            cell.Children.Add(text);
        }
        return cell;
    }

    private static double Measure(string text, FontFamily font, FontWeight weight, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font, FontStyles.Normal, weight, FontStretches.Normal),
            size, Brushes.Black, 1.0);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    /// <summary>The biggest font (at most 19) at which the text's box sits inside a circle of radius <paramref name="radius"/>:
    /// "9.2" gets large, "156" and "1.4k" shrink, a four-digit value shrinks further.</summary>
    private static double FitInCircle(string text, FontFamily font, FontWeight weight, double radius)
    {
        var unit = Measure(text, font, weight, 100) / 100;     // width per point of font size
        const double heightPerSize = 0.78;                      // digits are about this tall (no descenders in a gauge value)
        var best = 8.0;
        for (var size = 19.0; size >= 8; size -= 0.25)
        {
            var half = unit * size / 2;
            var tall = heightPerSize * size / 2;
            if (half * half + tall * tall <= radius * radius * 0.94) { best = size; break; }
        }
        return best;
    }

    private static Geometry Arc(double size, double r, double fraction)
    {
        var center = size / 2;
        fraction = Math.Clamp(fraction, 0.003, 0.995);
        var angle = fraction * 2 * Math.PI;
        var end = new Point(center + r * Math.Sin(angle), center - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(center, center - r) };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }

    // ---- graph: a frame-rate line over the last couple of minutes ---------------------------------

    private static FrameworkElement Graph(OverlaySettings o, Skin c, FontFamily font, IReadOnlyList<MetricView> metrics, IReadOnlyList<TelemetrySample>? history)
    {
        var root = new StackPanel { MinWidth = 190 };
        var fps = metrics.FirstOrDefault(m => m.Key == "fps");
        if (fps is not null)
            {
                var line = Text($"{Display(fps)} FPS".Replace(" FPS FPS", " FPS"), 25, ValueColor(o, c, fps), font, FontWeights.Bold, o.Shadow);
                line.Width = Math.Max(Measure(OverlaySlots.NumberPattern("fps").Replace(OverlaySlots.Digit, WidestDigit(font, FontWeights.Bold, 25)) + " FPS", font, FontWeights.Bold, 25), Measure("ADMIN FPS", font, FontWeights.Bold, 25)) + 2;
                line.TextTrimming = TextTrimming.CharacterEllipsis;
                root.Children.Add(line);
            }

        var samples = history ?? [];
        var values = samples.Select(s => s.Fps).TakeLast(60).ToList();
        var chart = new Canvas { Width = 190, Height = 54, Margin = new Thickness(0, 4, 0, 6), ClipToBounds = true };
        chart.Children.Add(new Rectangle { Width = 190, Height = 54, Fill = Brush(Color.FromArgb(0x30, c.Text.R, c.Text.G, c.Text.B)), RadiusX = 6, RadiusY = 6 });
        if (values.Count >= 2)
        {
            var top = Math.Max(30, values.Max() * 1.15);
            var points = new PointCollection();
            for (var i = 0; i < values.Count; i++)
                points.Add(new Point(190.0 * i / (values.Count - 1), 52 - Math.Clamp(values[i] / top, 0, 1) * 50));
            chart.Children.Add(new Polyline { Points = points, Stroke = Brush(c.Accent), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round });
        }
        root.Children.Add(chart);
        root.Children.Add(Compact(o, c, font, metrics.Where(m => m.Key != "fps").ToList()));
        return root;
    }
}

/// <summary>The transparent, click-through window that sits over the game. It never takes focus or input.</summary>
public sealed class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExLayered = 0x80000;
    /// <summary>The overlay is a picture of the screen: it is never mirrored, so left is left even on a right-to-left Windows.</summary>
    private const int WsExLayoutRtl = 0x400000, WsExNoInheritLayout = 0x100000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private const int HotToggle = 0xB011, HotMove = 0xB012;
    private const uint ModNoRepeat = 0x4000;

    private readonly Hub _hub;
    private readonly ContentControl _host = new();
    private readonly OverlaySizeLock _size = new();
    private IntPtr _handle;
    private bool _moving;
    private bool _userHidden;
    private bool _registered;
    private bool _demand;
    private string _hotkeys = "";
    private string _game = "";
    private readonly Dictionary<string, OverlayBlockWindow> _blocks = [];

    public OverlayWindow(Hub hub)
    {
        _hub = hub;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        OverlaySizeLock.Prepare(this);
        Content = _host;
        SnapsToDevicePixels = true;
        Title = "Shabpa overlay";

        hub.Telemetry.Updated += sample => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Render(sample));
        hub.Second += Evaluate;
        SizeChanged += (_, _) => Place();
        // DragMove runs until the button is released (the mouse-up is swallowed by it), so the spot is saved right after it returns
        MouseLeftButtonDown += (_, _) => { if (!_moving) return; OverlayWin.Drag(DragMove); SavePosition(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style = (style | WsExToolWindow | WsExNoActivate | WsExLayered | WsExTransparent | WsExNoInheritLayout) & ~WsExLayoutRtl;
        SetWindowLongPtr(_handle, GwlExStyle, (IntPtr)style);
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
        RegisterHotkeys();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;
        if (msg != WmHotkey) return IntPtr.Zero;
        if (wParam.ToInt32() == HotToggle)
        {
            _userHidden = !_userHidden;
            Evaluate();
            handled = true;
        }
        else if (wParam.ToInt32() == HotMove)
        {
            SetMoveMode(!_moving);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void RegisterHotkeys()
    {
        if (_handle == IntPtr.Zero) return;
        var o = _hub.Settings.Overlay;
        var wanted = o.ToggleKey + "|" + o.MoveKey;
        if (_registered && wanted == _hotkeys) return;
        if (_registered)
        {
            UnregisterHotKey(_handle, HotToggle);
            UnregisterHotKey(_handle, HotMove);
        }
        _hotkeys = wanted;
        _registered = true;
        var failed = new List<string>();
        void Register(int id, string text)
        {
            if (Hotkey.Parse(text) is not { } key) return;
            if (!RegisterHotKey(_handle, id, key.Modifiers | ModNoRepeat, key.VirtualKey)) failed.Add(key.ToString());
        }
        Register(HotToggle, o.ToggleKey);
        Register(HotMove, o.MoveKey);
        FailedHotkeys = [.. failed];
    }

    /// <summary>Shortcuts another program already owns, so Windows refused them.</summary>
    public IReadOnlyList<string> FailedHotkeys { get; private set; } = [];

    /// <summary>Show or hide according to the mode and what is running; also (re)applies settings.</summary>
    public void Evaluate()
    {
#if DEBUG || SHABPA_TESTHOOKS
        if (ProbeOnly) return;
#endif
        var o = _hub.Settings.Overlay;
        RegisterHotkeys();
        var want = !_userHidden && (o.Mode == OverlayMode.Always || (o.Mode == OverlayMode.InGame && _hub.Snapshot.GameRunning) || _moving);
        if (want && _fadingOut)
        {
            // wanted again while it was fading away: stay
            _fadingOut = false;
            SetOpacity(this, 1);
            foreach (var block in _blocks.Values) SetOpacity(block, 1);
            AcquireDemand(true);
        }
        if (want && !IsVisible)
        {
            Show();
            FadeIn(this);
            AcquireDemand(true);
            Render(_hub.Telemetry.Latest);
        }
        else if (!want && IsVisible)
        {
            if (_fadingOut) return;
            AcquireDemand(false);
            if (!Motion.Enabled) { HideNow(); return; }
            // the HUD fades away (160 ms), then it is hidden for real
            _fadingOut = true;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) =>
            {
                if (!_fadingOut) return;
                _fadingOut = false;
                SetOpacity(this, 1);
                HideNow();
            };
            BeginAnimation(OpacityProperty, fade);
            foreach (var block in _blocks.Values) block.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.HoldEnd });
        }
        else if (want)
        {
            Render(_hub.Telemetry.Latest);
        }
        // a game that goes borderless-fullscreen (or takes the top spot itself) can slip in above the overlay: take the top back
        if (IsVisible && !_fadingOut)
        {
            OverlayWin.KeepOnTop(_handle);
            foreach (var block in _blocks.Values) OverlayWin.KeepOnTop(new WindowInteropHelper(block).Handle);
        }
    }

    private bool _fadingOut;

    private void HideNow()
    {
        Hide();
        foreach (var block in _blocks.Values) block.Close();
        _blocks.Clear();
    }

    private static void SetOpacity(Window window, double value)
    {
        window.BeginAnimation(OpacityProperty, null);
        window.Opacity = value;
    }

    /// <summary>The HUD (and each separate block) fades in over 220 ms when it appears; nothing is left running afterwards.</summary>
    private static void FadeIn(Window window)
    {
        SetOpacity(window, 1);
        if (!Motion.Enabled) return;
        window.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
    }

    private void AcquireDemand(bool on)
    {
        if (on == _demand) return;
        _demand = on;
        if (on) _hub.Telemetry.Acquire();
        else _hub.Telemetry.Release();
    }

    private void Render(TelemetrySample sample)
    {
#if DEBUG || SHABPA_TESTHOOKS
        if (ProbeOnly) return;
#endif
        if (!IsVisible) return;
        _game = sample.InGame ? sample.Game : "";
        var o = _hub.Settings.Overlay.WithProfile(_game);
        var view = _hub.Demo ? OverlayMetrics.Demo(DateTime.Now) : sample;
        var metrics = OverlayMetrics.Build(view, o.Items);
        if (metrics.Count == 0) metrics = OverlayMetrics.Build(view, ["fps", "gpu", "cpu"]);
        // A reading that is empty only because Shabpa is not running as administrator says so, instead of a bare dash.
        if (!Hub.Elevated && !_hub.Demo)
            metrics = [.. metrics.Select(m => !m.HasValue && OverlayMetrics.NeedsAdmin(m.Key) ? m with { Value = "ADMIN", Unit = "" } : m)];
        var history = _hub.Telemetry.History();
        string GroupOf(MetricView m) => OverlayMetrics.Find(m.Key)?.Group ?? "general";
        metrics = [.. metrics.OrderBy(m => o.OrderOf(GroupOf(m)))]; // stable: readings keep their order inside a group
        var together = metrics.Where(m => o.PlaceOf(GroupOf(m)).Anchor == "main").ToList();
        _size.Apply(this, _host, together.Count > 0 ? OverlayRenderer.Build(o, together, history) : null, OverlaySlots.Signature(o, together.Select(m => m.Key), L.En));

        // Groups placed on their own each get a small window of their own.
        var apart = metrics.Except(together).GroupBy(GroupOf).ToList();
        foreach (var stale in _blocks.Keys.Where(g => apart.All(a => a.Key != g)).ToList())
        {
            _blocks[stale].Close();
            _blocks.Remove(stale);
        }
        var side = o.ForBlock();
        foreach (var group in apart)
        {
            if (!_blocks.TryGetValue(group.Key, out var block))
            {
                _blocks[group.Key] = block = new OverlayBlockWindow(group.Key, Place, SaveBlockPosition);
                block.Show();
                FadeIn(block);
                block.SetMoveMode(_moving);
            }
            block.Update(side, [.. group], history);
        }
        Place();
    }

    /// <summary>Puts the main block and every separate block at its anchor on the monitor the game is on. Blocks that
    /// share a corner are stacked one under the other instead of covering each other. Everything is done in physical
    /// pixels with the scale of the monitor it lands on, and no block is ever left partly outside its monitor.</summary>
    private void Place()
    {
        // while a block is being dragged the mouse owns its position: putting it back to the saved spot on every sensor tick made it jump
        if (!IsVisible || _handle == IntPtr.Zero || OverlayWin.Dragging) return;
        var o = _hub.Settings.Overlay.WithProfile(_game);
        var foreground = Native.GetForegroundWindow();
        var screen = foreground != IntPtr.Zero ? WF.Screen.FromHandle(foreground) : WF.Screen.PrimaryScreen ?? WF.Screen.AllScreens[0];
        var area = OverlayWin.Box(screen.Bounds);
        var scale = OverlayWin.ScaleOf(screen);
#if DEBUG || SHABPA_TESTHOOKS
        if (ProbeArea is { } fake) (area, scale) = (fake, ProbeScale);
#endif
        var gap = (int)Math.Round(8 * scale);
        var next = new Dictionary<string, int>();
        var placed = new List<PxBox>();

        void Put(Window window, string anchor, double x, double y, double offsetX, double offsetY)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            if (!OverlayWin.Rect(handle, out var now) || now.W <= 0 || now.H <= 0) return;
            var (width, height) = (now.W, now.H);
            var target = area;
            int left, top;
            if (anchor == "free")
            {
                // a saved spot: stay on the monitor it was saved on (the nearest one if that screen is gone)
                target = OverlayWin.Box(WF.Screen.FromRectangle(new System.Drawing.Rectangle((int)x, (int)y, width, height)).Bounds);
                (left, top) = ((int)Math.Round(x), (int)Math.Round(y));
            }
            else
            {
                var edge = next.TryGetValue(anchor, out var used) ? used : OverlayPlacement.SafeOffset(offsetY, area.H - height);
                (left, top) = OverlayPlacement.AtZone(anchor, width, height, area, (int)Math.Round(offsetX), edge);
                var bottom = anchor is "bl" or "br" or "bc";
                // never sit on top of a block that is already placed (for example the main block, which may have been
                // dragged to a free spot): slide away from the corner until the space is clear
                for (var guard = 0; guard < 12; guard++)
                {
                    var hit = placed.FirstOrDefault(r => left < r.Right && left + width > r.X && top < r.Bottom && top + height > r.Y);
                    if (hit.W <= 0) break;
                    top = bottom ? hit.Y - height - gap : hit.Bottom + gap;
                }
                next[anchor] = bottom ? area.Bottom - top + gap : top - area.Y + height + gap;
            }
            var spot = OverlayPlacement.Clamp(new PxBox(left, top, width, height), target);
            placed.Add(spot);
            if (spot.X != now.X || spot.Y != now.Y) OverlayWin.MoveTo(handle, spot.X, spot.Y);
        }

        Put(this, o.Anchor, o.X, o.Y, o.X * scale, o.Y * scale);
        foreach (var (group, block) in _blocks.OrderBy(b => o.OrderOf(b.Key)))
        {
            var place = o.PlaceOf(group);
            var flush = place.X < 0;
            Put(block, place.Anchor, place.X, place.Y, flush ? 0 : 20 * scale, flush ? 0 : 20 * scale);
        }
    }

    /// <summary>Called when a separate block was let go while moving: it snaps to a corner or an edge, then the spot is saved.</summary>
    private void SaveBlockPosition(string group, PxBox box)
    {
        var o = _hub.Settings.Overlay;
        var place = o.Places.FirstOrDefault(p => p.Group == group);
        if (place is null) o.Places.Add(place = new GroupPlace { Group = group });
        var (snapped, zone) = SnapToMonitor(box);
        if (zone is not null) (place.Anchor, place.X, place.Y) = (zone, -1, -1); // -1: flush against the corner
        else (place.Anchor, place.X, place.Y) = ("free", snapped.X, snapped.Y);
        _hub.Store.Save();
        _hub.OverlayChanged();
        Place();
    }

    /// <summary>Pulls a box that was just dropped to the corners / edges of the monitor it is on.</summary>
    private static (PxBox box, string? zone) SnapToMonitor(PxBox box)
    {
        var screen = WF.Screen.FromRectangle(new System.Drawing.Rectangle(box.X, box.Y, box.W, box.H));
        var scale = OverlayWin.ScaleOf(screen);
        var result = OverlayPlacement.Snap(box, OverlayWin.Box(screen.Bounds), (int)Math.Round(36 * scale), 0);
        return (result.Box, result.Zone);
    }

    public void SetMoveMode(bool on)
    {
        if (_moving == on || _handle == IntPtr.Zero) return;
        _moving = on;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style = on ? style & ~WsExTransparent : style | WsExTransparent;
        SetWindowLongPtr(_handle, GwlExStyle, (IntPtr)style);
        Cursor = on ? Cursors.SizeAll : Cursors.Arrow;
        foreach (var block in _blocks.Values) block.SetMoveMode(on);
        _hub.OverlayMoveChanged(on);
        Evaluate();
    }

    private void SavePosition()
    {
        if (!OverlayWin.Rect(_handle, out var box)) return;
        var o = _hub.Settings.Overlay;
        var (snapped, zone) = SnapToMonitor(box);
        void Store(string anchor, double x, double y)
        {
            if (o.ProfileFor(_game) is { } profile) { profile.Anchor = anchor; (profile.X, profile.Y) = (x, y); }
            else { o.Anchor = anchor; (o.X, o.Y) = (x, y); }
        }
        // a corner keeps the same distance from it at any size (offsets are in 96-dpi pixels); anywhere else is a free spot
        if (zone is not null) Store(zone, 0, 0);
        else Store("free", snapped.X, snapped.Y);
        _hub.Store.Save();
        _hub.OverlayChanged();
        Place();
    }

    public bool MoveMode => _moving;

#if DEBUG || SHABPA_TESTHOOKS
    /// <summary>Test aid: pretend the monitor is this off-screen rectangle (scale <see cref="ProbeScale"/>), so placement can be watched without a window on screen.</summary>
    internal bool ProbeOnly { get; set; }
    internal PxBox? ProbeArea { get; set; }
    internal double ProbeScale { get; set; } = 1;

    /// <summary>Test aid: draws the given readings exactly like a sensor tick would, and places the window.</summary>
    internal void ProbeRender(OverlaySettings o, IReadOnlyList<MetricView> metrics)
    {
        _size.Apply(this, _host, OverlayRenderer.Build(o, metrics, []), OverlaySlots.Signature(o, metrics.Select(m => m.Key), L.En));
        Place();
    }
#endif

    protected override void OnClosed(EventArgs e)
    {
        if (_registered && _handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, HotToggle);
            UnregisterHotKey(_handle, HotMove);
        }
        foreach (var block in _blocks.Values) block.Close();
        AcquireDemand(false);
        base.OnClosed(e);
    }
}

/// <summary>One group of readings (GPU, CPU, RAM, network, time...) that sits apart from the main overlay block.
/// Like the main block it never takes focus or input, except while the user is moving the overlay.</summary>
public sealed class OverlayBlockWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExLayered = 0x80000;
    /// <summary>The overlay is a picture of the screen: it is never mirrored, so left is left even on a right-to-left Windows.</summary>
    private const int WsExLayoutRtl = 0x400000, WsExNoInheritLayout = 0x100000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private readonly string _group;
    private readonly ContentControl _host = new();
    private readonly OverlaySizeLock _size = new();
    private IntPtr _handle;
    private bool _moving;

    public OverlayBlockWindow(string group, Action placed, Action<string, PxBox> moved)
    {
        _group = group;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        OverlaySizeLock.Prepare(this);
        Content = _host;
        SnapsToDevicePixels = true;
        Title = "Shabpa overlay block";
        SizeChanged += (_, _) => placed();
        MouseLeftButtonDown += (_, _) =>
        {
            if (!_moving) return;
            OverlayWin.Drag(DragMove);
            if (OverlayWin.Rect(new WindowInteropHelper(this).Handle, out var box)) moved(_group, box);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style = (style | (long)(uint)(WsExToolWindow | WsExNoActivate | WsExLayered | WsExNoInheritLayout | (_moving ? 0 : WsExTransparent))) & ~(long)WsExLayoutRtl;
        SetWindowLongPtr(_handle, GwlExStyle, (IntPtr)style);
    }

    public void Update(OverlaySettings settings, IReadOnlyList<MetricView> metrics, IReadOnlyList<TelemetrySample> history) =>
        _size.Apply(this, _host, OverlayRenderer.Build(settings, metrics, history), OverlaySlots.Signature(settings, metrics.Select(m => m.Key), L.En));

    public void SetMoveMode(bool on)
    {
        _moving = on;
        if (_handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style = on ? style & ~WsExTransparent : style | WsExTransparent;
        SetWindowLongPtr(_handle, GwlExStyle, (IntPtr)style);
        Cursor = on ? Cursors.SizeAll : Cursors.Arrow;
    }
}

/// <summary>Keeps an overlay window at one size. The size is measured once per <see cref="OverlaySlots.Signature"/> (layout, scale, labels,
/// the list of readings) and then fixed: a new reading only replaces the content, never the window. No SizeToContent runs per tick.</summary>
internal sealed class OverlaySizeLock
{
    private string? _signature;

    /// <summary>Before the first content arrives: manual sizing, a 1 px window, never mirrored.</summary>
    public static void Prepare(Window window)
    {
        window.FlowDirection = FlowDirection.LeftToRight;
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 1;
        window.Height = 1;
    }

    /// <summary>Shows <paramref name="content"/>; measures and fixes the window size only when <paramref name="signature"/> is new.
    /// Returns true when the size was (re)computed.</summary>
    public bool Apply(Window window, ContentControl host, FrameworkElement? content, string signature)
    {
        host.Content = content;
        if (signature == _signature) return false;
        _signature = signature;
        var (w, h) = (1.0, 1.0);
        if (content is not null)
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            (w, h) = (Math.Max(1, Math.Ceiling(content.DesiredSize.Width)), Math.Max(1, Math.Ceiling(content.DesiredSize.Height)));
        }
        window.Width = w;
        window.Height = h;
        return true;
    }
}

/// <summary>Win32 helpers of the overlay windows: pixel rectangles, per-monitor scale, moving without activating, staying topmost.</summary>
internal static class OverlayWin
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rc { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Pt { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rc rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Pt point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>True while the user holds an overlay window with the mouse (DragMove runs its own loop).</summary>
    public static bool Dragging { get; private set; }

    public static void Drag(Action dragMove)
    {
        Dragging = true;
        try { dragMove(); }
        finally { Dragging = false; }
    }

    private const uint NoSize = 0x1, NoMove = 0x2, NoZOrder = 0x4, NoActivate = 0x10;
    private static readonly IntPtr Topmost = new(-1);

    public static PxBox Box(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    public static bool Rect(IntPtr hwnd, out PxBox box)
    {
        box = default;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rc)) return false;
        box = new PxBox(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
        return true;
    }

    public static void MoveTo(IntPtr hwnd, int x, int y) => SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder | NoActivate);

    public static void KeepOnTop(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, Topmost, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
    }

    /// <summary>The monitor's scale (1 = 96 dpi, 1.5 = 150 %), so offsets and snap distances look the same on every screen.</summary>
    public static double ScaleOf(WF.Screen screen)
    {
        try
        {
            var b = screen.Bounds;
            var monitor = MonitorFromPoint(new Pt { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 }, 2);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0) return dpi / 96.0;
        }
        catch (Exception) { /* shcore missing: treat as 96 dpi */ }
        return 1;
    }
}
