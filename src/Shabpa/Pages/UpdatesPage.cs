using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Programs with a newer version, through Windows' own package manager (winget). winget only offers versions
/// built for this PC, downloads them from the publishers and checks them; Shabpa starts it only after you confirm.</summary>
public sealed class AppUpdatesPage : PageBase
{
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _update = new() { Style = St("BtnPrimary"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, (CheckBox box, AppUpdate app)> _rows = [];
    private CancellationTokenSource? _cts;
    private bool _busy;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("برنامه‌هایی که نسخه‌ی جدیدتر دارن، با نسخه‌ی نصب‌شده و نسخه‌ی در دسترس. به‌روزرسانی با تأیید خودت انجام می‌شه.",
        "Programs that have a newer version, with the installed and the available version. Updating happens only after you confirm.");

    public AppUpdatesPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("به‌روزرسانی برنامه‌ها", "Program updates"),
            L.T("از مدیر بسته‌ی خود ویندوز (winget) استفاده می‌کنه: فقط نسخه‌هایی که با این سیستم سازگارن پیشنهاد می‌شن و از سایت خود سازنده دانلود و بررسی می‌شن.",
                "Uses Windows' own package manager (winget): only versions that fit this PC are offered, downloaded from the publisher and checked."));
        var check = new Button { Style = St("BtnPrimary"), Content = L.T("بررسی به‌روزرسانی‌ها", "Check for updates"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        check.Click += (_, _) => Check();
        body.Children.Add(check);
        body.Children.Add(_status);
        _list.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_list);
        _update.Click += (_, _) => UpdateSelected();
        body.Children.Add(_update);
        root.Children.Add(card);
        Content = root;
    }

    public override void Refresh()
    {
        if (!Winget.IsAvailable() && _list.Children.Count == 0)
            _status.Text = L.T("winget روی این سیستم پیدا نشد. از Microsoft Store برنامه‌ی «App Installer» رو نصب یا به‌روز کن.",
                "winget was not found on this PC. Install or update “App Installer” from the Microsoft Store.");
    }

    private void Check()
    {
        if (_busy) return;
        if (!Winget.IsAvailable())
        {
            Refresh();
            return;
        }
        _busy = true;
        _status.Foreground = Res("Muted");
        _status.Text = L.T("در حال بررسی؛ ممکنه یک دقیقه طول بکشه…", "Checking; this can take a minute…");
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
                _ = Dispatcher.BeginInvoke(() => _status.Text = L.T("متوقف شد.", "Stopped."));
            }
            catch (Exception ex)
            {
                Log.Write($"winget list: {ex.GetType().Name}");
                _ = Dispatcher.BeginInvoke(() =>
                {
                    _status.Foreground = Res("Red");
                    _status.Text = L.T("بررسی انجام نشد (اینترنت یا winget).", "The check failed (internet or winget).");
                });
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(() => _busy = false);
            }
        });
    }

    private void Show(List<AppUpdate> found)
    {
        _list.Children.Clear();
        _rows.Clear();
        _status.Text = found.Count == 0
            ? L.T("همه‌ی برنامه‌ها به‌روزن ✅", "All programs are up to date ✅")
            : L.T($"{Fa.Digits(found.Count.ToString())} برنامه نسخه‌ی جدیدتر دارن.", $"{found.Count} program(s) have a newer version.");
        foreach (var app in found)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var box = new CheckBox { Style = St("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            box.Click += (_, _) => UpdateButton();
            row.Children.Add(box);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = app.Name, Style = St("Body"), FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
            var installed = app.InstalledUnknown ? L.T("نسخه‌ی فعلی نامشخص", "installed version unknown") : app.Installed;
            text.Children.Add(new TextBlock
            {
                Text = $"{installed}  →  {app.Available}  ·  {app.Source}", Style = St("Label"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            _list.Children.Add(row);
            _rows[app.Id] = (box, app);
        }
        UpdateButton();
    }

    private List<AppUpdate> Chosen() => [.. _rows.Values.Where(r => r.box.IsChecked == true).Select(r => r.app)];

    private void UpdateButton()
    {
        var n = Chosen().Count;
        _update.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        _update.Content = L.T($"به‌روزرسانی {Fa.Digits(n.ToString())} برنامه", $"Update {n} program(s)");
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
        _busy = true;
        _update.IsEnabled = false;
        _ = Task.Run(async () =>
        {
            var ok = 0;
            var failed = new List<string>();
            for (var i = 0; i < chosen.Count; i++)
            {
                var app = chosen[i];
                _ = Dispatcher.BeginInvoke(() => _status.Text = L.T($"({Fa.Digits((i + 1).ToString())}/{Fa.Digits(chosen.Count.ToString())}) در حال به‌روزرسانی {app.Name}…", $"({i + 1}/{chosen.Count}) updating {app.Name}…"));
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
                _busy = false;
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
    private readonly StackPanel _gpus = new();
    private readonly StackPanel _offers = new();
    private readonly TextBlock _offerStatus = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private bool _busy;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("درایور کارت گرافیک و درایورهایی که Windows Update برای سخت‌افزارت پیدا کرده.", "Your graphics card driver, and driver updates Windows Update has found for your hardware.");

    public DriversPage()
    {
        var root = new StackPanel();
        var (gCard, g) = Ui2.Card(L.T("کارت گرافیک", "Graphics card"),
            L.T("سازنده‌ها لیست عمومی آخرین نسخه ندارن؛ صفحه‌ی رسمی خودشون دقیقاً نسخه‌ی مناسب کارتت رو نشون می‌ده.",
                "The makers publish no public latest-version list; their official page shows the exact version for your card."));
        g.Children.Add(_gpus);
        root.Children.Add(gCard);

        var (oCard, o) = Ui2.Card(L.T("درایورهای پیشنهادی Windows Update", "Drivers offered by Windows Update"),
            L.T("فقط درایورهایی که ویندوز برای سخت‌افزار همین سیستم تأیید کرده.", "Only drivers Windows has matched to this PC's hardware."));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var search = new Button { Style = St("BtnPrimary"), Content = L.T("جستجو", "Search"), Margin = new Thickness(0, 0, 8, 0) };
        search.Click += (_, _) => Search();
        var open = new Button { Style = St("Btn"), Content = L.T("باز کردن Windows Update", "Open Windows Update") };
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-optionalupdates") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write($"open windows update: {ex.GetType().Name}"); }
        };
        buttons.Children.Add(search);
        buttons.Children.Add(open);
        o.Children.Add(buttons);
        o.Children.Add(_offerStatus);
        _offers.Margin = new Thickness(0, 8, 0, 0);
        o.Children.Add(_offers);
        root.Children.Add(oCard);
        Content = root;
    }

    public override void Refresh()
    {
        _gpus.Children.Clear();
        var gpus = Drivers.Gpus();
        if (gpus.Count == 0) _gpus.Children.Add(new TextBlock { Text = L.T("کارت گرافیکی پیدا نشد.", "No graphics card found."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
        foreach (var gpu in gpus)
        {
            var box = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
            box.Children.Add(new TextBlock { Text = gpu.Name, Style = St("H2"), FontSize = 15, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
            var age = gpu.Date is { } d ? (DateTime.Now - d).Days : -1;
            box.Children.Add(new TextBlock
            {
                Text = L.T($"نسخه‌ی نصب‌شده: {gpu.FriendlyVersion}" + (gpu.Date is { } date ? $" · تاریخ: {Fa.Date(date)}" : ""),
                    $"Installed version: {gpu.FriendlyVersion}" + (gpu.Date is { } date2 ? $" · date: {date2:yyyy-MM-dd}" : "")),
                Style = St("Body"), Margin = new Thickness(0, 3, 0, 0), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left,
            });
            if (age > 180)
                box.Children.Add(new TextBlock { Text = L.T($"این درایور {Fa.Digits((age / 30).ToString())} ماه پیشه؛ ارزشش رو داره صفحه‌ی رسمی رو چک کنی.", $"This driver is {age / 30} months old; worth checking the official page."), Style = St("Label"), Foreground = Res("Gold"), Margin = new Thickness(0, 3, 0, 0) });
            if (gpu.DownloadPage.Length > 0)
            {
                var open = new Button { Style = St("Btn"), Content = L.T($"صفحه‌ی رسمی {gpu.VendorName}", $"Official {gpu.VendorName} page"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                var url = gpu.DownloadPage;
                open.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch (Exception ex) { Log.Write($"open driver page: {ex.GetType().Name}"); }
                };
                box.Children.Add(open);
                box.Children.Add(new TextBlock
                {
                    Text = gpu.Vendor switch
                    {
                        GpuVendor.Nvidia => L.T("یا از خود برنامه‌ی NVIDIA App / GeForce Experience «Drivers» رو چک کن.", "Or check Drivers in the NVIDIA App / GeForce Experience."),
                        GpuVendor.Amd => L.T("یا از AMD Software: Adrenalin Edition گزینه‌ی «Check for updates» رو بزن.", "Or use Check for updates in AMD Software: Adrenalin Edition."),
                        _ => L.T("یا از Intel Driver & Support Assistant استفاده کن.", "Or use the Intel Driver & Support Assistant."),
                    },
                    Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                });
            }
            _gpus.Children.Add(box);
        }
    }

    private void Search()
    {
        if (_busy) return;
        _busy = true;
        _offerStatus.Text = L.T("در حال جستجو؛ ممکنه یک دقیقه یا بیشتر طول بکشه…", "Searching; this can take a minute or more…");
        _offers.Children.Clear();
        _ = Task.Run(async () =>
        {
            try
            {
                var offers = await Drivers.WindowsUpdateOffersAsync();
                _ = Dispatcher.BeginInvoke(() =>
                {
                    _offerStatus.Text = offers.Count == 0
                        ? L.T("درایور جدیدی پیشنهاد نشده ✅", "No driver updates are offered ✅")
                        : L.T($"{Fa.Digits(offers.Count.ToString())} درایور پیشنهاد شده؛ از «Windows Update» نصب کن.", $"{offers.Count} driver(s) offered; install them from Windows Update.");
                    foreach (var o in offers)
                    {
                        var box = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
                        box.Children.Add(new TextBlock { Text = o.Title, Style = St("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left });
                        box.Children.Add(new TextBlock
                        {
                            Text = $"{o.Manufacturer} · {o.Class}" + (o.Date is { } d ? $" · {d:yyyy-MM-dd}" : ""), Style = St("Label"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left,
                        });
                        _offers.Children.Add(box);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Write($"driver search: {ex.GetType().Name}");
                _ = Dispatcher.BeginInvoke(() => _offerStatus.Text = L.T("جستجو انجام نشد.", "The search failed."));
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(() => _busy = false);
            }
        });
    }
}
