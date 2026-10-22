using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«زمان بازی»: play time per game over a chosen period, and (one click on a game) its logged sessions with the
/// temperatures, usage, internet and the programs that ran next to it. Everything comes from the local game log.</summary>
public sealed class GameLogPage : PageBase
{
    private static readonly PlaySpan[] Spans = [PlaySpan.Today, PlaySpan.Week, PlaySpan.Month, PlaySpan.All];
    private const int SessionsShown = 60;

    private readonly Blocks _root = new();
    private readonly Segmented _seg;
    private readonly StatCard _time = new(L.T("مجموع زمان بازی", "Total play time"), "clk", "HueA");
    private readonly StatCard _games = new(L.T("تعداد بازی", "Games"), "game", "HueB");
    private readonly StatCard _top = new(L.T("پربازی‌ترین", "Most played"), "star", "HueC");
    private readonly StatCard _net = new(L.T("اینترنت مصرفی", "Internet used"), "net", "HueD");
    private StackPanel _list = new();
    private LiveGameCard? _liveCard;
    private LiveGameCard _live => _liveCard ??= new LiveGameCard(Hub);
    private StackPanel _sessions = new();
    private readonly GlassCard _detail = new() { Padding = new Thickness(22, 20, 22, 20) };
    private readonly Button _back;
    private PlaySpan _span = PlaySpan.Week;
    private string? _game, _session;
    private bool _animateBars;
    private bool _hooked;

    public override string PageTitle => _game is null ? L.T("زمان بازی", "Playtime") : (Hub.GameLog.Game(_game)?.Name ?? L.T("زمان بازی", "Playtime"));
    public override string PageSubtitle => _game is null
        ? L.T("هر بازی چقدر بازی کردی؛ روی هر بازی بزن تا دماها و برنامه‌های پس‌زمینه‌ی هر نوبت رو ببینی.", "How long you played each game; click one to see each session's temperatures and background programs.")
        : L.T("نوبت‌های ثبت‌شده، دماها و مصرف هر نوبت، و برنامه‌هایی که کنارش باز بودن.", "Logged sessions, their temperatures and load, and the programs that ran next to it.");
    public override UIElement? HeaderAccessory => _game is null ? null : _back;

