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
    private readonly GlassCard _offlineCard = new() { Padding = new Thickness(24, 20, 24, 20), Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _offlineHint = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _mirrorBox = new() { Style = St("Input"), Width = 320, FlowDirection = FlowDirection.LeftToRight, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
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

        // offline / restricted internet: always available, loud when every source failed
        var offline = new StackPanel();
        offline.Children.Add(new TextBlock { Style = St("H2"), Text = L.T("اینترنت محدوده یا وصل نمی‌شه؟", "Restricted or no internet?") });
        offline.Children.Add(_offlineHint);
        var offlineRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        offlineRow.Children.Add(OfflineButton("ext", L.T("باز کردن صفحه‌ی دانلود", "Open download page"), OnOpenDownloadPage));
        offlineRow.Children.Add(OfflineButton("dl", L.T("نصب از فایل…", "Install from file…"), OnInstallFromFile));
        offlineRow.Children.Add(OfflineButton("dl", L.T("وارد کردن بستهٔ به‌روزرسانی…", "Import update bundle…"), OnImportBundle));
        offline.Children.Add(offlineRow);
        _offlineCard.Child = offline;
        root.Children.Add(_offlineCard);

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
        _mirrorBox.Text = Hub.Settings.ExtraMirrorUrl;
        _mirrorBox.LostFocus += (_, _) => SaveMirror();
        body.Children.Add(K.SettingRow(L.T("آینه‌ی اضافه (پیشرفته): آدرس HTTPS پوشه‌ای که latest.json توشه", "Extra mirror (advanced): HTTPS folder that holds latest.json"), _mirrorBox));
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

        var failed = u.AllSourcesFailed && u.State == UpdateState.Error;
        if (failed) _offlineHint.Foreground = Res("Red"); else _offlineHint.ClearValue(TextBlock.ForegroundProperty);
        _offlineHint.Text = failed
            ? u.Error
            : L.T("اگه GitHub و سایت‌های خارجی باز نمی‌شن، شب‌پا خودش از آینه‌های داخلی هم می‌گیره. دستی هم می‌تونی فایل نصب یا بستهٔ به‌روزرسانی رو از فلش یا تلگرام بیاری؛ امضا و چک‌سامش قبل از نصب بررسی می‌شه.",
                "If GitHub and foreign sites are blocked, Shabpa also tries domestic mirrors. You can also bring an installer or update bundle from a USB stick or Telegram; its signature and checksum are verified before installing.");

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

    private Button OfflineButton(string icon, string text, RoutedEventHandler click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon(icon, 16) { Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = St("Btn"), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 8), Content = row };
        b.Click += click;
        return b;
    }

    private void Say(string text, bool bad)
    {
        if (bad) _offlineHint.Foreground = Res("Red"); else _offlineHint.ClearValue(TextBlock.ForegroundProperty);
        _offlineHint.Text = text;
    }

    private void SaveMirror()
    {
        var text = _mirrorBox.Text.Trim();
        if (text.Length > 0 && UpdateSources.NormalizeMirror(text) is null)
        {
            Say(L.T("آدرس آینه باید با https:// شروع بشه.", "The mirror address must start with https://."), true);
            _mirrorBox.Text = Hub.Settings.ExtraMirrorUrl;
            return;
        }
        if (text == Hub.Settings.ExtraMirrorUrl) return;
        Hub.Settings.ExtraMirrorUrl = text;
        Hub.Store.Save();
    }

    private void OnOpenDownloadPage(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.DownloadPage) { UseShellExecute = true }); }
        catch { /* no browser registered */ }
    }

    /// <summary>Picks ShabpaSetup.exe. A latest.json (+ .sig) next to it is verified; otherwise the checksum on the clipboard is.</summary>
    private async void OnInstallFromFile(object sender, RoutedEventArgs e)
    {
        if (Hub.Demo) return;
        string file;
        using (var dialog = new System.Windows.Forms.OpenFileDialog { Filter = "ShabpaSetup|*.exe", CheckFileExists = true })
        {
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            file = dialog.FileName;
        }
        string? sha = null, error = null;
        await Task.Run(() =>
        {
            try { sha = UpdateBundle.FromInstallerWithSiblingManifest(file, Updater.Current, UpdateKeys.PublicKeyBase64)?.Manifest.Sha256; }
            catch (UpdateRejectedException ex) { error = ex.Friendly; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { error = L.T("فایل خونده نشد.", "The file could not be read."); }
        });
        if (error is not null) { Say(error, true); return; }
        if (sha is null)
        {
            if (!ConfirmWindow.AskAlways(L.T("نصب از فایل", "Install from file"),
                    L.T("کنار این فایل latest.json پیدا نشد. چک‌سام SHA-256 اعلام‌شده در shabpa.ir یا GitHub رو کپی کن (کلیپ‌بورد)، بعد «بررسی و نصب» رو بزن.",
                        "No latest.json was found next to this file. Copy the SHA-256 published on shabpa.ir or GitHub to the clipboard, then press “Check and install”."),
                    yes: L.T("بررسی و نصب", "Check and install")))
                return;
            string clip;
            try { clip = System.Windows.Clipboard.GetText(); } catch { clip = ""; }
            sha = UpdateBundle.NormalizeHash(clip);
            if (sha is null) { Say(L.T("توی کلیپ‌بورد چک‌سام SHA-256 معتبری نبود.", "The clipboard has no valid SHA-256 checksum."), true); return; }
        }
        await InstallPicked(file, sha);
    }

    /// <summary>A .zip with ShabpaSetup.exe, latest.json and latest.json.sig (from a USB stick or Telegram).</summary>
    private async void OnImportBundle(object sender, RoutedEventArgs e)
    {
        if (Hub.Demo) return;
        string path;
        using (var dialog = new System.Windows.Forms.OpenFileDialog { Filter = "Shabpa update bundle|*.zip", CheckFileExists = true })
        {
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            path = dialog.FileName;
        }
        UpdateBundle.Prepared? prepared = null;
        string? error = null;
        await Task.Run(() =>
        {
            try { prepared = UpdateBundle.Open(path, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Shabpa-bundle"), Updater.Current, UpdateKeys.PublicKeyBase64); }
            catch (UpdateRejectedException ex) { error = ex.Friendly; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException) { error = L.T("بسته خونده نشد.", "The bundle could not be read."); }
        });
        if (error is not null || prepared is null) { Say(error ?? "", true); return; }
        await InstallPicked(prepared.InstallerPath, prepared.Manifest.Sha256);
    }

    private async Task InstallPicked(string file, string sha)
    {
        var problem = await Task.Run(() => Updater.InstallLocal(file, sha, Hub.Elevated));
        if (problem is not null) { Say(problem, true); return; }
        Hub.Notify(L.T("به‌روزرسانی", "Update"), L.T("نصب شروع شد؛ شب‌پا یه لحظه بسته و دوباره باز می‌شه.", "Installing; Shabpa will close and reopen in a moment."));
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true }); }
        catch { /* no browser registered */ }
    }
}
