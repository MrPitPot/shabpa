using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class PingPage : PageBase
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

    public override string PageTitle => L.T("پایداری پینگ", "Ping stability");
    public override string PageSubtitle => L.T("ببین کدوم برنامه داره اینترنتت رو می‌خوره و جلوش رو بگیر تا پینگ نوسان نکنه.", "See which app is eating your connection and rein it in so the ping stays steady.");

    private static string Mb(double value) => L.T($"{Fa.Mbit(value)} مگابیت", $"{Fa.Mbit(value)} Mbit");

    public PingPage()
    {
        InitializeComponent();
        HostBox.Text = Hub.Settings.PingHost;
        GuardCheck.IsChecked = Hub.Settings.GuardEnabled;
        Chips(GuardChips, new[] { 0.5, 1.0, 2.0, 5.0 }.Select(v => (v, Mb(v))), Hub.Settings.GuardCapMbps, v =>
        {
            Hub.Settings.GuardCapMbps = v;
            Hub.LimitsChanged();
        });
        Chips(GuardDownChips, new[] { 0.0, 2.0, 5.0, 10.0, 20.0 }.Select(v => (v, v == 0 ? L.T("آزاد", "Unlimited") : Mb(v))), Hub.Settings.GuardDownCapMbps, v =>
        {
            Hub.Settings.GuardDownCapMbps = v;
            Hub.LimitsChanged();
        });
        Chips(SortChips, [(false, L.T("الان", "Now")), (true, L.T("امروز", "Today"))], _sortByToday, v =>
        {
            _sortByToday = v;
            UpdateApps(true);
        });
        Hub.NetTick += () => { if (IsVisible) UpdateApps(false); };
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
        Hub.Settings.PingHost = HostBox.Text.Trim();
        Hub.Store.Save();
    }

    public override void OnSecond()
    {
        var samples = Hub.Ping.Samples;
        var stats = PingStats.From(samples);
        var has = stats.Samples > 0;

        PingText.Text = !has ? "—" : stats.Last is { } last ? last.ToString("0") : "×";
        // Colour by how far the current value sits above this line's normal, not by an absolute number:
        // 90 ms is fine on one connection and terrible on another.
        var spike = !has ? 0 : stats.Last is null ? double.MaxValue : stats.Last.Value - stats.Baseline;
        PingText.Foreground = Res(spike > 60 ? "Red" : spike > 20 ? "Gold" : "Green");
        BaseText.Text = L.T("پایه ", "Base ") + (has ? Fa.N(stats.Baseline, 0) : "—");
        JitterText.Text = L.T("نوسان ", "Jitter ") + (has ? Fa.N(stats.Jitter, 0) : "—");
        LossText.Text = L.T("افت ", "Loss ") + (has ? Fa.N(stats.LossPercent, 0) + L.T("٪", "%") : "—");
        LossText.Foreground = Res(stats.LossPercent >= 2 ? "Red" : "Muted");
        JitterText.Foreground = Res(stats.Jitter >= 15 ? "Gold" : "Muted");
        if (!has) return;

        Spark.Points = SparkPoints(samples, stats.Baseline, SparkHost.ActualWidth, SparkHost.ActualHeight);
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
        AdminPanel.Children.Clear();
        var canLimit = Hub.CanLimit;
        GuardCard.IsEnabled = canLimit;
        GuardCard.Opacity = canLimit ? 1 : 0.5;
        ClearButton.IsEnabled = canLimit;

        if (canLimit && Hub.NetError is null)
        {
            AdminCard.Visibility = Visibility.Collapsed;
            return;
        }

        AdminCard.Visibility = Visibility.Visible;
        if (!canLimit)
        {
            var button = new Button { Style = St("BtnPrimary"), Content = L.T("اجرا با دسترسی ادمین", "Run as administrator") };
            button.Click += (_, _) =>
            {
                if (Application.Current is App app && !app.Restart(elevated: true, page: "ping"))
                    Hub.Notify(L.T("دسترسی داده نشد", "Access was not granted"),
                        L.T("بدون تأیید ویندوز، مصرف هر برنامه و محدودیت‌ها در دسترس نیست.", "Without the Windows confirmation, per-app usage and limits are unavailable."));
            };
            AdminPanel.Children.Add(Row("", "Gold", L.T("برای دیدن مصرف هر برنامه، دسترسی ادمین لازمه", "Per-app usage needs administrator rights"),
                L.T("ویندوز آمار شبکه‌ی هر برنامه و محدود کردنش رو فقط به ادمین می‌ده. شب‌پا یک بار اجازه می‌گیره و دوباره باز می‌شه. اگه «اجرا همراه ویندوز» روشن باشه، دفعه‌های بعد خودش با همین دسترسی بالا میاد.",
                    "Windows only gives per-app network statistics and limiting to admins. Shabpa asks once and reopens. With “Start with Windows” on, it comes up with these rights by itself next time."),
                button));
        }
        else
        {
            AdminPanel.Children.Add(Row("", "Red", L.T("اندازه‌گیری ترافیک شروع نشد", "Traffic measurement did not start"), Hub.NetError ?? ""));
        }
    }

    // ---- guard -----------------------------------------------------------------------------------

    private void OnGuard(object sender, RoutedEventArgs e)
    {
        Hub.Settings.GuardEnabled = GuardCheck.IsChecked == true;
        Hub.LimitsChanged();
    }

    private void UpdateGuard()
    {
        var settings = Hub.Settings;
        GuardCheck.Content = settings.GuardEnabled ? L.T("محافظت روشن", "Protection on") : L.T("محافظت خاموش", "Protection off");
        if (!Hub.CanLimit)
            GuardStatus.Text = L.T("به دسترسی ادمین نیاز داره.", "Needs administrator rights.");
        else if (!settings.GuardEnabled)
            GuardStatus.Text = L.T("خاموشه؛ فقط محدودیت‌هایی که خودت پایین گذاشتی اعمال می‌شن.", "Off; only the limits you set below apply.");
        else if (Hub.GameProcess.Length == 0)
            GuardStatus.Text = L.T("آماده‌ست. تا بازی تمام‌صفحه‌ای اجرا نشه به هیچ برنامه‌ای دست نمی‌زنه.", "Ready. It touches nothing until a full-screen game is running.");
        else
        {
            var held = Hub.Planned.Count(l => l.Automatic);
            var down = settings.GuardDownCapMbps > 0 ? L.T($" و دانلود {Mb(settings.GuardDownCapMbps)}", $" and download {Mb(settings.GuardDownCapMbps)}") : "";
            GuardStatus.Text = L.T($"بازی فعال: {Hub.GameProcess} · {Fa.Digits(held.ToString())} برنامه زیر سقف آپلود {Mb(settings.GuardCapMbps)}{down}.",
                $"Game running: {Hub.GameProcess} · {held} app(s) capped at upload {Mb(settings.GuardCapMbps)}{down}.");
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
        foreach (var row in _rows.Values) row.Editor.Visibility = Visibility.Collapsed;
        Hub.ClearLimits();
    }

    private void UpdateApps(bool reorder)
    {
        UpdateGuard();
        ErrorText.Text = Hub.LimitError is { } error ? L.T($"محدودیت اعمال نشد: {error}", $"The limit was not applied: {error}") : "";
        ErrorText.Visibility = Hub.LimitError is null ? Visibility.Collapsed : Visibility.Visible;

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
        if (reorder || setChanged || (_openKey is null && _ticks % 5 == 0 && !AppsPanel.IsMouseOver))
        {
            _order = keys;
            AppsPanel.Children.Clear();
            foreach (var app in ordered)
            {
                if (!_rows.TryGetValue(app.Key, out var row)) _rows[app.Key] = row = BuildRow(app);
                if (AppsPanel.Children.Count > 0) AppsPanel.Children.Add(Divider());
                AppsPanel.Children.Add(row.Root);
            }
        }

        foreach (var app in ordered)
            if (_rows.TryGetValue(app.Key, out var row))
                UpdateRow(row, app);

        EmptyText.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Hub.CanLimit
            ? L.T("هنوز ترافیکی دیده نشده. چند ثانیه صبر کن.", "No traffic seen yet. Give it a few seconds.")
            : L.T("بعد از اجرا با دسترسی ادمین، برنامه‌ها اینجا با سرعت دانلود و آپلودشون فهرست می‌شن.", "Once running as administrator, apps are listed here with their download and upload speeds.");
        Header.Visibility = ordered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private AppRow BuildRow(AppTraffic app)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 7), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        var name = new DockPanel();
        FrameworkElement icon = IconFor(app.Path) is { } source
            ? new Image { Source = source, Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight }
            : Badge("", "Accent", 28);
        DockPanel.SetDock(icon, Dock.Left);
        name.Children.Add(icon);
        var title = new TextBlock { Style = St("H2"), FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        var sub = new TextBlock { Style = St("Label"), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
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

        var pillText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
        var pill = new Border { Style = St("Pill"), HorizontalAlignment = HorizontalAlignment.Center, Child = pillText };
        Grid.SetColumn(pill, 3);
        grid.Children.Add(pill);

        var chevron = new TextBlock { Text = "", Style = St("Icon"), FontSize = 11, Foreground = Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(chevron, 4);
        grid.Children.Add(chevron);

        var editor = new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 8), Margin = new Thickness(0, 4, 0, 8), Visibility = Visibility.Collapsed };
        var root = new StackPanel();
        root.Children.Add(grid);
        root.Children.Add(editor);

        var row = new AppRow { Key = app.Key, Root = root, Title = title, Sub = sub, Down = down, Up = up, Pill = pill, PillText = pillText, Editor = editor };
        grid.MouseLeftButtonUp += (_, _) => Toggle(row);
        return row;
    }

    private void Toggle(AppRow row)
    {
        var open = _openKey != row.Key;
        foreach (var other in _rows.Values) other.Editor.Visibility = Visibility.Collapsed;
        _openKey = open ? row.Key : null;
        if (!open) return;
        row.EditorSignature = "";
        BuildEditor(row);
        row.Editor.Visibility = Visibility.Visible;
    }

    /// <summary>Opens the first row's editor; used by the screenshot run.</summary>
    public void OpenFirst()
    {
        if (AppsPanel.Children.OfType<StackPanel>().FirstOrDefault() is { } first && _rows.Values.FirstOrDefault(r => r.Root == first) is { } row)
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
        var apply = new Button { Style = St("Btn"), Content = L.T("دلخواه (مگابیت)", "Custom (Mbit)"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 0, 6) };
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
        row.Down.Opacity = app.DownMbps < 0.001 ? 0.35 : 1;
        row.Up.Opacity = app.UpMbps < 0.001 ? 0.35 : 1;

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
        row.PillText.Text = pending ? $"{text} …" : text;
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
