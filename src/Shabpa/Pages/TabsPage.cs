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
        _thumb.Children.Add(Shadow.Themed(new Shadow { Radius = 9, Blur = 10, Spread = -4, OffsetY = 4, Layers = 4 }, "ShadowSoftC"));
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
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) PlaceThumb(false); };
        // Whatever moved a tab or the page without a size change (language, font, a page re-attached, a monitor change),
        // the thumb is put back under the chosen tab as soon as no spring is running.
        LayoutUpdated += (_, _) => Heal();
    }

    private long _springUntil;

    private void Heal()
    {
        if (Environment.TickCount64 < _springUntil || !IsVisible) return;
        var tab = CheckedTab;
        if (tab is null || !tab.IsLoaded || tab.ActualWidth <= 0 || !_strip.IsLoaded) return;
        var x = Fx.Slot(_tabs, tab).X;
        if (Math.Abs(_thumbMove.X - x) > 0.5 || Math.Abs(_thumb.Width - tab.ActualWidth) > 0.5 || Math.Abs(_thumb.Height - tab.ActualHeight) > 0.5) PlaceThumb(false);
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
        _springUntil = Environment.TickCount64 + 700;
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
        // the thumb springs first; the new tab's own work (Refresh may read the registry, list files...) follows in the next idle
        // slot so a click never waits for it, and clicks in quick succession only do the work of the last one
        tab.Checked += (_, _) => { PlaceThumb(true); Show(page); };
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

    /// <summary>Test aid (<see cref="TabsProbe"/>): where the thumb is drawn and where the chosen tab sits, both in the strip's left-to-right space.</summary>
    internal (double thumbX, double thumbW, double tabX, double tabW, bool animatingX, bool animatingW) ProbeThumb()
    {
        var tab = CheckedTab;
        if (tab is null) return (0, 0, 0, 0, false, false);
        var thumbAt = _thumb.TransformToAncestor(_strip).Transform(new Point(0, 0)).X;
        // a mirrored (right-to-left) element has its origin at its right edge: take the left one of the two corners
        var map = tab.TransformToAncestor(_strip);
        var tabAt = Math.Min(map.Transform(new Point(0, 0)).X, map.Transform(new Point(tab.ActualWidth, 0)).X);
        return (thumbAt, _thumb.ActualWidth, tabAt, tab.ActualWidth,
            DependencyPropertyHelper.GetValueSource(_thumbMove, TranslateTransform.XProperty).IsAnimated,
            DependencyPropertyHelper.GetValueSource(_thumb, FrameworkElement.WidthProperty).IsAnimated);
    }

    internal string ProbeDump()
    {
        var sb = new System.Text.StringBuilder($"tabsW={_tabs.ActualWidth:0.#} stripW={_strip.ActualWidth:0.#} flow={_tabs.FlowDirection} thumbMoveX={_thumbMove.X:0.#};");
        foreach (var e in _entries)
            sb.Append($" [{e.Key} w={e.Value.tab.ActualWidth:0.#} x={e.Value.tab.TransformToAncestor(_strip).Transform(new Point(0, 0)).X:0.#} slot={Fx.Slot(_tabs, e.Value.tab).X:0.#}]");
        return sb.ToString();
    }

    internal IReadOnlyList<string> Keys => [.. _entries.Keys];

    public T? PageOf<T>() where T : PageBase => _entries.Values.Select(e => e.page).OfType<T>().FirstOrDefault();

    private void Show(PageBase page)
    {
        _current = page;
        _host.Content = page;
        RaiseHeaderChanged();
        if (!IsLoaded) return;
        var ticket = ++_showTicket;
        Dispatcher.BeginInvoke(() =>
        {
            if (ticket != _showTicket || !ReferenceEquals(_current, page)) return;   // another tab was chosen meanwhile
            page.Refresh();
            page.OnTick(Hub.Snapshot);
            page.OnSecond();
            // a tab with several blocks: they rise one after another; otherwise the whole page does
            if (page.Content is Panel { Children.Count: > 1 }) Motion.Enter("", page);
            else Motion.Switch(page);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private int _showTicket;

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
