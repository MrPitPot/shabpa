using System.Windows;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class MemoryPage : PageBase
{
    public override string PageTitle => L.T("حافظه", "Memory");
    public override string PageSubtitle => L.T("ببین رم چقدر پره و هر وقت خواستی خالی‌اش کن.", "See how full the RAM is and free it up when you want.");

    public MemoryPage()
    {
        InitializeComponent();
        Chips(AutoChips, [(0, L.T("خاموش", "Off")), (80, L.T("۸۰٪", "80%")), (90, L.T("۹۰٪", "90%")), (95, L.T("۹۵٪", "95%"))], Hub.Settings.AutoTrimPercent, v =>
        {
            Hub.Settings.AutoTrimPercent = v;
            Hub.Store.Save();
        });
        Hub.MemoryCleaned += OnCleaned;
    }

    public override void Refresh()
    {
        CacheButton.IsEnabled = Hub.CanLimit && !Hub.Cleaning;
        TrimButton.IsEnabled = !Hub.Cleaning;
        CacheHint.Text = L.T("حافظه‌ای که ویندوز «محض احتیاط» از فایل‌ها و برنامه‌های قبلی نگه داشته رو آزاد می‌کنه.", "Releases the memory Windows keeps “just in case” from earlier files and apps.")
                         + (Hub.CanLimit ? "" : " " + L.T("به دسترسی ادمین نیاز داره (دکمه‌اش توی «پایداری پینگ» هست).", "Needs admin rights (the button is on the “Ping stability” page)."));
        Show();
    }

    public override void OnTick(SensorSnapshot s) => Show();

    private void Show()
    {
        var memory = Hub.Memory;
        if (memory.Total <= 0) return;
        UsedText.Text = $"{memory.UsedPercent:0}%";
        TotalText.Text = $"{Fa.Bytes(memory.Used)} / {Fa.Bytes(memory.Total)}";
        UsedBar.Value = memory.UsedPercent;
        UsedBar.Foreground = Res(memory.UsedPercent >= 90 ? "Red" : memory.UsedPercent >= 75 ? "Gold" : "Accent");
        FreeText.Text = L.T($"آزاد {Fa.Bytes(memory.Available)}", $"Free {Fa.Bytes(memory.Available)}");
        CacheText.Text = L.T($"کش {Fa.Bytes(memory.Standby)}", $"Cache {Fa.Bytes(memory.Standby)}");
    }

    private void OnTrim(object sender, RoutedEventArgs e) => Start(false);
    private void OnCache(object sender, RoutedEventArgs e) => Start(true);

    private void Start(bool cache)
    {
        TrimButton.IsEnabled = CacheButton.IsEnabled = false;
        ResultText.Visibility = Visibility.Visible;
        ResultText.Foreground = Res("Muted");
        ResultText.Text = L.T("در حال خالی کردن…", "Working…");
        Hub.CleanMemory(cache);
    }

    private void OnCleaned(MemoryResult result, bool cache)
    {
        ResultText.Visibility = Visibility.Visible;
        if (result.Error is { } error)
        {
            ResultText.Foreground = Res("Red");
            ResultText.Text = error == "needs-admin"
                ? L.T("خالی کردن کش به دسترسی ادمین نیاز داره.", "Clearing the cache needs admin rights.")
                : L.T($"انجام نشد: {error}", $"Failed: {error}");
        }
        else
        {
            ResultText.Foreground = Res("Green");
            ResultText.Text = cache
                ? L.T($"کش رم خالی شد؛ {Fa.Bytes(result.Freed)} آزاد شد.", $"RAM cache cleared; {Fa.Bytes(result.Freed)} freed.")
                : L.T($"{Fa.Bytes(result.Freed)} رم آزاد شد ({Fa.Digits(result.Processes.ToString())} برنامه سبک شدن).", $"{Fa.Bytes(result.Freed)} of RAM freed ({result.Processes} processes trimmed).");
        }
        Refresh();
    }
}
