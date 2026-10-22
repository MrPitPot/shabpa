using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Pages;
using Shabpa.Kit;

namespace Shabpa;

public partial class MainWindow : Window
{
    // Pages are built the first time they are opened: most sessions only ever look at a few of them.
    private readonly Dictionary<string, (RadioButton nav, Func<PageBase> make)> _pages;
    private readonly Dictionary<string, PageBase> _built = [];
    private PageBase? _current;

    /// <summary>While the window is hidden or minimized, memory it no longer needs goes back to Windows: first after a
    /// few seconds, then now and then while it stays hidden.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _release = new() { Interval = ReleaseFirst };
    private static readonly TimeSpan ReleaseFirst = TimeSpan.FromSeconds(8), ReleaseAgain = TimeSpan.FromMinutes(10);

    /// <summary>Every screen worth a screenshot, including the tabs inside «کنترل سیستم».</summary>
    public IEnumerable<string> CaptureKeys =>
        ["home", .. SystemPage.TabKeys.Select(t => $"system:{t}"), .. NetworkPage.TabKeys.Select(t => $"ping:{t}"), "gaming", "playtime", "playtime:eldenring", "weather", "dns", "download", "comfort", "tasks", .. TasksPage.TabKeys.Select(t => $"tasks:{t}"), "files", .. FilesPage.TabKeys.Select(t => $"files:{t}"), "health", .. HealthPage.TabKeys.Select(t => $"health:{t}"), "profiles", .. ProfilesPage.TabKeys.Select(t => $"profiles:{t}"), "mic", "support", "update", "settings", "kit"];

    public FrameworkElement CaptureRoot => Root;

    /// <summary>The name of the page that is open, for the stall log.</summary>
    public string CurrentPage => _current?.GetType().Name ?? "";

    private FrameworkElement? FirstBlock =>
        Host.Content is ContentControl { Content: Panel panel } ? panel.Children.OfType<FrameworkElement>().FirstOrDefault() : Host.Content as FrameworkElement;

    /// <summary>Test aid: how visible the first block of the current page is (0 to 1).</summary>
    public double FirstBlockOpacity() => FirstBlock?.Opacity ?? -1;

    public bool FirstBlockHasTransform() => FirstBlock?.RenderTransform is { } t && t != System.Windows.Media.Transform.Identity;

    /// <summary>Test aid (--motion-audit): opacity of the whole page stack (the exit animation fades it) and of every top-level block of the open page.</summary>
    public double PageStackOpacity => PageStack.Opacity;
    public List<FrameworkElement> PageBlocks() => Host.Content is ContentControl { Content: Panel panel } ? panel.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToList() : [];
    public FrameworkElement PageHost => Host;
    public FrameworkElement ShellHeader => HeaderGrid;
    public FrameworkElement ShellToast => Toast;
    public FrameworkElement ShellNav => NavPanel;