    public GameLogPage()
    {
        GroupC.EnsureIcons();
        _seg = new Segmented([L.T("امروز", "Today"), L.T("این هفته", "This week"), L.T("این ماه", "This month"), L.T("همه", "All time")], Array.IndexOf(Spans, _span), compact: true);
        _seg.Changed += i =>
        {
            _span = Spans[i];
            _animateBars = true;
            Build(false);
        };
        _back = new Button { Style = St("Btn"), Padding = new Thickness(16, 8, 16, 8) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var arrow = new Icon("back", 16) { Margin = new Thickness(0, 0, 8, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(L.En ? 1 : -1, 1) };
        row.Children.Add(arrow);
        row.Children.Add(new TextBlock { Text = L.T("همه‌ی بازی‌ها", "All games"), VerticalAlignment = VerticalAlignment.Center });
        _back.Content = row;
        _back.Click += (_, _) => Open(null, true);
        Fx.Attach(_back, press: 0.96);
        Content = _root;
        Loaded += (_, _) =>
        {
            if (!_hooked) { _hooked = true; Hub.GameLog.Changed += OnLogChanged; }
            _live.Start();
            _animateBars = true;
            Build(true);
        };
        Unloaded += (_, _) => { _live.Stop(); if (_hooked) { _hooked = false; Hub.GameLog.Changed -= OnLogChanged; } };
    }

    private void OnLogChanged() => Dispatcher.BeginInvoke(() => { if (IsLoaded) Build(false); });

    /// <summary>Shows the list (null) or one game's detail.</summary>
    public void Open(string? gameKey, bool animate = false)
    {
        _game = gameKey;
        _session = null;
        _animateBars = true;
        Build(animate);
    }

    public override void Refresh() => Build(false);

    private static string Pct(double p) => Fa.N(p, 0) + L.T("٪", "%");
    private static string Deg(double v) => Fa.N(v, 0) + "°";

    private static (string value, string unit) Hours(TimeSpan t)
    {
        if (t.TotalMinutes < 60) return (Fa.N(Math.Round(t.TotalMinutes), 0), L.T("دقیقه", "min"));
        var h = t.TotalHours;
        return (Fa.N(h, h < 10 ? 1 : 0), L.T("ساعت", "h"));
    }

    private void Build(bool cascade)
    {
        _root.Reset();
        if (_game is not null && Hub.GameLog.Game(_game) is { } entry) BuildDetail(entry);
        else
        {
            _game = null;
            BuildList();
        }
        RaiseHeaderChanged();
        if (cascade && IsLoaded) CMo.Cascade(_root);
        _animateBars = false;
    }

    // ---- the list of games -----------------------------------------------------------------------

    private void BuildList()
    {
        var now = DateTime.Now;
        var all = Hub.GameLog.Games;
        var totals = PlayStats.Totals(all, _span, now);
        _root.Add(_seg, Blocks.At(0, 0, 12));

        var from = PlayStats.From(_span, now);
        var sessions = all.SelectMany(g => g.Sessions).Where(s => s.Start >= from).ToList();
        var total = TimeSpan.FromSeconds(sessions.Sum(s => (double)s.Seconds));
        var (tv, tu) = Hours(total);
        _time.Set(tv, tu, Buckets(sessions, _span, now, s => s.Seconds / 60.0));
        _games.Set(Fa.Digits(totals.Count.ToString()), L.T("بازی", "games"), totals.Select(t => t.Time.TotalMinutes).Reverse().ToList());
        if (totals.Count > 0)
        {
            _top.Value.FontSize = 20;
            _top.Value.FlowDirection = Flow;
            _top.Value.TextTrimming = TextTrimming.CharacterEllipsis;
            _top.Value.MaxWidth = 170;
            var (hv, hu) = Hours(totals[0].Time);
            _top.Set(totals[0].Name, hv + " " + hu, null);
        }
        else
        {
            _top.Value.FontSize = 32;
            _top.Set("—", "", null);
        }
        var bytes = sessions.Sum(s => s.DownBytes + s.UpBytes);
        _net.Set(Fa.Bytes(bytes), "", Buckets(sessions, _span, now, s => (double)(s.DownBytes + s.UpBytes)));
        _root.Add(_time, Blocks.At(1, 0, 3), Blocks.At(1, 0, 6));
        _root.Add(_games, Blocks.At(1, 3, 3), Blocks.At(1, 6, 6));
        _root.Add(_top, Blocks.At(1, 6, 3), Blocks.At(2, 0, 6));
        _root.Add(_net, Blocks.At(1, 9, 3), Blocks.At(2, 6, 6));

        var card = new GlassCard { Padding = new Thickness(14, 12, 14, 12) };
        _list = new StackPanel();
        if (all.Count == 0) _list.Children.Add(Empty(true));
        else if (totals.Count == 0) _list.Children.Add(Empty(false));
        else
        {
            var top = Math.Max(1, totals[0].Time.TotalSeconds);
            var sum = Math.Max(1, totals.Sum(t => t.Time.TotalSeconds));
            var order = 0;
            foreach (var t in totals) _list.Children.Add(GameRow(t, top, sum, order++, now));
        }
        card.Child = _list;
        var block = new StackPanel();
        block.Children.Add(_live);
        block.Children.Add(new TextBlock { Text = L.T("بازی‌ها", "Games"), Style = St("Section"), Margin = new Thickness(0, 6, 0, 8) });
        block.Children.Add(card);
        block.Children.Add(new TextBlock
        {
            Style = St("Label"), Margin = new Thickness(6, 12, 6, 0), TextWrapping = TextWrapping.Wrap,
            Text = L.T("نوبت‌ها فقط روی همین کامپیوتر ذخیره می‌شن و جایی فرستاده نمی‌شن. ثبت رو از تنظیمات می‌تونی خاموش کنی.",
                "Sessions are stored only on this PC and never uploaded. You can turn the recording off in Settings."),
        });
        _root.Add(block, Blocks.At(2, 0, 12), Blocks.At(3, 0, 12));
    }

    private UIElement Empty(bool noData)
    {
        var panel = new StackPanel { Margin = new Thickness(10, 26, 10, 26), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460 };
        panel.Children.Add(new IconBadge("game", "HueA", 60, 28, 18) { HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = noData ? L.T("هنوز بازی‌ای ثبت نشده", "No games logged yet") : L.T("تو این بازه بازی نکردی", "No play in this period"),
            Style = St("H2"), Margin = new Thickness(0, 14, 0, 4), HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = noData
                ? (Hub.Settings.GameLog
                    ? L.T("وقتی یه بازی رو بیش از یک دقیقه بازی کنی و ببندی‌اش، این‌جا نوبتش با دماها و مصرف اینترنت ثبت می‌شه.", "Play a game for over a minute and close it: the session shows up here with temperatures and internet use.")
                    : L.T("ثبت نوبت‌های بازی خاموشه. از تنظیمات روشنش کن.", "Session recording is off. Turn it on in Settings."))
                : L.T("یه بازه‌ی بزرگ‌تر رو انتخاب کن.", "Pick a longer period."),
            Style = St("Body"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Res("Muted"),
        });
        if (noData && !Hub.Settings.GameLog)
        {
            var on = new Button { Style = St("BtnPrimary"), Content = L.T("روشن کن", "Turn on"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(20, 9, 20, 9) };
            on.Click += (_, _) =>
            {
                Hub.Settings.GameLog = true;
                Hub.Telemetry.LogWhileGameAlive = true;
                Hub.Store.Save();
                Build(false);
            };
            panel.Children.Add(on);
        }
        return panel;
    }

    private UIElement GameRow(GameTotal t, double top, double sum, int order, DateTime now)
    {
        var shell = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 1, 0, 1), Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent,
        };
        shell.MouseEnter += (_, _) => shell.Background = Res("Line");
        shell.MouseLeave += (_, _) => shell.Background = Brushes.Transparent;
        Fx.Attach(shell, nudge: 2, press: 0.985);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new IconBadge("game", order == 0 ? "HueC" : "HueA", 40, 20, 12) { VerticalAlignment = VerticalAlignment.Center });

        var mid = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = t.Name, Style = St("Body"), FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        mid.Children.Add(new TextBlock
        {
            Text = L.T($"{Fa.Digits(t.Sessions.ToString())} نوبت · آخرین بار {Fa.RelativeDay(t.Last, now)}", $"{t.Sessions} session{(t.Sessions == 1 ? "" : "s")} · last {Fa.RelativeDay(t.Last, now)}"),
            Style = St("Label"), FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0),
        });
        var bar = new Grid { Height = 8, Margin = new Thickness(0, 7, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        bar.Children.Add(new Border { Background = Res("Line"), CornerRadius = new CornerRadius(4) });
        var share = Math.Clamp(t.Time.TotalSeconds / top, 0.02, 1);
        var scaler = new Grid();
        scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share + 0.0001, GridUnitType.Star) });
        var fill = new Border { Background = Res("AccentGradientH"), CornerRadius = new CornerRadius(4) };
        scaler.Children.Add(fill);
        bar.Children.Add(scaler);
        if (_animateBars && IsLoaded) CMo.Grow(fill, order, vertical: false, seconds: 0.8, delayMs: 120);
        mid.Children.Add(bar);
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        var numbers = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 96 };
        numbers.Children.Add(new TextBlock { Text = Fa.Duration(t.Time), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = Flow });
        numbers.Children.Add(new TextBlock { Text = Pct(100 * t.Time.TotalSeconds / sum), Style = St("Label"), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = Flow });
        Grid.SetColumn(numbers, 2);
        grid.Children.Add(numbers);

        var delete = IconButton("trash", L.T("پاک کردن تاریخچه‌ی این بازی", "Delete this game's history"), () => DeleteGame(t));
        delete.Margin = new Thickness(8, 0, 0, 0);
        delete.VerticalAlignment = VerticalAlignment.Center;
        delete.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true; // the row below must not open
        Grid.SetColumn(delete, 3);
        grid.Children.Add(delete);
        shell.Child = grid;
        var key = t.Key;
        shell.MouseLeftButtonUp += (_, _) => Open(key, true);
        return shell;
    }

    private void DeleteGame(GameTotal t)
    {
        var all = Hub.GameLog.Game(t.Key);
        var count = all?.Sessions.Count ?? t.Sessions;
        var time = TimeSpan.FromSeconds(all?.Sessions.Sum(s => (double)s.Seconds) ?? t.Time.TotalSeconds);
        if (!ConfirmWindow.AskAlways(L.T("پاک کردن تاریخچه‌ی بازی", "Delete game history"),
                L.T($"همه‌ی {Fa.Digits(count.ToString())} نوبت ثبت‌شده‌ی «{t.Name}» ({Fa.Duration(time)}) برای همیشه پاک بشه؟", $"Delete all {count} logged sessions of \"{t.Name}\" ({Fa.Duration(time)}) for good?"),
                L.T("پاک کن", "Delete"), danger: true)) return;
        Hub.GameLog.DeleteGame(t.Key);
    }

    /// <summary>Per-bucket sums over the span (24 hours for today, days otherwise) for the stat cards' sparklines.</summary>
    private static List<double> Buckets(List<GameSessionRecord> sessions, PlaySpan span, DateTime now, Func<GameSessionRecord, double> value)
    {
        var count = span == PlaySpan.Today ? 24 : span == PlaySpan.Week ? 7 : 30;
        var result = new double[count];
        foreach (var s in sessions)
        {
            var index = span == PlaySpan.Today ? s.Start.Hour : count - 1 - (now.Date - s.Start.Date).Days;
            if (index >= 0 && index < count) result[index] += value(s);
        }
        return [.. result];
    }

    // ---- one game --------------------------------------------------------------------------------

    private void BuildDetail(GameEntry entry)
    {
        var now = DateTime.Now;
        var sessions = entry.Sessions.OrderByDescending(s => s.Start).ToList();
        _session = sessions.Any(s => s.Id == _session) ? _session : sessions[0].Id;
        var total = TimeSpan.FromSeconds(sessions.Sum(s => (double)s.Seconds));
        var net = sessions.Sum(s => s.DownBytes + s.UpBytes);

        var head = new GlassCard { Padding = new Thickness(24, 20, 24, 20) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new IconBadge("game", "HueA", 52, 26, 15) { VerticalAlignment = VerticalAlignment.Center });
        var texts = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = entry.Name, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock
        {
            Text = L.T($"{Fa.Digits(sessions.Count.ToString())} نوبت ثبت‌شده، از {Fa.Date(sessions[^1].Start)} تا {Fa.Date(sessions[0].Start)}",
                $"{sessions.Count} logged session{(sessions.Count == 1 ? "" : "s")}, {Fa.Date(sessions[^1].Start)} to {Fa.Date(sessions[0].Start)}"),
            Style = St("Label"), Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var delete = new Button { Style = St("BtnDanger"), Padding = new Thickness(16, 8, 16, 8) };
        var drow = new StackPanel { Orientation = Orientation.Horizontal };
        drow.Children.Add(new Icon("trash", 16) { Margin = new Thickness(0, 0, 8, 0) });
        drow.Children.Add(new TextBlock { Text = L.T("پاک کردن تاریخچه‌ی بازی", "Delete game history"), VerticalAlignment = VerticalAlignment.Center });
        delete.Content = drow;
        delete.Click += (_, _) => DeleteGame(new GameTotal(entry.Key, entry.Name, total, sessions.Count, 0, 0, sessions[0].Start));
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);
        head.Child = grid;
        _root.Add(head, Blocks.At(0, 0, 12));

        var (tv, tu) = Hours(total);
        var gpuMax = sessions.Select(s => s.Stat(GameMetric.GpuTemp)?.Max).Where(v => v is not null).DefaultIfEmpty().Max();
        var avgLen = TimeSpan.FromSeconds(total.TotalSeconds / sessions.Count);
        var asc = sessions.OrderBy(s => s.Start).TakeLast(30).ToList();
        _root.Add(new StatCard(L.T("مجموع زمان", "Total time"), "clk", "HueA"), Blocks.At(1, 0, 3), Blocks.At(1, 0, 6)).Set(tv, tu, asc.Select(s => (double)s.Seconds).ToList());
        _root.Add(new StatCard(L.T("میانگین هر نوبت", "Average session"), "play", "HueB"), Blocks.At(1, 3, 3), Blocks.At(1, 6, 6)).Set(Hours(avgLen).value, Hours(avgLen).unit, asc.Select(s => (double)s.Seconds).ToList());
        _root.Add(new StatCard(L.T("اینترنت مصرفی", "Internet used"), "net", "HueD"), Blocks.At(1, 6, 3), Blocks.At(2, 0, 6)).Set(Fa.Bytes(net), "", asc.Select(s => (double)(s.DownBytes + s.UpBytes)).ToList());
        _root.Add(new StatCard(L.T("بیشترین دمای گرافیک", "Hottest GPU"), "temp", "HueE"), Blocks.At(1, 9, 3), Blocks.At(2, 6, 6))
            .Set(gpuMax is { } g ? Fa.N(g, 0) : "—", gpuMax is null ? "" : "°C", asc.Select(s => s.Stat(GameMetric.GpuTemp)?.Max ?? 0).ToList());

        // sessions on one side, the chosen session on the other
        _sessions = new StackPanel();
        var listCard = new GlassCard { Padding = new Thickness(12, 12, 12, 12) };
        var listBlock = new StackPanel();
        listBlock.Children.Add(new TextBlock { Text = L.T("نوبت‌ها", "Sessions"), Style = St("H2"), Margin = new Thickness(8, 2, 8, 8) });
        listBlock.Children.Add(_sessions);
        if (sessions.Count > SessionsShown)
            listBlock.Children.Add(new TextBlock { Text = L.T($"و {Fa.Digits((sessions.Count - SessionsShown).ToString())} نوبت قدیمی‌تر.", $"and {sessions.Count - SessionsShown} older sessions."), Style = St("Label"), Margin = new Thickness(10, 8, 8, 2) });
        listCard.Child = listBlock;
        _root.Add(listCard, Blocks.At(3, 0, 5), Blocks.At(3, 0, 12)).VerticalAlignment = VerticalAlignment.Top;
        _root.Add(_detail, Blocks.At(3, 5, 7), Blocks.At(4, 0, 12)).VerticalAlignment = VerticalAlignment.Top;
        DrawSessions(entry, sessions, now);
        DrawDetail(sessions.First(s => s.Id == _session), now, false);
    }

    private void DrawSessions(GameEntry entry, List<GameSessionRecord> sessions, DateTime now)
    {
        var order = 0;
        foreach (var s in sessions.Take(SessionsShown))
        {
            var selected = s.Id == _session;
            var shell = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 8, 9), Margin = new Thickness(0, 1, 0, 1), Cursor = System.Windows.Input.Cursors.Hand,
                Background = selected ? Res("AccentSoft") : Brushes.Transparent,
            };
            if (!selected)
            {
                shell.MouseEnter += (_, _) => shell.Background = Res("Line");
                shell.MouseLeave += (_, _) => shell.Background = Brushes.Transparent;
            }
            Fx.Attach(shell, nudge: 2, press: 0.985);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = $"{Fa.DayOf(s.Start, now)}{L.Comma}{Fa.Clock(s.Start)}", Style = St("Body"), FontWeight = FontWeights.SemiBold,
                Foreground = Res(selected ? "Accent" : "Text"), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var temps = new List<string>();
            if (s.Stat(GameMetric.CpuTemp) is { } ct) temps.Add("CPU " + Deg(ct.Max));
            if (s.Stat(GameMetric.GpuTemp) is { } gt) temps.Add("GPU " + Deg(gt.Max));
            left.Children.Add(new TextBlock { Text = Fa.Duration(s.Length), Style = St("Label"), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            var facts = (s.DownBytes + s.UpBytes > 0 ? $"↓{Fa.Bytes(s.DownBytes)}  ↑{Fa.Bytes(s.UpBytes)}" : "") + (temps.Count > 0 ? (s.DownBytes + s.UpBytes > 0 ? "  ·  " : "") + string.Join("  ", temps) : "");
            if (facts.Length > 0)
                left.Children.Add(new TextBlock { Text = facts, Style = St("Label"), FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(left);
            var trash = IconButton("trash", L.T("پاک کردن این نوبت", "Delete this session"), () => DeleteSession(entry, s));
            trash.VerticalAlignment = VerticalAlignment.Center;
            trash.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true;
            Grid.SetColumn(trash, 1);
            grid.Children.Add(trash);
            shell.Child = grid;
            var id = s.Id;
            shell.MouseLeftButtonUp += (_, _) =>
            {
                if (_session == id) return;
                _session = id;
                _sessions.Children.Clear();
                DrawSessions(entry, sessions, now);
                DrawDetail(s, now, true);
            };
            _sessions.Children.Add(shell);
            if (_animateBars && IsLoaded) CMo.Appear(shell, order, 8, 0.4);
            order++;
        }
    }

    private void DeleteSession(GameEntry entry, GameSessionRecord s)
    {
        if (!ConfirmWindow.AskAlways(L.T("پاک کردن نوبت", "Delete session"),
                L.T($"نوبت «{entry.Name}» در {Fa.DayOf(s.Start, DateTime.Now)} ساعت {Fa.Clock(s.Start)} ({Fa.Duration(s.Length)}) برای همیشه پاک بشه؟",
                    $"Delete the \"{entry.Name}\" session of {Fa.DayOf(s.Start, DateTime.Now)} at {Fa.Clock(s.Start)} ({Fa.Duration(s.Length)}) for good?"),
                L.T("پاک کن", "Delete"), danger: true)) return;
        Hub.GameLog.DeleteSession(entry.Key, s.Id);
    }

    private void DrawDetail(GameSessionRecord s, DateTime now, bool animate)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"{Fa.DayOf(s.Start, now)}{L.Comma}{Fa.Clock(s.Start)}", Style = St("H2"), FontSize = 16 });
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        void Chip(string text, string tint, bool ltr = false)
        {
            var c = PillText(text, tint);
            c.Margin = new Thickness(0, 0, 8, 6);
            if (ltr) ((TextBlock)c.Child).FlowDirection = FlowDirection.LeftToRight;
            chips.Children.Add(c);
        }
        Chip(L.T("مدت ", "Length ") + Fa.Duration(s.Length), "HueA");
        Chip("↓ " + Fa.Bytes(s.DownBytes), "HueB", true);
        Chip("↑ " + Fa.Bytes(s.UpBytes), "HueC", true);
        if (s.NetApprox) Chip(L.T("حجم، کل سیستم (بدون دسترسی ادمین)", "Traffic of the whole PC (no admin rights)"), "HueD");
        panel.Children.Add(chips);

        var series = Hub.GameLog.SeriesOf(s.Id);
        var charts = new List<UIElement>();
        void Group(string title, params (string label, string metric, string hue, Func<double, string> fmt)[] lines)
        {
            var box = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            box.Children.Add(new TextBlock { Text = title, Style = St("Section"), Margin = new Thickness(0, 0, 0, 6) });
            var any = false;
            foreach (var (label, metric, hue, fmt) in lines)
            {
                if (s.Stat(metric) is not { } st) continue;
                any = true;
                box.Children.Add(Mini(label, hue, series?.Series.GetValueOrDefault(metric), st, fmt));
            }
            if (!any) box.Children.Add(new TextBlock { Text = L.T("برای این نوبت داده‌ای ثبت نشد.", "No data was recorded for this session."), Style = St("Label") });
            charts.Add(box);
        }
        Func<double, string> deg = v => Deg(v), pct = v => Pct(v), mb = v => Fa.N(v, v < 10 ? 1 : 0) + " Mb/s", fps = v => Fa.N(v, 0);
        Group(L.T("دما", "Temperature"), (L.T("پردازنده", "CPU"), GameMetric.CpuTemp, "HueE", deg), (L.T("کارت گرافیک", "GPU"), GameMetric.GpuTemp, "HueD", deg));
        if (s.Stat(GameMetric.CpuTemp) is null)
            charts.Add(new TextBlock
            {
                Text = L.T("دمای پردازنده فقط با سنسور برد (تنظیمات اورلی) و دسترسی ادمین خونده می‌شه.", "CPU temperature needs the board sensors (overlay settings) and administrator rights."),
                Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });
        Group(L.T("مصرف", "Usage"), (L.T("پردازنده", "CPU"), GameMetric.CpuLoad, "HueA", pct), (L.T("کارت گرافیک", "GPU"), GameMetric.GpuLoad, "HueB", pct), (L.T("رم", "RAM"), GameMetric.Ram, "HueC", pct));
        Group(L.T("اینترنت", "Internet"), (L.T("دانلود", "Download"), GameMetric.Down, "HueB", mb), (L.T("آپلود", "Upload"), GameMetric.Up, "HueA", mb));
        if (s.Stat(GameMetric.Fps) is not null) Group(L.T("فریم بر ثانیه", "Frame rate"), ("FPS", GameMetric.Fps, "HueC", fps));
        foreach (var c in charts) panel.Children.Add(c);

        // programs that ran next to the game
        var programs = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        programs.Children.Add(new TextBlock { Text = L.T("برنامه‌های پس‌زمینه", "Background programs"), Style = St("Section"), Margin = new Thickness(0, 0, 0, 2) });
        programs.Children.Add(new TextBlock
        {
            Text = L.T("برنامه‌هایی که حین این نوبت بیشترین پردازنده یا رم رو می‌گرفتن.", "Programs that took the most CPU or RAM during this session."),
            Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        if (s.Programs.Count == 0)
            programs.Children.Add(new TextBlock { Text = L.T("برنامه‌ی پرمصرفی دیده نشد (یا نوبت کوتاه‌تر از این بود که بررسی بشه).", "No heavy program was seen (or the session was too short to check)."), Style = St("Body"), Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap });
        else
        {
            var maxCpu = Math.Max(1, s.Programs.Max(p => p.AvgCpu));
            var i = 0;
            foreach (var p in s.Programs) programs.Children.Add(ProgramRow(p, maxCpu, s.ProcLooks, i++ == 0));
        }
        panel.Children.Add(programs);
        _detail.Child = panel;
        if (animate) CMo.Appear(_detail, 0, 10, 0.45);
    }

    private UIElement ProgramRow(BackgroundProgram p, double maxCpu, int looks, bool first)
    {
        var grid = new Grid { MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!first) grid.Children.Add(ListUi.Hairline());
        var left = new StackPanel { Margin = new Thickness(0, 7, 0, 7), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = p.Name, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        var bar = new Grid { Height = 5, Margin = new Thickness(0, 5, 14, 0), FlowDirection = FlowDirection.LeftToRight };
        bar.Children.Add(new Border { Background = Res("Line"), CornerRadius = new CornerRadius(3) });
        var share = Math.Clamp(p.AvgCpu / maxCpu, 0.03, 1);
        var scaler = new Grid();
        scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share + 0.0001, GridUnitType.Star) });
        scaler.Children.Add(new Border { Background = Res("HueC"), CornerRadius = new CornerRadius(3) });
        bar.Children.Add(scaler);
        left.Children.Add(bar);
        grid.Children.Add(left);
        var right = new TextBlock
        {
            Text = $"CPU {Fa.N(p.AvgCpu, 1)}%\nRAM {Fa.N(p.MaxRamMb, 0)} MB", Style = St("Label"), FontSize = 11.5, FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = L.T($"در {Fa.Digits(p.Seen.ToString())} از {Fa.Digits(looks.ToString())} بررسی دیده شد", $"Seen in {p.Seen} of {looks} checks"),
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private UIElement Mini(string label, string hue, double[]? data, MetricStat st, Func<double, string> fmt)
    {
        var box = new Border { Background = Res("Line"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
        var stack = new StackPanel();
        var head = new DockPanel();
        var dot = new StackPanel { Orientation = Orientation.Horizontal };
        dot.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Res(hue), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        dot.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(dot);
        var nums = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (name, value) in new[] { (L.T("کم", "min"), st.Min), (L.T("میانگین", "avg"), st.Avg), (L.T("بیشینه", "max"), st.Max) })
        {
            nums.Children.Add(new TextBlock { Text = name, Style = St("Label"), FontSize = 11.5, Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            nums.Children.Add(new TextBlock { Text = fmt(value), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center });
        }
        DockPanel.SetDock(nums, Dock.Right);
        head.Children.Insert(0, nums);
        stack.Children.Add(head);
        if (data is { Length: > 1 })
        {
            var spark = new Sparkline { Height = 44, Margin = new Thickness(0, 8, 0, 0), Line = Res(hue), Fill = true };
            spark.Set(data);
            stack.Children.Add(spark);
        }
        box.Child = stack;
        return box;
    }
}

/// <summary>«بازی در حال اجرا»: while a game runs, its live CPU / GPU / RAM / temperatures / internet and what that means
/// for the machine. Hidden when no game is running.</summary>
internal sealed class LiveGameCard : GlassCard
{
    private readonly Shabpa.Hub Hub;
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, FontSize = 16 };
    private readonly TextBlock _numbers = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _impact = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _acquired;

    public LiveGameCard(Shabpa.Hub hub)
    {
        Hub = hub;
        Padding = new Thickness(18, 14, 18, 14);
        Margin = new Thickness(0, 0, 0, 14);
        Visibility = Visibility.Collapsed;
        var panel = new StackPanel();
        panel.Children.Add(_title);
        panel.Children.Add(_numbers);
        panel.Children.Add(_impact);
        Child = panel;
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        if (!Hub.Demo && !_acquired) { _acquired = true; Hub.Telemetry.Acquire(); }
        _timer.Start();
        Refresh();
    }

    public void Stop()
    {
        _timer.Stop();
        if (_acquired) { _acquired = false; Hub.Telemetry.Release(); }
    }

    private void Refresh()
    {
        var s = Hub.Telemetry.Latest;
        if (s.GameKey.Length == 0) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        FlowDirection = Ui.Flow;
        _title.Text = "🎮 " + L.T("بازی در حال اجرا: ", "Now playing: ") + s.GameName + (s.GameTime > TimeSpan.Zero ? " · " + Fa.Duration(s.GameTime) : "");
        var parts = new List<string> { L.T($"پردازنده {Fa.N(s.CpuLoad, 0)}٪", $"CPU {Fa.N(s.CpuLoad, 0)}%") };
        if (s.GpuLoad is { } gl) parts.Add(L.T($"گرافیک {Fa.N(gl, 0)}٪", $"GPU {Fa.N(gl, 0)}%"));
        parts.Add(L.T($"رم {Fa.N(s.RamPercent, 0)}٪", $"RAM {Fa.N(s.RamPercent, 0)}%"));
        if (s.CpuTempC is { } ct) parts.Add(L.T($"دمای پردازنده {Fa.N(ct, 0)}°", $"CPU {Fa.N(ct, 0)}°C"));
        if (s.Gpu.TempC is { } gt) parts.Add(L.T($"دمای گرافیک {Fa.N(gt, 0)}°", $"GPU {Fa.N(gt, 0)}°C"));
        if (s.Fps > 0) parts.Add(L.T($"{Fa.N(s.Fps, 0)} فریم", $"{Fa.N(s.Fps, 0)} FPS"));
        parts.Add($"↓{Fa.N(s.DownMbps, 1)} ↑{Fa.N(s.UpMbps, 1)} Mbps");
        if (s.PingMs is { } p) parts.Add(L.T($"پینگ {Fa.N(p, 0)}", $"ping {Fa.N(p, 0)} ms"));
        _numbers.Text = string.Join(L.En ? "  ·  " : "  ·  ", parts);
        var level = GameImpact.Level(s.CpuLoad, s.GpuLoad, s.RamPercent, s.CpuTempC, s.Gpu.TempC);
        _impact.Text = L.T("تأثیر روی سیستم: ", "Effect on the system: ") + GameImpact.Describe(s.CpuLoad, s.GpuLoad, s.RamPercent, s.CpuTempC, s.Gpu.TempC);
        _impact.Foreground = level == 2 ? new SolidColorBrush(Color.FromRgb(0xEF, 0x5B, 0x5B)) : level == 1 ? new SolidColorBrush(Color.FromRgb(0xF2, 0xA5, 0x3A)) : (Brush)Application.Current.FindResource("Muted");
    }
}
