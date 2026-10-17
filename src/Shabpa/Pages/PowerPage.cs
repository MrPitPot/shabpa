using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«دستور برق» in the Glass Depth design: four option tiles (what), a card that says when, a live summary of exactly
/// what will happen, and the queue of scheduled commands. Behaviour is unchanged: every run or schedule asks first.</summary>
public sealed class PowerPage : PageBase
{
    private enum Mode { Now, After, At }

    private readonly Func<List<DayOfWeek>> _days;
    private readonly DayPicker _day;
    private PowerAction _action = PowerAction.Shutdown;
    private Mode _mode = Mode.Now;
    private bool _ready;
    private bool _seeded;
    private HashSet<string> _known = [];

    private readonly Grid _tiles = new() { Margin = new Thickness(-8, -8, -8, 8) };
    private readonly List<RadioButton> _tileList = [];
    private bool? _wide;

    private readonly TextBox _afterHours = new() { Style = St("NumberInput"), Text = "0" };
    private readonly TextBox _afterMinutes = new() { Style = St("NumberInput"), Text = "30" };
    private readonly TextBox _atHour = new() { Style = St("NumberInput"), Text = "01" };
    private readonly TextBox _atMinute = new() { Style = St("NumberInput"), Text = "30" };
    private readonly StackPanel _afterPanel = new() { Margin = new Thickness(0, 18, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _atPanel = new() { Margin = new Thickness(0, 18, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _dayRow = new() { Margin = new Thickness(0, 0, 0, 14) };
    private readonly StackPanel _dayPanel = new() { Orientation = Orientation.Horizontal };
    private readonly PowerUi.Callout _summary = new();
    private readonly Button _go = new() { Style = St("BtnPrimary"), Height = 46, Padding = new Thickness(26, 0, 26, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _goText = new() { FontWeight = FontWeights.Bold, FontSize = 14 };
    private readonly Icon _goIcon = new("pw", 18, null) { Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _queue = new();
    private readonly List<(TextBlock label, DateTime due)> _countdowns = [];

    public override string PageTitle => L.T("دستور برق", "Power command");
    public override string PageSubtitle => L.T("همین حالا، چند دقیقه‌ی دیگه یا هر شب سر یه ساعت مشخص.", "Right now, in a few minutes, or every night at a set time.");

    public PowerPage()
    {
        // ---- what: four option tiles -------------------------------------------------------------------
        for (var i = 0; i < 4; i++) _tiles.ColumnDefinitions.Add(new ColumnDefinition());
        AddTile("moon", "HueA", L.T("خواب", "Sleep"), L.T("سریع برمی‌گرده", "Back in seconds"), "Sleep");
        AddTile("hib", "HueB", L.T("هایبرنیت", "Hibernate"), L.T("بدون مصرف برق", "Uses no power"), "Hibernate");
        AddTile("pw", "HueE", L.T("خاموش کردن", "Shut down"), L.T("همه‌چی بسته می‌شه", "Everything closes"), "Shutdown");
        AddTile("refresh", "HueC", L.T("راه‌اندازی مجدد", "Restart"), L.T("خاموش و دوباره روشن", "Off and on again"), "Restart");
        _tileList[2].IsChecked = true;
        foreach (var tile in _tileList) tile.Checked += OnPick;
        ApplyLayout(true);

        // ---- when ----------------------------------------------------------------------------------------
        var (card, body) = PowerUi.Card(null);
        var modes = new StackPanel();
        body.Children.Add(modes);

        // "after a while": h and min boxes + quick durations
        var after = new StackPanel { Orientation = Orientation.Horizontal };
        after.Children.Add(_afterHours);
        after.Children.Add(Unit(L.T("ساعت و", "h and")));
        after.Children.Add(_afterMinutes);
        after.Children.Add(Unit(L.T("دقیقه‌ی دیگه", "min from now")));
        _afterPanel.Children.Add(after);
        var quick = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (minutes, label) in new[] { (15, L.T("۱۵ دقیقه", "15 min")), (30, L.T("۳۰ دقیقه", "30 min")), (60, L.T("۱ ساعت", "1 hour")), (120, L.T("۲ ساعت", "2 hours")), (240, L.T("۴ ساعت", "4 hours")) })
        {
            var button = new Button { Style = St("Btn"), Content = label, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 8) };
            var m = minutes;
            button.Click += (_, _) => OnQuick(m);
            quick.Children.Add(button);
        }
        _afterPanel.Children.Add(quick);
        body.Children.Add(_afterPanel);

        // "at a time": which day (only for a one-time command), the time, and the weekdays it repeats on
        _days = DayToggles(_dayPanel, UpdateSummary);
        TimeBoxes.Wire(_atHour, _atMinute);
        _day = new DayPicker(() => TimeBoxes.TryRead(_atHour, _atMinute, out var h, out var m) ? (h, m) : null);
        _day.Changed += UpdateSummary;
        _dayRow.Children.Add(PowerUi.Caption(L.T("روز", "Day")));
        _dayRow.Children.Add(new ContentControl { Content = _day.View, HorizontalAlignment = HorizontalAlignment.Left, Focusable = false });
        _atPanel.Children.Add(_dayRow);
        var clock = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 32, 8) };
        clock.Children.Add(_atHour);
        clock.Children.Add(new TextBlock { Text = ":", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        clock.Children.Add(_atMinute);
        var repeat = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
        repeat.Children.Add(PowerUi.Caption(L.T("تکرار در روزهای هفته (خالی یعنی فقط یک بار)", "Repeat on weekdays (none means just once)")));
        repeat.Children.Add(_dayPanel);
        var row = new WrapPanel();
        row.Children.Add(clock);
        row.Children.Add(repeat);
        _atPanel.Children.Add(row);
        body.Children.Add(_atPanel);

        // live summary + the one big button
        _summary.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_summary);
        _go.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _goIcon, _goText } };
        _goText.VerticalAlignment = VerticalAlignment.Center;
        _go.Click += OnGo;
        body.Children.Add(_go);

        // wire the inputs only now, so setting their initial text did not run the summary
        _afterHours.TextChanged += OnInput;
        _afterMinutes.TextChanged += OnInput;
        _atHour.TextChanged += OnInput;
        _atMinute.TextChanged += OnInput;
        if (PowerUi.Shot("power:after")) _mode = Mode.After;
        else if (PowerUi.Shot("power:at")) _mode = Mode.At;
        Chips(modes, [(Mode.Now, L.T("همین حالا", "Right now")), (Mode.After, L.T("چند وقت دیگه", "After a while")), (Mode.At, L.T("سر ساعت", "At a time"))], _mode, m =>
        {
            _mode = m;
            UpdateSummary();
        });

        // ---- queue -----------------------------------------------------------------------------------------
        var (queueCard, queueBody) = PowerUi.Card(L.T("دستورهای در صف", "Queued commands"));
        queueBody.Children.Add(_queue);

        var tip = new TextBlock
        {
            Text = L.T("همین دستورها از ربات تلگرام هم در دسترسه. قبل از اجرای هر دستور زمان‌بندی‌شده، یه شمارش معکوس با دکمه‌ی لغو می‌بینی.",
                "The same commands work from the Telegram bot. Every scheduled command shows a countdown with a cancel button first."),
            Style = St("Tip"), Margin = new Thickness(4, 0, 4, 8),
        };

        var root = new StackPanel();
        root.Children.Add(_tiles);
        root.Children.Add(card);
        root.Children.Add(queueCard);
        root.Children.Add(tip);
        Content = root;

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 800);
        _ready = true;
        UpdateSummary();
    }

    private static TextBlock Unit(string text) =>
        new() { Text = text, Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };

    private void AddTile(string icon, string hue, string title, string sub, string tag)
    {
        var tile = PowerUi.Tile(icon, hue, title, sub, "powerAction", tag);
        _tileList.Add(tile);
        _tiles.Children.Add(tile);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        _tiles.RowDefinitions.Clear();
        if (!wide) for (var r = 0; r < 2; r++) _tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < _tileList.Count; i++)
        {
            if (wide) PowerUi.Place(_tileList[i], 0, i);
            else PowerUi.Place(_tileList[i], i / 2, (i % 2) * 2, 2);
        }
        // narrow: two across, so the grid uses four columns of which each tile spans two
    }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        var tile = (RadioButton)sender;
        _action = Enum.Parse<PowerAction>((string)tile.Tag);
        PowerUi.PopTile(tile);
        UpdateSummary();
    }

    private void OnInput(object sender, TextChangedEventArgs e) => UpdateSummary();

    private void OnQuick(int minutes)
    {
        _afterHours.Text = (minutes / 60).ToString();
        _afterMinutes.Text = (minutes % 60).ToString();
        Motion.Pop(_afterHours);
        Motion.Pop(_afterMinutes);
    }

    /// <summary>The moment the current form describes, or null if the input is not usable.</summary>
    private DateTime? Due(DateTime now)
    {
        switch (_mode)
        {
            case Mode.After:
                var span = TimeSpan.FromHours(Int(_afterHours, 0, 99, 0)) + TimeSpan.FromMinutes(Int(_afterMinutes, 0, 59, 0));
                return span > TimeSpan.Zero ? now + span : null;
            case Mode.At:
                if (!TimeBoxes.TryRead(_atHour, _atMinute, out var h, out var m)) return null;
                var days = _days();
                if (days.Count > 0) return Schedule.Next(h, m, days, now);
                // A one-time command runs on the picked day; a moment already gone is not moved to tomorrow.
                return _day.Value(now) is { } at && at > now ? at : null;
            default:
                return now;
        }
    }

    private void ShowMode()
    {
        PowerUi.Toggle(_afterPanel, _mode == Mode.After);
        PowerUi.Toggle(_atPanel, _mode == Mode.At);
        PowerUi.Toggle(_dayRow, _days().Count == 0);
    }

    private void UpdateSummary()
    {
        if (!_ready) return;
        ShowMode();

        var now = DateTime.Now;
        _day.Update(now);
        var due = Due(now);
        _go.IsEnabled = due is not null;
        _goText.Text = _mode == Mode.Now ? Fa.ActionName(_action) : L.T("زمان‌بندی کن", "Schedule it");
        var goIcon = _mode == Mode.Now ? PowerUi.ActionIcon(_action) : "clk";
        if (_goIcon.IconName != goIcon && _go.IsLoaded) Motion.Pop(_goIcon);
        _goIcon.IconName = goIcon;

        if (due is null)
        {
            _summary.Set("Red", "warn");
            _summary.Say(_mode == Mode.After
                ? L.T("یه مدت زمان وارد کن.", "Enter a duration.")
                : TimeBoxes.TryRead(_atHour, _atMinute, out _, out _)
                    ? L.T("این زمان گذشته؛ یه روز یا ساعت بعدتر انتخاب کن.", "That time has passed; pick a later day or time.")
                    : L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59."));
            return;
        }
        _summary.Set("Accent", _mode == Mode.Now ? "bolt" : "clk");
        if (_mode == Mode.Now)
            _summary.Say(L.T($"{Fa.ActionSentence(_action)}؛ قبلش ۵ ثانیه فرصت لغو داری.", $"Then {Fa.ActionSentence(_action)}; you get 5 seconds to cancel first."));
        else
        {
            var days = _mode == Mode.At ? _days() : [];
            var repeat = days.Count > 0 ? L.T($" و بعد از اون {Fa.Days(days)} تکرار می‌شه", $", then repeats {Fa.Days(days)}") : "";
            _summary.Say(L.T(
                $"{Fa.DayOf(due.Value, now)} ساعت {Fa.Clock(due.Value)} ({Fa.Duration(due.Value - now)} دیگه) {Fa.ActionSentence(_action)}{repeat}.",
                $"{Fa.DayOf(due.Value, now)} at {Fa.Clock(due.Value)} (in {Fa.Duration(due.Value - now)}) {Fa.ActionSentence(_action)}{repeat}."));
        }
    }

    private void OnGo(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        if (Due(now) is not { } due) return;
        if (_mode == Mode.Now)
        {
            if (!Ask(L.T("همین الان اجرا بشه؟", "Run it right now?"),
                    L.T($"{Fa.ActionSentence(_action)}. بعد از تایید ۵ ثانیه شمارش معکوس می‌بینی و هنوز می‌تونی لغوش کنی. کارهای ذخیره‌نشده رو ذخیره کن.",
                        $"Then {Fa.ActionSentence(_action)}. After you confirm there is a 5-second countdown you can still cancel. Save your work first."),
                    L.T("تایید و اجرا", "Confirm and run"), danger: _action is PowerAction.Shutdown or PowerAction.Restart,
                    detail: L.T("شروع: همین الان", "Starts: right now")))
                return;
            Motion.Pop(_go);
            Hub.RequestAction(_action, L.T("دستور مستقیم", "Direct command"), 5);
            return;
        }

        var days = _mode == Mode.At ? _days() : [];
        var repeat = days.Count > 0 ? L.T($" · تکرار: {Fa.Days(days)}", $" · repeats: {Fa.Days(days)}") : "";
        if (!Ask(L.T("این زمان‌بندی ثبت بشه؟", "Schedule this?"),
                L.T($"{Fa.ActionSentence(_action)}. {Fa.Digits(Hub.Settings.CountdownSeconds.ToString())} ثانیه قبلش شمارش معکوس می‌بینی و می‌تونی لغوش کنی. از لیست پایین هر وقت خواستی حذفش کن.",
                    $"Then {Fa.ActionSentence(_action)}. You get a {Hub.Settings.CountdownSeconds}-second countdown first and can cancel. Delete it from the list below whenever you like."),
                L.T("تایید و ثبت", "Confirm and schedule"),
                detail: L.T($"شروع: {HomePage.At(due, now)} ({Fa.Duration(due - now)} دیگه){repeat}", $"Runs: {HomePage.At(due, now)} (in {Fa.Duration(due - now)}){repeat}")))
            return;
        Motion.Pop(_go);
        if (!Hub.AddCommand(_action, due, days.Count > 0 ? days : null))
            Hub.Notify(L.T("از قبل ثبت شده بود", "Already scheduled"), L.T("همین دستور با همین زمان قبلاً توی لیسته؛ دوباره اضافه نشد.", "The same command at about the same time is already in the list; nothing was added."));
    }

    public override void Refresh()
    {
        UpdateSummary();
        _queue.Children.Clear();
        _countdowns.Clear();
        var now = DateTime.Now;
        var current = new HashSet<string>();
        var fresh = 0;
        foreach (var cmd in Hub.Queue.Items.OrderBy(c => c.DueAt))
        {
            if (_queue.Children.Count > 0) _queue.Children.Add(Divider());
            var key = cmd.Id.ToString();
            current.Add(key);
            var isNew = _seeded && !_known.Contains(key);
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var countdown = new TextBlock { Text = Fa.Countdown(cmd.DueAt - now), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Res("Gold"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FlowDirection = FlowDirection.LeftToRight };
            _countdowns.Add((countdown, cmd.DueAt));
            trailing.Children.Add(countdown);
            var id = cmd.Id;
            var due = cmd.DueAt;
            var action = cmd.Action;
            Grid? rowRef = null;
            trailing.Children.Add(IconButton("trash", L.T("حذف", "Remove"), () =>
            {
                if (Ask(L.T("این دستور حذف بشه؟", "Delete this command?"),
                        L.T("برای همیشه از لیست پاک می‌شه و اجرا نمی‌شه؛ هیچ شمارش معکوسی هم نمی‌بینی.", "It is removed from the list for good and will not run; no countdown will appear either."),
                        L.T("حذف کن", "Delete"), danger: true, detail: $"{Fa.ActionName(action)} · {HomePage.At(due, DateTime.Now)}"))
                    PowerUi.Leave(rowRef!, () => Hub.RemoveCommand(id));
            }));
            rowRef = PowerUi.Live(Row(PowerUi.ActionIcon(cmd.Action), PowerUi.ActionHue(cmd.Action), Fa.ActionName(cmd.Action), HomePage.When(cmd, now), trailing));
            _queue.Children.Add(rowRef);
            if (isNew) PowerUi.Appear(rowRef, fresh++);
        }
        if (_queue.Children.Count == 0)
        {
            var empty = PowerUi.Empty(L.T("دستوری در صف نیست.", "Nothing is queued."));
            _queue.Children.Add(empty);
            if (_seeded && _known.Count > 0) PowerUi.Appear(empty);
        }
        _known = current;
        _seeded = true;
    }

    public override void OnSecond()
    {
        var now = DateTime.Now;
        foreach (var (label, due) in _countdowns) label.Text = Fa.Countdown(due - now);
        if (_mode != Mode.Now && now.Second % 15 == 0) UpdateSummary();
    }
}
