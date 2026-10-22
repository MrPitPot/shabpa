using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shabpa.Core;
using Shabpa.Kit;
using Switch = Shabpa.Kit.Switch;

namespace Shabpa.Pages;

/// <summary>«وضعیت زنده» of the ping page (Glass Depth): the live ping with its chart and target, the in-game guard, and the
/// per-app traffic list with limits. Built in code from the shared kit.</summary>
public sealed class PingPage : PageBase
{
    private const int MaxRows = 30;
    private static readonly double[] DownCaps = [0, 1, 2, 5, 10, 20, 50];
    private static readonly double[] UpCaps = [0, 0.5, 1, 2, 5, 10];

    private sealed class AppRow
    {
        public required string Key;
        public required StackPanel Root;
        public required TextBlock Title, Sub, Down, Up, PillText;
        public required Border Pill;
        public required Border Editor;
        public required Icon Chevron;
        public string Path = "";
        public string Name = "";
        public string EditorSignature = "";
    }

    private readonly Dictionary<string, AppRow> _rows = [];
    private readonly Dictionary<string, ImageSource?> _icons = [];
    private bool _sortByToday;
    private string? _openKey;
    private int _ticks;
    private string _order = "";
    private bool _sparkShown;
    private string _pingTint = "";

    // hero
    private readonly TextBlock _pingText = new() { FontSize = 34, FontWeight = FontWeights.Bold, Text = "—", Foreground = Res("Green"), LineHeight = 40, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _baseText = new() { FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Res("Muted") };
    private readonly TextBlock _jitterText = new() { FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Res("Muted") };
    private readonly TextBlock _lossText = new() { FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Res("Muted") };
    private readonly Polyline _spark = new() { Stroke = Res("Accent"), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Grid _sparkHost = new();
    private readonly TextBox _hostBox = new() { Style = St("Input"), FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(10, 6, 10, 6), FontSize = 13 };

    // admin banner
    private readonly GlassCard _adminCard = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(20, 12, 20, 12) };
    private readonly StackPanel _adminPanel = new();

    // guard
    private readonly GlassCard _guardCard = new();
    private readonly Switch _guardCheck = new();
    private readonly TextBlock _guardState = new() { FontWeight = FontWeights.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly WrapPanel _guardChips = new();
    private readonly WrapPanel _guardDownChips = new();
    private readonly TextBlock _guardStatus = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    // per-app list
    private readonly WrapPanel _sortChips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _clearButton = new() { Style = St("BtnDanger"), Padding = new Thickness(14, 8, 14, 8), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _errorText = new() { Foreground = Res("Red"), FontSize = 12.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Grid _header = new() { Margin = new Thickness(10, 14, 10, 4) };
    private readonly StackPanel _appsPanel = new();
    private readonly TextBlock _emptyText = new() { Style = St("Body"), Margin = new Thickness(0, 10, 0, 4), Visibility = Visibility.Collapsed };

    public override string PageTitle => L.T("پایداری پینگ", "Ping stability");
    public override string PageSubtitle => L.T("ببین کدوم برنامه داره اینترنتت رو می‌خوره و جلوش رو بگیر تا پینگ نوسان نکنه.", "See which app is eating your connection and rein it in so the ping stays steady.");

    private static string Mb(double value) => L.T($"{Fa.Mbit(value)} مگابیت", $"{Fa.Mbit(value)} Mbit");

    public PingPage()
    {
        _hostBox.Text = Hub.Settings.PingHost;
        _guardCheck.IsChecked = Hub.Settings.GuardEnabled;
        _guardCheck.Click += OnGuard;
        _clearButton.Content = L.T("حذف همه‌ی محدودیت‌ها", "Remove all limits");
        _clearButton.Click += OnClear;

        var root = new NetBlocks(1060, 5);
        root.Add(BuildAdmin(), 0, 0, 12, 0);
        root.Add(BuildHero(), 1, 0, 8, 1);
        root.Add(BuildGuard(), 1, 8, 4, 2, rowSpan: 2);
        root.Add(BuildApps(), 2, 0, 8, 3);
        var note = K.Text(L.T("روی هر برنامه بزن تا سقف دانلود و آپلودش رو تعیین کنی یا اینترنتش رو کامل ببندی. محدودیت‌ها فقط تا وقتی شب‌پا بازه برقرارن و با بستنش همه‌چی به حالت عادی برمی‌گرده. اگه از فیلترشکن یا پروکسی استفاده می‌کنی، ترافیک هر برنامه هم زیر اسم خودش و هم زیر اسم فیلترشکن دیده می‌شه.",
            "Click an app to set its download and upload caps or to cut it off completely. Limits only hold while Shabpa is running; closing it returns everything to normal. If you use a VPN or proxy client, an app shows up both under its own name and under the client."), "Tip");
        note.TextWrapping = TextWrapping.Wrap;
        note.LineHeight = 20;
        root.Add(note, 3, 0, 12, 4);
        Content = root;

        Chips(_guardChips, new[] { 0.5, 1.0, 2.0, 5.0 }.Select(v => (v, Mb(v))), Hub.Settings.GuardCapMbps, v =>
        {
            Hub.Settings.GuardCapMbps = v;
            Hub.LimitsChanged();
        });
        Chips(_guardDownChips, new[] { 0.0, 2.0, 5.0, 10.0, 20.0 }.Select(v => (v, v == 0 ? L.T("آزاد", "Unlimited") : Mb(v))), Hub.Settings.GuardDownCapMbps, v =>
        {
            Hub.Settings.GuardDownCapMbps = v;
            Hub.LimitsChanged();
        });
        Chips(_sortChips, [(false, L.T("الان", "Now")), (true, L.T("امروز", "Today"))], _sortByToday, v =>
        {
            _sortByToday = v;
            UpdateApps(true);
        });
        foreach (var chip in _sortChips.Children.OfType<FrameworkElement>()) chip.Margin = new Thickness(0);
        Hub.NetTick += () => { if (IsVisible) UpdateApps(false); };
    }

    // ---- layout ----------------------------------------------------------------------------------

    private FrameworkElement BuildAdmin()
    {
        _adminCard.Child = _adminPanel;
        return _adminCard;
    }

    private static Border Stat(TextBlock text, string tip)
    {
        var pill = new Border { Style = St("Pill"), Margin = new Thickness(0, 0, 6, 6), ToolTip = tip, Child = text };
        return pill;
    }

    private FrameworkElement BuildHero()
    {
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition());

        // left: the number
        var left = new StackPanel { MinWidth = 180, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new IconBadge("net", "HueB", 30, 16, 10) { Margin = new Thickness(0, 0, 10, 0) });
        head.Children.Add(new TextBlock { Text = L.T("پینگ همین لحظه", "Ping right now"), Style = St("Label"), FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(head);
        var value = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 6) };
        value.Children.Add(_pingText);
        value.Children.Add(new TextBlock { Text = "ms", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 6) });
        left.Children.Add(value);
        var pills = new WrapPanel();
        pills.Children.Add(Stat(_baseText, L.T("میانه‌ی دو دقیقه‌ی اخیر؛ عدد عادی خط تو", "Median of the last two minutes: what is normal for your line")));
        pills.Children.Add(Stat(_jitterText, L.T("پینگ چقدر بالا و پایین می‌پره؛ هر چی کمتر، بازی روون‌تر", "How much the ping jumps around; lower means smoother games")));
        pills.Children.Add(Stat(_lossText, L.T("درصد بسته‌هایی که جوابشون برنگشته", "Share of packets that never came back")));
        left.Children.Add(pills);
        inner.Children.Add(left);

        // right: chart on top, target below
        var right = new Grid { VerticalAlignment = VerticalAlignment.Center };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(right, 1);
        // Fixed height: the line is scaled into the box, so a spike can never stretch the page.
        _sparkHost.Children.Add(_spark);
        var chart = new Border { Style = St("Card"), Padding = new Thickness(8), Height = 60, ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight, Child = _sparkHost };
        right.Children.Add(chart);
        var target = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        target.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        target.ColumnDefinitions.Add(new ColumnDefinition());
        target.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        target.Children.Add(new TextBlock { Text = L.T("مقصد پینگ", "Ping target"), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        _hostBox.ToolTip = L.T("آی‌پی یا آدرس سرور بازی‌ات رو بذار تا عدد واقعی همون سرور رو ببینی.", "Enter the IP or address of your game server to see the real figure for it.");
        Grid.SetColumn(_hostBox, 1);
        target.Children.Add(_hostBox);
        var save = new Button { Style = St("Btn"), Content = L.T("ذخیره", "Save"), Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0) };
        save.Click += OnSaveHost;
        Grid.SetColumn(save, 2);
        target.Children.Add(save);
        Grid.SetRow(target, 1);
        right.Children.Add(target);
        inner.Children.Add(right);

        return new GlassCard { Padding = new Thickness(20, 14, 20, 14), Child = inner };
    }

    private FrameworkElement BuildGuard()
    {
        var body = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(NetParts.Header("shield", "HueB", L.T("محافظت موقع بازی", "In-game protection")));
        var toggle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        toggle.Children.Add(_guardState);
        toggle.Children.Add(_guardCheck);
        Grid.SetColumn(toggle, 1);
        head.Children.Add(toggle);
        body.Children.Add(head);
        body.Children.Add(new TextBlock
        {
            Text = L.T("تا بازی تمام‌صفحه‌ست، آپلود (و اگه بخوای دانلود) بقیه‌ی برنامه‌ها خودکار سقف می‌خوره تا پینگ نپره؛ بازی که بسته شد همه‌چی آزاد می‌شه.",
                "While a full-screen game runs, every other app gets an automatic upload cap (and, if you choose, a download cap) so the ping stays steady; it is lifted when the game closes."),
            Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });
        body.Children.Add(new TextBlock { Text = L.T("سقف آپلود هر برنامه موقع بازی:", "Upload cap per app while gaming:"), Style = St("Label"), Margin = new Thickness(0, 14, 0, 8) });
        body.Children.Add(_guardChips);
        body.Children.Add(new TextBlock { Text = L.T("سقف دانلود هر برنامه موقع بازی:", "Download cap per app while gaming:"), Style = St("Label"), Margin = new Thickness(0, 10, 0, 8) });
        body.Children.Add(_guardDownChips);
        body.Children.Add(_guardStatus);
        _guardCard.Child = body;
        return _guardCard;
    }

    private FrameworkElement BuildApps()
    {
        var body = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(NetParts.Header("dl", "HueD", L.T("چی داره اینترنتت رو می‌خوره؟", "What is using your internet?")));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(_sortChips);
        tools.Children.Add(_clearButton);
        Grid.SetColumn(tools, 1);
        head.Children.Add(tools);
        body.Children.Add(head);
        body.Children.Add(_errorText);

        foreach (var width in new[] { 0.0, 92.0, 92.0, 150.0, 22.0 })
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        void Heading(string text, int column, bool center = true)
        {
            var t = new TextBlock { Text = text, Style = St("Label"), FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            if (center) t.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(t, column);
            _header.Children.Add(t);
        }
        Heading(L.T("برنامه", "App"), 0, false);
        Heading(L.T("دانلود", "Download"), 1);
        Heading(L.T("آپلود", "Upload"), 2);
        Heading(L.T("وضعیت", "Status"), 3);
        body.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 14, 0, 0) });
        body.Children.Add(_header);
        body.Children.Add(_appsPanel);
        body.Children.Add(_emptyText);
        return new GlassCard { Padding = new Thickness(22, 20, 22, 14), Child = body };
    }

    public override void Refresh()
    {
        Hub.Ping.Start();
        ShowAdmin();
        UpdateApps(true);
    }

    // ---- ping ----------------------------------------------------------------------------------

    private void OnSaveHost(object sender, RoutedEventArgs e)
    {
        Hub.Settings.PingHost = _hostBox.Text.Trim();
        Hub.Store.Save();
        if (sender is FrameworkElement saved) Motion.Pop(saved);
    }

    public override void OnSecond()
    {
        var samples = Hub.Ping.Samples;
        var stats = PingStats.From(samples);
        var has = stats.Samples > 0;

        if (!has) NetFx.Plain(_pingText, "—");
        else if (stats.Last is { } last) NetFx.Count(_pingText, last, v => v.ToString("0"), 450);
        else NetFx.Plain(_pingText, "×");
        // Colour by how far the current value sits above this line's normal, not by an absolute number:
        // 90 ms is fine on one connection and terrible on another.
        var spike = !has ? 0 : stats.Last is null ? double.MaxValue : stats.Last.Value - stats.Baseline;
        var tint = spike > 60 ? "Red" : spike > 20 ? "Gold" : "Green";
        _pingText.Foreground = Res(tint);
        if (_pingTint.Length > 0 && tint != _pingTint) Motion.Pop(_pingText);
        _pingTint = tint;
        void Stat(TextBlock box, string label, double value, string suffix = "")
        {
            if (has) NetFx.Count(box, value, v => label + Fa.N(v, 0) + suffix, 450);
            else NetFx.Plain(box, label + "—");
        }
        Stat(_baseText, L.T("پایه ", "Base "), stats.Baseline);
        Stat(_jitterText, L.T("نوسان ", "Jitter "), stats.Jitter);
        Stat(_lossText, L.T("افت ", "Loss "), stats.LossPercent, L.T("٪", "%"));
        _lossText.Foreground = Res(stats.LossPercent >= 2 ? "Red" : "Muted");
        _jitterText.Foreground = Res(stats.Jitter >= 15 ? "Gold" : "Muted");
        if (!has) return;

        _spark.Points = SparkPoints(samples, stats.Baseline, _sparkHost.ActualWidth, _sparkHost.ActualHeight);
        if (!_sparkShown)
        {
            _sparkShown = true;
            Motion.Rise(_sparkHost, 0, 8, 0.7);
        }
    }

    /// <summary>Chart points in pixels. The scale starts at zero and tops out at a few times the normal
    /// ping, so one huge spike (or a lost packet, drawn at the top) cannot flatten everything else.</summary>
    internal static PointCollection SparkPoints(IReadOnlyList<double?> samples, double baseline, double width, double height)
    {
        var points = new PointCollection(samples.Count);
        if (samples.Count < 2 || width <= 0 || height <= 0) return points;
        var highest = samples.Max(s => s ?? 0);
        var top = Math.Max(20, Math.Min(highest * 1.1, Math.Max(baseline * 3, baseline + 60)));
        var step = width / (PingMeter.Keep - 1);
        var left = width - step * (samples.Count - 1); // newest sample on the right edge
        for (var i = 0; i < samples.Count; i++)
        {
            var value = Math.Min(samples[i] ?? top, top);
            points.Add(new Point(left + i * step, height - value / top * height));
        }
        return points;
    }

    // ---- admin banner ----------------------------------------------------------------------------

    private void ShowAdmin()
    {
        _adminPanel.Children.Clear();
        var canLimit = Hub.CanLimit;
        _guardCard.IsEnabled = canLimit;
        Fx.Anim(_guardCard, UIElement.OpacityProperty, canLimit ? 1 : 0.5, null, 320);
        _clearButton.IsEnabled = canLimit;

        if (canLimit && Hub.NetError is null)
        {
            _adminCard.Visibility = Visibility.Collapsed;
            return;
        }

        var adminWasHidden = _adminCard.Visibility != Visibility.Visible;
        _adminCard.Visibility = Visibility.Visible;
        if (adminWasHidden) Motion.Rise(_adminCard, 0, 14, 0.6);
        if (!canLimit)
        {
            var button = new Button { Style = St("BtnPrimary"), Content = L.T("اجرا با دسترسی ادمین", "Run as administrator"), Padding = new Thickness(18, 9, 18, 9) };
            button.Click += (_, _) =>
            {
                if (Application.Current is App app && !app.Restart(elevated: true, page: "ping"))
                    Hub.Notify(L.T("دسترسی داده نشد", "Access was not granted"),
                        L.T("بدون تأیید ویندوز، مصرف هر برنامه و محدودیت‌ها در دسترس نیست.", "Without the Windows confirmation, per-app usage and limits are unavailable."));
            };
            _adminPanel.Children.Add(Row("shield", "Gold", L.T("برای دیدن مصرف هر برنامه، دسترسی ادمین لازمه", "Per-app usage needs administrator rights"),
                L.T("ویندوز آمار شبکه‌ی هر برنامه و محدود کردنش رو فقط به ادمین می‌ده. شب‌پا یک بار اجازه می‌گیره و دوباره باز می‌شه. اگه «اجرا همراه ویندوز» روشن باشه، دفعه‌های بعد خودش با همین دسترسی بالا میاد.",
                    "Windows only gives per-app network statistics and limiting to admins. Shabpa asks once and reopens. With “Start with Windows” on, it comes up with these rights by itself next time."),
                button));
        }
        else
        {
            _adminPanel.Children.Add(Row("x", "Red", L.T("اندازه‌گیری ترافیک شروع نشد", "Traffic measurement did not start"), Hub.NetError ?? ""));
        }
    }

    // ---- guard -----------------------------------------------------------------------------------

    private void OnGuard(object sender, RoutedEventArgs e)
    {
        Hub.Settings.GuardEnabled = _guardCheck.IsChecked == true;
        Hub.LimitsChanged();
    }

    private void UpdateGuard()
    {
        var settings = Hub.Settings;
        var guardText = settings.GuardEnabled ? L.T("محافظت روشن", "Protection on") : L.T("محافظت خاموش", "Protection off");
        if (_guardState.Text.Length > 0 && _guardState.Text != guardText) Motion.Pop(_guardState);
        _guardState.Text = guardText;
        _guardState.Foreground = Res(settings.GuardEnabled ? "Green" : "Muted");
        if (!Hub.CanLimit)
            NetFx.Say(_guardStatus, L.T("به دسترسی ادمین نیاز داره.", "Needs administrator rights."), false);
        else if (!settings.GuardEnabled)
            NetFx.Say(_guardStatus, L.T("خاموشه؛ فقط محدودیت‌هایی که خودت پایین گذاشتی اعمال می‌شن.", "Off; only the limits you set below apply."), false);
        else if (Hub.GameProcess.Length == 0)
            NetFx.Say(_guardStatus, L.T("آماده‌ست. تا بازی تمام‌صفحه‌ای اجرا نشه به هیچ برنامه‌ای دست نمی‌زنه.", "Ready. It touches nothing until a full-screen game is running."), false);
        else
        {
            var held = Hub.Planned.Count(l => l.Automatic);
            var down = settings.GuardDownCapMbps > 0 ? L.T($" و دانلود {Mb(settings.GuardDownCapMbps)}", $" and download {Mb(settings.GuardDownCapMbps)}") : "";
            NetFx.Say(_guardStatus, L.T($"بازی فعال: {Hub.GameProcess} · {Fa.Digits(held.ToString())} برنامه زیر سقف آپلود {Mb(settings.GuardCapMbps)}{down}.",
                $"Game running: {Hub.GameProcess} · {held} app(s) capped at upload {Mb(settings.GuardCapMbps)}{down}."), false);
        }
    }

    // ---- per-app list ------------------------------------------------------------------------------

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (!Ask(L.T("همه‌ی محدودیت‌ها حذف بشه؟", "Remove every limit?"),
                L.T("سقف سرعت و بستن اینترنت همه‌ی برنامه‌ها برداشته می‌شه و ترافیک آزاد می‌شه.", "Every speed cap and block is lifted and traffic is free again."),
                L.T("حذف کن", "Remove all"), danger: true, detail: L.T($"{Fa.Digits(Hub.State.NetLimits.Count.ToString())} محدودیت", $"{Hub.State.NetLimits.Count} limit(s)")))
            return;
        _openKey = null;
        foreach (var row in _rows.Values)
        {
            row.Editor.Visibility = Visibility.Collapsed;
            NetFx.Chevron(row.Chevron, false, Flow == FlowDirection.RightToLeft);
        }
        Hub.ClearLimits();
    }

