using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Pages;

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
        ["home", .. SystemPage.TabKeys.Select(t => $"system:{t}"), .. NetworkPage.TabKeys.Select(t => $"ping:{t}"), "gaming", "weather", "dns", "download", "comfort", "tasks", .. TasksPage.TabKeys.Select(t => $"tasks:{t}"), "files", .. FilesPage.TabKeys.Select(t => $"files:{t}"), "health", .. HealthPage.TabKeys.Select(t => $"health:{t}"), "profiles", .. ProfilesPage.TabKeys.Select(t => $"profiles:{t}"), "mic", "support", "update", "settings"];

    public FrameworkElement CaptureRoot => Root;

    /// <summary>The name of the page that is open, for the stall log.</summary>
    public string CurrentPage => _current?.GetType().Name ?? "";

    private FrameworkElement? FirstBlock =>
        Host.Content is ContentControl { Content: Panel panel } ? panel.Children.OfType<FrameworkElement>().FirstOrDefault() : Host.Content as FrameworkElement;

    /// <summary>Test aid: how visible the first block of the current page is (0 to 1).</summary>
    public double FirstBlockOpacity() => FirstBlock?.Opacity ?? -1;

    public bool FirstBlockHasTransform() => FirstBlock?.RenderTransform is { } t && t != System.Windows.Media.Transform.Identity;

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
        };

        var hub = Hub.I;
        DryRunPill.Visibility = hub.DryRun ? Visibility.Visible : Visibility.Collapsed;
        // Nobody sees a hidden window, so it is not redrawn every second; showing it again refreshes everything.
        hub.Changed += () => { if (!Shown) return; _current?.Refresh(); UpdateSide(); };
        hub.Tick += s => { if (!Shown) return; _current?.OnTick(s); UpdateSide(); };
        hub.Second += () => { if (Shown) { _current?.OnSecond(); UpdateSide(); } };
        IsVisibleChanged += (_, _) => OnShownChanged();
        StateChanged += (_, _) => OnShownChanged();
        _release.Tick += (_, _) =>
        {
            _ = Task.Run(MemoryCleaner.ReleaseOwn); // off the window's thread, so a page change never stutters
            if (Shown) _release.Stop(); // "Use less RAM": one release after each page change while the window is open
            else _release.Interval = ReleaseAgain;
        };
        _release.Start(); // the window starts out hidden (and stays so when Shabpa starts in the tray)

        var version = Updater.Current;
        var shown = version.Build == 0 ? $"{version.Major}.{version.Minor}" : version.ToString(3);
        VersionText.Text = L.T($"ساخته‌ی Mr.PitPot · نسخه‌ی {Fa.Digits(shown).Replace('.', '٫')}", $"Made by Mr.PitPot · v{shown}");

        RestorePlacement();
        PreviewKeyDown += OnShortcut;
        _toastTimer.Tick += (_, _) => HideToast();

        Navigate("home");
        UpdateSide();
        Loaded += (_, _) => Motion.Stagger(NavPanel.Children.Cast<UIElement>());
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
        if (Motion.Enabled)
        {
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            Toast.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            ToastMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        }
        else Toast.Opacity = 1;
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
        fade.Completed += (_, _) => { if (!_toastTimer.IsEnabled) Toast.Visibility = Visibility.Collapsed; };
        Toast.BeginAnimation(OpacityProperty, fade);
        ToastMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(10, TimeSpan.FromMilliseconds(200)));
    }

    private void OnToastClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => HideToast();

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
        if (Shown)
        {
            _release.Stop();
            _release.Interval = ReleaseFirst;
            _current?.Refresh();
            if (_current is not null) _current.OnTick(Hub.I.Snapshot);
            UpdateSide();
        }
        else if (!_release.IsEnabled) _release.Start();
    }

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
        Show(key, Page(key));
    }

    private void Show(string key, PageBase page)
    {
        _current = page;
        UpdateHeader();
        Host.Content = page;
        Scroller.ScrollToTop();
        page.Refresh();
        page.OnTick(Hub.I.Snapshot);
        page.OnSecond();
        Motion.Header(PageTitle, PageSub);
        Motion.Enter(key, page);
        if (Hub.I.Settings.LowMemory)
        {
            _release.Stop();
            _release.Interval = TimeSpan.FromSeconds(3);
            _release.Start();
        }
    }

    public void OpenFirstPingEditor() => ((NetworkPage)Page("ping")).PageOf<PingPage>()?.OpenFirst();

    private void UpdateHeader()
    {
        PageTitle.Text = _current?.PageTitle ?? "";
        PageSub.Text = _current?.PageSubtitle ?? "";
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
