using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>A sidebar entry whose tabs each hold one page (the same idea as «کنترل سیستم», built in code).</summary>
public abstract class TabsPage : PageBase
{
    // The tab strip is the design's segmented control: a track with one thumb that springs to the chosen tab.
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, FlowDirection = Ui.Flow };
    private readonly Grid _thumb = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly TranslateTransform _thumbMove = new();
    private readonly Grid _strip = new() { FlowDirection = FlowDirection.LeftToRight };
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly Dictionary<string, (RadioButton tab, PageBase page)> _entries = [];
    private readonly string _group = "tabs" + Guid.NewGuid().ToString("N");
    private PageBase? _current;

    protected TabsPage()
    {
        _thumb.RenderTransform = _thumbMove;
        _thumb.Children.Add(new Shadow { Radius = 9, Blur = 10, Spread = -4, OffsetY = 4, Layers = 4, Color = K.ColorOf("ShadowSoftC") });
        _thumb.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Background = Res("Card2Fill"), BorderBrush = Res("CardRim") });
        _strip.Children.Add(_thumb);
        _strip.Children.Add(_tabs);
        var bar = new Border
        {
            Background = Res("Line"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _strip,
        };
        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(_host);
        Content = stack;
        _tabs.SizeChanged += (_, _) => PlaceThumb(false);
        Loaded += (_, _) => PlaceThumb(false);
    }

    private RadioButton? CheckedTab => _entries.Values.Select(e => e.tab).FirstOrDefault(t => t.IsChecked == true);

    /// <summary>Puts the thumb behind the chosen tab (animated with the spring when asked and motion is on).</summary>
    private void PlaceThumb(bool animate)
    {
        var tab = CheckedTab;
        if (tab is null || !tab.IsLoaded || tab.ActualWidth <= 0 || !_strip.IsLoaded) return;
        var x = Fx.Slot(_tabs, tab).X;
        _thumb.Height = tab.ActualHeight;
        if (!animate || !Fx.Enabled || double.IsNaN(_thumb.Width))
        {
            _thumb.Width = tab.ActualWidth;
            _thumbMove.BeginAnimation(TranslateTransform.XProperty, null);
            _thumbMove.X = x;
            return;
        }
        var from = _thumb.Width;
        _thumb.Width = tab.ActualWidth;
        _thumb.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(from, tab.ActualWidth, TimeSpan.FromMilliseconds(520)) { EasingFunction = SpringEase.I, FillBehavior = FillBehavior.Stop });
        Fx.Anim(_thumbMove, TranslateTransform.XProperty, x);
    }

    protected PageBase Current => _current ?? _entries.Values.First().page;

    public override string PageSubtitle => Current.PageSubtitle;
    /// <summary>The open tab's header accessory (for example Health's "scan again") shows in the shell header.</summary>
    public override UIElement? HeaderAccessory => Current.HeaderAccessory;

    protected void Add(string key, string title, PageBase page)
    {
        var content = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        var tab = new RadioButton { Style = St("SegTab"), GroupName = _group, Content = content };
        tab.Checked += (_, _) => { Show(page); PlaceThumb(true); };
        _tabs.Children.Add(tab);
        _entries[key] = (tab, page);
        page.HeaderChanged += () => { if (ReferenceEquals(page, _current)) RaiseHeaderChanged(); };
        if (_current is null)
        {
            _current = page;
            _host.Content = page;
            tab.IsChecked = true;
        }
    }

    public void Select(string key)
    {
        if (_entries.TryGetValue(key, out var entry)) entry.tab.IsChecked = true;
    }

    public T? PageOf<T>() where T : PageBase => _entries.Values.Select(e => e.page).OfType<T>().FirstOrDefault();

    private void Show(PageBase page)
    {
        _current = page;
        _host.Content = page;
        RaiseHeaderChanged();
        if (!IsLoaded) return;
        page.Refresh();
        page.OnTick(Hub.Snapshot);
        page.OnSecond();
        // a tab with several blocks: they rise one after another; otherwise the whole page does
        if (page.Content is Panel { Children.Count: > 1 }) Motion.Enter("", page);
        else Motion.Switch(page);
    }

    public override void Refresh() => Current.Refresh();
    public override void OnTick(SensorSnapshot s) => Current.OnTick(s);
    public override void OnSecond() => Current.OnSecond();
}

/// <summary>«پایداری پینگ»: live traffic and limits, plus the usage history.</summary>
public sealed class NetworkPage : TabsPage
{
    public static readonly string[] TabKeys = ["live", "usage"];

    public override string PageTitle => L.T("پایداری پینگ", "Ping stability");

    public NetworkPage()
    {
        Add("live", L.T("وضعیت زنده", "Live"), new PingPage());
        Add("usage", L.T("تاریخچه‌ی مصرف", "Usage history"), new UsagePage());
    }
}
