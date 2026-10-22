using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Home in the "Glass Depth" design: header, hero (countdown ring + schedule), plan timeline, four live stat cards
/// with sparklines, quick actions. It is also the reference port: everything is built from the shared kit.</summary>
public sealed class HomePage : PageBase
{
    private PowerAction _action = PowerAction.Shutdown;
    private int _delayMinutes = 60;
    private DateTime? _onSince;
    private string _ramNote = "";
    private bool _acquired;
    private int _seconds;

    // header
    private readonly StatusBadge _live = new(L.T("زنده", "Live"), "Green", "ecg");
    private string _title = "", _sub = "";

    // hero
    private readonly RingGauge _ring = new() { Width = 276, Height = 276 };
    private readonly TextBlock _ringLabel = K.Text(L.T("زمان باقی‌مانده", "Time remaining"), "Label", 12);
    private readonly TextBlock _clock = new() { FontSize = 38, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 46, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _clockAt = K.Text("", "Label", 12);
    private readonly TextBlock _eyebrow = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _heroTitle = new() { FontSize = 25, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, LineHeight = 38, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 2, 0, 4) };
    private readonly TextBlock _heroSub = K.Text("", "Body");
    private DateTime? _heroDue;
    private double _heroFraction = -1;

    // plan timeline
    private readonly StackPanel _plan = new();
    private readonly List<(TextBlock label, DateTime due)> _countdowns = [];
    private readonly Dictionary<string, Grid> _planRows = [];
    private HashSet<string> _planKeys = [];
    private bool _planBuilt;
    private ProgressBar? _ruleBar;
    private TextBlock? _ruleText;

    // stats
    private readonly StatCard _cpu = new(L.T("پردازنده", "Processor"), "cpu", "HueA");
    private readonly StatCard _ram = new(L.T("حافظه", "Memory"), "ram", "HueB");
    private readonly StatCard _gpu = new(L.T("کارت گرافیک", "Graphics card"), "temp", "HueC");
    private readonly StatCard _net = new(L.T("شبکه", "Network"), "net", "HueD");
    private readonly Dictionary<string, List<double>> _history = new() { ["cpu"] = [], ["ram"] = [], ["gpu"] = [], ["net"] = [] };

    // quick actions
    private QuickCard? _ramCard;
    private readonly TextBlock _today = K.Text("", "Tip");

    private readonly Grid _root = new() { Margin = new Thickness(-8, 0, -8, 0) };
    private FrameworkElement _hero = null!, _planCard = null!, _quick = null!;
    private Grid _quickGrid = null!;
    private readonly List<FrameworkElement> _quickCards = [];
    private bool? _wide;

    public override string PageTitle => _title;
    public override string PageSubtitle => _sub;
    public override UIElement? HeaderAccessory => _live;

    public HomePage()
    {
        for (var i = 0; i < 6; i++) _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < 12; i++) _root.ColumnDefinitions.Add(new ColumnDefinition());

