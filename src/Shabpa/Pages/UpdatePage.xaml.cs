using System.Windows;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class UpdatePage : PageBase
{
    public override string PageTitle => L.T("به‌روزرسانی", "Updates");
    public override string PageSubtitle => L.T("نسخه‌ی جدید رو ببین و با یه کلیک نصبش کن.", "See what is new and install it in one click.");

    private static Updater Updater => Hub.Updater;

    public UpdatePage()
    {
        InitializeComponent();
        AutoCheck.IsChecked = Hub.Settings.AutoUpdateCheck;
        Updater.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    private static string Version(Version v) => Fa.Digits(v.Build == 0 ? $"{v.Major}.{v.Minor}" : v.ToString(3)).Replace('.', L.En ? '.' : '٫');

    public override void Refresh()
    {
        var u = Updater;
        TitleText.Text = L.T($"شب‌پا {Version(Updater.Current)} نصبه", $"Shabpa {Version(Updater.Current)} is installed");
        var available = u.Available;
        var checkedAt = u.LastCheck is { } at ? L.T($" (آخرین بررسی ساعت {Fa.Clock(at)})", $" (last checked at {Fa.Clock(at)})") : "";

        (StatusText.Text, var tint, var glyph) = u.State switch
        {
            UpdateState.NotConfigured => (L.T("منبع به‌روزرسانی هنوز تنظیم نشده.", "No update source is set yet."), "Gold", ""),
            UpdateState.Checking => (L.T("در حال بررسی…", "Checking…"), "Accent", ""),
            UpdateState.UpToDate => (L.T("آخرین نسخه نصبه.", "You have the latest version.") + checkedAt, "Green", ""),
            UpdateState.Available when available is not null => (L.T($"نسخه‌ی {Version(available.Version)} آماده‌ست ({Fa.Date(available.Published)}).", $"Version {Version(available.Version)} is ready ({Fa.Date(available.Published)})."), "Gold", ""),
            UpdateState.Downloading => (L.T($"در حال دانلود… {Fa.N(u.Progress * 100, 0)}٪", $"Downloading… {u.Progress * 100:0}%"), "Accent", ""),
            UpdateState.Installing => (L.T("نصب شروع شد؛ شب‌پا یه لحظه بسته و دوباره باز می‌شه.", "Installing; Shabpa will close and reopen in a moment."), "Green", ""),
            UpdateState.Error => (u.Error, "Red", ""),
            _ => (L.T("هنوز بررسی نشده.", "Not checked yet."), "Accent", ""),
        };
        if (u.State == UpdateState.Available && u.Error.Length > 0) StatusText.Text += " " + u.Error;
        StatusBadge.Background = Res(tint + "Soft");
        StatusIcon.Foreground = Res(tint);
        StatusIcon.Text = glyph;

        var busy = u.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
        CheckButton.IsEnabled = !busy && u.State != UpdateState.NotConfigured;
        InstallButton.Visibility = available is not null && u.State is UpdateState.Available ? Visibility.Visible : Visibility.Collapsed;
        if (available is not null) InstallButton.Content = L.T($"نصب نسخه‌ی {Version(available.Version)}", $"Install {Version(available.Version)}");
        Progress.Visibility = u.State == UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = u.Progress;

        NotesPanel.Visibility = available is not null && available.Notes.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (available is not null)
        {
            NotesTitle.Text = L.T($"تغییرات نسخه‌ی {Version(available.Version)}", $"What is new in {Version(available.Version)}");
            NotesText.Text = available.Notes.Trim();
        }
    }

    private async void OnCheck(object sender, RoutedEventArgs e) => await Updater.CheckAsync();

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (Hub.Demo) return;
        if (await Updater.DownloadAndInstallAsync(Hub.Elevated) is null)
            Hub.Notify(L.T("به‌روزرسانی", "Update"), L.T("نصب شروع شد؛ شب‌پا یه لحظه بسته و دوباره باز می‌شه.", "Installing; Shabpa will close and reopen in a moment."));
    }

    private void OnAuto(object sender, RoutedEventArgs e)
    {
        Hub.Settings.AutoUpdateCheck = AutoCheck.IsChecked == true;
        Hub.Store.Save();
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true }); }
        catch { /* no browser registered */ }
    }
}
