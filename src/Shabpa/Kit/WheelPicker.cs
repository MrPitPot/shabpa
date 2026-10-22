using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;

namespace Shabpa.Kit;

/// <summary>The pure maths behind the wheel picker (no WPF types, so it is unit-tested): clamping, the drum pose of a row,
/// the rubber band at the ends, the fling target, and the day / year lists of a birthday.</summary>
public static class WheelMath
{
    /// <summary>Angle in radians between two neighbouring rows on the imaginary drum.</summary>
    public const double Theta = 0.42;

    public static int Clamp(int index, int count) => count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);

    public static int Step(int index, int delta, int count) => Clamp(index + delta, count);

    /// <summary>The row that a (fractional) scroll position rests on.</summary>
    public static int Nearest(double position, int count) => Clamp((int)Math.Round(position, MidpointRounding.AwayFromZero), count);

    /// <summary>Where a flick with the given speed (rows per second, positive = towards later rows) comes to rest.</summary>
    public static int Fling(double position, double velocity, int count)
    {
        var travel = Math.Clamp(velocity * 0.32, -14, 14);
        return Nearest(position + travel, count);
    }

    /// <summary>While dragging past either end the wheel gives way only a little (and never more than about one row).</summary>
    public static double Rubber(double position, int count)
    {
        var last = Math.Max(0, count - 1);
        if (position < 0) return Math.Max(-0.9, position * 0.35);
        if (position > last) return last + Math.Min(0.9, (position - last) * 0.35);
        return position;
    }

    /// <summary>How a row at distance <paramref name="d"/> rows from the centre sits on the drum: vertical offset in pixels,
    /// the vertical squash and the opacity. Rows round the back of the drum are not drawn (opacity 0).</summary>
    public static (double y, double scale, double opacity) Pose(double d, double rowHeight)
    {
        var angle = d * Theta;
        if (Math.Abs(angle) >= 1.45) return (0, 0, 0);
        var cos = Math.Cos(angle);
        return (rowHeight / Theta * Math.Sin(angle), cos, cos * cos);
    }

    /// <summary>The inverse of <see cref="Pose"/>'s offset: how many rows from the centre lie under a point <paramref name="y"/> pixels
    /// from the centre line. The rows are pressed together on the drum, so this is not a plain division by the row height.</summary>
    public static double RowAtOffset(double y, double rowHeight)
    {
        var s = Math.Clamp(y * Theta / rowHeight, -1, 1);
        return Math.Asin(s) / Theta;
    }

    /// <summary>How much of the "selected" colour a row at distance <paramref name="d"/> shows (1 in the middle, 0 half a row away).</summary>
    public static double Emphasis(double d) => Math.Clamp(1 - Math.Abs(d) * 2, 0, 1);

    // ---- birthday lists --------------------------------------------------------------------------------------

    /// <summary>How many days the day wheel offers. Without a year the month's longest length (Esfand 30, Feb 29 are allowed
    /// and fall on the last day of short years); with a year the length that month really had.</summary>
    public static int DayCount(bool jalali, int month, int year)
    {
        if (month is < 1 or > 12) return 31;
        if (year == 0) return Birthday.MaxDay(jalali, month);
        try { return Birthday.DaysIn(jalali, year, month); }
        catch (ArgumentException) { return Birthday.MaxDay(jalali, month); }
    }

    public static int ClampDay(int day, int count) => count <= 0 ? 1 : Math.Clamp(day, 1, count);

    /// <summary>The oldest and newest year on the wheel. A saved year outside the usual range widens it so it never gets lost.</summary>
    public static (int lo, int hi) YearRange(bool jalali, DateTime now, int saved = 0)
    {
        var hi = Birthday.YearOf(jalali, now);
        var lo = jalali ? 1280 : 1900;
        if (saved != 0) { lo = Math.Min(lo, saved); hi = Math.Max(hi, saved); }
        return (lo, hi);
    }

    /// <summary>Row 0 is "no year"; then the newest year first, going back in time.</summary>
    public static int YearCount(int lo, int hi) => hi - lo + 2;

    public static int YearAt(int index, int hi) => index <= 0 ? 0 : hi - index + 1;

    public static int YearIndex(int year, int lo, int hi) => year == 0 || year < lo || year > hi ? 0 : hi - year + 1;
}