        _hero = BuildHero();
        _planCard = BuildPlan();
        _root.Children.Add(_hero);
        _root.Children.Add(_planCard);
        foreach (var card in new[] { _cpu, _ram, _gpu, _net })
        {
            card.Margin = new Thickness(8);
            _root.Children.Add(card);
        }
        _quick = BuildQuick();
        _root.Children.Add(_quick);
        _today.Margin = new Thickness(8, 6, 8, 0);
        _root.Children.Add(_today);
        Content = _root;

        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 900, e.NewSize.Width > 1180);
        ApplyLayout(true, true);
        SeedDemoHistory();

        IsVisibleChanged += (_, _) => OnVisible();
        While<Action<MemoryResult, bool>>(h => Hub.MemoryCleaned += h, h => Hub.MemoryCleaned -= h, (result, cache) =>
        {
            if (_ramCard is not null) { _ramCard.IsEnabled = true; _ramCard.Opacity = 1; }
            if (cache) return;
            _ramNote = L.T($"{Fa.Bytes(result.Freed)} رم آزاد شد.", $"{Fa.Bytes(result.Freed)} of RAM freed.");
            Refresh();
        });

        // When the machine last came on: boot or resume, whichever is later.
        Task.Run(() =>
        {
            var last = PowerHistory.Read(DateTime.Now.AddDays(-14)).LastOrDefault(e => e.TurnsOn);
            Dispatcher.BeginInvoke(() =>
            {
                _onSince = last?.Time ?? DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
                OnSecond();
            });
        });
        UpdateTitles(DateTime.Now);
    }

    // ---- layout ---------------------------------------------------------------------------------------

    private static void Place(UIElement e, int row, int column, int span)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
    }

    private bool? _fiveQuick;

    private void ApplyLayout(bool wide, bool fiveQuick)
    {
        if (_wide == wide && _fiveQuick == fiveQuick) return;
        _wide = wide;
        _fiveQuick = fiveQuick;
        if (wide)
        {
            Place(_hero, 0, 0, 8);
            Place(_planCard, 0, 8, 4);
            Place(_cpu, 1, 0, 3); Place(_ram, 1, 3, 3); Place(_gpu, 1, 6, 3); Place(_net, 1, 9, 3);
            Place(_quick, 2, 0, 12);
            Place(_today, 3, 0, 12);
        }
        else
        {
            Place(_hero, 0, 0, 12);
            Place(_planCard, 1, 0, 12);
            Place(_cpu, 2, 0, 6); Place(_ram, 2, 6, 6); Place(_gpu, 3, 0, 6); Place(_net, 3, 6, 6);
            Place(_quick, 4, 0, 12);
            Place(_today, 5, 0, 12);
        }
        // quick actions: five across, or three
        _quickPer = wide && fiveQuick ? 5 : 3; // five long Persian titles do not fit five across below ~1180 px
        LayoutQuick();
    }

    private int _quickPer = 5;

    private void LayoutQuick()
    {
        var per = _quickPer;
        _quickGrid.ColumnDefinitions.Clear();
        _quickGrid.RowDefinitions.Clear();
        for (var c = 0; c < per; c++) _quickGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < (_quickCards.Count + per - 1) / per; r++) _quickGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < _quickCards.Count; i++)
        {
            Grid.SetRow(_quickCards[i], i / per);
            Grid.SetColumn(_quickCards[i], i % per);
        }
    }

    // ---- hero -----------------------------------------------------------------------------------------

    private FrameworkElement BuildHero()
    {
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition());

        // soft accent bloom and a big faint moon in the corner; both are clipped to the rounded card
        var decor = new Grid { IsHitTestVisible = false };
        decor.Children.Add(new Border { Background = Res("HeroGlow") });
        var watermark = new Icon("moon", 300, Res("Accent"), 0) { Opacity = 0.06, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -60, -90), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-12) };
        watermark.Weight = 0.9;
        decor.Children.Add(watermark);

        // ring with the countdown inside
        var ringBox = new Grid { Width = 276, Height = 276, Margin = new Thickness(0, 0, 30, 0), VerticalAlignment = VerticalAlignment.Center };
        ringBox.Children.Add(_ring);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _ringLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _clockAt.HorizontalAlignment = HorizontalAlignment.Center;
        _clock.Foreground = Res("Text");
        mid.Children.Add(_ringLabel);
        mid.Children.Add(_clock);
        mid.Children.Add(_clockAt);
        ringBox.Children.Add(mid);
        inner.Children.Add(ringBox);

        // text, schedule choice, buttons
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        var eyebrow = new StackPanel { Orientation = Orientation.Horizontal };
        eyebrow.Children.Add(new Icon("moon", 16, Res("Accent")) { Margin = new Thickness(0, 0, 8, 0) });
        _eyebrow.Foreground = Res("Accent");
        eyebrow.Children.Add(_eyebrow);
        info.Children.Add(eyebrow);
        _heroTitle.Foreground = Res("Text");
        info.Children.Add(_heroTitle);
        info.Children.Add(_heroSub);

        var choices = new WrapPanel { Margin = new Thickness(0, 14, 0, 14) };
        var actions = new Segmented([L.T("خاموش", "Shut down"), L.T("خواب", "Sleep"), L.T("هایبرنیت", "Hibernate")], 0) { Margin = new Thickness(0, 0, 10, 6) };
        PowerAction[] map = [PowerAction.Shutdown, PowerAction.Sleep, PowerAction.Hibernate];
        actions.Changed += i => _action = map[i];
        var delays = new Segmented([L.T("۳۰ دقیقه", "30 min"), L.T("۱ ساعت", "1 hour"), L.T("۲ ساعت", "2 hours")], 1) { Margin = new Thickness(0, 0, 0, 6) };
        int[] minutes = [30, 60, 120];
        delays.Changed += i => _delayMinutes = minutes[i];
        choices.Children.Add(actions);
        choices.Children.Add(delays);
        info.Children.Add(choices);

        var buttons = new WrapPanel { Margin = new Thickness(-7, 0, -7, 0) }; // each button carries 7 px per side (margins are physical, so this holds in RTL too)
        var confirm = new Button { Style = St("BtnPrimary"), Padding = new Thickness(22, 0, 22, 0), Height = 42, Margin = new Thickness(7, 0, 7, 8) };
        confirm.Content = ButtonContent("pw", L.T("تأیید و زمان‌بندی", "Confirm & schedule"));
        confirm.Click += (_, _) => Schedule(_delayMinutes);
        var change = new Button { Style = St("Btn"), Padding = new Thickness(22, 0, 22, 0), Height = 42, Margin = new Thickness(7, 0, 7, 8) };
        change.Content = L.T("تغییر زمان", "Change time");
        change.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.Navigate("system");
        buttons.Children.Add(confirm);
        buttons.Children.Add(change);
        info.Children.Add(buttons);
        inner.Children.Add(info);

        var card = new GlassCard { Padding = new Thickness(0), Background = Res("HeroGradient"), Margin = new Thickness(8) };
        inner.Margin = new Thickness(30, 26, 30, 26);
        // the decor reaches the card edges: it lives in an outer grid that is clipped to the rounded corners
        var outer = new Grid();
        outer.Children.Add(decor);
        outer.Children.Add(inner);
        outer.SizeChanged += (_, e) => outer.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 17, 17);
        card.Child = outer;
        return card;
    }

    private static UIElement ButtonContent(string icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon(icon, 18, null) { Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    // ---- plan timeline --------------------------------------------------------------------------------

    private FrameworkElement BuildPlan()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = L.T("بعدی چیه؟", "What is next?"), Style = St("H2") });
        body.Children.Add(_plan);
        return new GlassCard { Padding = new Thickness(24, 22, 24, 22), Margin = new Thickness(8), Child = body };
    }

    /// <summary>One timeline entry: icon dot with the connector below it, bold title, small time line, optional end element.</summary>
    private static Grid Event(string icon, bool next, string title, string sub, bool last, UIElement? trailing = null, TextBlock? subBlock = null)
    {
        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!last)
            row.Children.Add(new Border { Width = 2, Margin = new Thickness(0, 32, 0, -18), HorizontalAlignment = HorizontalAlignment.Center, Background = Res("Line") });
        var dot = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
        if (next)
        {
            dot.Background = Res("AccentGradient");
            dot.Child = new Icon(icon, 18, Res("White")) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            dot.Background = Res("AccentSoft");
            dot.Child = new Icon(icon, 18, Res("Accent")) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        row.Children.Add(dot);
        var texts = new StackPanel { Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(texts, 1);
        texts.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Res("Text") });
        var s = subBlock ?? new TextBlock { Text = sub, Style = St("Label"), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        texts.Children.Add(s);
        row.Children.Add(texts);
        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            if (trailing is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(trailing);
        }
        return row;
    }

    private int _fixedQuick;
    private string _frequentKeys = "";

    /// <summary>The most opened pages (counted locally, never sent) sit after the fixed quick actions.</summary>
    private void SyncFrequent()
    {
        var top = PageUse.Top(Hub.State.PageVisits);
        var keys = string.Join(',', top.Select(t => t.Key));
        if (keys == _frequentKeys) return;
        _frequentKeys = keys;
        foreach (var old in _quickCards.Skip(_fixedQuick).ToList()) { _quickCards.Remove(old); _quickGrid.Children.Remove(old); }
        foreach (var t in top)
        {
            var key = t.Key;
            var card = new QuickCard(t.Icon, t.Hue, L.T(t.Fa, t.En), L.T("پراستفاده", "Often used"), () => (Application.Current.MainWindow as MainWindow)?.Navigate(key)) { Margin = new Thickness(8) };
            _quickCards.Add(card);
            _quickGrid.Children.Add(card);
        }
        LayoutQuick();
    }

    // ---- quick actions --------------------------------------------------------------------------------

    private FrameworkElement BuildQuick()
    {
        var panel = new StackPanel { Margin = new Thickness(8, 6, 8, 0) };
        panel.Children.Add(new TextBlock { Text = L.T("اقدام سریع", "Quick actions"), Style = St("H2") });
        _quickGrid = new Grid { Margin = new Thickness(-8, 4, -8, 0) };
        void Add(QuickCard card)
        {
            card.Margin = new Thickness(8, 8, 8, 8);
            _quickCards.Add(card);
            _quickGrid.Children.Add(card);
        }
        Add(new QuickCard("lock", "HueD", L.T("قفل صفحه", "Lock screen"), L.T("همین حالا", "Right now"), OnLock));
        _ramCard = new QuickCard("brush", "HueC", L.T("خالی کردن رم", "Free up RAM"), L.T("حافظه‌ی بلااستفاده", "Unused memory"), OnFreeRam);
        Add(_ramCard);
        Add(new QuickCard("dl", "HueB", L.T("وقتی دانلود تموم شد", "After the download"), L.T("خاموشی خودکار", "Automatic shutdown"), OnAfterDownload));
        Add(new QuickCard("clk", "HueA", L.T("وقتی بی‌استفاده موند", "When idle"), L.T("بعد از ۳۰ دقیقه", "After 30 minutes"), OnWhenIdle));
        Add(new QuickCard("mon", "HueE", L.T("اورلی بازی", "Game overlay"), L.T("نمایش و تنظیم", "Show and tune"), () => (Application.Current.MainWindow as MainWindow)?.Navigate("gaming")));
        panel.Children.Add(_quickGrid);
        _fixedQuick = _quickCards.Count;
        SyncFrequent();
        return panel;
    }

    // ---- data -----------------------------------------------------------------------------------------

    private void UpdateTitles(DateTime now)
    {
        var greeting = Hub.BirthdayToday ? Birthday.HomeLine(Hub.Settings.Birthday.Name) : Friendly.Greeting(now, Hub.Settings.Birthday.Name) ?? Greetings.Pick(now);
        var date = L.T($"{Fa.DayName(now.DayOfWeek)} {Fa.Date(now)} · ساعت {Fa.Clock(now)}", $"{Fa.DayName(now.DayOfWeek)}, {Fa.Date(now)} · {Fa.Clock(now)}");
        var up = _onSince is { } since ? L.T($" — از آخرین روشن‌شدن {Fa.Duration(now - since)}", $" — up for {Fa.Duration(now - since)}") : "";
        var sub = date + up;
        if (greeting == _title && sub == _sub) return;
        var changed = _title.Length > 0;
        _title = greeting;
        _sub = sub;
        if (changed) RaiseHeaderChanged();
    }

    public override void Refresh()
    {
        SyncFrequent();
        var now = DateTime.Now;
        UpdateTitles(now);
        RebuildPlan(now);
        UpdateHero(now);

        var today = Hub.Energy.Summarize(now).Days[^1];
        var energy = today.TotalWh > 1
            ? L.T($"امروز تا الان {Fa.N(today.TotalWh / 1000, 2)} کیلووات‌ساعت مصرف شده، حدود {Fa.N((long)(today.TotalWh / 1000 * Hub.Settings.Tariff))} تومان.",
                $"Today so far: {Fa.N(today.TotalWh / 1000, 2)} kWh, about {Fa.N((long)(today.TotalWh / 1000 * Hub.Settings.Tariff))} Toman.")
            : "";
        _today.Text = string.Join("  ", new[] { _ramNote, energy }.Where(t => t.Length > 0));
        _today.Visibility = _today.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildPlan(DateTime now)
    {
        // rows that were not there before rise in (only once the page is on screen; the page entrance covers the first build)
        var wasKeys = _planKeys;
        var animate = IsLoaded && _planBuilt && Motion.Enabled;
        _planBuilt = true;
        _planKeys = [];
        _planRows.Clear();
        _plan.Children.Clear();
        _countdowns.Clear();
        _ruleBar = null;
        _ruleText = null;
        var entries = new List<(string key, Func<bool, Grid> build)>();
        var first = true;

        foreach (var cmd in Hub.Queue.Items.OrderBy(c => c.DueAt).Take(4))
        {
            var id = cmd.Id;
            var due = cmd.DueAt;
            var action = cmd.Action;
            var countdown = new TextBlock { Text = Fa.Countdown(due - now), FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Res("Gold"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FlowDirection = FlowDirection.LeftToRight };
            _countdowns.Add((countdown, due));
            var trailing = new StackPanel { Orientation = Orientation.Horizontal };
            trailing.Children.Add(countdown);
            trailing.Children.Add(IconButton("trash", L.T("حذف", "Delete"), () => DeleteCommand(id, action, due)));
            var isFirst = first;
            var cmdCopy = cmd;
            entries.Add(("cmd:" + id, last => Event(ActionIcon(action), isFirst, Fa.ActionName(action), When(cmdCopy, now), last, trailing)));
            first = false;
        }

        if (Hub.Engine.Rule is { } rule)
        {
            _ruleBar = new ProgressBar { Style = St("Bar"), Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Maximum = 1, Value = Hub.Engine.Status.Progress };
            var trailing = new StackPanel { Orientation = Orientation.Horizontal };
            trailing.Children.Add(_ruleBar);
            trailing.Children.Add(IconButton("x", L.T("غیرفعال کردن", "Turn off"), () => DeleteRule(rule.Action)));
            var statusText = new TextBlock { Text = Hub.Engine.Status.Text, Style = St("Label"), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            _ruleText = statusText;
            var isFirst = first;
            entries.Add(("rule:" + rule.Action, last => Event("bolt", isFirst, L.T($"خاموشی شرطی: {Fa.ActionName(rule.Action)}", $"Conditional rule: {Fa.ActionName(rule.Action)}"), "", last, trailing, statusText)));
            first = false;
        }

        if (Hub.NextWake(now) is { } wake)
        {
            var isFirst = first;
            entries.Add(("wake:" + wake.Ticks, last => Event("alarm", isFirst, L.T("بیدارباش", "Wake-up"), At(wake, now), last)));
        }

        if (entries.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = L.T("هیچ برنامه‌ای در راه نیست. از دکمه‌ی «تأیید و زمان‌بندی» یا «کنترل سیستم» یکی بساز.", "Nothing is scheduled. Create something with “Confirm & schedule” or in “System control”."),
                Style = St("Body"), Margin = new Thickness(0, 12, 0, 0),
            };
            _plan.Children.Add(empty);
            _planKeys.Add("empty");
            if (animate && !wasKeys.Contains("empty")) Motion.Rise(empty, 0, 10, 0.45);
        }
        var riseIndex = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var row = entries[i].build(i == entries.Count - 1);
            _plan.Children.Add(row);
            _planKeys.Add(entries[i].key);
            _planRows[entries[i].key] = row;
            if (animate && !wasKeys.Contains(entries[i].key)) Motion.Rise(row, riseIndex++, 12, 0.5);
        }
    }

    private static string ActionIcon(PowerAction a) => a switch
    {
        PowerAction.Sleep => "moon",
        PowerAction.Hibernate => "moon",
        PowerAction.Lock => "lock",
        _ => "pw",
    };

    private void UpdateHero(DateTime now)
    {
        var next = Hub.Queue.Next;
        if (next is { } cmd)
        {
            _heroDue = cmd.DueAt;
            NetFx.Say(_eyebrow, Fa.DayOf(cmd.DueAt, now), false);
            NetFx.Say(_heroTitle, L.T($"{Fa.ActionName(cmd.Action)} بعدی سیستم", $"Next: {Fa.ActionName(cmd.Action)}"), false);
            NetFx.Say(_heroSub, When(cmd, now), false);
            _clockAt.Text = L.T($"تا ساعت {Fa.Clock(cmd.DueAt)}", $"until {Fa.Clock(cmd.DueAt)}");
            _ringLabel.Text = L.T("زمان باقی‌مانده", "Time remaining");
            _ring.ArcBrush = null;
        }
        else if (Hub.Engine.Rule is { } rule)
        {
            _heroDue = null;
            NetFx.Say(_eyebrow, L.T("قانون فعال", "Active rule"), false);
            NetFx.Say(_heroTitle, L.T($"خاموشی شرطی: {Fa.ActionName(rule.Action)}", $"Conditional rule: {Fa.ActionName(rule.Action)}"), false);
            NetFx.Say(_heroSub, Hub.Engine.Status.Text, false);
            _clock.Text = "—";
            _clockAt.Text = "";
            _ringLabel.Text = L.T("در حال نظارت", "Watching");
            SetFraction(Hub.Engine.Status.Progress);
        }
        else
        {
            _heroDue = null;
            NetFx.Say(_eyebrow, L.T("امشب", "Tonight"), false);
            NetFx.Say(_heroTitle, Friendly.Calm(Hub.Settings.Birthday.Name) ?? L.T("هیچ برنامه‌ای در راه نیست", "Nothing is scheduled"), false);
            NetFx.Say(_heroSub, L.T("یک زمان انتخاب کن و تأیید بزن؛ شب‌پا بقیه‌اش رو انجام می‌ده.", "Pick a time and confirm; Shabpa does the rest."), false);
            _clock.Text = "—";
            _clockAt.Text = "";
            _ringLabel.Text = L.T("زمان باقی‌مانده", "Time remaining");
            SetFraction(0);
        }
        TickHero(now);
    }

    private void SetFraction(double f)
    {
        if (Math.Abs(f - _heroFraction) < 0.0005) return;
        var first = _heroFraction < 0;
        _heroFraction = f;
        _ring.AnimateTo(f, first ? 1600 : 900); // the ring glides to every new value, not only on the first one
    }

    private void TickHero(DateTime now)
    {
        if (_heroDue is not { } due) return;
        var left = due - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        _clock.Text = Fa.Digits($"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}");
        // the ring shows the share of the next five hours that is left; it moves less than a pixel per second, so it updates every 30 s
        if (_seconds % 30 == 0 || _heroFraction < 0) SetFraction(Math.Min(1, left.TotalSeconds / (5 * 3600)));
    }

    /// <summary>«فردا ساعت ۰۱:۳۰» / "Tomorrow at 01:30".</summary>
    internal static string At(DateTime when, DateTime now) =>
        L.T($"{Fa.DayOf(when, now)} ساعت {Fa.Clock(when)}", $"{Fa.DayOf(when, now)} at {Fa.Clock(when)}");

    internal static string When(ScheduledCommand cmd, DateTime now)
    {
        var text = L.T("اجرا: ", "Runs: ") + At(cmd.DueAt, now);
        if (cmd.Repeats) text += L.T($" · تکرار: {Fa.Days(cmd.RepeatDays)}", $" · repeats: {Fa.Days(cmd.RepeatDays)}");
        if (Hub.SourceText(cmd.Source) is { Length: > 0 } source) text += $" · {source}";
        return text;
    }

    public override void OnTick(SensorSnapshot s)
    {
        _live.Set(s.TotalWatts > 0 ? L.T($"زنده · ⁦{s.TotalWatts:0} W⁩", $"Live · {s.TotalWatts:0} W") : L.T("زنده", "Live"), "Green", "ecg");
        ShowLive(false);
        if (_ruleBar is not null)
        {
            _ruleBar.Value = Hub.Engine.Status.Progress;
            if (_ruleText is not null) _ruleText.Text = Hub.Engine.Status.Text;
        }
    }

    private static string CleanGpuName(string name) =>
        string.Join(' ', name.Replace("(R)", "").Replace("(TM)", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("AMD" or "NVIDIA" or "Radeon" or "GeForce" or "Intel" or "Graphics")));

    private static Brush Tint(double value, double warn, double bad) => Res(value >= bad ? "Red" : value >= warn ? "Gold" : "Text");

    private void SeedDemoHistory()
    {
        if (!Hub.Demo) return;
        var rnd = new Random(7);
        double Walk(double v, double lo, double hi, double step) => Math.Clamp(v + (rnd.NextDouble() - 0.5) * step, lo, hi);
        double c = 30, r = 55, g = 64, n = 6;
        for (var i = 0; i < 24; i++)
        {
            _history["cpu"].Add(c = Walk(c, 8, 60, 22));
            _history["ram"].Add(r = Walk(r, 48, 62, 5));
            _history["gpu"].Add(g = Walk(g, 55, 74, 6));
            _history["net"].Add(n = Walk(n, 0.5, 14, 6));
        }
    }

    private void Push(string key, double value)
    {
        var h = _history[key];
        h.Add(value);
        if (h.Count > 24) h.RemoveAt(0);
    }

    /// <summary>The four live cards. The sparklines take a new point every 3 s, like the design.</summary>
    private void ShowLive(bool sample)
    {
        var s = Hub.Demo ? OverlayMetrics.Demo(DateTime.Now) : Hub.Telemetry.Latest;
        var g = s.Gpu;

        // processor
        var temperature = s.CpuTempC;
        if (sample) Push("cpu", s.CpuLoad);
        _cpu.Set(Fa.N(temperature ?? s.CpuLoad, 0), temperature is not null ? "°C" : L.T("٪", "%"), _history["cpu"]);
        _cpu.Value.Foreground = temperature is { } ct ? Tint(ct, 75, 90) : Tint(s.CpuLoad, 75, 92);
        _cpu.Label.Text = HardwareLabel.Title(L.T("پردازنده", "Processor"), HardwareLabel.Cpu(Telemetry.CpuName()));
        _cpu.ToolTip = Telemetry.CpuName() + (s.CpuMHz is { } mhz ? $" · {mhz / 1000:0.0} GHz" : "") + (temperature is not null ? L.T($" · بار {Fa.N(s.CpuLoad, 0)}٪", $" · load {s.CpuLoad:0}%") : "");

        // memory
        var memory = Hub.Demo ? new MemoryInfo(32L << 30, 14L << 30, 0) : Hub.Memory;
        if (memory.Total > 0)
        {
            if (sample) Push("ram", memory.UsedPercent);
            _ram.Set(Fa.N(memory.Used / 1073741824.0, 1), L.T($"گیگ از {Fa.N(memory.Total / 1073741824.0, 0)}", $"GB of {memory.Total / 1073741824.0:0}"), _history["ram"]);
            _ram.Value.Foreground = Tint(memory.UsedPercent, 75, 92);
            _ram.ToolTip = L.T($"{Fa.N(memory.UsedPercent, 0)}٪ استفاده شده", $"{memory.UsedPercent:0}% used");
        }

        // graphics card
        if (g.Name.Length == 0 && !Hub.Demo)
        {
            _gpu.Set("—", "", _history["gpu"]);
            _gpu.ToolTip = L.T("کارت گرافیک خونده نشد", "Graphics card not read");
        }
        else
        {
            var value = g.TempC ?? g.LoadPercent ?? 0;
            if (sample) Push("gpu", value);
            _gpu.Set(Fa.N(value, 0), g.TempC is not null ? "°C" : L.T("٪", "%"), _history["gpu"]);
            _gpu.Value.Foreground = g.TempC is { } temp ? Tint(temp, 75, 88) : Res("Text");
            _gpu.Label.Text = HardwareLabel.Title(L.T("کارت گرافیک", "Graphics card"), HardwareLabel.Gpu(g.Name));
            _gpu.ToolTip = CleanGpuName(g.Name) + (g.VramTotalMb is { } vram ? $" · {vram / 1024:0} GB" : "") + (g.TempC is not null && g.LoadPercent is { } l ? L.T($" · بار {Fa.N(l, 0)}٪", $" · load {l:0}%") : "");
        }

        // network
        if (sample) Push("net", s.DownMbps);
        _net.Set(Fa.N(s.DownMbps, s.DownMbps >= 10 ? 0 : 1), "Mb/s ↓", _history["net"]);
        _net.ToolTip = L.T($"آپلود {Fa.N(s.UpMbps, s.UpMbps >= 10 ? 0 : 1)} Mb/s", $"Up {s.UpMbps:0.0} Mb/s");
    }

    private void OnVisible()
    {
        if (IsVisible && !_acquired && !Hub.Demo)
        {
            _acquired = true;
            Hub.Telemetry.Acquire();
        }
        else if (!IsVisible && _acquired)
        {
            _acquired = false;
            Hub.Telemetry.Release();
        }
    }

    public override void OnSecond()
    {
        var now = DateTime.Now;
        _seconds++;
        UpdateTitles(now);
        ShowLive(_seconds % 3 == 0);
        foreach (var (label, due) in _countdowns) label.Text = Fa.Countdown(due - now);
        TickHero(now);
    }

    // ---- actions (unchanged behaviour: every one asks first, with the exact time) ---------------------------

    private void Schedule(int minutes)
    {
        var now = DateTime.Now;
        var due = now.AddMinutes(minutes);
        var seconds = Hub.Settings.CountdownSeconds;
        if (!Ask(L.T("این زمان‌بندی ثبت بشه؟", "Schedule this?"),
                L.T($"{Fa.ActionSentence(_action)}. {Fa.Digits(seconds.ToString())} ثانیه قبلش شمارش معکوس می‌بینی و می‌تونی لغوش کنی. هر وقت خواستی از لیست «بعدی چیه؟» با دکمه‌ی حذف پاکش کن.",
                    $"Then {Fa.ActionSentence(_action)}. You get a {seconds}-second countdown first and can cancel. Delete it any time from the “What's next?” list."),
                L.T("تایید و ثبت", "Confirm and schedule"),
                detail: L.T($"شروع: {At(due, now)} ({Fa.Duration(due - now)} دیگه)", $"Runs: {At(due, now)} (in {Fa.Duration(due - now)})")))
            return;
        if (Hub.AddCommand(_action, due))
            Hub.Notify(L.T("ثبت شد", "Scheduled"), L.T($"{Fa.ActionSentence(_action)} · {At(due, now)}", $"{Fa.ActionSentence(_action)} · {At(due, now)}"));
        else
            Hub.Notify(L.T("از قبل ثبت شده بود", "Already scheduled"), L.T("همین دستور با همین زمان قبلاً توی لیسته؛ دوباره اضافه نشد.", "The same command at about the same time is already in the list; nothing was added."));
    }

    private void OnAfterDownload()
    {
        if (!Ask(L.T("خاموشی بعد از دانلود فعال بشه؟", "Turn on shutdown-after-download?"),
                L.T($"شب‌پا از همین الان سرعت اینترنت رو زیر نظر می‌گیره. وقتی دانلودی شروع بشه و بعدش ۵ دقیقه سرعت زیر ۱ مگابیت بمونه، {Fa.ActionSentence(_action)} (با شمارش معکوس قابل لغو). تا وقتی دانلودی شروع نشه هیچ اتفاقی نمی‌افته.",
                    $"Shabpa starts watching your connection right now. Once a download has started and the speed then stays under 1 Mbit for 5 minutes, {Fa.ActionSentence(_action)} (with a cancellable countdown). Nothing happens until a download has started."),
                L.T("تایید و فعال کن", "Confirm and turn on"),
                detail: L.T("شروع نظارت: همین الان · هر وقت خواستی از لیست «بعدی چیه؟» غیرفعالش کن", "Watching starts: right now · turn it off any time from the “What's next?” list")))
            return;
        Hub.ActivateRule(new Rule { Kind = RuleKind.Network, ThresholdMbps = 1, HoldMinutes = 5, WaitForStart = true, Action = _action });
    }

    private void OnWhenIdle()
    {
        if (!Ask(L.T("خاموشی بعد از بی‌استفاده موندن فعال بشه؟", "Turn on shutdown-when-idle?"),
                L.T($"اگه ۳۰ دقیقه پشت سر هم موس و کیبورد دست نخوره، {Fa.ActionSentence(_action)} (با شمارش معکوس قابل لغو). با هر حرکت موس شمارش از اول شروع می‌شه.",
                    $"If the mouse and keyboard stay untouched for 30 minutes in a row, {Fa.ActionSentence(_action)} (with a cancellable countdown). Any movement restarts the count."),
                L.T("تایید و فعال کن", "Confirm and turn on"),
                detail: L.T("شروع شمارش: همین الان · هر وقت خواستی از لیست «بعدی چیه؟» غیرفعالش کن", "Counting starts: right now · turn it off any time from the “What's next?” list")))
            return;
        Hub.ActivateRule(new Rule { Kind = RuleKind.Idle, HoldMinutes = 30, Action = _action });
    }

    private void OnLock()
    {
        if (!Ask(L.T("صفحه قفل بشه؟", "Lock the screen?"),
                L.T("صفحه همین الان قفل می‌شه و برای ورود باید رمز یا پین بزنی. برنامه‌ها و دانلودها بازمی‌مونن.", "The screen locks right now and you will need your password or PIN. Apps and downloads keep running."),
                L.T("قفل کن", "Lock now"), detail: L.T("شروع: همین الان", "Starts: right now")))
            return;
        Hub.RequestAction(PowerAction.Lock, "", 0);
    }

    private void DeleteCommand(Guid id, PowerAction action, DateTime due)
    {
        var now = DateTime.Now;
        if (!Ask(L.T("این دستور حذف بشه؟", "Delete this command?"),
                L.T("برای همیشه از لیست پاک می‌شه و اجرا نمی‌شه؛ هیچ شمارش معکوسی هم نمی‌بینی.", "It is removed from the list for good and will not run; no countdown will appear either."),
                L.T("حذف کن", "Delete"), danger: true,
                detail: $"{Fa.ActionName(action)} · {At(due, now)}"))
            return;
        if (IsLoaded && _planRows.TryGetValue("cmd:" + id, out var row)) NetFx.Out(row, () => Hub.RemoveCommand(id)); // the row slides away first
        else Hub.RemoveCommand(id);
    }

    private void DeleteRule(PowerAction action)
    {
        if (!Ask(L.T("خاموشی شرطی غیرفعال بشه؟", "Turn this rule off?"),
                L.T("دیگه چیزی زیر نظر گرفته نمی‌شه و سیستم به خاطر این قانون چیزی اجرا نمی‌کنه.", "Nothing is watched any more and the PC will not act because of this rule."),
                L.T("غیرفعال کن", "Turn off"), danger: true, detail: Fa.ActionName(action)))
            return;
        if (IsLoaded && _planRows.TryGetValue("rule:" + action, out var row)) NetFx.Out(row, Hub.DeactivateRule);
        else Hub.DeactivateRule();
    }

    private void OnFreeRam()
    {
        if (!Ask(L.T("رم خالی بشه؟", "Free up RAM?"),
                L.T("حافظه‌ی بلااستفاده‌ی برنامه‌ها برداشته می‌شه. برنامه‌ها بسته نمی‌شن و چیزی ذخیره‌نشده از بین نمی‌ره؛ فقط ممکنه چند ثانیه کند باشن.",
                    "Unused memory is taken back from apps. Nothing is closed and no unsaved work is lost; apps may just feel slow for a few seconds."),
                L.T("خالی کن", "Free it")))
            return;
        if (_ramCard is not null) { _ramCard.IsEnabled = false; _ramCard.Opacity = 0.5; }
        Hub.CleanMemory(cache: false);
    }
}
