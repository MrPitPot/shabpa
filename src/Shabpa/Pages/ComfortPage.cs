using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Eye comfort and healthy habits: a screen dimmer that goes darker than the monitor's own buttons, optional
/// break reminders, and a "going to sleep" mode that fades the volume and the screen out before switching off.</summary>
public sealed class ComfortPage : PageBase
{
    private bool _loading;
    private bool? _wasDimmed, _wasSleeping;
    private readonly TextBlock _dimStatus = new();
    private readonly Button _dimToggle = new();
    private IconBadge _dimBadge = null!;
    private Slider _dimLevel = null!, _breakEvery = null!, _sleepMinutes = null!;
    private TextBlock _dimLevelValue = null!, _breakValue = null!, _sleepValue = null!;
    private readonly CheckBox _warm = new Switch(), _auto = new Switch(), _breakOn = new Switch(), _breakGame = new Switch(), _sleepDim = new Switch(), _sleepVolume = new Switch();
    private readonly TextBox _fromH = GroupB.TimeBox(), _fromM = GroupB.TimeBox(), _toH = GroupB.TimeBox(), _toM = GroupB.TimeBox();
    private readonly HotkeyBox _toggleKey = new(), _darkerKey = new(), _lighterKey = new();
    private readonly TextBlock _keyNote = new();
    private readonly WrapPanel _sleepActions = new();
    private readonly Button _sleepStart = new();
    private readonly TextBlock _sleepStatus = new();
    private readonly CheckBox _gameOn = new Switch(), _gamePower = new Switch(), _gameRam = new Switch(), _gameQuiet = new Switch();

    public override string PageTitle => L.T("آسایش و استراحت", "Comfort & rest");
    public override string PageSubtitle => L.T("کم‌نور کردن صفحه برای شب، یادآور استراحت و حالت شب‌خواب.", "Night dimmer, break reminders and sleep mode.");

    /// <summary>Cards one under another, 16 px apart (one block of the page grid).</summary>
    private static StackPanel Column(params FrameworkElement[] cards)
    {
        var column = new StackPanel();
        for (var i = 0; i < cards.Length; i++)
        {
            cards[i].Margin = new Thickness(0, i == 0 ? 0 : 16, 0, 0);
            column.Children.Add(cards[i]);
        }
        return column;
    }