/// <summary>One scrolling column of a wheel picker. Rows sit on an imaginary drum: the middle row is large and in the accent
/// colour, the others shrink, fade and bend away. Scroll with the mouse wheel, drag (with momentum and a snap to the nearest
/// row), click a row, or use Up / Down / PageUp / PageDown / Home / End. Left / Right move to the neighbouring column.</summary>
public sealed class WheelColumn : Border
{
    public const double RowHeight = 38;
    public const int VisibleRows = 5;
    private const int Slots = 7;

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(WheelColumn),
        new PropertyMetadata(0.0, (d, _) => ((WheelColumn)d).Layout()));

    /// <summary>The scroll position in rows (fractional while moving). Animated.</summary>
    public double Position { get => (double)GetValue(PositionProperty); private set => SetValue(PositionProperty, value); }

    private sealed class Slot
    {
        public Grid Host = new();
        public TextBlock Plain = new();
        public TextBlock Accent = new();
        public ScaleTransform Scale = new();
        public TranslateTransform Move = new();
        public int Index = int.MinValue;
    }

    private readonly List<Slot> _slots = [];
    private readonly Border _focusRing = new();
    private IReadOnlyList<string> _items = [];
    private int _selected;
    private double _wheelRest;

    // drag state
    private bool _down, _dragging;
    private double _startY, _startPosition;
    private readonly List<(long ms, double pos)> _samples = [];

    /// <summary>Raised when the person picks another row (by any means), not when the list is replaced from code.</summary>
    public event Action<int>? Changed;

    public int SelectedIndex => _selected;
    public int Count => _items.Count;
    public string? SelectedText => _selected < _items.Count ? _items[_selected] : null;

    public WheelColumn(string name = "")
    {
        Height = RowHeight * VisibleRows;
        ClipToBounds = true;
        Background = Brushes.Transparent; // so empty parts of the column are hit-testable
        Focusable = true;
        FocusVisualStyle = null;
        FlowDirection = FlowDirection.LeftToRight;
        Cursor = Cursors.Hand;
        if (name.Length > 0) System.Windows.Automation.AutomationProperties.SetName(this, name);

        var grid = new Grid();
        _focusRing.CornerRadius = new CornerRadius(10);
        _focusRing.BorderThickness = new Thickness(1.5);
        _focusRing.BorderBrush = K.Brush("Accent");
        _focusRing.Margin = new Thickness(2, 0, 2, 0);
        _focusRing.Height = RowHeight;
        _focusRing.VerticalAlignment = VerticalAlignment.Center;
        _focusRing.Opacity = 0;
        _focusRing.IsHitTestVisible = false;

        for (var i = 0; i < Slots; i++)
        {
            var s = new Slot();
            s.Host.Height = RowHeight;
            s.Host.VerticalAlignment = VerticalAlignment.Top;
            s.Host.RenderTransformOrigin = new Point(0.5, 0.5);
            s.Host.RenderTransform = new TransformGroup { Children = { s.Scale, s.Move } };
            s.Host.IsHitTestVisible = false;
            Setup(s.Plain, "Text", FontWeights.Normal);
            Setup(s.Accent, "Accent", FontWeights.SemiBold);
            s.Host.Children.Add(s.Plain);
            s.Host.Children.Add(s.Accent);
            _slots.Add(s);
            grid.Children.Add(s.Host);
        }
        grid.Children.Add(_focusRing);
        Child = grid;

        GotKeyboardFocus += (_, _) => Fx.Anim(_focusRing, OpacityProperty, 0.7, null, 160);
        LostKeyboardFocus += (_, _) => Fx.Anim(_focusRing, OpacityProperty, 0, null, 200);
        Layout();
    }

    private static void Setup(TextBlock t, string brush, FontWeight weight)
    {
        t.FontSize = 18;
        t.FontWeight = weight;
        t.Foreground = K.Brush(brush);
        t.TextAlignment = TextAlignment.Center;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        t.FlowDirection = Ui.Flow;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.Margin = new Thickness(6, 0, 6, 0);
    }

    // ---- content ---------------------------------------------------------------------------------------------

    /// <summary>Replaces the rows. The selection is kept as close as possible; a smaller list moves the wheel there smoothly.</summary>
    public void SetItems(IReadOnlyList<string> items, int selected, bool animate = false)
    {
        _items = items;
        foreach (var s in _slots) s.Index = int.MinValue;
        Select(selected, animate, raise: false);
    }

    /// <summary>Moves to a row from code (no <see cref="Changed"/>).</summary>
    public void Select(int index, bool animate = true, bool raise = false)
    {
        index = WheelMath.Clamp(index, _items.Count);
        var changed = index != _selected;
        _selected = index;
        MoveTo(index, animate ? 320 : 0);
        if (raise && changed) Changed?.Invoke(index);
    }

    /// <summary>Test aid and keyboard path: the same as the person picking a row.</summary>
    public void Pick(int index, int ms = 320)
    {
        index = WheelMath.Clamp(index, _items.Count);
        var changed = index != _selected;
        _selected = index;
        MoveTo(index, ms);
        if (changed) Changed?.Invoke(index);
    }

    private void MoveTo(int row, int ms)
    {
        var from = Position;
        BeginAnimation(PositionProperty, null);
        if (ms <= 0 || !Motion.Enabled || !IsLoaded || Math.Abs(from - row) < 0.001)
        {
            Position = row;
            Layout();
            return;
        }
        Position = row;
        BeginAnimation(PositionProperty, new DoubleAnimation(from, row, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    // ---- drawing ---------------------------------------------------------------------------------------------

    private void Layout()
    {
        if (_slots.Count == 0) return;
        var pos = Position;
        var centre = (int)Math.Round(pos, MidpointRounding.AwayFromZero);
        var top = (RowHeight * VisibleRows - RowHeight) / 2;
        for (var k = 0; k < Slots; k++)
        {
            var s = _slots[k];
            var index = centre + k - Slots / 2;
            if (index < 0 || index >= _items.Count) { s.Host.Visibility = Visibility.Collapsed; s.Index = int.MinValue; continue; }
            var d = index - pos;
            var (y, scale, opacity) = WheelMath.Pose(d, RowHeight);
            if (opacity <= 0.01) { s.Host.Visibility = Visibility.Collapsed; continue; }
            if (s.Index != index)
            {
                s.Plain.Text = s.Accent.Text = _items[index];
                s.Index = index;
            }
            s.Host.Visibility = Visibility.Visible;
            s.Host.Opacity = opacity;
            s.Move.Y = top + y;
            s.Scale.ScaleY = scale;
            s.Scale.ScaleX = 1 - (1 - scale) * 0.25;
            var e = WheelMath.Emphasis(d);
            s.Accent.Opacity = e;
            s.Plain.Opacity = 1 - e;
        }
    }

    /// <summary>Test aid: the text of the row drawn in the middle.</summary>
    internal string? CentreText => _slots.Where(s => s.Host.Visibility == Visibility.Visible && s.Index >= 0)
        .OrderBy(s => Math.Abs(s.Index - Position)).FirstOrDefault() is { } best ? _items[best.Index] : null;

    // ---- input -----------------------------------------------------------------------------------------------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_items.Count < 2) return;
        e.Handled = true; // the wheel scrolls this column, not the page behind it
        _wheelRest += e.Delta;
        var steps = (int)(_wheelRest / 120);
        if (steps == 0) return;
        _wheelRest -= steps * 120;
        Pick(WheelMath.Step(_selected, -steps, _items.Count), 240);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_items.Count == 0) return;
        _down = true;
        _dragging = false;
        _startY = e.GetPosition(this).Y;
        var current = Position;
        BeginAnimation(PositionProperty, null); // freeze where it is, so a grab stops a coasting wheel
        Position = current;
        _startPosition = current;
        _samples.Clear();
        _samples.Add((Environment.TickCount64, current));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_down) return;
        var dy = e.GetPosition(this).Y - _startY;
        if (!_dragging && Math.Abs(dy) > 4) _dragging = true;
        if (!_dragging) return;
        Position = WheelMath.Rubber(_startPosition - dy / RowHeight, _items.Count);
        var now = Environment.TickCount64;
        _samples.Add((now, Position));
        _samples.RemoveAll(s => now - s.ms > 140);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_down) return;
        var y = e.GetPosition(this).Y;
        End();
        if (_dragging)
        {
            var now = Environment.TickCount64;
            var velocity = 0.0;
            if (_samples.Count > 1 && now - _samples[^1].ms < 90)
            {
                var (t0, p0) = _samples[0];
                var (t1, p1) = _samples[^1];
                if (t1 > t0) velocity = (p1 - p0) / ((t1 - t0) / 1000.0);
            }
            var target = WheelMath.Fling(Position, velocity, _items.Count);
            Pick(target, (int)Math.Clamp(300 + Math.Abs(target - Position) * 45, 300, 900));
        }
        else
        {
            var offset = WheelMath.RowAtOffset(y - RowHeight * VisibleRows / 2, RowHeight);
            Pick(WheelMath.Nearest(Position + offset, _items.Count), 300);
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_down) return;
        _down = false;
        if (_dragging) Pick(WheelMath.Nearest(Position, _items.Count), 260);
        _dragging = false;
    }

    private void End()
    {
        _down = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var count = _items.Count;
        int? target = e.Key switch
        {
            Key.Up => WheelMath.Step(_selected, -1, count),
            Key.Down => WheelMath.Step(_selected, 1, count),
            Key.PageUp => WheelMath.Step(_selected, -5, count),
            Key.PageDown => WheelMath.Step(_selected, 5, count),
            Key.Home => 0,
            Key.End => WheelMath.Clamp(count - 1, count),
            _ => null,
        };
        if (target is { } t)
        {
            Pick(t, 240);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Left or Key.Right)
        {
            MoveFocus(new TraversalRequest(e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right));
            e.Handled = true;
        }
    }
}

