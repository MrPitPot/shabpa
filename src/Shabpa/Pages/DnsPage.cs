using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Pick a DNS server from a big ready-made list (with live ping), or type your own, and set it on the
/// network adapter. Also resets the adapter to automatic.</summary>
public sealed class DnsPage : PageBase
{
    private readonly WrapPanel _adapterChips = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _current = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly WrapPanel _groupChips = new() { Margin = new Thickness(0, 10, 0, 6) };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _test;
    private readonly TextBox _name = new() { Style = St("Input"), Width = 180, Padding = new Thickness(10, 6, 10, 6) };
    private readonly TextBox _dns1 = new() { Style = St("Input"), Width = 160, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(10, 6, 10, 6) };
    private readonly TextBox _dns2 = new() { Style = St("Input"), Width = 160, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(10, 6, 10, 6) };
    private readonly Dictionary<string, double?> _ping = [];
    private readonly HashSet<string> _pending = [];
    private List<DnsAdapter> _adapters = [];
    private string? _adapterId;
    private string _group = "all";
    private bool _sortByPing;
    private bool _testing;
    private CancellationTokenSource? _cts;

    public override string PageTitle => L.T("دی‌ان‌اس", "DNS");
    public override string PageSubtitle => L.T("دی‌ان‌اس مناسب رو از لیست انتخاب کن، پینگش رو ببین و روی کارت شبکه‌ت ثبتش کن.",
        "Pick a DNS server from the list, see its ping and set it on your network adapter.");

    public DnsPage()
    {
        var root = new StackPanel();

        var (adapterCard, adapter) = Ui2.Card(L.T("کارت شبکه", "Network adapter"),
            L.T("تغییر دی‌ان‌اس به دسترسی ادمین نیاز داره و روی همین کارت شبکه اعمال می‌شه.", "Changing DNS needs administrator rights and applies to this adapter."));
        adapter.Children.Add(_adapterChips);
        adapter.Children.Add(_current);
        var reset = new Button { Style = St("Btn"), Content = L.T("برگردوندن به حالت خودکار", "Back to automatic"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 12, 0, 0) };
        reset.Click += (_, _) => ApplyDns(L.T("خودکار (DHCP)", "Automatic (DHCP)"), "", "");
        adapter.Children.Add(reset);
        root.Children.Add(adapterCard);

        var (listCard, list) = Ui2.Card(L.T("لیست دی‌ان‌اس‌ها", "DNS servers"),
            L.T("پینگ با یه درخواست واقعی DNS سنجیده می‌شه. روی «اعمال» بزن تا ثبت بشه.", "Ping is measured with a real DNS query. Press Apply to set one."));
        _test = new Button { Style = St("BtnPrimary"), Content = L.T("تست پینگ همه", "Test all pings"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 12, 0, 0) };
        _test.Click += async (_, _) => await TestAll();
        var sort = new CheckBox { Style = St("Check"), Content = L.T("مرتب‌سازی بر اساس پینگ", "Sort by ping"), Margin = new Thickness(14, 12, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sort.Click += (_, _) =>
        {
            _sortByPing = sort.IsChecked == true;
            DrawRows();
        };
        list.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _test, sort } });
        list.Children.Add(_groupChips);
        list.Children.Add(_rows);
        list.Children.Add(_status);
        root.Children.Add(listCard);