    public ComfortPage()
    {
        var root = new BlockGrid();
        var c = Hub.Settings.Comfort;

        // ---- dim screen ---------------------------------------------------------------------------
        var (dimCard, dim) = Ui2.Card(L.T("کم‌نور کردن صفحه", "Dim screen"),
            L.T("یه لایه‌ی تیره روی همه‌ی مانیتورها می‌ندازه و نور صفحه رو از حدی که دکمه‌های خود مانیتور اجازه می‌دن هم کمتر می‌کنه. کلیک و بازی از زیرش عادی کار می‌کنن.",
                "Puts a dark layer over every monitor, so the screen gets dimmer than the monitor's own buttons allow. Clicks and games work normally underneath."));
        var status = new Grid { Margin = new Thickness(0, 16, 0, 4) };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _dimBadge = new IconBadge("moon", "HueA", 42, 22, 12);
        status.Children.Add(_dimBadge);
        _dimStatus.Style = St("Body");
        _dimStatus.FontWeight = FontWeights.SemiBold;
        _dimStatus.TextWrapping = TextWrapping.Wrap;
        _dimStatus.VerticalAlignment = VerticalAlignment.Center;
        _dimStatus.Margin = new Thickness(14, 0, 14, 0);
        Grid.SetColumn(_dimStatus, 1);
        status.Children.Add(_dimStatus);
        _dimToggle.Style = St("BtnPrimary");
        _dimToggle.Padding = new Thickness(20, 0, 20, 0);
        _dimToggle.Height = 40;
        _dimToggle.VerticalAlignment = VerticalAlignment.Center;
        _dimToggle.Click += (_, _) => Hub.Dim?.Toggle();
        Grid.SetColumn(_dimToggle, 2);
        status.Children.Add(_dimToggle);
        dim.Children.Add(status);
        var level = Ui2.SliderRow(L.T("میزان تیرگی", "Darkness"), 0.05, 0.92, 0.01, v => $"{v * 100:0}%");
        (_dimLevel, _dimLevelValue) = (level.slider, level.value);
        level.row.Margin = new Thickness(0, 10, 0, 6);
        dim.Children.Add(level.row);
        dim.Children.Add(K.SettingRow(L.T("رنگ گرم (نور آبی کمتر، برای چشم راحت‌تر)", "Warm tint (less blue light, easier on the eyes)"), _warm));
        dim.Children.Add(K.SettingRow(L.T("خودکار توی این بازه‌ی شب کم‌نور شه", "Dim by itself during this night window"), _auto));
        var window = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        window.Children.Add(GroupB.Field(L.T("از ساعت", "From"), GroupB.TimePair(_fromH, _fromM), 28));
        window.Children.Add(GroupB.Field(L.T("تا ساعت", "To"), GroupB.TimePair(_toH, _toM), 0));
        dim.Children.Add(window);
        TimeBoxes.Wire(_fromH, _fromM);
        TimeBoxes.Wire(_toH, _toM);

        // ---- shortcuts ----------------------------------------------------------------------------
        var (keyCard, keys) = Ui2.Card(L.T("میانبرها", "Shortcuts"),
            L.T("کلیک کن و ترکیب جدید رو بزن؛ Backspace خاموش می‌کنه", "Click and press the new keys; Backspace turns one off"));
        var firstKey = K.SettingRow(L.T("روشن / خاموش", "On / off"), _toggleKey, first: true);
        firstKey.Margin = new Thickness(0, 8, 0, 0);
        keys.Children.Add(firstKey);
        keys.Children.Add(K.SettingRow(L.T("تیره‌تر", "Darker"), _darkerKey));
        keys.Children.Add(K.SettingRow(L.T("روشن‌تر", "Lighter"), _lighterKey));
        _keyNote.Style = St("Label");
        _keyNote.TextWrapping = TextWrapping.Wrap;
        _keyNote.Margin = new Thickness(0, 8, 0, 0);
        keys.Children.Add(_keyNote);
        root.Add(Column(dimCard, keyCard), 0, 0, 7);

        // ---- break reminders ----------------------------------------------------------------------
        var (breakCard, brk) = Ui2.Card(L.T("یادآور استراحت", "Break reminders"),
            L.T("اختیاریه و پیش‌فرض خاموشه. فقط وقتی پشت کامپیوتر باشی حساب می‌کنه؛ پنج دقیقه دور بشی، شمارش از اول شروع می‌شه.",
                "Optional and off by default. It only counts time you are actually at the PC; five minutes away start the count again."));
        var breakRow = K.SettingRow(L.T("یادآور استراحت روشن باشه", "Remind me to take breaks"), _breakOn, first: true);
        breakRow.Margin = new Thickness(0, 8, 0, 0);
        brk.Children.Add(breakRow);
        var every = Ui2.SliderRow(L.T("هر چند دقیقه", "Every"), 15, 120, 5, v => L.T($"{Fa.Digits(((int)v).ToString())} دقیقه", $"{(int)v} min"));
        (_breakEvery, _breakValue) = (every.slider, every.value);
        every.row.Margin = new Thickness(0, 8, 0, 6);
        brk.Children.Add(every.row);
        brk.Children.Add(K.SettingRow(L.T("وسط بازی هم یادآوری کن (وگرنه بعد از بازی)", "Remind me during games too (otherwise after the game)"), _breakGame));
        var test = new Button { Style = St("Btn"), Content = GroupB.IconText("bell", L.T("یه نمونه نشونم بده", "Show me a sample")), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        test.Click += (_, _) => Hub.Notify(L.T("وقت یه استراحته", "Break time"), (L.En ? BreakPlanner.En : BreakPlanner.Fa)[new Random().Next(BreakPlanner.Fa.Length)]);
        brk.Children.Add(test);

        // ---- sleep mode ---------------------------------------------------------------------------
        var (sleepCard, sleep) = Ui2.Card(L.T("حالت شب‌خواب", "Sleep mode"),
            L.T("وقتی می‌خوای بخوابی شروعش کن: توی مدتی که تعیین می‌کنی صدا و نور صفحه کم‌کم کم می‌شن و آخرش کامپیوتر خاموش (یا هر کار دیگه‌ای که انتخاب کردی) می‌شه.",
                "Start it when you go to bed: over the time you choose the volume and the screen fade out slowly, then the PC switches off (or does what you picked)."));
        var minutes = Ui2.SliderRow(L.T("بعد از", "After"), 5, 180, 5, v => L.T($"{Fa.Digits(((int)v).ToString())} دقیقه", $"{(int)v} min"));
        (_sleepMinutes, _sleepValue) = (minutes.slider, minutes.value);
        minutes.row.Margin = new Thickness(0, 14, 0, 0);
        sleep.Children.Add(minutes.row);
        sleep.Children.Add(Ui2.Label(L.T("در پایان", "At the end"), 14, 6));
        sleep.Children.Add(_sleepActions);
        var sleepDimRow = K.SettingRow(L.T("نور صفحه کم‌کم کم بشه", "Fade the screen out"), _sleepDim);
        sleepDimRow.Margin = new Thickness(0, 8, 0, 0);
        sleep.Children.Add(sleepDimRow);
        sleep.Children.Add(K.SettingRow(L.T("صدا کم‌کم کم بشه", "Fade the volume out"), _sleepVolume));
        _sleepStart.Style = St("BtnPrimary");
        _sleepStart.HorizontalAlignment = HorizontalAlignment.Left;
        _sleepStart.Padding = new Thickness(20, 0, 20, 0);
        _sleepStart.Height = 40;
        _sleepStart.Margin = new Thickness(0, 16, 0, 0);
        _sleepStart.Click += (_, _) => OnSleepButton();
        sleep.Children.Add(_sleepStart);
        _sleepStatus.Style = St("Label");
        _sleepStatus.TextWrapping = TextWrapping.Wrap;
        _sleepStatus.Margin = new Thickness(0, 8, 0, 0);
        sleep.Children.Add(_sleepStatus);
        root.Add(Column(breakCard, sleepCard), 0, 7, 5);

        // ---- game mode ----------------------------------------------------------------------------
        var (gameCard, game) = Ui2.Card(L.T("حالت بازی", "Game mode"),
            L.T("وقتی بازی شروع می‌شه خودش حواسش به چند چیز هست و وقتی تموم شد همه‌چی رو برمی‌گردونه.",
                "When a game starts it takes care of a few things, and puts everything back when the game is over."));
        var gameOnRow = K.SettingRow(L.T("حالت بازی روشن باشه", "Game mode on"), _gameOn, first: true);
        gameOnRow.Margin = new Thickness(0, 8, 0, 0);
        game.Children.Add(gameOnRow);
        foreach (var (box, fa, en) in new[]
        {
            (_gamePower, "برق روی بهترین عملکرد (بعد از بازی برمی‌گرده)", "Best-performance power plan (put back afterwards)"),
            (_gameRam, "رم خالی بشه", "Free up RAM"),
            (_gameQuiet, "اعلان‌های شب‌پا وسط بازی نیان", "Keep Shabpa's notices quiet during the game"),
        })
        {
            // the three options belong to the master switch: indented from the start edge
            var row = K.SettingRow(L.T(fa, en), box);
            row.Margin = new Thickness(18, 0, 0, 0);
            game.Children.Add(row);
        }
        game.Children.Add(Ui2.Label(L.T("اگه بازی رو همین شب‌پا تشخیص بده، شروعش خودکار شناخته می‌شه (همون تشخیصی که اورلی و گزارش بازی استفاده می‌کنن).",
            "Starts when Shabpa detects a game (the same detection the overlay and game report use)."), 10, 0));
        root.Add(gameCard, 1, 0, 12);

        Content = root;

        Chips(_sleepActions, new[]
        {
            (PowerAction.Shutdown, L.T("خاموش کردن", "Shut down")), (PowerAction.Sleep, L.T("خواب", "Sleep")),
            (PowerAction.Hibernate, L.T("هایبرنیت", "Hibernate")), (PowerAction.Lock, L.T("قفل صفحه", "Lock")),
        }, c.SleepAction, v => Edit(s => s.SleepAction = v));

        _dimLevel.ValueChanged += (_, _) => Edit(s => s.DimLevel = _dimLevel.Value);
        _warm.Click += (_, _) => Edit(s => s.DimWarm = _warm.IsChecked == true);
        _auto.Click += (_, _) => Edit(s => s.DimAuto = _auto.IsChecked == true);
        foreach (var box in new[] { _fromH, _fromM, _toH, _toM }) box.LostKeyboardFocus += (_, _) => SaveWindow();
        _toggleKey.Changed += v => Edit(s => s.DimKey = v);
        _darkerKey.Changed += v => Edit(s => s.DimDarkerKey = v);
        _lighterKey.Changed += v => Edit(s => s.DimLighterKey = v);
        _breakOn.Click += (_, _) => Edit(s => s.BreakOn = _breakOn.IsChecked == true);
        _breakEvery.ValueChanged += (_, _) => Edit(s => s.BreakEveryMinutes = (int)_breakEvery.Value);
        _breakGame.Click += (_, _) => Edit(s => s.BreakInGame = _breakGame.IsChecked == true);
        _sleepMinutes.ValueChanged += (_, _) => Edit(s => s.SleepMinutes = (int)_sleepMinutes.Value);
        _sleepDim.Click += (_, _) => Edit(s => s.SleepDim = _sleepDim.IsChecked == true);
        _sleepVolume.Click += (_, _) => Edit(s => s.SleepVolume = _sleepVolume.IsChecked == true);
        _gameOn.Click += (_, _) => Edit(s => s.GameModeOn = _gameOn.IsChecked == true);
        _gamePower.Click += (_, _) => Edit(s => s.GameModePower = _gamePower.IsChecked == true);
        _gameRam.Click += (_, _) => Edit(s => s.GameModeRam = _gameRam.IsChecked == true);
        _gameQuiet.Click += (_, _) => Edit(s => s.GameModeQuiet = _gameQuiet.IsChecked == true);
        Hub.ComfortUpdated += () => Dispatcher.BeginInvoke(Refresh);
    }

    private void Edit(Action<ComfortSettings> change)
    {
        if (_loading) return;
        change(Hub.Settings.Comfort);
        Hub.Store.Save();
        Hub.Dim?.Apply();
        UpdateTexts();
    }

    private void SaveWindow()
    {
        if (_loading) return;
        if (TimeBoxes.TryRead(_fromH, _fromM, out var fh, out var fm) && TimeBoxes.TryRead(_toH, _toM, out var th, out var tm))
            Edit(s => (s.DimFrom, s.DimTo) = ($"{fh:00}:{fm:00}", $"{th:00}:{tm:00}"));
    }

    private void OnSleepButton()
    {
        if (Hub.SleepActive)
        {
            Hub.CancelSleepMode();
            return;
        }
        var c = Hub.Settings.Comfort;
        var end = DateTime.Now.AddMinutes(c.SleepMinutes);
        if (!Ask(L.T("حالت شب‌خواب شروع بشه؟", "Start sleep mode?"),
                L.T($"صدا و نور صفحه تا {Fa.Clock(end)} کم‌کم کم می‌شن و بعدش «{Fa.ActionName(c.SleepAction)}» انجام می‌شه. هر وقت خواستی می‌تونی لغوش کنی.",
                    $"The volume and the screen fade out until {Fa.Clock(end)}, then “{Fa.ActionName(c.SleepAction)}” runs. You can cancel any time."),
                L.T("شروع", "Start"))) return;
        Hub.StartSleepMode();
    }

    public override void Refresh()
    {
        _loading = true;
        var c = Hub.Settings.Comfort;
        _dimLevel.Value = c.DimLevel;
        _warm.IsChecked = c.DimWarm;
        _auto.IsChecked = c.DimAuto;
        var from = ComfortTime.Parse(c.DimFrom) ?? new TimeSpan(22, 0, 0);
        var to = ComfortTime.Parse(c.DimTo) ?? new TimeSpan(7, 0, 0);
        (_fromH.Text, _fromM.Text) = (from.Hours.ToString("00"), from.Minutes.ToString("00"));
        (_toH.Text, _toM.Text) = (to.Hours.ToString("00"), to.Minutes.ToString("00"));
        _toggleKey.Set(c.DimKey);
        _darkerKey.Set(c.DimDarkerKey);
        _lighterKey.Set(c.DimLighterKey);
        _breakOn.IsChecked = c.BreakOn;
        _breakEvery.Value = c.BreakEveryMinutes;
        _breakGame.IsChecked = c.BreakInGame;
        _sleepMinutes.Value = c.SleepMinutes;
        _sleepDim.IsChecked = c.SleepDim;
        _sleepVolume.IsChecked = c.SleepVolume;
        _gameOn.IsChecked = c.GameModeOn;
        _gamePower.IsChecked = c.GameModePower;
        _gameRam.IsChecked = c.GameModeRam;
        _gameQuiet.IsChecked = c.GameModeQuiet;
        _loading = false;
        UpdateTexts();
    }

    public override void OnSecond()
    {
        if (IsVisible && Hub.SleepActive) UpdateTexts();
    }

    private void UpdateTexts()
    {
        var c = Hub.Settings.Comfort;
        var active = Hub.Dim?.Active == true;
        _dimStatus.Text = active
            ? L.T($"صفحه الان کم‌نوره ({Fa.Digits(((int)Math.Round(c.DimLevel * 100)).ToString())}٪).", $"The screen is dimmed ({c.DimLevel * 100:0}%).")
            : L.T("صفحه الان عادیه.", "The screen is normal right now.");
        _dimBadge.Tint(active ? "HueC" : "HueA");
        if (_wasDimmed is { } before && before != active)
        {
            // the screen was just dimmed or released: badge, text and button answer with a short spring
            Motion.Pop(_dimBadge);
            Motion.Pop(_dimToggle);
            GroupB.Appear(_dimStatus);
        }
        _wasDimmed = active;
        _dimToggle.Content = active ? L.T("خاموش کردن کم‌نوری", "Turn dimming off") : L.T("کم‌نور کن", "Dim now");
        _keyNote.Text = Hub.Dim is { FailedHotkeys.Count: > 0 } d
            ? L.T($"این میانبرها رو برنامه‌ی دیگه‌ای گرفته: {string.Join("، ", d.FailedHotkeys)}. یه ترکیب دیگه انتخاب کن.",
                $"Another program already uses: {string.Join(", ", d.FailedHotkeys)}. Pick a different combination.")
            : "";
        _keyNote.Foreground = Res("Gold");
        _keyNote.Visibility = _keyNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var sleeping = Hub.SleepActive && Hub.SleepEndsAt is not null;
        if (_wasSleeping is { } slept && slept != sleeping)
        {
            Motion.Pop(_sleepStart);
            if (sleeping) GroupB.Appear(_sleepStatus);
        }
        _wasSleeping = sleeping;
        if (Hub.SleepActive && Hub.SleepEndsAt is { } end)
        {
            _sleepStart.Content = L.T("لغو حالت شب‌خواب", "Cancel sleep mode");
            _sleepStart.Style = St("Btn");
            var left = end - DateTime.Now;
            _sleepStatus.Text = L.T($"در حال کم شدن… {Fa.Duration(left < TimeSpan.Zero ? TimeSpan.Zero : left)} دیگه تا «{Fa.ActionName(c.SleepAction)}».",
                $"Fading out… {Fa.Duration(left < TimeSpan.Zero ? TimeSpan.Zero : left)} until “{Fa.ActionName(c.SleepAction)}”.");
        }
        else
        {
            _sleepStart.Content = L.T("شروع حالت شب‌خواب", "Start sleep mode");
            _sleepStart.Style = St("BtnPrimary");
            _sleepStatus.Text = "";
        }
        _sleepStatus.Visibility = _sleepStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
