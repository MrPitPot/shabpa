using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;
using Shabpa.Tour;

namespace Shabpa;

/// <summary>The guided tour of the window: starts it the first time (or offers it to someone who updated), finds the
/// controls its steps point at, and removes everything again when it ends. The tour itself lives in Tour/.</summary>
public partial class MainWindow : ITourHost
{
    private TourOverlay? _tour;
    private GlassCard? _offer;
    private bool _tourChecked;

    /// <summary>Test aid: the tour that is on screen (null otherwise).</summary>
    public TourOverlay? ActiveTour => _tour;

    /// <summary>Test aid: the key of the page that is open, and how many tour layers are left on the window.</summary>
    public string ActiveTourPage => ActiveKey;
    public int CaptureRootOverlays => Root.Children.OfType<TourOverlay>().Count();

    /// <summary>Test aid: the small "want a tour?" card.</summary>
    public FrameworkElement? TourOfferCard => _offer;

    Window ITourHost.Window => this;
    Panel ITourHost.Surface => Root;
    string ITourHost.ActiveKey => ActiveKey;
    void ITourHost.GoTo(string pageKey) => Navigate(pageKey);

    private void InitTour()
    {
        IsVisibleChanged += (_, _) => _ = TourFirstLookAsync();
        Loaded += (_, _) => _ = TourFirstLookAsync();
    }

    /// <summary>The first time the window really shows: a new install gets the tour, an updated one is only offered it, once.
    /// Never in demo and screenshot runs (their data is read-only).</summary>
    private async Task TourFirstLookAsync()
    {
        if (_tourChecked || !IsVisible || !IsLoaded || WindowState == WindowState.Minimized) return;
        _tourChecked = true;
        var hub = Hub.I;
        if (hub.Demo || hub.Store.ReadOnly) return;
        var decision = TourState.Decide(hub.Settings.TourStatus, hub.Store.FreshInstall);
        if (decision == TourStart.None) return;
        await Task.Delay(1400); // the menu and the first page finish arriving first
        if (!IsVisible || WindowState == WindowState.Minimized) { _tourChecked = false; return; } // closed to the tray meanwhile: look again next time
        if (_tour is not null || _offer is not null) return;
        if (decision == TourStart.Run)
        {
            hub.Settings.TourStatus = TourState.Pending;
            hub.Store.Save();
            StartTour();
            return;
        }
        // an existing user: a small card, shown once, and not on top of a notice
        while (Toast.Visibility == Visibility.Visible && IsVisible) await Task.Delay(500);
        hub.Settings.TourStatus = TourState.Offered;
        hub.Store.Save();
        ShowTourOffer();
    }

    /// <summary>Starts the tour from its first step (first run, the offer card, or the button in Settings).</summary>
    public void StartTour()
    {
        if (_tour is not null) return;
        HideTourOffer();
        if (!Shown) { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; }
        var session = new TourSession(TourSteps.All);
        _tour = new TourOverlay(this, session, how =>
        {
            _tour = null;
            TourSession.Record(Hub.I.Settings, how);
            Hub.I.Store.Save();
            Navigate("home");
        });
        _tour.Open();
    }

    private void HideTourOffer()
    {
        if (_offer is not { } card) return;
        _offer = null;
        if (!Motion.Enabled) { Root.Children.Remove(card); return; }
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => Root.Children.Remove(card);
        card.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>The small card for people who updated: it asks, never starts anything by itself.</summary>
    public void ShowTourOffer()
    {
        if (_offer is not null || _tour is not null) return;
        var (card, body) = K.Card();
        card.Margin = new Thickness(0, 0, 22, 44);
        card.Padding = new Thickness(20, 16, 20, 14);
        card.MaxWidth = 340;
        card.HorizontalAlignment = HorizontalAlignment.Right; // flow-relative: the end side in both languages
        card.VerticalAlignment = VerticalAlignment.Bottom;
        card.Elevated = true;
        card.Background = TourOverlay.OpaqueCard();
        Panel.SetZIndex(card, 800);
        body.Children.Add(new TextBlock { Text = L.T("تور آموزشی می‌خوای؟", "Want a quick tour?"), Style = K.Style("H2") });
        body.Children.Add(new TextBlock
        {
            Text = L.T("یه تور یک‌دقیقه‌ای نشونت می‌ده هر بخش کجاست و به چه دردی می‌خوره. اجباری نیست.",
                "A one-minute tour shows where everything is and what it is for. Totally optional."),
            Style = K.Style("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        var yes = new Button { Style = K.Style("BtnPrimary"), Content = L.T("آره، نشونم بده", "Yes, show me"), Padding = new Thickness(16, 8, 16, 8) };
        var no = new Button { Style = K.Style("Btn"), Content = L.T("نه، ممنون", "No thanks"), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(8, 0, 0, 0) };
        yes.Click += (_, _) => StartTour();
        no.Click += (_, _) =>
        {
            Hub.I.Settings.TourStatus = TourState.Declined;
            Hub.I.Store.Save();
            HideTourOffer();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(yes);
        row.Children.Add(no);
        body.Children.Add(row);
        _offer = card;
        Root.Children.Add(card);
        if (Motion.Enabled) Motion.Rise(card, 0, 24, 0.7);
    }

    // ---- what the steps point at -------------------------------------------------------------------

    FrameworkElement? ITourHost.Resolve(string target)
    {
        if (target == TourTargets.Theme) return ThemeButton;
        if (target == TourTargets.Language) return LangChip;
        if (target == TourTargets.Sidebar)
        {
            // the whole menu column: the scrolling menu and the live card and language chip under it
            for (DependencyObject? d = NavHost; d is not null; d = VisualTreeHelper.GetParent(d))
                if (d is ScrollViewer { Parent: FrameworkElement column }) return column;
            return null;
        }
        if (target.StartsWith(TourTargets.NavPrefix, StringComparison.Ordinal))
        {
            if (!_pages.TryGetValue(target[TourTargets.NavPrefix.Length..], out var entry)) return null;
            if (entry.nav.IsLoaded) entry.nav.BringIntoView();
            return entry.nav;
        }
        if (target.StartsWith(TourTargets.PagePrefix, StringComparison.Ordinal) && int.TryParse(target.AsSpan(TourTargets.PagePrefix.Length), out var n))
            return Host.Content is not null ? PageBlocks().ElementAtOrDefault(n) : null;
        if (target.StartsWith(TourTargets.IdPrefix, StringComparison.Ordinal))
        {
            var id = target[TourTargets.IdPrefix.Length..];
            var found = Host.Content is DependencyObject page ? FindById(page, id) : null;
            if (found is not null && !_tourScrolled.Contains(id)) { _tourScrolled.Add(id); found.BringIntoView(); }
            return found;
        }
        return null;
    }

    private readonly HashSet<string> _tourScrolled = [];

    private static FrameworkElement? FindById(DependencyObject root, string id)
    {
        if (root is FrameworkElement fe && AutomationProperties.GetAutomationId(fe) == id) return fe;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindById(VisualTreeHelper.GetChild(root, i), id) is { } hit) return hit;
        return null;
    }
}
