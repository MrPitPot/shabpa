using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>«کنترل سیستم»: one sidebar entry whose tabs hold the power-related pages. It is a <see cref="TabsPage"/>, so the tab
/// strip is the design's segmented control with one sliding thumb and the header shows the open tab's subtitle and accessory.</summary>
public sealed class SystemPage : TabsPage
{
    public static readonly string[] TabKeys = ["power", "rules", "energy", "history", "wake", "memory", "remote"];

    public override string PageTitle => L.T("کنترل سیستم", "System control");

    public SystemPage()
    {
        Add("power", L.T("دستور برق", "Power"), new PowerPage());
        Add("rules", L.T("خاموشی شرطی", "Rules"), new RulesPage());
        Add("energy", L.T("مصرف برق", "Energy"), new EnergyPage());
        Add("history", L.T("خواب و بیداری", "History"), new HistoryPage());
        Add("wake", L.T("بیدارباش", "Wake-up"), new WakePage());
        Add("memory", L.T("حافظه", "Memory"), new MemoryPage());
        Add("remote", L.T("تلگرام", "Telegram"), new RemotePage());
    }
}
