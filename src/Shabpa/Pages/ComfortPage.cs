using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Eye comfort and healthy habits: a screen dimmer that goes darker than the monitor's own buttons, optional
/// break reminders, and a "going to sleep" mode that fades the volume and the screen out before switching off.</summary>
public sealed class ComfortPage : PageBase
{
    private bool _loading;
    private readonly TextBlock _dimStatus = new();
    private readonly Button _dimToggle = new();
    private Slider _dimLevel = null!, _breakEvery = null!, _sleepMinutes = null!;
    private TextBlock _dimLevelValue = null!, _breakValue = null!, _sleepValue = null!;
    private readonly CheckBox _warm = new(), _auto = new(), _breakOn = new(), _breakGame = new(), _sleepDim = new(), _sleepVolume = new();
    private readonly TextBox _fromH = Time(), _fromM = Time(), _toH = Time(), _toM = Time();
    private readonly HotkeyBox _toggleKey = new(), _darkerKey = new(), _lighterKey = new();
    private readonly TextBlock _keyNote = new();
    private readonly WrapPanel _sleepActions = new();
    private readonly Button _sleepStart = new();
    private readonly TextBlock _sleepStatus = new();
    private readonly CheckBox _gameOn = new(), _gamePower = new(), _gameRam = new(), _gameQuiet = new();

    public override string PageTitle => L.T("آسایش و استراحت", "Comfort & rest");
    public override string PageSubtitle => L.T("کم‌نور کردن صفحه برای شب، یادآور استراحت و حالت شب‌خواب.", "Night dimmer, break reminders and sleep mode.");

    private static TextBox Time() => new()
    {
        Style = (Style)Application.Current.Resources["Input"], Width = 56, FlowDirection = FlowDirection.LeftToRight, MaxLength = 2,
        Padding = new Thickness(8, 6, 8, 6), TextAlignment = TextAlignment.Center, Text = "00",
    };

    private static CheckBox Check(string text) => new() { Style = St("Check"), Content = text, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };

