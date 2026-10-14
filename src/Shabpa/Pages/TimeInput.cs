using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Hour and minute boxes that behave like a clock field: only digits (Persian or Latin), at most two,
/// never above 23 / 59, focus moves to the minute once the hour is complete, Backspace in an empty minute goes
/// back to the hour, and the arrow keys step the value.</summary>
internal static class TimeBoxes
{
    public static void Wire(TextBox hour, TextBox minute)
    {
        Field(hour, 23, next: minute, prev: null);
        Field(minute, 59, next: null, prev: hour);
    }

    public static bool TryRead(TextBox hour, TextBox minute, out int h, out int m)
    {
        m = 0;
        return int.TryParse(PageBase.Latin(hour.Text), out h) && h is >= 0 and <= 23
            && int.TryParse(PageBase.Latin(minute.Text), out m) && m is >= 0 and <= 59;
    }

    private static void Field(TextBox box, int max, TextBox? next, TextBox? prev)
    {
        box.MaxLength = 2;

        box.PreviewTextInput += (_, e) =>
        {
            e.Handled = true;
            var typed = PageBase.Latin(e.Text);
            if (typed.Length == 0 || !typed.All(char.IsAsciiDigit)) return;
            var text = PageBase.Latin(box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, typed));
            // A full box that keeps being typed into starts over with the new digit instead of growing or refusing.
            if (!ClockField.Accepts(text, max)) text = typed;
            if (!ClockField.Accepts(text, max)) return;
            box.Text = text;
            box.CaretIndex = text.Length;
            if (next is not null && ClockField.Complete(text, max))
            {
                next.Focus();
                next.SelectAll();
            }
        };

        DataObject.AddPastingHandler(box, (_, e) =>
        {
            var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
            var text = PageBase.Latin(box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, pasted));
            if (!ClockField.Accepts(text, max)) e.CancelCommand();
        });

        box.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Space:
                    e.Handled = true;
                    break;
                case Key.Back when prev is not null && box.SelectionLength == 0 && box.CaretIndex == 0:
                    prev.Focus();
                    prev.CaretIndex = prev.Text.Length;
                    e.Handled = true;
                    break;
                case Key.Up or Key.Down:
                    var value = int.TryParse(PageBase.Latin(box.Text), out var v) ? v : 0;
                    value = (value + (e.Key == Key.Up ? 1 : -1) + max + 1) % (max + 1);
                    box.Text = value.ToString("00");
                    box.SelectAll();
                    e.Handled = true;
                    break;
            }
        };

        // Clicking or tabbing in selects the whole value, so typing replaces it.
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (box.IsKeyboardFocusWithin) return;
            box.Focus();
            e.Handled = true;
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            var text = PageBase.Latin(box.Text);
            if (text.Length == 1 && ClockField.Accepts(text, max)) box.Text = "0" + text;
        };
    }
}

/// <summary>Chooses the day of a one-time schedule, like a download manager's scheduler: ‹ previous day · day · next day ›.
/// Until a day is picked it follows the time (today, or tomorrow once that time has passed); after that it stays on
/// the picked day and a time in the past is reported as such instead of silently moving to tomorrow.</summary>
internal sealed class DayPicker
{
    /// <summary>How far ahead a day can be picked.</summary>
    public const int MaxDaysAhead = 365;

    private readonly Func<(int hour, int minute)?> _time;
    private readonly TextBlock _label = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _prev;
    private readonly Button _next;
    private DateTime? _day;

    public event Action? Changed;
    public FrameworkElement View { get; }

    public DayPicker(Func<(int hour, int minute)?> time)
    {
        _time = time;
        // The earlier day sits on the reading-start side: right in Persian, left in English.
        _prev = Arrow(L.En ? "\uE76B" : "\uE76C", L.T("روز قبل", "Previous day"), -1);
        _next = Arrow(L.En ? "\uE76C" : "\uE76B", L.T("روز بعد", "Next day"), 1);
        var day = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.Resources["Panel2"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["Line"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(6, 0, 6, 0),
            MinWidth = 170,
            Child = _label,
        };
        View = new StackPanel { Orientation = Orientation.Horizontal, Children = { _prev, day, _next } };
        Update(DateTime.Now);
    }

    private Button Arrow(string glyph, string tooltip, int step)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Btn"],
            ToolTip = tooltip,
            Padding = new Thickness(10, 8, 10, 8),
            Content = new TextBlock { Text = glyph, Style = (Style)Application.Current.Resources["Icon"], FontSize = 13 },
        };
        button.Click += (_, _) => Step(step);
        return button;
    }

    /// <summary>The chosen moment, or null when the time boxes do not hold a valid time. May be in the past.</summary>
    public DateTime? Value(DateTime now) => _time() is { } t ? Schedule.On(_day, t.hour, t.minute, now) : null;

    /// <summary>The day the picker currently stands on.</summary>
    private DateTime Day(DateTime now) => _day ?? Value(now)?.Date ?? now.Date;

    private void Step(int days)
    {
        var now = DateTime.Now;
        var target = Day(now).AddDays(days);
        _day = target < now.Date ? now.Date : target > now.Date.AddDays(MaxDaysAhead) ? now.Date.AddDays(MaxDaysAhead) : target;
        Update(now);
        Changed?.Invoke();
    }

    /// <summary>Redraws the day label; call when the time boxes change or the clock moves on.</summary>
    public void Update(DateTime now)
    {
        var day = Day(now);
        _label.Text = $"{Fa.RelativeDay(day, now)} · {Fa.Date(day)}";
        _prev.IsEnabled = day > now.Date;
        _next.IsEnabled = day < now.Date.AddDays(MaxDaysAhead);
    }
}
