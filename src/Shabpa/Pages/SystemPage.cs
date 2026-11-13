using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>«کنترل سیستم»: one sidebar entry whose tabs hold the power-related pages. It is a <see cref="TabsPage"/>, so the tab
/// strip is the design's segmented control with one sliding thumb and the header shows the open tab's subtitle and accessory.</summary>
public sealed class SystemPage : TabsPage
{
    public static readonly string[] TabKeys = ["power", "energy", "history", "optimize", "remote"];

    public override string PageTitle => L.T("کنترل سیستم", "System control");

    public SystemPage()
    {
        // two tabs that did one job became one page each; the old keys still open the right tab (see Aliases)
        Add("power", L.T("خاموشی", "Shutdown"), new StackedPage(PageTitle,
            L.T("خاموش، ری‌استارت یا خواب با یک دستور، یا خودکار وقتی یک شرط پیش اومد.", "Shut down, restart or sleep with one command, or automatically when a condition is met."),
            (L.T("دستور برق", "Power command"), new PowerPage()), (L.T("خاموشی شرطی", "Conditional shutdown"), new RulesPage())));
        Add("energy", L.T("مصرف برق", "Energy"), new EnergyPage());
        Add("history", L.T("خواب و بیداری", "Sleep & wake"), new StackedPage(PageTitle,
            L.T("تاریخچه‌ی خواب و بیداری سیستم و بیدارباش برای صبح.", "When the PC slept and woke, and wake-up alarms for the morning."),
            (L.T("تاریخچه", "History"), new HistoryPage()), (L.T("بیدارباش", "Wake-up"), new WakePage())));
        Add("optimize", L.T("بهینه‌سازی", "Optimise"), new StackedPage(PageTitle,
            L.T("مصرف حافظه و چند تنظیم ویندوز برای بازی، در یک جا.", "Memory use and a few Windows settings for games, in one place."),
            (L.T("حافظه", "Memory"), new MemoryPage()), (L.T("بهینه‌سازی بازی", "Game tuning"), new GameTunePage())));
        Add("remote", L.T("تلگرام", "Telegram"), new RemotePage());
        Aliases["rules"] = "power";
        Aliases["wake"] = "history";
        Aliases["memory"] = "optimize";
        Aliases["tune"] = "optimize";
    }
}
