using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Tour;

/// <summary>What the overlay needs from the main window: a layer to sit on, the open page, a way to find a target and to open a page.</summary>
public interface ITourHost
{
    Window Window { get; }
    Panel Surface { get; }
    string ActiveKey { get; }
    /// <summary>The element a target name (see <see cref="TourTargets"/>) stands for right now, or null while it is not on screen.</summary>
    FrameworkElement? Resolve(string target);
    void GoTo(string pageKey);
}

/// <summary>The dimmed layer with one lit "hole". The hole (x, y, width, height) and the ring pulse are plain properties so the
/// overlay can spring them; clicks pass through the hole only when the step asks the person to click there.</summary>
internal sealed class Spotlight : FrameworkElement
{
    private static DependencyProperty Reg(string name, double value) =>
        DependencyProperty.Register(name, typeof(double), typeof(Spotlight), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoleXProperty = Reg(nameof(HoleX), 0), HoleYProperty = Reg(nameof(HoleY), 0),
        HoleWProperty = Reg(nameof(HoleW), 0), HoleHProperty = Reg(nameof(HoleH), 0),
        PulseProperty = Reg(nameof(Pulse), 0), DimProperty = Reg(nameof(Dim), 0);

    public double HoleX { get => (double)GetValue(HoleXProperty); set => SetValue(HoleXProperty, value); }
    public double HoleY { get => (double)GetValue(HoleYProperty); set => SetValue(HoleYProperty, value); }
    public double HoleW { get => (double)GetValue(HoleWProperty); set => SetValue(HoleWProperty, value); }
    public double HoleH { get => (double)GetValue(HoleHProperty); set => SetValue(HoleHProperty, value); }
    /// <summary>0 to 1, repeating: the ring around the target grows and fades.</summary>
    public double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    /// <summary>0 to 1: how much of the dimming is shown (fades in and out).</summary>
    public double Dim { get => (double)GetValue(DimProperty); set => SetValue(DimProperty, value); }

    /// <summary>Clicks inside the hole reach the page below.</summary>
    public bool PassThrough { get; set; }
    public double Radius { get; set; } = 14;

    private bool HasHole => HoleW >= 2 && HoleH >= 2;

    protected override HitTestResult? HitTestCore(PointHitTestParameters hit)
    {
        if (PassThrough && HasHole && new Rect(HoleX, HoleY, HoleW, HoleH).Contains(hit.HitPoint)) return null;
        return new PointHitTestResult(this, hit.HitPoint);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        var light = Ui.Theme.Light;
        // a deep ink in both themes; slightly softer on the light one so it still feels airy
        var scrim = light ? Color.FromRgb(0x12, 0x18, 0x2A) : Color.FromRgb(0x03, 0x05, 0x0B);
        var dim = new SolidColorBrush(Color.FromArgb((byte)((light ? 0xA6 : 0xC2) * Math.Clamp(Dim, 0, 1)), scrim.R, scrim.G, scrim.B));
        if (!HasHole)
        {
            dc.DrawRectangle(dim, null, full);
            return;
        }
        var hole = new Rect(HoleX, HoleY, HoleW, HoleH);
        var r = Math.Min(Radius, Math.Min(hole.Width, hole.Height) / 2);
        dc.DrawGeometry(dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(hole, r, r)));

        var accent = K.ColorOf("Accent");
        var fade = Math.Clamp(Dim, 0, 1);
        Color A(double alpha) => Color.FromArgb((byte)(255 * Math.Clamp(alpha, 0, 1) * fade), accent.R, accent.G, accent.B);
        var glow = hole; glow.Inflate(3, 3);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(A(0.16)), 9), glow, r + 3, r + 3);   // soft halo
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(A(0.95)), 2), glow, r + 3, r + 3);   // crisp ring
        if (Pulse > 0)
        {
            var wave = hole;
            var grow = 4 + 18 * Pulse;
            wave.Inflate(grow, grow);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(A(0.55 * (1 - Pulse))), 2), wave, r + grow, r + grow);
        }
    }
}

