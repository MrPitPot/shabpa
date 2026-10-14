using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>A sidebar entry whose tabs each hold one page (the same idea as «کنترل سیستم», built in code).</summary>
public abstract class TabsPage : PageBase
{
    private readonly UniformGrid _tabs = new() { Rows = 1 };
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly Dictionary<string, (RadioButton tab, PageBase page)> _entries = [];
    private readonly string _group = "tabs" + Guid.NewGuid().ToString("N");
    private PageBase? _current;

    protected TabsPage()
    {
        var bar = new Border
        {
            Background = Res("Panel"),
            BorderBrush = Res("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 14),
            Child = _tabs,
        };
        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(_host);
        Content = stack;
    }

    protected PageBase Current => _current ?? _entries.Values.First().page;

    public override string PageSubtitle => Current.PageSubtitle;

    protected void Add(string key, string title, PageBase page)
    {
        var content = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        var tab = new RadioButton { Style = St("Tab"), GroupName = _group, Content = content };
        tab.Checked += (_, _) => Show(page);
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
