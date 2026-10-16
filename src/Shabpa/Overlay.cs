using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
                var p = Ui.Theme;
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

    private static Color LevelColor(int level) => Hex(LevelColors[Math.Clamp(level, 0, 2)]);

    private static Color ValueColor(OverlaySettings o, Skin c, MetricView m) =>
        o.ColorByValue && m.HasValue && m.Level > 0 ? LevelColor(m.Level)
        : o.ColorByValue && m.HasValue && m.Key is "fps" or "low1" && m.Level == 0 && m.Fraction is > 0 ? LevelColor(0)
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
                var label = Text(m.Label, 12, c.Muted, font, FontWeights.Medium, o.Shadow);
                label.Margin = new Thickness(0, 1.5, 0, 1.5);
                Grid.SetRow(label, i);
                grid.Children.Add(label);
            }
            var value = Text(Display(m), big ? 22 : 14, ValueColor(o, c, m), font, FontWeights.Bold, o.Shadow);
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
            if (o.ShowLabels) cell.Children.Add(Text(Short(m), 11.5, c.Muted, font, FontWeights.Medium, o.Shadow));
            var value = Text(Display(m), 14, ValueColor(o, c, m), font, FontWeights.Bold, o.Shadow);
            value.Margin = new Thickness(o.ShowLabels ? 6 : 0, 0, 0, 0);
            cell.Children.Add(value);
            row.Children.Add(cell);
        }
        return row;
    }

    private static string Short(MetricView m) => m.Key switch
    {
        "fps" => "FPS", "low1" => "1%", "frame" => "FT", "gpu" => "GPU", "gputemp" => "GPU°", "hotspot" => "HOT", "memtemp" => "MEM°",
        "gpuclock" => "CLK", "memclock" => "VCLK", "gpupower" => "PWR", "gpufan" => "FAN", "vram" => "VRAM", "cpu" => "CPU", "cputemp" => "SYS°",
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
            if (o.ShowLabels) cell.Children.Add(Text(Short(m), 10.5, c.Muted, font, FontWeights.Medium, o.Shadow));
            var value = Text(Display(m), 13, ValueColor(o, c, m), font, FontWeights.Bold, o.Shadow);
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
                Text = Short(m) + " ", FontSize = 12, FontFamily = font, FontWeight = FontWeights.Medium, Foreground = Brush(c.Text), Opacity = 0.75,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 1 },
            });
            line.Children.Add(new TextBlock
            {
                Text = Display(m), FontSize = m.Key == "fps" ? 20 : 14, FontFamily = font, FontWeight = FontWeights.Bold, Foreground = Brush(ValueColor(o, c, m)),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1.5, Opacity = 1 },
            });
            panel.Children.Add(line);
        }
        return panel;
    }

    // ---- rings: load metrics as gauges ----------------------------------------------------------

    private static FrameworkElement Rings(OverlaySettings o, Skin c, FontFamily font, IReadOnlyList<MetricView> metrics)
    {
        var rings = metrics.Where(m => m.Fraction is not null && m.Key is "gpu" or "cpu" or "ram" or "vram" or "gputemp" or "hotspot" or "fps" or "gpufan").ToList();
        var rest = metrics.Except(rings).ToList();
        var root = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var m in rings)
        {
            const double size = 62;
            var cell = new Grid { Width = size + 8, Height = size + 22, Margin = new Thickness(2, 0, 2, 0) };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size) });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var gauge = new Grid { Width = size, Height = size };
            gauge.Children.Add(new Ellipse { Stroke = Brush(Color.FromArgb(0x40, c.Text.R, c.Text.G, c.Text.B)), StrokeThickness = 5, Margin = new Thickness(3) });
            gauge.Children.Add(new Path
            {
                Stroke = Brush(ValueColor(o, c, m)), StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Data = Arc(size, m.Fraction ?? 0),
            });
            gauge.Children.Add(Text(m.HasValue ? m.Value : "—", 14, c.Text, font, FontWeights.Bold, o.Shadow));
            cell.Children.Add(gauge);
            if (o.ShowLabels)
            {
                var label = Text(Short(m), 10.5, c.Muted, font, FontWeights.Medium, o.Shadow);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(label, 1);
                cell.Children.Add(label);
            }
            row.Children.Add(cell);
        }
        if (rings.Count > 0) root.Children.Add(row);
        if (rest.Count > 0) root.Children.Add(Compact(o, c, font, rest));
        return root;
    }

    private static Geometry Arc(double size, double fraction)
    {
        var r = size / 2 - 5.5;
        var center = size / 2;
        fraction = Math.Clamp(fraction, 0.003, 0.999);
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
            root.Children.Add(Text($"{Display(fps)} FPS".Replace(" FPS FPS", " FPS"), 22, ValueColor(o, c, fps), font, FontWeights.Bold, o.Shadow));

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
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = _host;
        SnapsToDevicePixels = true;
        Title = "Shabpa overlay";

        hub.Telemetry.Updated += sample => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Render(sample));
        hub.Second += Evaluate;
        SizeChanged += (_, _) => Place();
        MouseLeftButtonDown += (_, _) => { if (_moving) DragMove(); };
        MouseLeftButtonUp += (_, _) => { if (_moving) SavePosition(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate | WsExLayered | WsExTransparent;
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
        var o = _hub.Settings.Overlay;
        RegisterHotkeys();
        var want = !_userHidden && (o.Mode == OverlayMode.Always || (o.Mode == OverlayMode.InGame && _hub.Snapshot.GameRunning) || _moving);
        if (want && !IsVisible)
        {
            Show();
            AcquireDemand(true);
            Render(_hub.Telemetry.Latest);
        }
        else if (!want && IsVisible)
        {
            Hide();
            foreach (var block in _blocks.Values) block.Close();
            _blocks.Clear();
            AcquireDemand(false);
        }
        else if (want)
        {
            Render(_hub.Telemetry.Latest);
        }
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
        _host.Content = together.Count > 0 ? OverlayRenderer.Build(o, together, history) : null;

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
                block.SetMoveMode(_moving);
            }
            block.Update(side, [.. group], history);
        }
        Place();
    }

    /// <summary>Puts the main block and every separate block at its anchor on the monitor the game is on. Blocks that
    /// share a corner are stacked one under the other instead of covering each other.</summary>
    private void Place()
    {
        if (!IsVisible || _handle == IntPtr.Zero) return;
        var o = _hub.Settings.Overlay.WithProfile(_game);
        var dpi = VisualTreeHelper.GetDpi(this);
        var area = WF.Screen.FromHandle(Native.GetForegroundWindow()).Bounds;
        var gap = 8 * dpi.DpiScaleY;
        var next = new Dictionary<string, double>();

        void Put(Window window, string anchor, double x, double y, double offsetX, double offsetY)
        {
            var width = window.ActualWidth * dpi.DpiScaleX;
            var height = window.ActualHeight * dpi.DpiScaleY;
            if (width <= 0 || height <= 0) return;
            double left, top;
            if (anchor == "free")
            {
                (left, top) = (x, y);
            }
            else
            {
                var edge = next.TryGetValue(anchor, out var used) ? used : offsetY;
                left = anchor switch
                {
                    "tr" or "br" => area.Right - width - offsetX,
                    "tc" or "bc" => area.Left + (area.Width - width) / 2,
                    _ => area.Left + offsetX,
                };
                top = anchor is "bl" or "br" or "bc" ? area.Bottom - height - edge : area.Top + edge;
                next[anchor] = edge + height + gap;
            }
            window.Left = left / dpi.DpiScaleX;
            window.Top = top / dpi.DpiScaleY;
        }

        Put(this, o.Anchor, o.X, o.Y, o.X * dpi.DpiScaleX, o.Y * dpi.DpiScaleY);
        foreach (var (group, block) in _blocks.OrderBy(b => o.OrderOf(b.Key)))
        {
            var place = o.PlaceOf(group);
            Put(block, place.Anchor, place.X, place.Y, 20 * dpi.DpiScaleX, 20 * dpi.DpiScaleY);
        }
    }

    private void SaveBlockPosition(string group, double x, double y)
    {
        var o = _hub.Settings.Overlay;
        var place = o.Places.FirstOrDefault(p => p.Group == group);
        if (place is null) o.Places.Add(place = new GroupPlace { Group = group });
        (place.Anchor, place.X, place.Y) = ("free", x, y);
        _hub.Store.Save();
        _hub.OverlayChanged();
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
        var dpi = VisualTreeHelper.GetDpi(this);
        var o = _hub.Settings.Overlay;
        var (x, y) = (Left * dpi.DpiScaleX, Top * dpi.DpiScaleY);
        if (o.ProfileFor(_game) is { } profile)
        {
            profile.Anchor = "free";
            (profile.X, profile.Y) = (x, y);
        }
        else
        {
            o.Anchor = "free";
            (o.X, o.Y) = (x, y);
        }
        _hub.Store.Save();
        _hub.OverlayChanged();
    }

    public bool MoveMode => _moving;

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private readonly string _group;
    private readonly ContentControl _host = new();
    private IntPtr _handle;
    private bool _moving;

    public OverlayBlockWindow(string group, Action placed, Action<string, double, double> moved)
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
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = _host;
        SnapsToDevicePixels = true;
        Title = "Shabpa overlay block";
        SizeChanged += (_, _) => placed();
        MouseLeftButtonDown += (_, _) => { if (_moving) DragMove(); };
        MouseLeftButtonUp += (_, _) =>
        {
            if (!_moving) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            moved(_group, Left * dpi.DpiScaleX, Top * dpi.DpiScaleY);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        style |= (long)(uint)(WsExToolWindow | WsExNoActivate | WsExLayered | (_moving ? 0 : WsExTransparent));
        SetWindowLongPtr(_handle, GwlExStyle, (IntPtr)style);
    }

    public void Update(OverlaySettings settings, IReadOnlyList<MetricView> metrics, IReadOnlyList<TelemetrySample> history) =>
        _host.Content = OverlayRenderer.Build(settings, metrics, history);

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
