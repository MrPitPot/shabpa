using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Pages;

namespace Shabpa;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, (RadioButton nav, PageBase page)> _pages;
    private PageBase? _current;

    /// <summary>Every screen worth a screenshot, including the tabs inside «کنترل سیستم».</summary>
    public IEnumerable<string> CaptureKeys =>
        ["home", .. SystemPage.TabKeys.Select(t => $"system:{t}"), "ping", "update", "settings"];

    public FrameworkElement CaptureRoot => Root;

    public MainWindow()
    {
        InitializeComponent();
        FlowDirection = Ui.Flow;
        _pages = new Dictionary<string, (RadioButton, PageBase)>
        {
            ["home"] = (NavHome, new HomePage()),
            ["system"] = (NavSystem, new SystemPage()),
            ["ping"] = (NavPing, new PingPage()),
            ["update"] = (NavUpdate, new UpdatePage()),
            ["settings"] = (NavSettings, new SettingsPage()),
        };
        foreach (var (_, page) in _pages.Values)
            page.HeaderChanged += () => { if (ReferenceEquals(page, _current)) UpdateHeader(); };

        var hub = Hub.I;
        DryRunPill.Visibility = hub.DryRun ? Visibility.Visible : Visibility.Collapsed;
        hub.Changed += () => { _current?.Refresh(); UpdateSide(); };
        hub.Tick += s => { _current?.OnTick(s); UpdateSide(); };
        hub.Second += () => { if (IsVisible) _current?.OnSecond(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) _current?.Refresh(); };

        Navigate("home");
        UpdateSide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Ui.Chrome(this);
    }

    /// <summary>"home", "ping", … or "system:rules" for a tab inside «کنترل سیستم».</summary>
    public void Navigate(string key)
    {
        var parts = key.Split(':');
        if (!_pages.TryGetValue(parts[0], out var entry)) return;
        if (parts.Length > 1 && entry.page is SystemPage system) system.Select(parts[1]);
        if (entry.nav.IsChecked != true)
        {
            entry.nav.IsChecked = true; // re-enters through OnNav
            return;
        }
        Show(entry.page);
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (_pages is null) return;
        Show(_pages.Values.First(p => ReferenceEquals(p.nav, sender)).page);
    }

    private void Show(PageBase page)
    {
        _current = page;
        UpdateHeader();
        Host.Content = page;
        Scroller.ScrollToTop();
        page.Refresh();
        page.OnTick(Hub.I.Snapshot);
        page.OnSecond();
    }

    public void OpenFirstPingEditor() => ((PingPage)_pages["ping"].page).OpenFirst();

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
