using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«بیدارباش» in the Glass Depth design: two short explanations, the state of Windows' wake timers, the form that
/// adds a wake-up, and the list of active ones. Behaviour is unchanged (confirm before create and delete).</summary>
public sealed class WakePage : PageBase
{
    private readonly Func<List<DayOfWeek>> _days;
    private bool _checked;
    private bool _seeded;
    private HashSet<string> _known = [];

    private readonly TextBox _hour = new() { Style = St("NumberInput"), Text = "07" };
    private readonly TextBox _minute = new() { Style = St("NumberInput"), Text = "30" };
    private readonly StackPanel _dayPanel = new() { Orientation = Orientation.Horizontal };
    private readonly PowerUi.Callout _error = new("Red", "warn") { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _list = new();
    private readonly StackPanel _timerPanel = new();
    private readonly GlassCard _timerCard;
    private readonly Grid _info = new() { Margin = new Thickness(-8, -8, -8, 8) };
    private readonly List<FrameworkElement> _infoCards = [];
    private bool? _wide;
    private Button _addButton = null!;

    public override string PageTitle => L.T("بیدارباش", "Wake-up");
    public override string PageSubtitle => L.T("سیستم رو شب بخوابون و صبح قبل از اینکه بهش برسی آماده باشه.", "Put the PC to sleep at night and have it ready before you get to it in the morning.");

    public WakePage()
    {
        // ---- two short explanations side by side ------------------------------------------------------------
        _info.ColumnDefinitions.Add(new ColumnDefinition());
        _info.ColumnDefinitions.Add(new ColumnDefinition());
        AddInfo("alarm", "HueB", L.T("بیدارباش از خواب و هایبرنیت", "Wake from sleep and hibernate"), L.T("سر ساعت مقرر، سیستم خودش بیدار می‌شه و صفحه روشن می‌شه.", "At the set time the PC wakes itself and turns the screen on."));
        AddInfo("warn", "HueC", L.T("با «خاموش کردن» بیدار نمی‌شه", "It cannot wake from a shutdown"), L.T("شب‌ها به‌جای خاموشی کامل، سیستم رو بخوابون یا هایبرنیت کن.", "At night, use sleep or hibernate instead of a full shutdown."));
        ApplyLayout(true);

        // ---- Windows wake timers (filled in once the check has run) ------------------------------------------
        _timerCard = new GlassCard { Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(22, 14, 22, 14), Visibility = Visibility.Collapsed, Child = _timerPanel };

        // ---- add a wake-up -----------------------------------------------------------------------------------
        var (addCard, add) = PowerUi.Card(L.T("افزودن بیدارباش", "Add a wake-up"));
        _days = DayToggles(_dayPanel);
        TimeBoxes.Wire(_hour, _minute);

        var form = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var time = new StackPanel { Margin = new Thickness(0, 0, 32, 8), VerticalAlignment = VerticalAlignment.Bottom };
        time.Children.Add(PowerUi.Caption(L.T("ساعت (۲۴ ساعته)", "Time (24-hour)")));
        var clock = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        clock.Children.Add(_hour);
        clock.Children.Add(new TextBlock { Text = ":", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        clock.Children.Add(_minute);
        time.Children.Add(clock);
        form.Children.Add(time);

        var days = new StackPanel { Margin = new Thickness(0, 0, 32, 8), VerticalAlignment = VerticalAlignment.Bottom };
        days.Children.Add(PowerUi.Caption(L.T("روزهای هفته (خالی یعنی هر روز)", "Weekdays (none means every day)")));
        _dayPanel.Margin = new Thickness(0, 0, 0, 8);
        days.Children.Add(_dayPanel);
        form.Children.Add(days);

        var addButton = _addButton = new Button { Style = St("BtnPrimary"), Height = 46, Padding = new Thickness(26, 0, 26, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16) };
        addButton.Content = PowerUi.ButtonContent("plus", new TextBlock { Text = L.T("افزودن", "Add"), FontWeight = FontWeights.Bold, FontSize = 14 });
        addButton.Click += OnAdd;
        form.Children.Add(addButton);
        add.Children.Add(form);
        add.Children.Add(_error);

        // ---- active wake-ups ---------------------------------------------------------------------------------
        var (listCard, listBody) = PowerUi.Card(L.T("بیدارباش‌های فعال", "Active wake-ups"));
        listBody.Children.Add(_list);

        var tip = new TextBlock
        {
            Text = L.T("بیدارباش‌ها توی Task Scheduler ویندوز ثبت می‌شن، پس حتی اگه شب‌پا بسته باشه هم کار می‌کنن.", "Wake-ups are registered in Windows Task Scheduler, so they work even when Shabpa is closed."),
            Style = St("Tip"), Margin = new Thickness(4, 0, 4, 8),
        };

        var root = new StackPanel();
        root.Children.Add(_info);
        root.Children.Add(_timerCard);
        root.Children.Add(addCard);
        root.Children.Add(listCard);
        root.Children.Add(tip);
        Content = root;

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 800);
        if (PowerUi.Shot("wake:error")) ShowError(L.T("همین بیدارباش رو قبلاً ساختی.", "You already have this wake-up."));
    }

    private void AddInfo(string icon, string hue, string title, string text)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new IconBadge(icon, hue, 42, 20, 12) { VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(badge);
        var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        texts.Children.Add(new TextBlock { Text = title, Style = St("H2") });
        texts.Children.Add(new TextBlock { Text = text, Style = St("Body"), Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var card = new GlassCard { Margin = new Thickness(8), Child = grid };
        PowerUi.HoverBadge(card, badge);
        _infoCards.Add(card);
        _info.Children.Add(card);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        _info.RowDefinitions.Clear();
        if (!wide) for (var r = 0; r < 2; r++) _info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < _infoCards.Count; i++)
        {
            if (wide) PowerUi.Place(_infoCards[i], 0, i);
            else PowerUi.Place(_infoCards[i], i, 0, 2);
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        PowerUi.Toggle(_error, false);
        if (!int.TryParse(Latin(_hour.Text), out var hour) || hour is < 0 or > 23 ||
            !int.TryParse(Latin(_minute.Text), out var minute) || minute is < 0 or > 59)
        {
            ShowError(L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59."));
            return;
        }
        var days = _days();
        if (Hub.State.WakeSchedules.Any(w => w.Hour == hour && w.Minute == minute && w.Days.Order().SequenceEqual(days.Order())))
        {
            ShowError(L.T("همین بیدارباش رو قبلاً ساختی.", "You already have this wake-up."));
            return;
        }
        if (!Ask(L.T("این بیدارباش ساخته بشه؟", "Create this wake-up?"),
                L.T("سیستم از خواب یا هایبرنیت بیدار می‌شه و فقط روشن می‌شه؛ خاموشش نمی‌کنه.", "The PC wakes from sleep or hibernate and simply turns on; nothing is shut down."),
                L.T("تایید و ثبت", "Confirm and create"), detail: L.T($"ساعت {Fa.Clock(hour, minute)} · {Fa.Days(days)}", $"{Fa.Clock(hour, minute)} · {Fa.Days(days)}")))
            return;
        Motion.Pop(_addButton);
        if (Hub.AddWake(new WakeSchedule { Hour = hour, Minute = minute, Days = days }) is { } error)
            ShowError(L.T($"ویندوز این زمان‌بندی رو ثبت نکرد: {error}", $"Windows did not accept this schedule: {error}"));
    }

    private void ShowError(string text)
    {
        _error.Say(text);
        PowerUi.Toggle(_error, true);
    }

    public override void Refresh()
    {
        _list.Children.Clear();
        var now = DateTime.Now;
        var current = new HashSet<string>();
        var fresh = 0;
        foreach (var w in Hub.State.WakeSchedules.OrderBy(w => Schedule.Next(w.Hour, w.Minute, w.Days, now)))
        {
            if (_list.Children.Count > 0) _list.Children.Add(Divider());
            var key = w.Id.ToString();
            current.Add(key);
            var isNew = _seeded && !_known.Contains(key);
            Grid? rowRef = null;
            var next = Schedule.Next(w.Hour, w.Minute, w.Days, now);
            var id = w.Id;
            rowRef = PowerUi.Live(Row("alarm", "HueB",
                L.T($"ساعت {Fa.Clock(w.Hour, w.Minute)} · {Fa.Days(w.Days)}", $"{Fa.Clock(w.Hour, w.Minute)} · {Fa.Days(w.Days)}"),
                L.T($"بعدی: {Fa.RelativeDay(next, now)} {Fa.Date(next)}، {Fa.Duration(next - now)} دیگه", $"Next: {Fa.RelativeDay(next, now)}, {Fa.Date(next)}, in {Fa.Duration(next - now)}"),
                IconButton("trash", L.T("حذف", "Remove"), () =>
                {
                    if (Ask(L.T("این بیدارباش حذف بشه؟", "Delete this wake-up?"),
                            L.T("از ویندوز هم پاک می‌شه و سیستم سر اون ساعت بیدار نمی‌شه.", "It is also removed from Windows and the PC will not wake at that time."),
                            L.T("حذف کن", "Delete"), danger: true, detail: L.T($"ساعت {Fa.Clock(w.Hour, w.Minute)} · {Fa.Days(w.Days)}", $"{Fa.Clock(w.Hour, w.Minute)} · {Fa.Days(w.Days)}")))
                        PowerUi.Leave(rowRef!, () => Hub.RemoveWake(id));
                })));
            _list.Children.Add(rowRef);
            if (isNew) PowerUi.Appear(rowRef, fresh++);
        }
        if (_list.Children.Count == 0)
        {
            var empty = PowerUi.Empty(L.T("هنوز بیدارباشی نساخته‌ای.", "No wake-ups yet."));
            _list.Children.Add(empty);
            if (_seeded && _known.Count > 0) PowerUi.Appear(empty);
        }
        _known = current;
        _seeded = true;

        if (!_checked)
        {
            _checked = true;
            if (PowerUi.Shot("wake:off")) ShowWakeTimers(false);
            else CheckWakeTimers();
        }
    }

    /// <summary>Wake tasks silently do nothing when the power plan has wake timers turned off.</summary>
    private void CheckWakeTimers()
    {
        Task.Run(() =>
        {
            var enabled = WakeScheduler.WakeTimersEnabled();
            Dispatcher.BeginInvoke(() => ShowWakeTimers(enabled));
        });
    }

    private void ShowWakeTimers(bool? enabled)
    {
        _timerPanel.Children.Clear();
        var wasShown = _timerCard.Visibility == Visibility.Visible;
        _timerCard.Visibility = Visibility.Visible;
        switch (enabled)
        {
            case true:
                _timerPanel.Children.Add(PowerUi.Live(Row("chk", "Green", L.T("تایمر بیداری ویندوز روشنه", "Windows wake timers are on"), L.T("همه‌چی برای بیدارباش آماده‌ست.", "Everything is ready for wake-ups."))));
                break;
            case false:
                var fix = new Button { Style = St("BtnPrimary"), Content = L.T("روشنش کن", "Turn it on") };
                fix.Click += (_, _) =>
                {
                    fix.IsEnabled = false;
                    Task.Run(() =>
                    {
                        var ok = Hub.Demo || WakeScheduler.EnableWakeTimers();
                        var now = ok ? WakeScheduler.WakeTimersEnabled() : false;
                        Dispatcher.BeginInvoke(() =>
                        {
                            ShowWakeTimers(now);
                            if (now != true)
                                ShowError(L.T("ویندوز اجازه نداد. از Control Panel ← Power Options ← Change advanced power settings ← Sleep ← Allow wake timers روشنش کن.",
                                    "Windows refused. Turn it on under Control Panel → Power Options → Change advanced power settings → Sleep → Allow wake timers."));
                        });
                    });
                };
                _timerPanel.Children.Add(PowerUi.Live(Row("warn", "HueC", L.T("تایمر بیداری ویندوز خاموشه", "Windows wake timers are off"),
                    L.T("تا وقتی «Allow wake timers» توی تنظیمات برق ویندوز خاموش باشه، سیستم سر ساعت بیدار نمی‌شه.", "While “Allow wake timers” is off in the Windows power settings, the PC will not wake on time."), fix)));
                break;
            default:
                _timerCard.Visibility = Visibility.Collapsed;
                break;
        }
        if (!wasShown && _timerCard.Visibility == Visibility.Visible) PowerUi.Appear(_timerCard);
    }
}