    private static FrameworkElement TimeRow(string label, TextBox hour, TextBox minute)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 4, 18, 0) };
        row.Children.Add(new TextBlock { Text = ":", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        row.Children.Insert(0, hour);
        row.Children.Add(minute);
        var cell = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        cell.Children.Add(new TextBlock { Text = label, Style = St("Label") });
        cell.Children.Add(row);
        return cell;
    }

    public ComfortPage()
    {
        var root = new StackPanel();
        var c = Hub.Settings.Comfort;

        // ---- dim screen ---------------------------------------------------------------------------
        var (dimCard, dim) = Ui2.Card(L.T("کم‌نور کردن صفحه", "Dim screen"),
            L.T("یه لایه‌ی تیره روی همه‌ی مانیتورها می‌ندازه و نور صفحه رو از حدی که دکمه‌های خود مانیتور اجازه می‌دن هم کمتر می‌کنه. کلیک و بازی از زیرش عادی کار می‌کنن.",
                "Puts a dark layer over every monitor, so the screen gets dimmer than the monitor's own buttons allow. Clicks and games work normally underneath."));
        _dimStatus.Style = St("Label");
        _dimStatus.Margin = new Thickness(0, 10, 0, 0);
        dim.Children.Add(_dimStatus);
        _dimToggle.Style = St("BtnPrimary");
        _dimToggle.HorizontalAlignment = HorizontalAlignment.Left;
        _dimToggle.Padding = new Thickness(18, 9, 18, 9);
        _dimToggle.Margin = new Thickness(0, 10, 0, 0);
        _dimToggle.Click += (_, _) => Hub.Dim?.Toggle();
        dim.Children.Add(_dimToggle);
        var level = Ui2.SliderRow(L.T("میزان تیرگی", "Darkness"), 0.05, 0.92, 0.01, v => $"{v * 100:0}%");
        (_dimLevel, _dimLevelValue) = (level.slider, level.value);
        dim.Children.Add(level.row);
        _warm.Style = St("Check");
        _warm.Content = L.T("رنگ گرم (نور آبی کمتر، برای چشم راحت‌تر)", "Warm tint (less blue light, easier on the eyes)");
        _warm.Margin = new Thickness(0, 10, 0, 0);
        dim.Children.Add(_warm);
        _auto.Style = St("Check");
        _auto.Content = L.T("خودکار توی این بازه‌ی شب کم‌نور شه", "Dim by itself during this night window");
        _auto.Margin = new Thickness(0, 10, 0, 0);
        dim.Children.Add(_auto);
        var window = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        window.Children.Add(TimeRow(L.T("از ساعت", "From"), _fromH, _fromM));
        window.Children.Add(TimeRow(L.T("تا ساعت", "To"), _toH, _toM));
        dim.Children.Add(window);
        TimeBoxes.Wire(_fromH, _fromM);
        TimeBoxes.Wire(_toH, _toM);
        dim.Children.Add(Ui2.Label(L.T("میانبرها (کلیک کن و ترکیب جدید رو بزن؛ Backspace خاموش می‌کنه)", "Shortcuts (click and press the new keys; Backspace turns one off)"), 14, 4));
        var keys = new WrapPanel();
        void Key(string label, HotkeyBox box)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            cell.Children.Add(new TextBlock { Text = label, Style = St("Label") });
            box.Margin = new Thickness(0, 4, 0, 0);
            cell.Children.Add(box);
            keys.Children.Add(cell);
        }
        Key(L.T("روشن / خاموش", "On / off"), _toggleKey);
        Key(L.T("تیره‌تر", "Darker"), _darkerKey);
        Key(L.T("روشن‌تر", "Lighter"), _lighterKey);
        dim.Children.Add(keys);
        _keyNote.Style = St("Label");
        _keyNote.TextWrapping = TextWrapping.Wrap;
        _keyNote.Margin = new Thickness(0, 8, 0, 0);
        dim.Children.Add(_keyNote);
        root.Children.Add(dimCard);

        // ---- break reminders ----------------------------------------------------------------------
        var (breakCard, brk) = Ui2.Card(L.T("یادآور استراحت", "Break reminders"),
            L.T("اختیاریه و پیش‌فرض خاموشه. فقط وقتی پشت کامپیوتر باشی حساب می‌کنه؛ پنج دقیقه دور بشی، شمارش از اول شروع می‌شه.",
                "Optional and off by default. It only counts time you are actually at the PC; five minutes away start the count again."));
        _breakOn.Style = St("Check");
        _breakOn.Content = L.T("یادآور استراحت روشن باشه", "Remind me to take breaks");
        _breakOn.Margin = new Thickness(0, 10, 0, 0);
        brk.Children.Add(_breakOn);
        var every = Ui2.SliderRow(L.T("هر چند دقیقه", "Every"), 15, 120, 5, v => L.T($"{Fa.Digits(((int)v).ToString())} دقیقه", $"{(int)v} min"));
        (_breakEvery, _breakValue) = (every.slider, every.value);
        brk.Children.Add(every.row);
        _breakGame.Style = St("Check");
        _breakGame.Content = L.T("وسط بازی هم یادآوری کن (وگرنه بعد از بازی)", "Remind me during games too (otherwise after the game)");
        _breakGame.Margin = new Thickness(0, 10, 0, 0);
        brk.Children.Add(_breakGame);
        var test = new Button { Style = St("Btn"), Content = L.T("یه نمونه نشونم بده", "Show me a sample"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        test.Click += (_, _) => Hub.Notify(L.T("وقت یه استراحته", "Break time"), (L.En ? BreakPlanner.En : BreakPlanner.Fa)[new Random().Next(BreakPlanner.Fa.Length)]);
        brk.Children.Add(test);
        root.Children.Add(breakCard);

        // ---- sleep mode ---------------------------------------------------------------------------
        var (sleepCard, sleep) = Ui2.Card(L.T("حالت شب‌خواب", "Sleep mode"),
            L.T("وقتی می‌خوای بخوابی شروعش کن: توی مدتی که تعیین می‌کنی صدا و نور صفحه کم‌کم کم می‌شن و آخرش کامپیوتر خاموش (یا هر کار دیگه‌ای که انتخاب کردی) می‌شه.",
                "Start it when you go to bed: over the time you choose the volume and the screen fade out slowly, then the PC switches off (or does what you picked)."));
        var minutes = Ui2.SliderRow(L.T("بعد از", "After"), 5, 180, 5, v => L.T($"{Fa.Digits(((int)v).ToString())} دقیقه", $"{(int)v} min"));
        (_sleepMinutes, _sleepValue) = (minutes.slider, minutes.value);
        sleep.Children.Add(minutes.row);
        sleep.Children.Add(Ui2.Label(L.T("در پایان", "At the end"), 10, 4));
        sleep.Children.Add(_sleepActions);
        _sleepDim.Style = St("Check");
        _sleepDim.Content = L.T("نور صفحه کم‌کم کم بشه", "Fade the screen out");
        _sleepDim.Margin = new Thickness(0, 10, 0, 0);
        _sleepVolume.Style = St("Check");
        _sleepVolume.Content = L.T("صدا کم‌کم کم بشه", "Fade the volume out");
        _sleepVolume.Margin = new Thickness(0, 6, 0, 0);
        sleep.Children.Add(_sleepDim);
        sleep.Children.Add(_sleepVolume);
        _sleepStart.Style = St("BtnPrimary");
        _sleepStart.HorizontalAlignment = HorizontalAlignment.Left;
        _sleepStart.Padding = new Thickness(18, 9, 18, 9);
        _sleepStart.Margin = new Thickness(0, 14, 0, 0);
        _sleepStart.Click += (_, _) => OnSleepButton();
        sleep.Children.Add(_sleepStart);
        _sleepStatus.Style = St("Label");
        _sleepStatus.TextWrapping = TextWrapping.Wrap;
        _sleepStatus.Margin = new Thickness(0, 8, 0, 0);
        sleep.Children.Add(_sleepStatus);
        root.Children.Add(sleepCard);

        // ---- game mode ----------------------------------------------------------------------------
        var (gameCard, game) = Ui2.Card(L.T("حالت بازی", "Game mode"),
            L.T("وقتی بازی شروع می‌شه خودش حواسش به چند چیز هست و وقتی تموم شد همه‌چی رو برمی‌گردونه.",
                "When a game starts it takes care of a few things, and puts everything back when the game is over."));
        _gameOn.Style = St("Check");
        _gameOn.Content = L.T("حالت بازی روشن باشه", "Game mode on");
        _gameOn.Margin = new Thickness(0, 10, 0, 0);
        game.Children.Add(_gameOn);
        foreach (var (box, fa, en) in new[]
        {
            (_gamePower, "برق روی بهترین عملکرد (بعد از بازی برمی‌گرده)", "Best-performance power plan (put back afterwards)"),
            (_gameRam, "رم خالی بشه", "Free up RAM"),
            (_gameQuiet, "اعلان‌های شب‌پا وسط بازی نیان", "Keep Shabpa's notices quiet during the game"),
        })
        {
            box.Style = St("Check");
            box.Content = L.T(fa, en);
            box.Margin = new Thickness(18, 6, 0, 0);
            game.Children.Add(box);
        }
        game.Children.Add(Ui2.Label(L.T("اگه بازی رو همین شب‌پا تشخیص بده، شروعش خودکار شناخته می‌شه (همون تشخیصی که اورلی و گزارش بازی استفاده می‌کنن).",
            "Starts when Shabpa detects a game (the same detection the overlay and game report use)."), 8, 0));
        root.Children.Add(gameCard);

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
        _dimToggle.Content = active ? L.T("خاموش کردن کم‌نوری", "Turn dimming off") : L.T("کم‌نور کن", "Dim now");
        _keyNote.Text = Hub.Dim is { FailedHotkeys.Count: > 0 } d
            ? L.T($"این میانبرها رو برنامه‌ی دیگه‌ای گرفته: {string.Join("، ", d.FailedHotkeys)}. یه ترکیب دیگه انتخاب کن.",
                $"Another program already uses: {string.Join(", ", d.FailedHotkeys)}. Pick a different combination.")
            : "";
        _keyNote.Foreground = Res("Gold");

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
    }
}