        var (manualCard, manual) = Ui2.Card(L.T("دی‌ان‌اس دلخواه", "Your own DNS"),
            L.T("آدرس IPv4 دی‌ان‌اس رو بنویس. می‌تونی فقط اعمالش کنی یا توی لیست هم ذخیره کنی.", "Type IPv4 addresses. You can apply it only, or save it to the list too."));
        manual.Children.Add(Ui2.Label(L.T("نام (برای ذخیره)", "Name (to save)")));
        manual.Children.Add(new StackPanel { Children = { _name }, HorizontalAlignment = HorizontalAlignment.Left });
        manual.Children.Add(Ui2.Label(L.T("دی‌ان‌اس اصلی", "Primary DNS")));
        manual.Children.Add(new StackPanel { Children = { _dns1 }, HorizontalAlignment = HorizontalAlignment.Left });
        manual.Children.Add(Ui2.Label(L.T("دی‌ان‌اس دوم (اختیاری)", "Secondary DNS (optional)")));
        manual.Children.Add(new StackPanel { Children = { _dns2 }, HorizontalAlignment = HorizontalAlignment.Left });
        var applyOwn = new Button { Style = St("BtnPrimary"), Content = L.T("اعمال", "Apply"), Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 8, 0) };
        applyOwn.Click += (_, _) => ApplyOwn(save: false);
        var saveOwn = new Button { Style = St("Btn"), Content = L.T("ذخیره در لیست و اعمال", "Save to list and apply"), Padding = new Thickness(18, 9, 18, 9) };
        saveOwn.Click += (_, _) => ApplyOwn(save: true);
        manual.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), Children = { applyOwn, saveOwn } });
        root.Children.Add(manualCard);

        Content = root;
        DrawGroupChips();
    }

    private DnsAdapter? Adapter => _adapters.FirstOrDefault(a => a.Id == _adapterId);

    private IEnumerable<DnsEntry> Entries() =>
        DnsCatalog.All.Concat(Hub.Settings.CustomDns).Where(e => _group == "all" || e.Group == _group);

    public override void Refresh()
    {
        _adapters = Hub.Demo
            ? [new DnsAdapter("Wi-Fi", 12, "demo", ["8.8.8.8", "8.8.4.4"], false, true)]
            : DnsConfig.Adapters();
        if (Adapter is null) _adapterId = _adapters.FirstOrDefault()?.Id;
        Chips(_adapterChips, _adapters.Select(a => (a.Id, a.Name)), _adapterId ?? "", id =>
        {
            _adapterId = id;
            ShowCurrent();
        });
        ShowCurrent();
        DrawRows();
    }

    private void ShowCurrent()
    {
        var a = Adapter;
        if (a is null)
        {
            _current.Text = L.T("کارت شبکه‌ی فعالی پیدا نشد.", "No active network adapter was found.");
            return;
        }
        var servers = a.Servers.Length == 0 ? "—" : string.Join("  ·  ", a.Servers);
        var name = KnownName(a.Servers.FirstOrDefault());
        _current.Text = L.T($"دی‌ان‌اس فعلی: {servers}{(name is null ? "" : $"  ({name})")} · {(a.IsAutomatic ? "خودکار" : "دستی")}",
            $"Current DNS: {servers}{(name is null ? "" : $"  ({name})")} · {(a.IsAutomatic ? "automatic" : "manual")}");
        _current.FlowDirection = Flow;
    }

    private string? KnownName(string? ip) =>
        ip is null ? null : DnsCatalog.All.Concat(Hub.Settings.CustomDns).FirstOrDefault(e => e.Primary == ip || e.Secondary == ip)?.Name;

    private void DrawGroupChips()
    {
        Chips(_groupChips, DnsCatalog.Groups.Select(g => (g.key, L.T(g.fa, g.en))), _group, key =>
        {
            _group = key;
            DrawRows();
        });
    }

    private void DrawRows()
    {
        _rows.Children.Clear();
        var entries = Entries().ToList();
        if (_sortByPing)
            entries = [.. entries.OrderBy(e => _ping.TryGetValue(Key(e), out var ms) && ms is { } v ? v : double.MaxValue)];
        if (entries.Count == 0)
        {
            _rows.Children.Add(Ui2.Label(L.T("چیزی توی این دسته نیست.", "Nothing in this group.")));
            return;
        }

        foreach (var entry in entries)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), FlowDirection = Flow };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock { Text = entry.Name, Style = St("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = L.En ? HorizontalAlignment.Left : HorizontalAlignment.Right };
            var addr = new TextBlock
            {
                Text = entry.Secondary.Length > 0 ? $"{entry.Primary}   {entry.Secondary}" : entry.Primary,
                Style = St("Label"), FontFamily = (FontFamily)Application.Current.Resources["Num"], FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            };
            var key = Key(entry);
            var pingText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
            ShowPing(pingText, key);
            var apply = new Button { Style = St("Btn"), Content = L.T("اعمال", "Apply"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
            var captured = entry;
            apply.Click += (_, _) => ApplyDns(captured.Name, captured.Primary, captured.Secondary);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { apply } };
            if (entry.IsCustom)
            {
                var remove = IconButton("", L.T("حذف از لیست", "Remove"), () =>
                {
                    if (!Ask(L.T("این دی‌ان‌اس از لیست حذف بشه؟", "Remove this DNS from the list?"), entry.Name, L.T("حذف", "Remove"), danger: true)) return;
                    Hub.Settings.CustomDns.Remove(captured);
                    Hub.Store.Save();
                    DrawRows();
                });
                buttons.Children.Add(remove);
            }

            Grid.SetColumn(name, 0);
            Grid.SetColumn(addr, 1);
            Grid.SetColumn(pingText, 2);
            Grid.SetColumn(buttons, 3);
            grid.Children.Add(name);
            grid.Children.Add(addr);
            grid.Children.Add(pingText);
            grid.Children.Add(buttons);
            _rows.Children.Add(grid);
            _rows.Children.Add(new Border { Height = 1, Background = Res("Line"), Opacity = 0.5 });
        }
    }

    private static string Key(DnsEntry e) => e.Primary + "/" + e.Secondary;

    private void ShowPing(TextBlock box, string key)
    {
        if (_pending.Contains(key))
        {
            box.Text = "…";
            box.Foreground = Res("Muted");
        }
        else if (!_ping.TryGetValue(key, out var ms))
        {
            box.Text = "";
        }
        else if (ms is null)
        {
            box.Text = L.T("بی‌پاسخ", "timeout");
            box.Foreground = Res("Red");
        }
        else
        {
            box.Text = $"{ms:0} ms";
            box.Foreground = Res(ms < 60 ? "Green" : ms < 150 ? "Gold" : "Red");
        }
    }

    private async Task TestAll()
    {
        if (_testing)
        {
            _cts?.Cancel();
            return;
        }
        _testing = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _test.Content = L.T("توقف تست", "Stop");
        var entries = DnsCatalog.All.Concat(Hub.Settings.CustomDns).ToList();
        foreach (var e in entries) _pending.Add(Key(e));
        DrawRows();

        var gate = new SemaphoreSlim(10);
        try
        {
            await Task.WhenAll(entries.Select(async entry =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                double? ms;
                try
                {
                    // Demo mode shows plausible numbers instead of touching the network.
                    ms = Hub.Demo ? 18 + Math.Abs(entry.Name.GetHashCode() % 120) : await DnsProbe.PingAsync(entry.Primary, 1500, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    gate.Release();
                }
                await Dispatcher.InvokeAsync(() =>
                {
                    _ping[Key(entry)] = ms;
                    _pending.Remove(Key(entry));
                    if (!_sortByPing) DrawRows();
                });
            }));
        }
        catch (OperationCanceledException)
        {
            // stopped by the user
        }
        finally
        {
            _pending.Clear();
            _testing = false;
            _test.Content = L.T("تست پینگ همه", "Test all pings");
            DrawRows();
            var ok = _ping.Values.Count(v => v is not null);
            _status.Text = L.T($"{Fa.Digits(ok.ToString())} دی‌ان‌اس پاسخ داد.", $"{ok} servers answered.");
        }
    }

    private void ApplyOwn(bool save)
    {
        var a = _dns1.Text.Trim();
        var b = _dns2.Text.Trim();
        if (!DnsCatalog.TryParseIPv4(Latin(a), out _) || (b.Length > 0 && !DnsCatalog.TryParseIPv4(Latin(b), out _)))
        {
            _status.Text = L.T("آدرس‌ها باید IPv4 درست باشن؛ مثل 1.1.1.1", "Addresses must be valid IPv4, like 1.1.1.1");
            _status.Foreground = Res("Red");
            return;
        }
        a = Latin(a);
        b = Latin(b);
        var name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : a;
        if (!ApplyDns(name, a, b)) return;
        if (!save) return;
        if (Hub.Settings.CustomDns.Any(e => e.Primary == a && e.Secondary == b)) return;
        Hub.Settings.CustomDns.Add(new DnsEntry { Name = name, Primary = a, Secondary = b, Group = "custom" });
        Hub.Store.Save();
        DrawRows();
    }

    /// <summary>Confirms, sets the DNS and reads it back. Empty addresses mean "automatic". False if not applied.</summary>
    private bool ApplyDns(string name, string primary, string secondary)
    {
        var adapter = Adapter;
        if (adapter is null)
        {
            _status.Text = L.T("کارت شبکه‌ی فعالی پیدا نشد.", "No active network adapter was found.");
            return false;
        }
        var what = primary.Length == 0 ? L.T("خودکار (DHCP)", "automatic (DHCP)") : secondary.Length > 0 ? $"{primary} · {secondary}" : primary;
        if (!Ask(L.T("این دی‌ان‌اس ثبت بشه؟", "Set this DNS?"),
                L.T($"روی «{adapter.Name}» دی‌ان‌اس «{name}» ({what}) ثبت می‌شه. اتصال‌های موجود یک لحظه قطع و وصل می‌شن.",
                    $"On “{adapter.Name}” the DNS “{name}” ({what}) will be set. Existing connections may blip for a moment."),
                L.T("ثبت", "Set"))) return false;

        if (Hub.Demo)
        {
            _status.Foreground = Res("Muted");
            _status.Text = L.T("حالت نمایشی: چیزی ثبت نشد.", "Demo mode: nothing was changed.");
            return false;
        }
        if (!Hub.Elevated)
        {
            if (Ask(L.T("دسترسی ادمین لازمه", "Administrator rights needed"),
                    L.T("برای تغییر دی‌ان‌اس شب‌پا باید با دسترسی ادمین اجرا بشه. الان دوباره اجرا بشه؟", "Shabpa must run as administrator to change DNS. Restart it now?"),
                    L.T("اجرا با ادمین", "Restart as admin")))
                if (Application.Current is App app && !app.Restart(elevated: true, page: "dns"))
                    _status.Text = L.T("اجازه‌ی ادمین داده نشد.", "Administrator permission was not given.");
            return false;
        }

        var error = DnsConfig.Apply(adapter, primary, secondary);
        if (error is not null)
        {
            _status.Foreground = Res("Red");
            _status.Text = error switch
            {
                "admin" => L.T("دسترسی ادمین نیست.", "No administrator rights."),
                "invalid" => L.T("آدرس نامعتبره.", "Invalid address."),
                "readback" => L.T("ثبت شد ولی ویندوز همون مقدار رو برنگردوند؛ دوباره امتحان کن.", "It was set but Windows did not report the same value; try again."),
                _ => error,
            };
            Refresh();
            return false;
        }
        _status.Foreground = Res("Green");
        _status.Text = L.T($"ثبت شد و از ویندوز تایید گرفت: {what}", $"Set and confirmed by Windows: {what}");
        Refresh();
        return true;
    }
}