/// <summary>A row of <see cref="WheelColumn"/>s on a recessed glass groove with one highlight band across the middle, like the
/// iOS date picker. The columns sit in reading order (the first one at the start edge, so at the right in Persian).</summary>
public sealed class WheelPicker : Border
{
    private readonly Grid _columns = new();
    public IReadOnlyList<WheelColumn> Columns => _list;
    private readonly List<WheelColumn> _list = [];

    public WheelPicker()
    {
        CornerRadius = new CornerRadius(16);
        Background = K.Brush("Line");
        Padding = new Thickness(8, 0, 8, 0);
        MaxWidth = 400;
        HorizontalAlignment = HorizontalAlignment.Left;
        FlowDirection = Ui.Flow;

        var host = new Grid();
        var band = new Grid { Height = WheelColumn.RowHeight, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 0) };
        band.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = K.Brush("AccentSoft"), Opacity = 0.9 });
        band.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = K.Brush("Accent"), Opacity = 0.55 });
        band.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = K.Brush("Accent"), Opacity = 0.55 });
        host.Children.Add(band);
        host.Children.Add(_columns);
        Child = host;
    }

    public WheelColumn Add(WheelColumn column, double weight = 1)
    {
        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
        Grid.SetColumn(column, _list.Count);
        _columns.Children.Add(column);
        _list.Add(column);
        return column;
    }
}
