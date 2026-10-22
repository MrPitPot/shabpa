using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>A colour square: pick the shade (saturation across, brightness down) of the current hue. Drag anywhere on it.</summary>
internal sealed class ShadeSquare : FrameworkElement
{
    public double Hue { get => _hue; set { _hue = value; InvalidateVisual(); } }
    public double Sat { get; private set; } = 1;
    public double Val { get; private set; } = 1;
    public event Action? Changed;
    private double _hue;

    public ShadeSquare() { Cursor = Cursors.Cross; Focusable = false; }

    public void Set(double s, double v) { (Sat, Val) = (Math.Clamp(s, 0, 1), Math.Clamp(v, 0, 1)); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        var (r, g, b) = ColorMath.FromHsv(_hue, 1, 1);
        var round = new RectangleGeometry(rect, 8, 8);
        dc.PushClip(round);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), null, rect);
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0)), null, rect);
        dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1)), null, rect);
        dc.Pop();
        var at = new Point(Sat * ActualWidth, (1 - Val) * ActualHeight);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), at, 8, 8);
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), at, 8, 8);
    }

    private void Pick(Point p)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        Set(p.X / ActualWidth, 1 - p.Y / ActualHeight);
        Changed?.Invoke();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { CaptureMouse(); Pick(e.GetPosition(this)); base.OnMouseLeftButtonDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) Pick(e.GetPosition(this)); base.OnMouseMove(e); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { ReleaseMouseCapture(); base.OnMouseLeftButtonUp(e); }
}

/// <summary>The rainbow strip that picks the hue.</summary>
internal sealed class HueStrip : FrameworkElement
{
    public double Hue { get; private set; }
    public event Action? Changed;

    public HueStrip() { Cursor = Cursors.Hand; Focusable = false; }

    public void Set(double hue) { Hue = Math.Clamp(hue, 0, 360); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i <= 6; i++)
        {
            var (r, g, b) = ColorMath.FromHsv(i * 60, 1, 1);
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), i / 6.0));
        }
        dc.DrawRoundedRectangle(brush, null, rect, ActualHeight / 2, ActualHeight / 2);
        var x = Math.Clamp(Hue / 360 * ActualWidth, ActualHeight / 2, ActualWidth - ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), new Point(x, ActualHeight / 2), ActualHeight / 2 + 1, ActualHeight / 2 + 1);
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), new Point(x, ActualHeight / 2), ActualHeight / 2 + 1, ActualHeight / 2 + 1);
    }

    private void Pick(Point p)
    {
        if (ActualWidth <= 0) return;
        Set(p.X / ActualWidth * 360);
        Changed?.Invoke();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { CaptureMouse(); Pick(e.GetPosition(this)); base.OnMouseLeftButtonDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) Pick(e.GetPosition(this)); base.OnMouseMove(e); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { ReleaseMouseCapture(); base.OnMouseLeftButtonUp(e); }
}

