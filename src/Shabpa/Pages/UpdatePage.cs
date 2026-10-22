using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«به‌روزرسانی»: the installed version, checking, one-click install, release notes and the update settings.</summary>
public sealed class UpdatePage : PageBase
{
    public override string PageTitle => L.T("به‌روزرسانی", "Updates");
    public override string PageSubtitle => L.T("نسخه‌ی جدید رو ببین و با یه کلیک نصبش کن.", "See what is new and install it in one click.");
    public override UIElement? HeaderAccessory => _badge;

    private static Updater Updater => Hub.Updater;

    private readonly StatusBadge _badge = new("", "Accent", "refresh");
    private readonly IconBadge _statusBadge = new("refresh", "Accent", 52, 26, 15);
    private readonly TextBlock _title = new() { FontSize = 19, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    private readonly Button _check = new() { Style = St("Btn"), Padding = new Thickness(18, 9, 18, 9) };
    private readonly Button _install = new() { Style = St("BtnPrimary"), Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _installText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _progress = new() { Style = St("Bar"), Margin = new Thickness(0, 18, 0, 0), Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly GlassCard _notesCard = new() { Padding = new Thickness(24, 20, 24, 20), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _notesTitle = new() { Style = St("H2") };
    private readonly TextBlock _notesText = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Switch _auto = new();
    private UpdateState? _lastState;
    private bool _installShown, _notesShown;

    public UpdatePage()
    {
        GroupC.EnsureIcons();
        var root = new StackPanel();

        // status card: big badge, version headline, state line, actions, download progress
        var status = new GlassCard { Padding = new Thickness(26, 22, 26, 22), Margin = new Thickness(0, 12, 0, 0) };
        var inner = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_statusBadge);
        var texts = new StackPanel { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        _title.Foreground = Res("Text");
        texts.Children.Add(_title);
        texts.Children.Add(_status);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var checkRow = new StackPanel { Orientation = Orientation.Horizontal };
        var checkIcon = new Icon("refresh", 17) { Margin = new Thickness(0, 0, 8, 0) };
        checkRow.Children.Add(checkIcon);
        checkRow.Children.Add(new TextBlock { Text = L.T("بررسی", "Check now"), VerticalAlignment = VerticalAlignment.Center });
        _check.Content = checkRow;
        _check.Click += async (_, _) =>
        {
            CMo.Spin(checkIcon);
            await Updater.CheckAsync();
        };
        var installRow = new StackPanel { Orientation = Orientation.Horizontal };
        installRow.Children.Add(new Icon("dl", 18) { Margin = new Thickness(0, 0, 8, 0) });
        installRow.Children.Add(_installText);
        _install.Content = installRow;
        _install.Click += OnInstall;
        actions.Children.Add(_check);
        actions.Children.Add(_install);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        inner.Children.Add(grid);
        inner.Children.Add(_progress);
        status.Child = inner;
        root.Children.Add(status);

        // release notes (only when the available version has some)
        var notes = new StackPanel();
        notes.Children.Add(_notesTitle);
        notes.Children.Add(_notesText);
        _notesCard.Child = notes;
        root.Children.Add(_notesCard);

        // settings
        var (settings, body) = K.Card(L.T("تنظیمات به‌روزرسانی", "Update settings"));
        _auto.Click += (_, _) =>
        {
            Hub.Settings.AutoUpdateCheck = _auto.IsChecked == true;
            Hub.Store.Save();
        };
        _auto.IsChecked = Hub.Settings.AutoUpdateCheck;
        body.Children.Add(K.SettingRow(L.T("خودش هر چند ساعت بررسی کنه و خبر بده", "Check every few hours and let me know"), _auto, first: true));
        var open = new Button { Style = St("Btn"), Padding = new Thickness(16, 8, 16, 8) };
        var openRow = new StackPanel { Orientation = Orientation.Horizontal };
        openRow.Children.Add(new Icon("ext", 16) { Margin = new Thickness(0, 0, 8, 0) });
        openRow.Children.Add(new TextBlock { Text = L.T("صفحه‌ی نسخه‌ها در GitHub", "Releases on GitHub"), VerticalAlignment = VerticalAlignment.Center });
        open.Content = openRow;
        open.Click += OnOpenReleases;
        body.Children.Add(K.SettingRow(L.T("نسخه‌های جدید مستقیم از صفحه‌ی رسمی شب‌پا در GitHub گرفته می‌شن؛ لازم نیست لینکی وارد کنی.", "New versions come straight from Shabpa’s official GitHub page; there is no link to enter."), open));
        settings.Padding = new Thickness(24, 20, 24, 12);
        root.Children.Add(settings);

        root.Children.Add(new TextBlock
        {
            Style = St("Label"), Margin = new Thickness(6, 14, 6, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 20,
            Text = L.T("از ربات تلگرام هم با دستور «به‌روزرسانی» می‌شه از راه دور به‌روزش کرد (اگه شب‌پا با دسترسی ادمین اجرا شده باشه). فایل نصبی قبل از اجرا با نسخه‌ی منتشرشده مقایسه می‌شه؛ اگه یکی نباشه نصب نمی‌شه. تنظیمات و تاریخچه‌ات دست نمی‌خوره.",
                "You can also update remotely by sending “Update” to the Telegram bot (when Shabpa runs as administrator). The installer is checked against the published file before it runs; if they differ it is not installed. Your settings and history are kept."),
        });
        Content = root;
        CMo.CascadeOnLoad(this, root);

        Loaded += (_, _) => { Updater.Changed -= OnUpdaterChanged; Updater.Changed += OnUpdaterChanged; Refresh(); };
        Unloaded += (_, _) => Updater.Changed -= OnUpdaterChanged;
        Refresh();
    }

    private static string Version(Version v) => Fa.Digits(v.Build == 0 ? $"{v.Major}.{v.Minor}" : v.ToString(3)).Replace('.', L.En ? '.' : '٫');

    private void OnUpdaterChanged() => Dispatcher.BeginInvoke(Refresh);

    public override void Refresh()
    {
        var u = Updater;
        _title.Text = L.T($"شب‌پا {Version(Updater.Current)} نصبه", $"Shabpa {Version(Updater.Current)} is installed");
        var available = u.Available;
        var checkedAt = u.LastCheck is { } at ? L.T($" (آخرین بررسی ساعت {Fa.Clock(at)})", $" (last checked at {Fa.Clock(at)})") : "";

        var (text, tint, icon, badge) = u.State switch
        {
            UpdateState.NotConfigured => (L.T("منبع به‌روزرسانی هنوز تنظیم نشده.", "No update source is set yet."), "HueC", "warn", L.T("تنظیم نشده", "Not set up")),
            UpdateState.Checking => (L.T("در حال بررسی…", "Checking…"), "HueA", "refresh", L.T("در حال بررسی", "Checking")),
            UpdateState.UpToDate => (L.T("آخرین نسخه نصبه.", "You have the latest version.") + checkedAt, "HueB", "chk", L.T("به‌روز", "Up to date")),
            UpdateState.Available when available is not null => (L.T($"نسخه‌ی {Version(available.Version)} آماده‌ست ({Fa.Date(available.Published)}).", $"Version {Version(available.Version)} is ready ({Fa.Date(available.Published)})."), "HueC", "dl", L.T("نسخه‌ی جدید", "Update ready")),
            UpdateState.Downloading => (L.T($"در حال دانلود… {Fa.N(u.Progress * 100, 0)}٪", $"Downloading… {u.Progress * 100:0}%"), "HueA", "dl", L.T("در حال دانلود", "Downloading")),
            UpdateState.Installing => (L.T("نصب شروع شد؛ شب‌پا یه لحظه بسته و دوباره باز می‌شه.", "Installing; Shabpa will close and reopen in a moment."), "HueB", "bolt", L.T("در حال نصب", "Installing")),
            UpdateState.Error => (u.Error, "HueE", "warn", L.T("خطا", "Error")),
            _ => (L.T("هنوز بررسی نشده.", "Not checked yet."), "HueA", "clk", L.T("بررسی نشده", "Not checked")),
        };
        if (u.State == UpdateState.Available && u.Error.Length > 0) text += " " + u.Error;
        var stateChanged = _lastState is not null && _lastState != u.State;
        _lastState = u.State;
        _status.Text = text;
        if (stateChanged) CMo.Flash(_status);
        _statusBadge.Tint(tint);
        if (_statusBadge.Child is Icon glyph) glyph.Glyph = icon;
        if (stateChanged && _statusBadge.IsLoaded) Motion.Pop(_statusBadge);
        var accent = tint switch { "HueB" => "Green", "HueC" => "Gold", "HueE" => "Red", _ => "Accent" };
        _badge.Set(badge, accent, icon);

        var busy = u.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
        _check.IsEnabled = !busy && u.State != UpdateState.NotConfigured;
        var showInstall = available is not null && u.State is UpdateState.Available;
        _install.Visibility = showInstall ? Visibility.Visible : Visibility.Collapsed;
        if (showInstall && !_installShown && IsLoaded) CMo.Appear(_install, 0, 8, 0.5);
        _installShown = showInstall;
        if (available is not null) _installText.Text = L.T($"نصب نسخه‌ی {Version(available.Version)}", $"Install {Version(available.Version)}");
        var downloading = u.State == UpdateState.Downloading;
        var beforeProgress = _progress.Visibility == Visibility.Visible ? _progress.Value : 0;
        _progress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        _progress.Value = u.Progress;
        if (downloading && IsLoaded) CMo.Fill(_progress, beforeProgress, 450);

        var showNotes = available is not null && available.Notes.Trim().Length > 0;
        _notesCard.Visibility = showNotes ? Visibility.Visible : Visibility.Collapsed;
        if (showNotes && !_notesShown && IsLoaded) CMo.Appear(_notesCard, 0, 14, 0.6);
        _notesShown = showNotes;
        if (available is not null)
        {
            _notesTitle.Text = L.T($"تغییرات نسخه‌ی {Version(available.Version)}", $"What is new in {Version(available.Version)}");
            _notesText.Text = available.Notes.Trim();
        }
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (Hub.Demo) return;
        if (await Updater.DownloadAndInstallAsync(Hub.Elevated) is null)
            Hub.Notify(L.T("به‌روزرسانی", "Update"), L.T("نصب شروع شد؛ شب‌پا یه لحظه بسته و دوباره باز می‌شه.", "Installing; Shabpa will close and reopen in a moment."));
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true }); }
        catch { /* no browser registered */ }
    }
}
