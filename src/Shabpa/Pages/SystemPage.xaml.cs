using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>«کنترل سیستم»: one sidebar entry whose tabs hold the power-related pages.</summary>
public partial class SystemPage : PageBase
{
    public static readonly string[] TabKeys = ["power", "rules", "energy", "history", "wake", "memory", "remote"];

    private readonly Dictionary<string, (RadioButton tab, PageBase page)> _tabs = [];
    private PageBase _current;

    public override string PageTitle => L.T("کنترل سیستم", "System control");
    public override string PageSubtitle => _current.PageSubtitle;

    public SystemPage()
    {
        InitializeComponent();
        Add("power", "", L.T("دستور برق", "Power"), new PowerPage());
        Add("rules", "", L.T("خاموشی شرطی", "Rules"), new RulesPage());
        Add("energy", "", L.T("مصرف برق", "Energy"), new EnergyPage());
        Add("history", "", L.T("خواب و بیداری", "History"), new HistoryPage());
        Add("wake", "", L.T("بیدارباش", "Wake-up"), new WakePage());
        Add("memory", "", L.T("حافظه", "Memory"), new MemoryPage());
        Add("remote", "", L.T("تلگرام", "Telegram"), new RemotePage());
        _current = _tabs["power"].page;
        _tabs["power"].tab.IsChecked = true;
    }

    private void Add(string key, string glyph, string title, PageBase page)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, Style = St("Icon"), FontSize = 13, Margin = new Thickness(0, 0, 7, 0) });
        content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });

        var tab = new RadioButton { Style = St("Tab"), GroupName = "systemTabs", Content = content };
        tab.Checked += (_, _) => Show(page);
        Tabs.Children.Add(tab);
        _tabs[key] = (tab, page);
    }

    public void Select(string key)
    {
        if (_tabs.TryGetValue(key, out var entry)) entry.tab.IsChecked = true;
    }

    private void Show(PageBase page)
    {
        _current = page;
        TabHost.Content = page;
        RaiseHeaderChanged();
        if (!IsLoaded) return;
        page.Refresh();
        page.OnTick(Hub.Snapshot);
        page.OnSecond();
    }

    public override void Refresh() => _current.Refresh();
    public override void OnTick(SensorSnapshot s) => _current.OnTick(s);
    public override void OnSecond() => _current.OnSecond();
}