    private void UpdateApps(bool reorder)
    {
        UpdateGuard();
        _errorText.Text = Hub.LimitError is { } error ? L.T($"محدودیت اعمال نشد: {error}", $"The limit was not applied: {error}") : "";
        _errorText.Visibility = Hub.LimitError is null ? Visibility.Collapsed : Visibility.Visible;

        // Apps seen on the network, plus apps that have a limit but are quiet right now.
        var apps = Hub.Apps.Where(a => a.DownBytes + a.UpBytes > 0).ToDictionary(a => a.Key);
        foreach (var limit in Hub.State.NetLimits)
            apps.TryAdd(limit.Key, new AppTraffic(limit.Key, limit.Title.Length > 0 ? limit.Title : limit.Key, limit.Path, 0, 0, 0, 0, default));

        var limited = Hub.State.NetLimits.Select(l => l.Key).ToHashSet();
        var ordered = apps.Values
            .OrderByDescending(a => limited.Contains(a.Key))
            .ThenByDescending(a => _sortByToday ? a.DownBytes + a.UpBytes : (a.DownMbps + a.UpMbps) * 1e12 + a.DownBytes + a.UpBytes)
            .Take(MaxRows)
            .ToList();

        // Re-sorting on every tick makes rows jump under the mouse; do it now and then, and never
        // while an editor is open.
        _ticks++;
        var keys = string.Join("|", ordered.Select(a => a.Key).Order());
        var setChanged = keys != _order;
        if (reorder || setChanged || (_openKey is null && _ticks % 5 == 0 && !_appsPanel.IsMouseOver))
        {
            _order = keys;
            _appsPanel.Children.Clear();
            foreach (var app in ordered)
            {
                if (!_rows.TryGetValue(app.Key, out var row))
                {
                    _rows[app.Key] = row = BuildRow(app);
                    Motion.Rise(row.Root, Math.Min(_appsPanel.Children.Count / 2, 10), 10, 0.5);
                }
                if (_appsPanel.Children.Count > 0) _appsPanel.Children.Add(Divider());
                _appsPanel.Children.Add(row.Root);
            }
        }

        foreach (var app in ordered)
            if (_rows.TryGetValue(app.Key, out var row))
                UpdateRow(row, app);

        _emptyText.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NetFx.Say(_emptyText, Hub.CanLimit
            ? L.T("هنوز ترافیکی دیده نشده. چند ثانیه صبر کن.", "No traffic seen yet. Give it a few seconds.")
            : L.T("بعد از اجرا با دسترسی ادمین، برنامه‌ها اینجا با سرعت دانلود و آپلودشون فهرست می‌شن.", "Once running as administrator, apps are listed here with their download and upload speeds."), false);
        _header.Visibility = ordered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private AppRow BuildRow(AppTraffic app)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

        var name = new DockPanel();
        FrameworkElement icon = IconFor(app.Path) is { } source
            ? new Image { Source = source, Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight }
            : Badge("net", "Accent", 28);
        DockPanel.SetDock(icon, Dock.Left);
        name.Children.Add(icon);
        var title = NetParts.LatinText("", "H2", 13.5);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var sub = NetParts.LatinText("", "Label", 11.5);
        sub.Margin = new Thickness(0, 2, 0, 0);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(sub);
        name.Children.Add(texts);
        grid.Children.Add(name);

        TextBlock Figure(int column, string tint)
        {
            var text = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res(tint), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
            return text;
        }
        var down = Figure(1, "Green");
        var up = Figure(2, "Gold");

        var pillText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var pill = new Border { Style = St("Pill"), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 142, Child = pillText };
        Grid.SetColumn(pill, 3);
        grid.Children.Add(pill);

        var chevron = new Icon("chev", 14, Res("Muted"), 2) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        NetFx.Chevron(chevron, false, Flow == FlowDirection.RightToLeft);
        Grid.SetColumn(chevron, 4);
        grid.Children.Add(chevron);

        // the whole line is the click target and lights up under the pointer
        var line = new Border { Child = grid, CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 8, 6, 8), Cursor = Cursors.Hand };
        NetFx.HoverTint(line);
        Fx.Attach(line, 0, 0, 1, 0.99);

