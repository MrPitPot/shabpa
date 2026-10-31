using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«آماده‌باش قطعی برق»: the person types the times the power is expected to go; shortly before one, Shabpa warns and then
/// hibernates the PC through the usual cancellable countdown, so open work is kept and the disk is not cut mid-write.
/// The schedule is typed in by hand and nothing leaves this PC.</summary>
public sealed class OutagePage : PageBase
{
    private readonly Func<List<DayOfWeek>> _days;
    private readonly TextBox _hour = new() { Style = St("NumberInput"), Text = "14" };
    private readonly TextBox _minute = new() { Style = St("NumberInput"), Text = "00" };
    private readonly TextBox _length = new() { Style = St("Input"), Width = 76, FlowDirection = FlowDirection.LeftToRight, Text = "120", Padding = new Thickness(10, 6, 10, 6), TextAlignment = TextAlignment.Center };
    private readonly TextBox _warn = new() { Style = St("Input"), Width = 76, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(10, 6, 10, 6), TextAlignment = TextAlignment.Center };
    private readonly TextBox _hibernate = new() { Style = St("Input"), Width = 76, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(10, 6, 10, 6), TextAlignment = TextAlignment.Center };
    private readonly Switch _enabled = new(), _skipGame = new();
    private readonly StackPanel _dayPanel = new() { Orientation = Orientation.Horizontal };
    private readonly PowerUi.Callout _error = new("Red", "warn") { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _list = new();
    private readonly TextBlock _next = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private bool _building;

    public override string PageTitle => L.T("قطعی برق", "Power cut");
    public override string PageSubtitle => L.T("ساعت‌های قطعی برق رو بنویس؛ شب‌پا قبلش هشدار می‌ده و سیستم رو هایبرنیت می‌کنه.",
        "Type the times the power goes; Shabpa warns first and hibernates the PC before the cut.");

    public OutagePage()
    {
        var tip = new PowerUi.Callout("Accent", "bolt");
        tip.Say(L.T("هایبرنیت قبل از قطعی یعنی کارهای بازت سر جاشون می‌مونن و دیسک وسط نوشتن قطع نمی‌شه. با روشن شدن برق، سیستم رو دستی روشن می‌کنی و همه‌چی همون‌جاست.",
            "Hibernating before the cut keeps your open work and avoids a cut in the middle of a disk write. When the power is back you switch the PC on and everything is where you left it."));

        var (setCard, set) = PowerUi.Card(L.T("آماده‌باش", "Guard"));
        _enabled.Checked += (_, _) => Save();
        _enabled.Unchecked += (_, _) => Save();
        _skipGame.Checked += (_, _) => Save();
        _skipGame.Unchecked += (_, _) => Save();
        _warn.LostFocus += (_, _) => Save();
        _hibernate.LostFocus += (_, _) => Save();
        set.Children.Add(K.SettingRow(L.T("آماده‌باش قطعی برق روشن باشه", "Power-cut guard is on"), _enabled, first: true));
        set.Children.Add(K.SettingRow(L.T("چند دقیقه قبل از قطعی هشدار بده", "Warn this many minutes before the cut"), _warn));
        set.Children.Add(K.SettingRow(L.T("چند دقیقه قبل از قطعی هایبرنیت کن (با شمارش معکوس قابل لغو)", "Hibernate this many minutes before (with a cancellable countdown)"), _hibernate));
        set.Children.Add(K.SettingRow(L.T("وقتی بازی باز بود هایبرنیت نکن (فقط هشدار بده)", "Do not hibernate while a game runs (only warn)"), _skipGame,
            hint: L.T("هایبرنیت باید توی ویندوزت فعال باشه. اگه نبود، شب‌پا موقع اجرا خطا نشون می‌ده.", "Hibernate must be enabled in Windows; if not, Shabpa shows the error when it runs.")));

        var (addCard, add) = PowerUi.Card(L.T("افزودن زمان قطعی", "Add a cut"), L.T("هر خط یک بازه‌ی قطعی؛ می‌تونی چند تا بنویسی.", "One line per cut; you can add several."));
        TimeBoxes.Wire(_hour, _minute);
        _days = DayToggles(_dayPanel);
        var form = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var time = new StackPanel { Margin = new Thickness(0, 0, 32, 8), VerticalAlignment = VerticalAlignment.Bottom };
        time.Children.Add(PowerUi.Caption(L.T("شروع قطعی (۲۴ ساعته)", "Cut starts (24-hour)")));
        time.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                _hour,
                new TextBlock { Text = ":", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) },
                _minute,
            },
        });
        form.Children.Add(time);
        var len = new StackPanel { Margin = new Thickness(0, 0, 32, 8), VerticalAlignment = VerticalAlignment.Bottom };
        len.Children.Add(PowerUi.Caption(L.T("مدت (دقیقه)", "Length (minutes)")));
        len.Children.Add(_length);
        form.Children.Add(len);
        var days = new StackPanel { Margin = new Thickness(0, 0, 32, 8), VerticalAlignment = VerticalAlignment.Bottom };
        days.Children.Add(PowerUi.Caption(L.T("روزهای هفته (خالی یعنی هر روز)", "Weekdays (none means every day)")));
        _dayPanel.Margin = new Thickness(0, 0, 0, 8);
        days.Children.Add(_dayPanel);
        form.Children.Add(days);
        var addButton = new Button { Style = St("BtnPrimary"), Height = 46, Padding = new Thickness(26, 0, 26, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16) };
        addButton.Content = PowerUi.ButtonContent("plus", new TextBlock { Text = L.T("افزودن", "Add"), FontWeight = FontWeights.Bold, FontSize = 14 });
        addButton.Click += (_, _) => OnAdd();
        form.Children.Add(addButton);
        add.Children.Add(form);
        add.Children.Add(_error);

        var (listCard, listBody) = PowerUi.Card(L.T("زمان‌های قطعی", "Expected cuts"));
        listBody.Children.Add(_list);
        listBody.Children.Add(_next);

        var root = new StackPanel();
        root.Children.Add(tip);
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(setCard);
        root.Children.Add(addCard);
        root.Children.Add(listCard);
        Content = root;
    }

    private void Save()
    {
        if (_building) return;
        var s = Hub.Settings.Outage;
        s.Enabled = _enabled.IsChecked == true;
        s.SkipDuringGame = _skipGame.IsChecked == true;
        if (int.TryParse(Latin(_warn.Text), out var warn)) s.WarnMinutes = Math.Clamp(warn, 1, 120);
        if (int.TryParse(Latin(_hibernate.Text), out var hib)) s.HibernateMinutes = Math.Clamp(hib, 1, 60);
        if (s.HibernateMinutes > s.WarnMinutes) s.WarnMinutes = s.HibernateMinutes + 1;
        Hub.OutageChanged();
        Refresh();
    }

    private void OnAdd()
    {
        PowerUi.Toggle(_error, false);
        if (!int.TryParse(Latin(_hour.Text), out var hour) || hour is < 0 or > 23 ||
            !int.TryParse(Latin(_minute.Text), out var minute) || minute is < 0 or > 59)
        {
            Fail(L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59."));
            return;
        }
        if (!int.TryParse(Latin(_length.Text), out var length) || length is < 5 or > 24 * 60)
        {
            Fail(L.T("مدت قطعی باید بین ۵ دقیقه و ۲۴ ساعت باشه.", "The length must be between 5 minutes and 24 hours."));
            return;
        }
        var days = _days();
        var start = hour * 60 + minute;
        var s = Hub.Settings.Outage;
        if (s.Windows.Any(w => w.StartMinute == start && w.Days.Order().SequenceEqual(days.Order())))
        {
            Fail(L.T("همین قطعی رو قبلاً نوشتی.", "You already have this cut."));
            return;
        }
        s.Windows.Add(new OutageWindow { StartMinute = start, DurationMinutes = length, Days = days });
        s.Enabled = true;
        Hub.OutageChanged();
        Refresh();
    }

    private void Fail(string text)
    {
        _error.Say(text);
        PowerUi.Toggle(_error, true);
    }

    public override void Refresh()
    {
        _building = true;
        try
        {
            var s = Hub.Settings.Outage;
            _enabled.IsChecked = s.Enabled;
            _skipGame.IsChecked = s.SkipDuringGame;
            if (!_warn.IsKeyboardFocused) _warn.Text = s.WarnMinutes.ToString();
            if (!_hibernate.IsKeyboardFocused) _hibernate.Text = s.HibernateMinutes.ToString();

            _list.Children.Clear();
            foreach (var w in s.Windows.OrderBy(w => w.StartMinute))
            {
                if (_list.Children.Count > 0) _list.Children.Add(Divider());
                var id = w.Id;
                var end = (w.StartMinute + w.DurationMinutes) % (24 * 60);
                _list.Children.Add(Row("bolt", "HueC",
                    L.T($"{Fa.Clock(w.StartMinute / 60, w.StartMinute % 60)} تا {Fa.Clock(end / 60, end % 60)} · {Fa.Days(w.Days)}", $"{Fa.Clock(w.StartMinute / 60, w.StartMinute % 60)} to {Fa.Clock(end / 60, end % 60)} · {Fa.Days(w.Days)}"),
                    L.T($"{Fa.Digits(w.DurationMinutes.ToString())} دقیقه", $"{w.DurationMinutes} minutes"),
                    IconButton("trash", L.T("حذف", "Remove"), () =>
                    {
                        s.Windows.RemoveAll(x => x.Id == id);
                        Hub.OutageChanged();
                        Refresh();
                    })));
            }
            if (_list.Children.Count == 0) _list.Children.Add(PowerUi.Empty(L.T("هنوز زمان قطعی‌ای ننوشتی.", "No cuts typed yet.")));

            var now = DateTime.Now;
            _next.Text = OutageGuard.Upcoming(s, now) is { } up
                ? L.T($"قطعی بعدی: {Fa.RelativeDay(up.Start, now)} ساعت {Fa.Clock(up.Start)}" + (s.Enabled ? "" : " (آماده‌باش خاموشه)"),
                    $"Next cut: {Fa.RelativeDay(up.Start, now)} at {Fa.Clock(up.Start)}" + (s.Enabled ? "" : " (the guard is off)"))
                : "";
        }
        finally { _building = false; }
    }
}