    public MainWindow()
    {
        InitializeComponent();
        FlowDirection = Ui.Flow;
        _pages = new Dictionary<string, (RadioButton, Func<PageBase>)>
        {
            ["home"] = (NavHome, () => new HomePage()),
            ["system"] = (NavSystem, () => new SystemPage()),
            ["comfort"] = (NavComfort, () => new ComfortPage()),
            ["profiles"] = (NavProfiles, () => new ProfilesPage()),
            ["gaming"] = (NavGaming, () => new OverlayPage()),
            ["playtime"] = (NavPlaytime, () => new GameLogPage()),
            ["ping"] = (NavPing, () => new NetworkPage()),
            ["dns"] = (NavDns, () => new DnsPage()),
            ["download"] = (NavDownload, () => new DownloadsPage()),
            ["files"] = (NavFiles, () => new FilesPage()),
            ["health"] = (NavHealth, () => new HealthPage()),
            ["tasks"] = (NavTasks, () => new TasksPage()),
            ["mic"] = (NavMic, () => new MicrophonePage()),
            ["weather"] = (NavWeather, () => new WeatherPage()),
            ["support"] = (NavSupport, () => new SupportPage()),
            ["update"] = (NavUpdate, () => new UpdatePage()),
            ["settings"] = (NavSettings, () => new SettingsPage()),
            ["kit"] = (NavKit, () => new KitPage()),
        };

        var hub = Hub.I;
        DryRunPill.Visibility = hub.DryRun ? Visibility.Visible : Visibility.Collapsed;
        // Nobody sees a hidden window, so it is not redrawn every second; showing it again refreshes everything.
        hub.OpenPage += Navigate;
        hub.Changed += () => { if (!Shown) return; _current?.Refresh(); UpdateSide(); };
        hub.Tick += s => { if (!Shown) return; _current?.OnTick(s); UpdateSide(); };
        hub.Second += () => { if (Shown) { _current?.OnSecond(); UpdateSide(); } };
        hub.BirthdayChanged += OnBirthdayChanged;
        hub.BirthdayGreet += () => ShowBirthdayGreeting();
        UpdateBirthdayDecor();
        InitTour();
        IsVisibleChanged += (_, _) => OnShownChanged();
        StateChanged += (_, _) => OnShownChanged();
        _release.Tick += (_, _) =>
        {
            if (Hub.I.Settings.LowMemory)
            {
                // "Use less RAM": pages that were left are thrown away (rebuilt on the next visit). Pages that run their own
                // clock or a live operation stay.
                foreach (var key in _built.Keys.ToList())
                    if (!ReferenceEquals(_built[key], _current) && !KeepBuilt.Contains(key)) _built.Remove(key);
            }
            _ = Task.Run(MemoryCleaner.ReleaseOwn); // off the window's thread, so a page change never stutters
            // once the first release is done the window has settled: one more a little later catches what the settling left behind
            if (Shown) { if (_releases++ == 0) _release.Interval = TimeSpan.FromSeconds(20); else _release.Stop(); }
            else _release.Interval = ReleaseAgain;
        };
        _release.Start(); // the window starts out hidden (and stays so when Shabpa starts in the tray)

        var version = Updater.Current;
        var shown = version.Build == 0 ? $"{version.Major}.{version.Minor}" : version.ToString(3);
        VersionText.Text = L.T($"ساخته‌ی Mr.PitPot · نسخه‌ی {Fa.Digits(shown).Replace('.', '٫')}", $"Made by Mr.PitPot · v{shown}");

        RestorePlacement();
        PreviewKeyDown += OnShortcut;
        _toastTimer.Tick += (_, _) => HideToast();

        LangText.Text = L.En ? "فارسی" : "English";
        ThemeButton.Tag = Ui.Theme.Light ? "sun" : "moon";
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        NavPanel.SizeChanged += (_, _) => PlacePill(false);
        Navigate("home");
        UpdateSide();
        Loaded += (_, _) =>
        {
            PlacePill(false);
            Motion.Stagger(NavPanel.Children.Cast<UIElement>());
        };
    }

    // ---- window placement: it opens where it was left -----------------------------------------

