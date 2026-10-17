using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Programs with a newer version, through Windows' own package manager (winget). winget only offers versions
/// built for this PC, downloads them from the publishers and checks them; Shabpa starts it only after you confirm.
/// Glass Depth: one glass card (badge, title, check button, status line, the list with hairlines, update button).</summary>
public sealed class AppUpdatesPage : PageBase
{
    private readonly StackPanel _list = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly StatusLine _status = new();
    private readonly Button _update = ListUi.Btn("dl", "", "BtnPrimary");
    private readonly Button _check;
    private readonly Icon? _checkIcon;
    private readonly Dictionary<string, (CheckBox box, AppUpdate app)> _rows = [];
    private readonly StackPanel _root = new();
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _updateShown;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("برنامه‌هایی که نسخه‌ی جدیدتر دارن، با نسخه‌ی نصب‌شده و نسخه‌ی در دسترس. به‌روزرسانی با تأیید خودت انجام می‌شه.",
        "Programs that have a newer version, with the installed and the available version. Updating happens only after you confirm.");

    public AppUpdatesPage()
    {
        GroupC.EnsureIcons();
        var card = new GlassCard { Padding = new Thickness(26, 22, 26, 20) };
        var body = new StackPanel();

        // header: badge, title and what it does, the check button at the end
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new IconBadge("dl", "HueA", 48, 24, 14) { VerticalAlignment = VerticalAlignment.Top });
        var texts = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = L.T("به‌روزرسانی برنامه‌ها", "Program updates"), Style = St("H2"), FontSize = 16 });
        texts.Children.Add(new TextBlock
        {
            Text = L.T("از مدیر بسته‌ی خود ویندوز (winget) استفاده می‌کنه: فقط نسخه‌هایی که با این سیستم سازگارن پیشنهاد می‌شن و از سایت خود سازنده دانلود و بررسی می‌شن.",
                "Uses Windows' own package manager (winget): only versions that fit this PC are offered, downloaded from the publisher and checked."),
            Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        });
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        _check = ListUi.Btn("refresh", L.T("بررسی به‌روزرسانی‌ها", "Check for updates"), "BtnPrimary", Check);
        _check.VerticalAlignment = VerticalAlignment.Center;
        _checkIcon = CMo.IconOf(_check);
        Grid.SetColumn(_check, 2);
        head.Children.Add(_check);
        body.Children.Add(head);

        body.Children.Add(_status);
        body.Children.Add(_list);
        _update.Visibility = Visibility.Collapsed;
        _update.Margin = new Thickness(0, 14, 0, 0);
        _update.HorizontalAlignment = HorizontalAlignment.Left;
        _update.Click += (_, _) => UpdateSelected();
        body.Children.Add(_update);
        card.Child = body;
        _root.Children.Add(card);
        Content = _root;
        CMo.CascadeOnLoad(this, _root);
    }

    public override void Refresh()
    {
        if (!Winget.IsAvailable() && _list.Children.Count == 0)
            _status.Set(L.T("winget روی این سیستم پیدا نشد. از Microsoft Store برنامه‌ی «App Installer» رو نصب یا به‌روز کن.",
                "winget was not found on this PC. Install or update “App Installer” from the Microsoft Store."), "Gold", "warn");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_checkIcon is not null) CMo.Busy(_checkIcon, busy);
    }

    private void Check()
    {
        if (_busy) return;
        if (!Winget.IsAvailable())
        {
            Refresh();
            return;
        }
        SetBusy(true);
        _status.Set(L.T("در حال بررسی؛ ممکنه یک دقیقه طول بکشه…", "Checking; this can take a minute…"), "Muted", "refresh", busy: true);
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var found = await Winget.ListUpgradesAsync(token);
                _ = Dispatcher.BeginInvoke(() => Show(found));
            }
            catch (OperationCanceledException)
            {
                _ = Dispatcher.BeginInvoke(() => _status.Set(L.T("متوقف شد.", "Stopped."), "Muted", "min"));
            }
            catch (Exception ex)
            {
                Log.Write($"winget list: {ex.GetType().Name}");
                _ = Dispatcher.BeginInvoke(() =>
                    _status.Set(L.T("بررسی انجام نشد (اینترنت یا winget).", "The check failed (internet or winget)."), "Red", "warn"));
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(() => SetBusy(false));
            }
        });
    }

    private void Show(List<AppUpdate> found)
    {
        _list.Children.Clear();
        _rows.Clear();
        _status.Set(found.Count == 0
                ? L.T("همه‌ی برنامه‌ها به‌روزن ✅", "All programs are up to date ✅")
                : L.T($"{Fa.Digits(found.Count.ToString())} برنامه نسخه‌ی جدیدتر دارن.", $"{found.Count} program(s) have a newer version."),
            found.Count == 0 ? "Green" : "Gold", found.Count == 0 ? "chk" : "dl");
        var first = true;
        var order = 0;
        foreach (var app in found)
        {
            var box = new CheckBox { Style = St("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            box.Click += (_, _) => UpdateButton();
            System.Windows.Automation.AutomationProperties.SetName(box, app.Name);
            var row = Row(app, box, first);
            _list.Children.Add(row);
            if (_list.IsLoaded) CMo.Appear(row, order++);
            _rows[app.Id] = (box, app);
            first = false;
        }
        UpdateButton();
    }

    /// <summary>One program: check box, name, "installed → available · source" (Latin runs stay left-to-right), hairline above.</summary>
    private static Grid Row(AppUpdate app, CheckBox box, bool first)
    {
        var row = new Grid { MinHeight = 56 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        if (!first)
        {
            var line = ListUi.Hairline();
            Grid.SetColumnSpan(line, 2);
            row.Children.Add(line);
        }
        row.Children.Add(box);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 9, 0, 9) };
        text.Children.Add(new TextBlock
        {
            Text = app.Name, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Res("Text"), FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var installed = app.InstalledUnknown ? L.T("نسخه‌ی فعلی نامشخص", "installed version unknown") : app.Installed;
        text.Children.Add(new TextBlock
        {
            Text = $"{installed}  →  {app.Available}  ·  {app.Source}", Style = St("Label"), FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private List<AppUpdate> Chosen() => [.. _rows.Values.Where(r => r.box.IsChecked == true).Select(r => r.app)];

    private void UpdateButton()
    {
        var n = Chosen().Count;
        var show = n > 0;
        _update.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ListUi.SetContent(_update, "dl", L.T($"به‌روزرسانی {Fa.Digits(n.ToString())} برنامه", $"Update {n} program(s)"));
        if (show && !_updateShown && _update.IsLoaded) CMo.Appear(_update, 0, 8, 0.45);
        _updateShown = show;
    }

    private void UpdateSelected()
    {
        var chosen = Chosen();
        if (chosen.Count == 0 || _busy) return;
        var detail = string.Join("\n", chosen.Select(a => $"• {a.Name}  {a.Installed} → {a.Available}"));
        if (!ConfirmWindow.AskAlways(L.T("به‌روزرسانی برنامه‌ها", "Update programs"),
                L.T($"{Fa.Digits(chosen.Count.ToString())} برنامه با winget از سایت سازنده‌شون دانلود و نصب می‌شن. ممکنه ویندوز برای بعضی‌ها تأیید ادمین بخواد و برنامه‌های بازشون بسته بشن.",
                    $"{chosen.Count} program(s) are downloaded from their publishers and installed with winget. Windows may ask for administrator approval for some, and open programs may be closed."),
                L.T("به‌روزرسانی کن", "Update"), detail: detail)) return;
        SetBusy(true);
        _update.IsEnabled = false;
        _ = Task.Run(async () =>
        {
            var ok = 0;
            var failed = new List<string>();
            for (var i = 0; i < chosen.Count; i++)
            {
                var app = chosen[i];
                _ = Dispatcher.BeginInvoke(() => _status.Set(L.T($"({Fa.Digits((i + 1).ToString())}/{Fa.Digits(chosen.Count.ToString())}) در حال به‌روزرسانی {app.Name}…", $"({i + 1}/{chosen.Count}) updating {app.Name}…"), "Muted", "refresh", busy: true));
                string? error;
                try { error = await Winget.UpgradeAsync(app.Id); }
                catch (Exception ex)
                {
                    Log.Write($"winget upgrade: {ex.GetType().Name}");
                    error = "error";
                }
                if (error is null) ok++;
                else failed.Add(app.Name);
            }
            _ = Dispatcher.BeginInvoke(() =>
            {
                SetBusy(false);
                _update.IsEnabled = true;
                Hub.Notify(L.T("به‌روزرسانی برنامه‌ها", "Program updates"),
                    L.T($"{Fa.Digits(ok.ToString())} برنامه به‌روز شد." + (failed.Count > 0 ? $" نشد: {string.Join("، ", failed)}" : " ✅"),
                        $"{ok} program(s) updated." + (failed.Count > 0 ? $" Failed: {string.Join(", ", failed)}" : " ✅")));
                Check();
            });
        });
    }
}

/// <summary>Graphics card drivers (with a link to the maker's own page) and driver updates Windows Update has found.</summary>
public sealed class DriversPage : PageBase
{
    private readonly StackPanel _gpus = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _offers = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly StatusLine _offerStatus = new();
    private readonly Blocks _root = new();
    private readonly Icon? _searchIcon;
    private bool _busy;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("درایور کارت گرافیک و درایورهایی که Windows Update برای سخت‌افزارت پیدا کرده.", "Your graphics card driver, and driver updates Windows Update has found for your hardware.");

    public DriversPage()
    {
        GroupC.EnsureIcons();

        var gCard = new GlassCard { Padding = new Thickness(24, 20, 24, 14) };
        var g = new StackPanel();
        g.Children.Add(Heading("cpu", "HueA", L.T("کارت گرافیک", "Graphics card"),
            L.T("سازنده‌ها لیست عمومی آخرین نسخه ندارن؛ صفحه‌ی رسمی خودشون دقیقاً نسخه‌ی مناسب کارتت رو نشون می‌ده.",
                "The makers publish no public latest-version list; their official page shows the exact version for your card.")));
        g.Children.Add(_gpus);
        gCard.Child = g;
        _root.Add(gCard, Blocks.At(0, 0, 6), Blocks.At(0, 0, 12));

        var oCard = new GlassCard { Padding = new Thickness(24, 20, 24, 18) };
        var o = new StackPanel();
        o.Children.Add(Heading("shield", "HueC", L.T("درایورهای پیشنهادی Windows Update", "Drivers offered by Windows Update"),
            L.T("فقط درایورهایی که ویندوز برای سخت‌افزار همین سیستم تأیید کرده.", "Only drivers Windows has matched to this PC's hardware.")));
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var search = ListUi.Btn("refresh", L.T("جستجو", "Search"), "BtnPrimary", Search);
        search.Margin = new Thickness(0, 0, 8, 6);
        _searchIcon = CMo.IconOf(search);
        var open = ListUi.Btn("ext", L.T("باز کردن Windows Update", "Open Windows Update"), "Btn", () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-optionalupdates") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write($"open windows update: {ex.GetType().Name}"); }
        });
        open.Margin = new Thickness(0, 0, 0, 6);
        buttons.Children.Add(search);
        buttons.Children.Add(open);
        o.Children.Add(buttons);
        o.Children.Add(_offerStatus);
        o.Children.Add(_offers);
        oCard.Child = o;
        _root.Add(oCard, Blocks.At(0, 6, 6), Blocks.At(1, 0, 12));

        Content = _root;
        CMo.CascadeOnLoad(this, _root);
    }

    /// <summary>Card title with a tinted badge and one explaining line.</summary>
    private static Grid Heading(string icon, string tint, string title, string subtitle)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(new IconBadge(icon, tint, 40, 20, 12) { VerticalAlignment = VerticalAlignment.Top });
        var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, Style = St("H2"), TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock { Text = subtitle, Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        return head;
    }

    public override void Refresh()
    {
        _gpus.Children.Clear();
        var gpus = Drivers.Gpus();
        if (gpus.Count == 0) _gpus.Children.Add(new TextBlock { Text = L.T("کارت گرافیکی پیدا نشد.", "No graphics card found."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 8) });
        var first = true;
        foreach (var gpu in gpus)
        {
            var age = gpu.Date is { } d ? (DateTime.Now - d).Days : -1;
            var sub = L.T($"نسخه‌ی نصب‌شده: {gpu.FriendlyVersion}" + (gpu.Date is { } date ? $" · تاریخ: {Fa.Date(date)}" : ""),
                $"Installed version: {gpu.FriendlyVersion}" + (gpu.Date is { } date2 ? $" · date: {date2:yyyy-MM-dd}" : ""));
            Button? open = null;
            if (gpu.DownloadPage.Length > 0)
            {
                var url = gpu.DownloadPage;
                open = ListUi.Btn("ext", L.T($"صفحه‌ی رسمی {gpu.VendorName}", $"Official {gpu.VendorName} page"), "Btn", () =>
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch (Exception ex) { Log.Write($"open driver page: {ex.GetType().Name}"); }
                });
                open.Padding = new Thickness(14, 7, 14, 7);
            }
            var note = gpu.DownloadPage.Length > 0
                ? gpu.Vendor switch
                {
                    GpuVendor.Nvidia => L.T("یا از خود برنامه‌ی NVIDIA App / GeForce Experience «Drivers» رو چک کن.", "Or check Drivers in the NVIDIA App / GeForce Experience."),
                    GpuVendor.Amd => L.T("یا از AMD Software: Adrenalin Edition گزینه‌ی «Check for updates» رو بزن.", "Or use Check for updates in AMD Software: Adrenalin Edition."),
                    _ => L.T("یا از Intel Driver & Support Assistant استفاده کن.", "Or use the Intel Driver & Support Assistant."),
                }
                : null;
            var item = ListUi.Item("cpu", "HueA", gpu.Name, sub, open, first, titleLtr: true, note: note);
            if (age > 180)
                ListUi.AddText(item, new TextBlock { Text = L.T($"این درایور {Fa.Digits((age / 30).ToString())} ماه پیشه؛ ارزشش رو داره صفحه‌ی رسمی رو چک کنی.", $"This driver is {age / 30} months old; worth checking the official page."), Style = St("Label"), Foreground = Res("Gold"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            _gpus.Children.Add(item);
            first = false;
        }
    }

    private void Search()
    {
        if (_busy) return;
        _busy = true;
        if (_searchIcon is not null) CMo.Busy(_searchIcon, true);
        _offerStatus.Set(L.T("در حال جستجو؛ ممکنه یک دقیقه یا بیشتر طول بکشه…", "Searching; this can take a minute or more…"), "Muted", "refresh", busy: true);
        _offers.Children.Clear();
        _ = Task.Run(async () =>
        {
            try
            {
                var offers = await Drivers.WindowsUpdateOffersAsync();
                _ = Dispatcher.BeginInvoke(() =>
                {
                    _offerStatus.Set(offers.Count == 0
                            ? L.T("درایور جدیدی پیشنهاد نشده ✅", "No driver updates are offered ✅")
                            : L.T($"{Fa.Digits(offers.Count.ToString())} درایور پیشنهاد شده؛ از «Windows Update» نصب کن.", $"{offers.Count} driver(s) offered; install them from Windows Update."),
                        offers.Count == 0 ? "Green" : "Gold", offers.Count == 0 ? "chk" : "dl");
                    var first = true;
                    var order = 0;
                    foreach (var o in offers)
                    {
                        var row = ListUi.Item("dl", "HueC", o.Title, $"{o.Manufacturer} · {o.Class}" + (o.Date is { } d ? $" · {d:yyyy-MM-dd}" : ""), null, first, subLtr: true, titleLtr: true);
                        _offers.Children.Add(row);
                        if (_offers.IsLoaded) CMo.Appear(row, order++);
                        first = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Write($"driver search: {ex.GetType().Name}");
                _ = Dispatcher.BeginInvoke(() => _offerStatus.Set(L.T("جستجو انجام نشد.", "The search failed."), "Red", "warn"));
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    _busy = false;
                    if (_searchIcon is not null) CMo.Busy(_searchIcon, false);
                });
            }
        });
    }
}