        var editor = new Border { Background = Res("Line"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 8), Margin = new Thickness(0, 2, 0, 8), Visibility = Visibility.Collapsed };
        var root = new StackPanel();
        root.Children.Add(line);
        root.Children.Add(editor);

        var row = new AppRow { Key = app.Key, Root = root, Title = title, Sub = sub, Down = down, Up = up, Pill = pill, PillText = pillText, Editor = editor, Chevron = chevron };
        line.MouseLeftButtonUp += (_, _) => Toggle(row);
        return row;
    }

    private void Toggle(AppRow row)
    {
        var open = _openKey != row.Key;
        foreach (var other in _rows.Values)
        {
            other.Editor.Visibility = Visibility.Collapsed;
            if (other != row) NetFx.Chevron(other.Chevron, false, Flow == FlowDirection.RightToLeft);
        }
        _openKey = open ? row.Key : null;
        NetFx.Chevron(row.Chevron, open, Flow == FlowDirection.RightToLeft);
        if (!open) return;
        row.EditorSignature = "";
        BuildEditor(row);
        row.Editor.Visibility = Visibility.Visible;
        Motion.Rise(row.Editor, 0, 8, 0.45);
    }

    /// <summary>Opens the first row's editor; used by the screenshot run.</summary>
    public void OpenFirst()
    {
        if (_appsPanel.Children.OfType<StackPanel>().FirstOrDefault() is { } first && _rows.Values.FirstOrDefault(r => r.Root == first) is { } row)
            Toggle(row);
    }

    private AppLimit LimitFor(AppRow row)
    {
        var existing = Hub.State.NetLimits.FirstOrDefault(l => l.Key == row.Key);
        return new AppLimit
        {
            Key = row.Key,
            Title = row.Name,
            Path = row.Path.Length > 0 ? row.Path : existing?.Path ?? "",
            DownMbps = existing?.DownMbps ?? 0,
            UpMbps = existing?.UpMbps ?? 0,
            Blocked = existing?.Blocked ?? false,
            Exempt = existing?.Exempt ?? false,
        };
    }

    /// <summary>One line of the editor: a label, preset chips and a box for any other value.</summary>
    private UIElement CapEditor(AppRow row, string label, double[] presets, double current, Action<AppLimit, double> set)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Style = St("Label"), Margin = new Thickness(0, 0, 0, 8) });
        var caps = new WrapPanel();
        Chips(caps, presets.Select(v => (v, v == 0 ? L.T("آزاد", "Unlimited") : Fa.Mbit(v))), current, value => Change(row, l => set(l, value)));
        var custom = new TextBox
        {
            Style = St("Input"), Width = 70, Padding = new Thickness(8, 6, 8, 6), FontSize = 13, TextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(6, 0, 8, 6), ToolTip = L.T("عدد دلخواه به مگابیت", "Any value, in Mbit"),
        };
        if (current > 0 && !presets.Contains(current)) custom.Text = current.ToString("0.##");
        var apply = new Button { Style = St("Btn"), Content = L.T("دلخواه (مگابیت)", "Custom (Mbit)"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 0, 6) };
        apply.Click += (_, _) =>
        {
            var value = Number(custom, 0, 10000, -1);
            if (value >= 0) Change(row, l => set(l, value));
        };
        caps.Children.Add(custom);
        caps.Children.Add(apply);
        panel.Children.Add(caps);
        return panel;
    }

    private void BuildEditor(AppRow row)
    {
        var limit = LimitFor(row);
        var signature = $"{limit.DownMbps}|{limit.UpMbps}|{limit.Blocked}|{limit.Exempt}|{Hub.CanLimit}";
        if (signature == row.EditorSignature) return;
        row.EditorSignature = signature;

        var panel = new StackPanel { IsEnabled = Hub.CanLimit };
        panel.Children.Add(CapEditor(row, L.T("سقف دانلود این برنامه (مگابیت):", "Download cap for this app (Mbit):"), DownCaps, limit.DownMbps, (l, v) => l.DownMbps = v));
        panel.Children.Add(new Border { Height = 6 });
        panel.Children.Add(CapEditor(row, L.T("سقف آپلود این برنامه (مگابیت):", "Upload cap for this app (Mbit):"), UpCaps, limit.UpMbps, (l, v) => l.UpMbps = v));

        var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var block = new CheckBox { Style = St("Check"), Content = L.T("اینترنتش رو کامل ببند", "Cut off its internet completely"), IsChecked = limit.Blocked, Margin = new Thickness(0, 0, 8, 6) };
        block.Click += (_, _) => Change(row, l => l.Blocked = block.IsChecked == true);
        var exempt = new CheckBox
        {
            Style = St("Check"), Content = L.T("موقع بازی به این دست نزن", "Leave it alone while gaming"), IsChecked = limit.Exempt, Margin = new Thickness(0, 0, 8, 6),
            ToolTip = L.T("مثلاً برای فیلترشکن یا برنامه‌ی وُیس که خودِ بازی بهش نیاز داره.", "For example a VPN client or voice chat the game itself depends on."),
        };
        exempt.Click += (_, _) => Change(row, l => l.Exempt = exempt.IsChecked == true);
        options.Children.Add(block);
        options.Children.Add(exempt);
        panel.Children.Add(options);

        if (!Hub.CanLimit)
            panel.Children.Add(new TextBlock { Text = L.T("برای تغییر، شب‌پا رو با دسترسی ادمین اجرا کن.", "Run Shabpa as administrator to change this."), Style = St("Label"), Margin = new Thickness(0, 2, 0, 6) });
        row.Editor.Child = panel;
    }

    private void Change(AppRow row, Action<AppLimit> change)
    {
        var limit = LimitFor(row);
        change(limit);
        Hub.SetLimit(limit);
    }

    private void UpdateRow(AppRow row, AppTraffic app)
    {
        row.Name = app.Title;
        if (app.Path.Length > 0) row.Path = app.Path;
        row.Title.Text = app.Title;
        row.Sub.Text = app.DownBytes + app.UpBytes > 0 ? $"↓ {Fa.Bytes(app.DownBytes)}   ↑ {Fa.Bytes(app.UpBytes)}" : app.Key;
        row.Down.Text = Fa.Rate(app.DownMbps);
        row.Up.Text = Fa.Rate(app.UpMbps);
        Fx.Anim(row.Down, UIElement.OpacityProperty, app.DownMbps < 0.001 ? 0.35 : 1, null, 300);
        Fx.Anim(row.Up, UIElement.OpacityProperty, app.UpMbps < 0.001 ? 0.35 : 1, null, 300);

        var planned = Hub.Planned.FirstOrDefault(l => l.Key == app.Key);
        var exempt = Hub.State.NetLimits.Any(l => l.Key == app.Key && l.Exempt);
        string text, tint;
        if (planned is { Blocked: true })
            (text, tint) = (L.T("اینترنت بسته", "Cut off"), "Red");
        else if (planned is not null && (planned.DownMbps > 0 || planned.UpMbps > 0))
        {
            var parts = new List<string>(2);
            if (planned.DownMbps > 0) parts.Add($"↓ {Fa.Mbit(planned.DownMbps)}");
            if (planned.UpMbps > 0) parts.Add($"↑ {Fa.Mbit(planned.UpMbps)}");
            var caps = string.Join("  ", parts);
            (text, tint) = (planned.Automatic ? L.T($"{caps} · بازی", $"{caps} · game") : L.T($"سقف {caps}", $"Cap {caps}"), "Gold");
        }
        else
            (text, tint) = exempt ? (L.T("معاف موقع بازی", "Exempt in game"), "Green") : (L.T("آزاد", "Unlimited"), "Accent");

        // Planned but not yet in force.
        var pending = planned is not null && !Hub.Applied.Contains(planned) && !Hub.Demo;
        var pillShown = pending ? $"{text} …" : text;
        if (row.PillText.Text.Length > 0 && row.PillText.Text != pillShown) Motion.Pop(row.Pill);
        row.PillText.Text = pillShown;
        row.PillText.Foreground = Res(tint);
        row.Pill.Background = Res(tint + "Soft");

        if (_openKey == row.Key) BuildEditor(row);
    }

    private ImageSource? IconFor(string path)
    {
        if (path.Length == 0) return null;
        if (_icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? source = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
            }
        }
        catch { /* no icon: the generic badge is used */ }
        return _icons[path] = source;
    }
}