/// <summary>A full personal colour picker in a pop-up: click the swatch, drag in the shade square (saturation / lightness) and
/// on the rainbow strip (hue); the old and the new colour sit side by side and the overlay follows live. The hex code
/// is still there for people who have one, but nobody has to type it.</summary>
internal sealed class OverlayColorField : Border
{
    private readonly Border _dot = new() { Width = 26, Height = 26, CornerRadius = new CornerRadius(8) };
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, PopupAnimation = PopupAnimation.Fade };
    private readonly ShadeSquare _square = new() { Width = 236, Height = 150 };
    private readonly HueStrip _hue = new() { Width = 236, Height = 14, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Border _before = new() { Height = 30, CornerRadius = new CornerRadius(8, 0, 0, 8) };
    private readonly Border _after = new() { Height = 30, CornerRadius = new CornerRadius(0, 8, 8, 0) };
    private readonly TextBox _hex = new();
    private string _start = "FFFFFF";
    private string _current = "FFFFFF";
    private bool _sync;

    /// <summary>Raised on every drag step with the new "RRGGBB".</summary>
    public event Action<string>? Picked;

    public OverlayColorField()
    {
        Cursor = Cursors.Hand;
        Padding = new Thickness(6);
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1.5);
        BorderBrush = Ui.Live("Line");
        Background = Ui.Live("Panel3");
        Width = 52;
        Height = 40;
        Child = _dot;
        Fx.Attach(this, grow: 1.06, press: 0.95);

        _popup.PlacementTarget = this;
        _popup.Child = BuildPopup();
        MouseLeftButtonUp += (_, _) => Open();
        _square.Changed += FromPickers;
        _hue.Changed += FromPickers;
        _hex.TextChanged += (_, _) =>
        {
            if (_sync || !ColorMath.TryParse(_hex.Text, out var c)) return;
            Apply(ColorMath.ToHex(c.r, c.g, c.b), fromHex: true);
        };
    }

    private FrameworkElement BuildPopup()
    {
        var swatches = new Grid { Margin = new Thickness(0, 12, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        swatches.ColumnDefinitions.Add(new ColumnDefinition());
        swatches.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_after, 1);
        swatches.Children.Add(_before);
        swatches.Children.Add(_after);

        _hex.Style = (Style)Application.Current.Resources["Input"];
        _hex.FlowDirection = FlowDirection.LeftToRight;
        _hex.FontFamily = new FontFamily("Consolas");
        _hex.MaxLength = 7;
        _hex.Padding = new Thickness(8, 4, 8, 4);
        _hex.Margin = new Thickness(0, 10, 0, 0);

        var panel = new StackPanel { Width = 236, FlowDirection = FlowDirection.LeftToRight };
        panel.Children.Add(_square);
        panel.Children.Add(_hue);
        panel.Children.Add(swatches);
        panel.Children.Add(new TextBlock
        {
            Text = L.T("قبلی ‹ › جدید", "Before ‹ › Now"), Style = (Style)Application.Current.Resources["Label"], FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(_hex);
        return new Border
        {
            Child = panel, Padding = new Thickness(14), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), Margin = new Thickness(8),
            BorderBrush = Ui.Live("Line"), Background = Ui.Live("Panel3"),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black },
        };
    }

    /// <summary>Shows a colour without raising <see cref="Picked"/>.</summary>
    public void Set(string hex)
    {
        if (!ColorMath.TryParse(hex, out var c)) c = (255, 255, 255);
        _current = ColorMath.ToHex(c.r, c.g, c.b);
        Paint(updatePickers: true);
    }

    private void Open()
    {
        if (_popup.IsOpen) return;
        _start = _current;
        _before.Background = Frozen(_start);
        Paint(updatePickers: true);
        _popup.IsOpen = true;
    }

    private void FromPickers()
    {
        var (r, g, b) = ColorMath.FromHsv(_hue.Hue, _square.Sat, _square.Val);
        Apply(ColorMath.ToHex(r, g, b), fromHex: false);
    }

    private void Apply(string hex, bool fromHex)
    {
        _current = hex;
        Paint(updatePickers: fromHex);
        Picked?.Invoke(hex);
    }

    private static Brush Frozen(string hex)
    {
        ColorMath.TryParse(hex, out var c);
        var brush = new SolidColorBrush(Color.FromRgb(c.r, c.g, c.b));
        brush.Freeze();
        return brush;
    }

    private void Paint(bool updatePickers)
    {
        _dot.Background = Frozen(_current);
        _after.Background = Frozen(_current);
        _sync = true;
        if (updatePickers && ColorMath.TryParse(_current, out var c))
        {
            var (h, s, v) = ColorMath.ToHsv(c.r, c.g, c.b);
            // a grey has no hue of its own: keep the one on the strip so the square does not jump
            if (s > 0.001 && v > 0.001) { _hue.Set(h); _square.Hue = h; }
            _square.Set(s, v);
        }
        else _square.Hue = _hue.Hue;
        if (!_hex.IsKeyboardFocused) _hex.Text = "#" + _current;
        _sync = false;
    }
}
