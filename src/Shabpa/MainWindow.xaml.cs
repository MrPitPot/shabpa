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
        ["home", .. SystemPage.TabKeys.Select(t => $"system:{t}"), .. NetworkPage.TabKeys.Select(t => $"ping:{t}"), "gaming", "weather", "dns", "download", "comfort", "support", "update", "settings"];

    public FrameworkElement CaptureRoot => Root;

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
            ["ping"] = (NavPing, () => new NetworkPage()),
            ["gaming"] = (NavGaming, () => new OverlayPage()),
            ["weather"] = (NavWeather, () => new WeatherPage()),
            ["dns"] = (NavDns, () => new DnsPage()),
            ["download"] = (NavDownload, () => new DownloadsPage()),
            ["comfort"] = (NavComfort, () => new ComfortPage()),
            ["support"] = (NavSupport, () => new SupportPage()),
            ["update"] = (NavUpdate, () => new UpdatePage()),
            ["settings"] = (NavSettings, () => new SettingsPage()),
        };

        var hub = Hub.I;
        DryRunPill.Visibility = hub.DryRun ? Visibility.Visible : Visibility.Collapsed;
        // Nobody sees a hidden window, so it is not redrawn every second; showing it again refreshes everything.
        hub.Changed += () => { if (!Shown) return; _current?.Refresh(); UpdateSide(); };
        hub.Tick += s => { if (!Shown) return; _current?.OnTick(s); UpdateSide(); };
        hub.Second += () => { if (Shown) _current?.OnSecond(); };
        IsVisibleChanged += (_, _) => OnShownChanged();
        StateChanged += (_, _) => OnShownChanged();
        _release.Tick += (_, _) =>
        {
            _release.Interval = ReleaseAgain;
            MemoryCleaner.ReleaseOwn();
        };
        _release.Start(); // the window starts out hidden (and stays so when Shabpa starts in the tray)

        var version = Updater.Current;
        var shown = version.Build == 0 ? $"{version.Major}.{version.Minor}" : version.ToString(3);
        VersionText.Text = L.T($"ساخته‌ی Mr.PitPot · نسخه‌ی {Fa.Digits(shown).Replace('.', '٫')}", $"Made by Mr.PitPot · v{shown}");

        Navigate("home");
        UpdateSide();
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
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps the app running in the tray; schedules and rules need it alive.
        var hub = Hub.I;
        e.Cancel = true;
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