/// <summary>The guided tour on screen: a dim layer over the whole window with one lit hole that springs from target to target
/// (a ring pulses around it), and a glass bubble beside it with the title, the text, the step counter and the buttons.
/// Esc skips, Enter or the arrow keys move. With animations off everything just appears. Drawn on the window's own layer, so
/// nothing is opened on the desktop.</summary>
public sealed class TourOverlay : Grid
{
    private readonly ITourHost _host;
    private readonly TourSession _session;
    private readonly Spotlight _spot = new() { IsHitTestVisible = true };
    private readonly GlassCard _bubble = new();
    private readonly TranslateTransform _move = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly StackPanel _content = new();
    private readonly TranslateTransform _contentMove = new();
    private readonly TextBlock _counter = new(), _title = new(), _text = new(), _hintText = new();
    private readonly Border _hint = new();
    private readonly Border _progressFill = new();
    private readonly Button _back, _next, _skip;
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private Rect? _lastHole;
    private Point _lastBubble;
    private bool _placed, _closing;
    private DateTime _holdUntil = DateTime.MinValue;
    private string _themeKey = "";
    private readonly Action<TourEnd> _onFinished;

    private static Style St(string key) => K.Style(key);

    /// <summary>The card colour laid over the window colour: the bubble must not let the dimmed page show through its text.</summary>
    public static Brush OpaqueCard()
    {
        var top = K.ColorOf("Panel3");
        var under = K.ColorOf("Bg");
        double a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        var brush = new SolidColorBrush(Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B)));
        brush.Freeze();
        return brush;
    }

    public TourSession Session => _session;
    public bool IsClosing => _closing;

    /// <summary>Test aid: where the bubble and the lit hole are right now (the overlay's own coordinates).</summary>
    public Rect Bubble => new(_lastBubble, _bubble.DesiredSize);
    public Rect? Hole => _lastHole;
    /// <summary>Test aid: the hole as drawn this instant (half-way through a spring while it moves).</summary>
    public Rect LiveHole => new(_spot.HoleX, _spot.HoleY, _spot.HoleW, _spot.HoleH);
    public FrameworkElement BubbleElement => _bubble;

    public TourOverlay(ITourHost host, TourSession session, Action<TourEnd> onFinished)
    {
        _host = host;
        _session = session;
        _onFinished = onFinished;
        FlowDirection = FlowDirection.LeftToRight; // a positioning layer: coordinates are the visible ones, the content below is flowed by the language
        Panel.SetZIndex(this, 950);
        Focusable = false;
        Children.Add(_spot);

        // ---- the bubble ------------------------------------------------------------------------------
        _bubble.HorizontalAlignment = HorizontalAlignment.Left;
        _bubble.VerticalAlignment = VerticalAlignment.Top;
        _bubble.CornerRadius = new CornerRadius(18);
        _bubble.Padding = new Thickness(22, 18, 22, 16);
        _bubble.Elevated = true;
        _bubble.Background = OpaqueCard();
        _bubble.MinWidth = 320;
        _bubble.MaxWidth = 390;
        _bubble.Opacity = 0;
        _bubble.RenderTransformOrigin = new Point(0.5, 0.5);
        _bubble.RenderTransform = new TransformGroup { Children = { _scale, _move } };
        Panel.SetZIndex(_bubble, 2);

        _content.FlowDirection = Ui.Flow;
        _content.RenderTransform = _contentMove;

        _counter.FontSize = 11.5;
        _counter.FontWeight = FontWeights.SemiBold;
        _counter.Foreground = K.Brush("Accent");
        _counter.FontFamily = (FontFamily)Application.Current.Resources["Num"];
        var counterPill = new Border
        {
            Background = K.Brush("AccentSoft"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left, Child = _counter,
        };

        _title.Style = St("H2");
        _title.TextWrapping = TextWrapping.Wrap;
        _title.Margin = new Thickness(0, 12, 0, 0);
        _text.Style = St("Body");
        _text.TextWrapping = TextWrapping.Wrap;
        _text.Margin = new Thickness(0, 6, 0, 0);
        _text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

        var hintRow = new StackPanel { Orientation = Orientation.Horizontal };
        hintRow.Children.Add(new Icon { Glyph = "play", Width = 14, Height = 14, Weight = 1.8, Foreground = K.Brush("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _hintText.FontSize = 12.5;
        _hintText.FontWeight = FontWeights.SemiBold;
        _hintText.Foreground = K.Brush("Accent");
        hintRow.Children.Add(_hintText);
        _hint.Child = hintRow;
        _hint.Background = K.Brush("AccentSoft");
        _hint.CornerRadius = new CornerRadius(10);
        _hint.Padding = new Thickness(12, 7, 12, 7);
        _hint.Margin = new Thickness(0, 12, 0, 0);

        // a thin progress line
        var track = new Border { Height = 3, CornerRadius = new CornerRadius(2), Background = K.Brush("Line"), Margin = new Thickness(0, 14, 0, 0), ClipToBounds = true };
        _progressFill.Height = 3;
        _progressFill.CornerRadius = new CornerRadius(2);
        _progressFill.Background = K.Brush("Accent");
        _progressFill.HorizontalAlignment = HorizontalAlignment.Left;
        track.Child = _progressFill;
        track.SizeChanged += (_, _) => SetProgress(false);
        _progressTrack = track;

        _skip = new Button { Style = St("Btn"), Padding = new Thickness(10, 8, 10, 8), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = K.Brush("Muted"), FontWeight = FontWeights.Medium };
        _back = new Button { Style = St("Btn"), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        _next = new Button { Style = St("BtnPrimary"), Padding = new Thickness(18, 8, 18, 8) };
        _skip.Click += (_, _) => _session.Skip();
        _back.Click += (_, _) => _session.Back();
        _next.Click += (_, _) => _session.Next();
        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_back, 2);
        Grid.SetColumn(_next, 3);
        buttons.Children.Add(_skip);
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);

        _content.Children.Add(counterPill);
        _content.Children.Add(_title);
        _content.Children.Add(_text);
        _content.Children.Add(_hint);
        _content.Children.Add(track);
        _content.Children.Add(buttons);
        _bubble.Child = _content;
        Children.Add(_bubble);

        _session.Changed += OnStepChanged;
        _session.Finished += OnFinished;
        _follow.Tick += (_, _) => Follow();
        Loaded += OnLoaded;
    }

    private readonly Border _progressTrack;

    // ---- life cycle ------------------------------------------------------------------------------------

    /// <summary>Puts the overlay on the window and starts the first step.</summary>
    public void Open()
    {
        _host.Surface.Children.Add(this);
        _host.Window.PreviewKeyDown += OnKey;
        _host.Window.SizeChanged += OnWindowSize;
        Keyboard.ClearFocus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // the first frame: the whole window dims (fades in), the hole starts as a point in the middle and opens
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        _spot.HoleX = cx; _spot.HoleY = cy; _spot.HoleW = 0; _spot.HoleH = 0;
        Fx.Anim(_spot, Spotlight.DimProperty, 1, new CubicEase { EasingMode = EasingMode.EaseOut }, 380);
        if (Motion.Enabled)
            _spot.BeginAnimation(Spotlight.PulseProperty, new DoubleAnimation(0.001, 1, TimeSpan.FromMilliseconds(1700))
            { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }.Slow());
        ApplyStep(first: true);
        _follow.Start();
    }

    private void OnFinished(TourEnd how)
    {
        Close();
        _onFinished(how);
    }

    /// <summary>Takes the overlay away (fade) and gives the window its keys back.</summary>
    public void Close()
    {
        if (_closing) return;
        _closing = true;
        _follow.Stop();
        _host.Window.PreviewKeyDown -= OnKey;
        _host.Window.SizeChanged -= OnWindowSize;
        _spot.PassThrough = false;
        _spot.IsHitTestVisible = false;
        void Remove()
        {
            _spot.BeginAnimation(Spotlight.PulseProperty, null);
            _host.Surface.Children.Remove(this);
        }
        if (!Motion.Enabled || !IsLoaded) { Remove(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) => Remove();
        BeginAnimation(OpacityProperty, fade);
        Fx.Anim(_move, TranslateTransform.YProperty, _move.Y + 14, new CubicEase { EasingMode = EasingMode.EaseIn }, 240);
    }

    /// <summary>The window opened a page (the person clicked the menu item the step asked for).</summary>
    public void PageOpened(string key)
    {
        if (!_closing) _session.PageOpened(key);
    }

    // ---- keyboard --------------------------------------------------------------------------------------

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var forward = Ui.Flow == FlowDirection.LeftToRight ? Key.Right : Key.Left;
        var backward = Ui.Flow == FlowDirection.LeftToRight ? Key.Left : Key.Right;
        if (key == Key.Escape) _session.Skip();
        else if (key == forward || key == Key.Enter || key == Key.PageDown) _session.Next();
        else if (key == backward || key == Key.PageUp) _session.Back();
        else return;
        e.Handled = true;
    }

    private void OnWindowSize(object sender, SizeChangedEventArgs e) => Follow();

    // ---- one step --------------------------------------------------------------------------------------

    private void OnStepChanged() => ApplyStep(first: false);

    private void ApplyStep(bool first)
    {
        if (_closing) return;
        var step = _session.Current;
        if (step.Page.Length > 0 && !string.Equals(_host.ActiveKey, step.Page, StringComparison.Ordinal))
        {
            _host.GoTo(step.Page);
            _holdUntil = DateTime.UtcNow.AddMilliseconds(Motion.Enabled ? 260 : 40); // the old page is still leaving
        }

        // texts (the bubble content softly comes in again)
        _content.FlowDirection = Ui.Flow;
        _counter.Text = _session.Counter;
        _title.Text = step.Title;
        _text.Text = step.Text;
        _hint.Visibility = step.NeedsClick ? Visibility.Visible : Visibility.Collapsed;
        _hintText.Text = step.Click;
        _spot.PassThrough = step.NeedsClick;
        _back.Visibility = _session.IsFirst ? Visibility.Collapsed : Visibility.Visible;
        _back.Content = L.T("قبلی", "Back");
        _skip.Content = _session.IsLast ? "" : L.T("رد کردن", "Skip");
        _skip.Visibility = _session.IsLast ? Visibility.Hidden : Visibility.Visible;
        _next.Content = _session.IsFirst ? L.T("شروع تور", "Start the tour")
            : _session.IsLast ? L.T("تمام شد", "Finish")
            : step.NeedsClick ? L.T("ببرم اونجا", "Take me there")
            : L.T("بعدی", "Next");
        SetProgress(!first);
        if (!first && Motion.Enabled)
        {
            _content.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(320)) { FillBehavior = FillBehavior.Stop });
            _contentMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop });
        }
        _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Place(animate: !first, force: true);
    }

    private void SetProgress(bool animate)
    {
        var width = Math.Max(0, _progressTrack.ActualWidth * _session.Progress);
        if (!animate) _progressFill.BeginAnimation(WidthProperty, null);
        if (_progressTrack.ActualWidth <= 0) return;
        if (animate) Fx.Anim(_progressFill, WidthProperty, width, SpringEase.I, 600);
        else _progressFill.Width = width;
    }

    /// <summary>Re-measures the target now and then: the page may still be arriving, the window may be resized, the theme may change.</summary>
    private void Follow()
    {
        if (_closing || !IsLoaded) return;
        if (_themeKey != Ui.Theme.Key) { _themeKey = Ui.Theme.Key; _spot.InvalidateVisual(); _bubble.Background = OpaqueCard(); }
        if (DateTime.UtcNow < _holdUntil) return;
        Place(animate: true, force: false);
    }

    /// <summary>The lit rectangle of the current step in this layer's coordinates, or null for a centred step (or while the target is not shown).</summary>
    private Rect? TargetRect(out bool missing)
    {
        missing = false;
        var step = _session.Current;
        if (step.Target.Length == 0) return null;
        var element = _host.Resolve(step.Target);
        if (element is not { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 }) { missing = true; return null; }
        Rect box;
        try { box = element.TransformToVisual(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
        catch (InvalidOperationException) { missing = true; return null; }
        var pad = step.Target.StartsWith(TourTargets.NavPrefix, StringComparison.Ordinal) ? 3 : step.Target == TourTargets.Theme ? 4 : 8;
        box.Inflate(pad, pad);
        box.Intersect(new Rect(4, 4, Math.Max(0, ActualWidth - 8), Math.Max(0, ActualHeight - 8)));
        if (box.IsEmpty || box.Width < 4 || box.Height < 4) { missing = true; return null; }
        return box;
    }

    private void Place(bool animate, bool force)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var target = TargetRect(out var missing);
        if (missing && !force) return; // keep where we are until the target shows up
        if (missing && _lastHole is null) target = null;
        var area = new Rect(0, 0, ActualWidth, ActualHeight);

        // hole
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var hole = target ?? new Rect(cx, cy, 0, 0);
        var changedHole = _lastHole is not { } old || !Close(old, hole);
        if (changedHole || force)
        {
            var spring = animate && Motion.Enabled;
            var ms = force ? 720 : 360;
            void Move(DependencyProperty p, double v) { if (spring) Fx.Anim(_spot, p, v, SpringEase.I, ms); else { _spot.BeginAnimation(p, null); _spot.SetValue(p, v); } }
            Move(Spotlight.HoleXProperty, hole.X);
            Move(Spotlight.HoleYProperty, hole.Y);
            Move(Spotlight.HoleWProperty, hole.Width);
            Move(Spotlight.HoleHProperty, hole.Height);
            _lastHole = hole;
        }

        // bubble
        _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _bubble.DesiredSize;
        var spot = BubbleSpot(target, size, area);
        var movedBubble = !_placed || Math.Abs(spot.X - _lastBubble.X) > 1 || Math.Abs(spot.Y - _lastBubble.Y) > 1;
        if (!movedBubble && !force) return;
        var wasPlaced = _placed;
        _placed = true;
        _lastBubble = spot;
        if (!wasPlaced)
        {
            _move.X = spot.X; _move.Y = spot.Y;
            if (Motion.Enabled)
            {
                Fx.Anim(_bubble, OpacityProperty, 1, new CubicEase { EasingMode = EasingMode.EaseOut }, 360);
                _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(spot.Y + 26, spot.Y, TimeSpan.FromMilliseconds(700)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop });
                var grow = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
            }
            else _bubble.Opacity = 1;
            return;
        }
        _bubble.Opacity = 1;
        var ms2 = force ? 700 : 360;
        Fx.Anim(_move, TranslateTransform.XProperty, spot.X, SpringEase.I, ms2);
        Fx.Anim(_move, TranslateTransform.YProperty, spot.Y, SpringEase.I, ms2);
    }

    private static bool Close(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 1.5 && Math.Abs(a.Y - b.Y) < 1.5 && Math.Abs(a.Width - b.Width) < 1.5 && Math.Abs(a.Height - b.Height) < 1.5;

    /// <summary>Where the bubble goes: under the lit area if it fits, else above, else beside it (either side), else in the
    /// biggest free corner; never outside the window. Centred when there is no target. Pure geometry, so it is unit-tested.</summary>
    public static Point BubbleSpot(Rect? target, Size bubble, Rect area, double gap = 18, double edge = 16)
    {
        var bounds = new Rect(area.X + edge, area.Y + edge, Math.Max(0, area.Width - 2 * edge), Math.Max(0, area.Height - 2 * edge));
        Point Clamp(double x, double y) => new(
            Math.Max(bounds.Left, Math.Min(x, bounds.Right - bubble.Width)),
            Math.Max(bounds.Top, Math.Min(y, bounds.Bottom - bubble.Height)));
        if (target is not { } t) return Clamp(area.X + (area.Width - bubble.Width) / 2, area.Y + (area.Height - bubble.Height) / 2);

        var cx = t.X + t.Width / 2 - bubble.Width / 2;
        var cy = t.Y + t.Height / 2 - bubble.Height / 2;
        var below = Clamp(cx, t.Bottom + gap);
        var above = Clamp(cx, t.Top - gap - bubble.Height);
        var right = Clamp(t.Right + gap, cy);
        var left = Clamp(t.Left - gap - bubble.Width, cy);
        // a narrow target at the window's edge (a menu item, the menu) is explained beside it, towards the middle of the window
        var towardsMiddle = t.X + t.Width / 2 > area.X + area.Width / 2 ? new[] { left, right } : new[] { right, left };
        var centre = (t.X + t.Width / 2 - area.X) / Math.Max(1, area.Width);
        var candidates = t.Height >= 40 && t.Width < area.Width * 0.4 && (centre < 0.25 || centre > 0.75)
            ? towardsMiddle.Concat([below, above]).ToArray()
            : new[] { below, above }.Concat(towardsMiddle).ToArray();
        foreach (var c in candidates)
        {
            var r = new Rect(c, bubble);
            if (bounds.Contains(r) && !r.IntersectsWith(Inflated(t, gap - 4))) return c;
        }
        // nothing is free: the corner that hides the least of the lit area
        var corners = new[]
        {
            Clamp(bounds.Right, bounds.Bottom), Clamp(bounds.Left, bounds.Bottom), Clamp(bounds.Right, bounds.Top), Clamp(bounds.Left, bounds.Top),
        };
        return corners.OrderBy(c => { var o = Rect.Intersect(new Rect(c, bubble), t); return o.IsEmpty ? 0 : o.Width * o.Height; }).First();
    }

    private static Rect Inflated(Rect r, double by) { r.Inflate(by, by); return r; }
}