    private void RestorePlacement()
    {
        var parts = Hub.I.Settings.WindowPlacement.Split(',');
        if (parts.Length != 5 || !double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var left)
            || !double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var top)
            || !double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var height)) return;
        // Only if it still fits on the screens there are now (a monitor may have been unplugged).
        var area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var title = new Rect(left + 40, top, Math.Max(10, width - 80), 30);
        if (!area.IntersectsWith(title) || width < MinWidth || height < MinHeight) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        if (parts[4] == "1") WindowState = WindowState.Maximized;
    }

    public void SavePlacement()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (b.IsEmpty || double.IsNaN(b.Left)) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Hub.I.Settings.WindowPlacement = string.Join(',', b.Left.ToString("0", inv), b.Top.ToString("0", inv), b.Width.ToString("0", inv), b.Height.ToString("0", inv),
            WindowState == WindowState.Maximized ? "1" : "0");
        Hub.I.Store.Save();
    }

    // ---- keyboard: Ctrl+1…9 opens the sections in order, Ctrl+, the settings ----------------------

    private void OnShortcut(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // hidden: Ctrl+Shift+K opens the design kit
        if (e.Key == System.Windows.Input.Key.K && System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
        {
            Navigate("kit");
            e.Handled = true;
            return;
        }
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        string? key = e.Key switch
        {
            >= System.Windows.Input.Key.D1 and <= System.Windows.Input.Key.D9 => _pages.Keys.ElementAtOrDefault(e.Key - System.Windows.Input.Key.D1),
            >= System.Windows.Input.Key.NumPad1 and <= System.Windows.Input.Key.NumPad9 => _pages.Keys.ElementAtOrDefault(e.Key - System.Windows.Input.Key.NumPad1),
            System.Windows.Input.Key.OemComma => "settings",
            _ => null,
        };
        if (key is null) return;
        Navigate(key);
        e.Handled = true;
    }

    // ---- in-app notices ----------------------------------------------------------------------------

    private readonly System.Windows.Threading.DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>True when the notice was shown here (the window is open); otherwise the caller uses the tray.</summary>
    public bool ShowToast(string title, string text)
    {
        if (!Shown) return false;
        ToastTitle.Text = title;
        ToastText.Text = text;
        ToastText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Toast.Visibility = Visibility.Visible;
        UiSound.Play(UiSound.Kind.Notice);
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(3 + text.Length / 25.0, 4, 9)); // longer texts stay longer
        _toastTimer.Start();
        ToastMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        ToastMove.Y = 0;
        ToastScale.ScaleX = ToastScale.ScaleY = 1;
        Toast.RenderTransformOrigin = new Point(0.5, 1);
        Toast.BeginAnimation(OpacityProperty, null);
        Toast.Opacity = 1;
        ToastTick.Replay(180); // the tick draws left to right after the pill has sprung in (finished tick when motion is off)
        if (Motion.Enabled)
        {
            var stop = System.Windows.Media.Animation.FillBehavior.Stop;
            Toast.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { FillBehavior = stop });
            ToastMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = SpringEase.I, FillBehavior = stop });
            ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = SpringEase.I, FillBehavior = stop });
            ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = SpringEase.I, FillBehavior = stop });
        }
        return true;
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        if (Toast.Visibility != Visibility.Visible) return;
        if (!Motion.Enabled)
        {
            Toast.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            if (_toastTimer.IsEnabled) return;
            Toast.BeginAnimation(OpacityProperty, null);
            Toast.Opacity = 1;
            Toast.Visibility = Visibility.Collapsed;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
        ToastMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 14, TimeSpan.FromMilliseconds(200)));
    }

    private void OnToastClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HideToast();
        Hub.I.OpenNoticeTarget();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Ui.Chrome(this);
        ApplyIcon();
        DpiChanged += (_, _) => ApplyIcon();
    }

    /// <summary>The title-bar icon. WPF would stretch the first (16 px) frame of the .ico on a scaled screen and look
    /// pixelated, so the frame that matches 16 DIP at this screen's scaling is picked by hand.</summary>
    private void ApplyIcon()
    {
        try
        {
            var wanted = (int)Math.Round(16 * System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/shabpa.ico"),
                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            Icon = decoder.Frames.OrderBy(f => f.PixelWidth < wanted ? int.MaxValue - f.PixelWidth : f.PixelWidth - wanted).First();
        }
        catch
        {
            // keep the icon from the XAML
        }
    }

    private bool Shown => IsVisible && WindowState != WindowState.Minimized;

    private void OnShownChanged()
    {
        Hub.I.WindowShown = Shown;
        if (Shown)
        {
            _release.Stop();
            _releases = 0;
            _release.Interval = ReleaseFirst;
            _current?.Refresh();
            if (_current is not null) _current.OnTick(Hub.I.Snapshot);
            UpdateSide();
        }
        else if (!_release.IsEnabled) _release.Start();
    }

    private static readonly HashSet<string> KeepBuilt = ["mic", "gaming", "settings", "weather", "ping", "dns", "download"];

    private int _releases;

    private PageBase Page(string key)
    {
        if (_built.TryGetValue(key, out var page)) return page;
        page = _pages[key].make();
        page.HeaderChanged += () => { if (ReferenceEquals(page, _current)) UpdateHeader(); };
        _built[key] = page;
        return page;
    }

    /// <summary>"home", "ping", … or "system:rules" for a tab inside «کنترل سیستم».</summary>
    public void Navigate(string key)
    {
        var parts = key.Split(':');
        if (!_pages.TryGetValue(parts[0], out var entry)) return;
        var page = Page(parts[0]);
        if (parts.Length > 1 && page is SystemPage system) system.Select(parts[1]);
        if (parts.Length > 1 && page is TabsPage tabs) tabs.Select(parts[1]);
        if (page is GameLogPage log) log.Open(parts.Length > 1 ? parts[1] : null);
        if (entry.nav.IsChecked != true)
        {
            entry.nav.IsChecked = true; // re-enters through OnNav
            return;
        }
        Show(parts[0], page);
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (_pages is null) return;
        var key = _pages.First(p => ReferenceEquals(p.Value.nav, sender)).Key;
        if (PageUse.Record(Hub.I.State.PageVisits, key)) Hub.I.Store.Save();
        Show(key, Page(key));
    }

    private int _navTicket;

    private void Show(string key, PageBase page)
    {
        var previous = _current;
        _current = page;
        _tour?.PageOpened(key);
        PlacePill(true);
        void Swap()
        {
            UpdateHeader();
            Host.Content = page;
            Scroller.ScrollToTop();
            page.Refresh();
            page.OnTick(Hub.I.Snapshot);
            page.OnSecond();
            Motion.Header(HeaderGrid);
            Motion.Enter(key, page, 1);
            if (Hub.I.Settings.LowMemory)
            {
                _release.Stop();
                _releases = 1;
                _release.Interval = TimeSpan.FromSeconds(3);
                _release.Start();
            }
        }
        // the old page leaves in 170 ms, then the new one rises in (the page transition of the design)
        if (previous is null || ReferenceEquals(previous, page) || !Motion.Enabled || !IsLoaded || !IsVisible) { _navTicket++; Swap(); return; }
        var ticket = ++_navTicket;
        Motion.Leave(PageStack, () => { if (ticket == _navTicket) Swap(); });
    }

    private string ActiveKey => _pages.First(p => p.Value.nav.IsChecked == true).Key;

    /// <summary>Moves the one sliding pill to the checked nav row (spring); instant when <paramref name="animate"/> is false.</summary>
    private void PlacePill(bool animate)
    {
        var active = _pages.Values.Select(p => p.nav).FirstOrDefault(n => n.IsChecked == true);
        if (active is null || !active.IsLoaded || active.ActualHeight <= 0) return;
        var y = active.TransformToAncestor(NavHost).Transform(new Point(0, 0)).Y - active.Margin.Top;
        Pill.Opacity = 1;
        if (animate) Fx.Anim(PillMove, System.Windows.Media.TranslateTransform.YProperty, y, null, 600);
        else
        {
            PillMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            PillMove.Y = y;
        }
        active.BringIntoView();
    }

    public void OpenFirstPingEditor() => ((NetworkPage)Page("ping")).PageOf<PingPage>()?.OpenFirst();

    private void UpdateHeader()
    {
        ColorEmoji.Fill(PageTitle, _current?.PageTitle ?? "");
        PageSub.Text = _current?.PageSubtitle ?? "";
        PageSub.Visibility = PageSub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeaderExtra.Content = _current?.HeaderAccessory;
    }

    // ---- title bar, theme and language -----------------------------------------------------------

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Dark / light: switches live with the reveal growing out of this button (no restart).</summary>
    private void OnThemeClick(object sender, RoutedEventArgs e) => SwitchTheme(Ui.Theme.Light ? "night" : "white", ThemeButton);

    private void OnLangClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!ConfirmWindow.AskAlways(L.T("تغییر زبان", "Change language"),
                L.T("زبان عوض می‌شه و برنامه یه لحظه بسته و دوباره باز می‌شه. چیزی از دست نمی‌ره.", "The language changes and the app restarts for a moment. Nothing is lost."),
                L.T("عوض کن", "Change"), L.T("بیخیال", "Cancel"))) return;
        Hub.I.Settings.Language = L.En ? "fa" : "en";
        Hub.I.Store.Save();
        if (Hub.I.Store.ReadOnly) return;
        (Application.Current as App)?.Restart(page: ActiveKey);
    }

    /// <summary>Scrolls one viewport down; false when already at the bottom. Used by the screenshot run.</summary>
    public bool ScrollPage()
    {
        UpdateLayout();
        if (Scroller.VerticalOffset >= Scroller.ScrollableHeight - 1) return false;
        Scroller.ScrollToVerticalOffset(Math.Min(Scroller.ScrollableHeight, Scroller.VerticalOffset + Scroller.ViewportHeight - 60));
        UpdateLayout();
        return true;
    }

    private void UpdateSide()
    {
        var s = Hub.I.Snapshot;
        SideWatts.Text = s.TotalWatts > 0 ? $"{s.TotalWatts:0} W" : "— W";
        SideNext.Text = Hub.I.NextText();
        // the game that is running, and for how long
        if (s.GameName.Length > 0 && s.GameSince is { } since)
        {
            SideGame.Visibility = Visibility.Visible;
            SideGameName.Text = s.GameName;
            SideGameTime.Text = "▶ " + GameRules.Elapsed(DateTime.Now - since);
        }
        else SideGame.Visibility = Visibility.Collapsed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps the app running in the tray; schedules and rules need it alive.
        var hub = Hub.I;
        e.Cancel = true;
        SavePlacement();
        Hide();
        if (!hub.Settings.TrayNoticeShown)
        {
            hub.Settings.TrayNoticeShown = true;
            hub.Store.Save();
            hub.Notify(L.T("شب‌پا هنوز بیداره", "Shabpa is still running"),
                L.T("برنامه کنار ساعت ویندوز می‌مونه تا زمان‌بندی‌ها اجرا بشن. برای بستن کامل، روی آیکونش راست‌کلیک کن.",
                    "It stays next to the Windows clock so schedules keep working. Right-click its icon to exit completely."));
        }
        base.OnClosing(e);
    }
}
