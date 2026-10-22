using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

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

    private readonly TextBlock _testLabel;
    private readonly Dictionary<string, List<TextBlock>> _pingBoxes = [];
    private bool _drawn;

    private string StatusText { set => NetFx.Say(_status, value); }

    public DnsPage()
    {
        var root = new NetBlocks(1000, 3);

        var (adapterCard, adapter) = NetParts.Card("net", "HueA", L.T("کارت شبکه", "Network adapter"),
            L.T("تغییر دی‌ان‌اس به دسترسی ادمین نیاز داره و روی همین کارت شبکه اعمال می‌شه.", "Changing DNS needs administrator rights and applies to this adapter."));
        _adapterChips.Margin = new Thickness(0, 12, 0, 0);
        adapter.Children.Add(_adapterChips);
        var currentBox = new Border { Style = St("Card"), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 4, 0, 0), Child = _current };
        _current.Margin = new Thickness(0);
        adapter.Children.Add(currentBox);
        var reset = new Button { Style = St("Btn"), Content = L.T("برگردوندن به حالت خودکار", "Back to automatic"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 14, 0, 0) };
        reset.Click += (_, _) => ApplyDns(L.T("خودکار (DHCP)", "Automatic (DHCP)"), "", "");
        adapter.Children.Add(reset);

        var (listCard, list) = NetParts.Card("globe", "HueB", L.T("لیست دی‌ان‌اس‌ها", "DNS servers"),
            L.T("پینگ با یه درخواست واقعی DNS سنجیده می‌شه. روی «اعمال» بزن تا ثبت بشه.", "Ping is measured with a real DNS query. Press Apply to set one."));
        _test = new Button { Style = St("BtnPrimary"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 0, 18, 0), Height = 40, VerticalAlignment = VerticalAlignment.Center };
        _test.Content = NetParts.IconText("refresh", L.T("تست پینگ همه", "Test all pings"), out _testLabel);
        _test.Click += async (_, _) => await TestAll();
        var sort = new CheckBox { Style = St("Check"), Content = L.T("مرتب‌سازی بر اساس پینگ", "Sort by ping"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sort.Click += (_, _) =>
        {
            _sortByPing = sort.IsChecked == true;
            DrawRows(true);
        };
        list.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), Children = { _test, sort } });
        list.Children.Add(_groupChips);
        list.Children.Add(_rows);
        list.Children.Add(_status);

        var (manualCard, manual) = NetParts.Card("edit", "HueC", L.T("دی‌ان‌اس دلخواه", "Your own DNS"),
            L.T("آدرس IPv4 دی‌ان‌اس رو بنویس. «اعمال» فقط ثبتش می‌کنه، «فقط اضافه به لیست» فقط توی لیست نگه می‌داره و چیزی روی سیستم عوض نمی‌شه.", "Type IPv4 addresses. “Apply” only sets it; “Add to list only” just keeps it in the list and changes nothing on the system."));
        var fields = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        StackPanel Field(string label, TextBox box)
        {
            var f = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            f.Children.Add(Ui2.Label(label, 8, 6));
            f.Children.Add(box);
            return f;
        }
        fields.Children.Add(Field(L.T("نام (برای ذخیره)", "Name (to save)"), _name));
        fields.Children.Add(Field(L.T("دی‌ان‌اس اصلی", "Primary DNS"), _dns1));
        fields.Children.Add(Field(L.T("دی‌ان‌اس دوم (اختیاری)", "Secondary DNS (optional)"), _dns2));
        manual.Children.Add(fields);
        var applyOwn = new Button { Style = St("BtnPrimary"), Content = L.T("اعمال", "Apply"), Padding = new Thickness(20, 0, 20, 0), Height = 40, Margin = new Thickness(0, 0, 8, 0) };
        applyOwn.Click += (_, _) => ApplyOwn();
        var saveOwn = new Button { Style = St("Btn"), Content = L.T("فقط اضافه به لیست", "Add to list only"), Padding = new Thickness(18, 0, 18, 0), Height = 40 };
        saveOwn.Click += (_, _) => AddOwn();
        manual.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { applyOwn, saveOwn } });

        root.Add(adapterCard, 0, 0, 5, 0);
        root.Add(manualCard, 0, 5, 7, 2);
        root.Add(listCard, 1, 0, 12, 1);
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
            : Offload.KeepResponsive(DnsConfig.Adapters);
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
            NetFx.Say(_current, L.T("کارت شبکه‌ی فعالی پیدا نشد.", "No active network adapter was found."), false);
            return;
        }
        var servers = a.Servers.Length == 0 ? "—" : string.Join("  ·  ", a.Servers);
        var name = KnownName(a.Servers.FirstOrDefault());
        NetFx.Say(_current, L.T($"دی‌ان‌اس فعلی: {servers}{(name is null ? "" : $"  ({name})")} · {(a.IsAutomatic ? "خودکار" : "دستی")}",
            $"Current DNS: {servers}{(name is null ? "" : $"  ({name})")} · {(a.IsAutomatic ? "automatic" : "manual")}"), false);
        _current.FlowDirection = Flow;
    }

    private string? KnownName(string? ip) =>
        ip is null ? null : DnsCatalog.All.Concat(Hub.Settings.CustomDns).FirstOrDefault(e => e.Primary == ip || e.Secondary == ip)?.Name;

    private void DrawGroupChips()
    {
        Chips(_groupChips, DnsCatalog.Groups.Select(g => (g.key, L.T(g.fa, g.en))), _group, key =>
        {
            _group = key;
            DrawRows(true);
        });
    }

    private void DrawRows(bool animate = false)
    {
        _rows.Children.Clear();
        _pingBoxes.Clear();
        var entries = Entries().ToList();
        if (_sortByPing)
            entries = [.. entries.OrderBy(e => _ping.TryGetValue(Key(e), out var ms) && ms is { } v ? v : double.MaxValue)];
        if (entries.Count == 0)
        {
            _rows.Children.Add(Ui2.Label(L.T("چیزی توی این دسته نیست.", "Nothing in this group.")));
            if (animate) NetFx.RiseChildren(_rows);
            return;
        }

        _rows.Children.Add(Columns(header: true, null, 0));
        var number = 0;
        foreach (var entry in entries)
        {
            number++;
            var line = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(-8, 0, -8, 0), Child = Columns(header: false, entry, number) };
            NetFx.HoverTint(line);
            _rows.Children.Add(line);
            _rows.Children.Add(new Border { Height = 1, Background = Res("Line"), Opacity = 0.5 });
        }
        // the list rises in when it is (re)filtered or first shown; ping results later update in place, without a redraw
        if (animate || !_drawn) NetFx.RiseChildren(_rows, 21, 8, 0.45);
        _drawn = true;
    }

    private void UpdatePing(string key)
    {
        if (!_pingBoxes.TryGetValue(key, out var boxes)) return;
        foreach (var box in boxes)
        {
            ShowPing(box, key);
            Motion.Pop(box);
        }
    }

    private static string GroupName(string key) => key switch
    {
        "iran" => L.T("ایران", "Iran"),
        "global" => L.T("عمومی", "Public"),
        "secure" => L.T("امنیتی", "Secure"),
        "custom" => L.T("من", "Mine"),
        _ => key,
    };

    // column widths, shared by the header and every row so they line up: # · name · DNS 1 · DNS 2 · type · ping · note · actions
    private static readonly double[] Widths = [28, 0, 112, 112, 64, 66, 170, 150];
    private const int NoteMax = 140;

    /// <summary>One table row (or the header when <paramref name="header"/>). Every column has a fixed width
    /// (the name takes the rest), including the actions one, so the trash button never shifts anything.</summary>
    private Grid Columns(bool header, DnsEntry? entry, int number)
    {
        var grid = new Grid { Margin = new Thickness(0, header ? 4 : 3, 0, header ? 6 : 3), FlowDirection = Flow };
        foreach (var width in Widths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width), MinWidth = width == 0 ? 110 : 0 });

        TextBlock Cell(string text, int column, bool ltr = false, bool bold = false, bool center = false)
        {
            var t = new TextBlock
            {
                Text = text, Style = St(header ? "Label" : bold ? "Body" : "Label"), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold || header ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 4, 0),
            };
            if (ltr)
            {
                t.FlowDirection = FlowDirection.LeftToRight;
                t.HorizontalAlignment = HorizontalAlignment.Stretch;
                t.TextAlignment = Flow == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left;
                if (!header) t.FontFamily = (FontFamily)Application.Current.Resources["Num"];
            }
            if (center) t.TextAlignment = TextAlignment.Center;
            Grid.SetColumn(t, column);
            grid.Children.Add(t);
            return t;
        }

        if (header)
        {
            Cell("#", 0);
            Cell(L.T("نام", "Name"), 1);
            Cell("DNS 1", 2, ltr: true);
            Cell("DNS 2", 3, ltr: true);
            Cell(L.T("نوع", "Type"), 4);
            Cell(L.T("پینگ", "Ping"), 5, center: true);
            Cell(L.T("یادداشت", "Note"), 6);
            Cell(L.T("عملیات", "Actions"), 7);
            return grid;
        }

        var e = entry!;
        Cell(Fa.Digits(number.ToString()), 0);
        var name = Cell(e.Name, 1, bold: true);
        name.FlowDirection = FlowDirection.LeftToRight;
        name.HorizontalAlignment = HorizontalAlignment.Stretch;
        name.TextAlignment = Flow == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left;
        Cell(e.Primary, 2, ltr: true);
        Cell(e.Secondary.Length > 0 ? e.Secondary : "—", 3, ltr: true);
        Cell(GroupName(e.Group), 4);
        var pingText = Cell("", 5, ltr: true, bold: true, center: true);
        ShowPing(pingText, Key(e));
        if (!_pingBoxes.TryGetValue(Key(e), out var boxList)) _pingBoxes[Key(e)] = boxList = [];
        boxList.Add(pingText);

        // note: short text with an ellipsis; a click opens the whole thing in a comic-style bubble
        var note = Hub.Settings.DnsNotes.TryGetValue(Key(e), out var saved) ? saved : "";
        var noteText = new TextBlock
        {
            Text = note.Length > 0 ? note : L.T("+ یادداشت", "+ note"), Style = St("Label"), TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Opacity = note.Length > 0 ? 1 : 0.55,
        };
        var noteBox = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(0, 4, 0, 4), Child = noteText };
        noteBox.MouseLeftButtonUp += (_, _) => OpenBubble(noteBox, e, edit: note.Length == 0);
        Grid.SetColumn(noteBox, 6);
        grid.Children.Add(noteBox);

        var apply = new Button { Style = St("Btn"), Content = L.T("اعمال", "Apply"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0), MinWidth = 62 };
        apply.Click += (_, _) => ApplyDns(e.Name, e.Primary, e.Secondary);
        var edit = IconButton("edit", L.T("یادداشت", "Note"), () => OpenBubble(noteBox, e, edit: true));
        // a fixed slot for the trash button, so rows without one line up with rows that have it
        var trashSlot = new Border { Width = 38, HorizontalAlignment = HorizontalAlignment.Center };
        if (e.IsCustom)
            trashSlot.Child = IconButton("trash", L.T("حذف از لیست", "Remove"), () =>
            {
                if (!Ask(L.T("این دی‌ان‌اس از لیست حذف بشه؟", "Remove this DNS from the list?"), e.Name, L.T("حذف", "Remove"), danger: true)) return;
                NetFx.Out(grid, () =>
                {
                    Hub.Settings.CustomDns.Remove(e);
                    Hub.Settings.DnsNotes.Remove(Key(e));
                    Hub.Store.Save();
                    DrawRows();
                });
            });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { apply, edit, trashSlot } };
        Grid.SetColumn(buttons, 7);
        grid.Children.Add(buttons);
        return grid;
    }

    private static string CleanNote(string raw)
    {
        var t = string.Join(' ', raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return t.Length > NoteMax ? t[..NoteMax] : t;
    }

    /// <summary>Stores (or clears) the note of one DNS, next to the other settings.</summary>
    internal static void SetNote(Settings settings, string key, string text)
    {
        var t = CleanNote(text);
        if (t.Length == 0) settings.DnsNotes.Remove(key);
        else settings.DnsNotes[key] = t;
    }

    /// <summary>A speech bubble (comic-style, with a tail pointing at the note) that pops open from its tail:
    /// reads the whole note, or edits it.</summary>
    private void OpenBubble(FrameworkElement anchor, DnsEntry e, bool edit)
    {
        var key = Key(e);
        Hub.Settings.DnsNotes.TryGetValue(key, out var current);
        current ??= "";
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };

        var body = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        body.Children.Add(new TextBlock { Text = e.Name, Style = St("Label"), FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = Flow == FlowDirection.RightToLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) });
        void Close(bool redraw)
        {
            popup.IsOpen = false;
            if (redraw) DrawRows();
        }
        if (edit)
        {
            var box = new TextBox { Style = St("Input"), Text = current, MaxLength = NoteMax, TextWrapping = TextWrapping.Wrap, MinHeight = 64, Width = 260, Padding = new Thickness(10, 6, 10, 6), AcceptsReturn = false, FlowDirection = Flow };
            ToolTipService.SetToolTip(box, L.T("مثلاً: برای بازی X خوب بود / برای رفع تحریم Y جواب داد", "e.g. good for game X / got around block Y"));
            body.Children.Add(box);
            var save = new Button { Style = St("BtnPrimary"), Content = L.T("ذخیره", "Save"), Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
            var clear = new Button { Style = St("Btn"), Content = L.T("پاک‌کردن", "Clear"), Padding = new Thickness(14, 6, 14, 6) };
            void Commit(string text)
            {
                SetNote(Hub.Settings, key, text);
                Hub.Store.Save();
                Close(true);
            }
            save.Click += (_, _) => Commit(box.Text);
            clear.Click += (_, _) => Commit("");
            box.KeyDown += (_, k) => { if (k.Key == System.Windows.Input.Key.Enter) Commit(box.Text); };
            body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { save, clear } });
            popup.Opened += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };
        }
        else
        {
            body.Children.Add(new TextBlock { Text = current, Style = St("Body"), TextWrapping = TextWrapping.Wrap, MaxWidth = 280, LineHeight = 22 });
            var change = new Button { Style = St("Btn"), Content = L.T("ویرایش", "Edit"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            change.Click += (_, _) => { popup.IsOpen = false; OpenBubble(anchor, e, edit: true); };
            body.Children.Add(change);
        }

        var tail = new System.Windows.Shapes.Polygon
        {
            Points = [new Point(0, 11), new Point(11, 0), new Point(22, 11)], Fill = Res("Panel"), Stroke = Res("CardRim"), StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -1),
        };
        var bubble = new Border { Style = St("Card"), CornerRadius = new CornerRadius(16), Padding = new Thickness(0), Child = body, Background = Res("Panel") };
        Panel.SetZIndex(bubble, 1);
        var root = new StackPanel { Margin = new Thickness(10, 2, 10, 14), FlowDirection = Flow, Children = { tail, bubble }, RenderTransformOrigin = new Point(0.5, 0), Opacity = 0 };
        root.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.35, Direction = 270 };
        var scale = new ScaleTransform(0.55, 0.55);
        root.RenderTransform = scale;
        popup.Child = root;
        popup.Opened += (_, _) =>
        {
            var ease = new System.Windows.Media.Animation.BackEase { Amplitude = 0.55, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(320);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.55, 1, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.55, 1, dur) { EasingFunction = ease });
            root.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        };
        popup.IsOpen = true;
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
        _testLabel.Text = L.T("توقف تست", "Stop");
        NetFx.Spin(_test, true);
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
                    if (!_sortByPing) UpdatePing(Key(entry));
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
            _testLabel.Text = L.T("تست پینگ همه", "Test all pings");
            NetFx.Spin(_test, false);
            DrawRows(_sortByPing);
            var ok = _ping.Values.Count(v => v is not null);
            StatusText = L.T($"{Fa.Digits(ok.ToString())} دی‌ان‌اس پاسخ داد.", $"{ok} servers answered.");
        }
    }

    /// <summary>Reads and checks the manual fields; false (with a message) when they are not valid IPv4 addresses.</summary>
    private bool ReadOwn(out string a, out string b, out string name)
    {
        a = Latin(_dns1.Text.Trim());
        b = Latin(_dns2.Text.Trim());
        name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : a;
        if (DnsCatalog.TryParseIPv4(a, out _) && (b.Length == 0 || DnsCatalog.TryParseIPv4(b, out _))) return true;
        StatusText = L.T("آدرس‌ها باید IPv4 درست باشن؛ مثل 1.1.1.1", "Addresses must be valid IPv4, like 1.1.1.1");
        _status.Foreground = Res("Red");
        return false;
    }

    private void ApplyOwn()
    {
        if (ReadOwn(out var a, out var b, out var name)) ApplyDns(name, a, b);
    }

    /// <summary>Only keeps the entry in the list: no change is made to the system.</summary>
    private void AddOwn()
    {
        if (!ReadOwn(out var a, out var b, out var name)) return;
        if (Hub.Settings.CustomDns.Any(e => e.Primary == a && e.Secondary == b) || DnsCatalog.All.Any(e => e.Primary == a && e.Secondary == b))
        {
            _status.Foreground = Res("Muted");
            StatusText = L.T("این دی‌ان‌اس قبلاً توی لیست هست.", "That DNS is already in the list.");
            return;
        }
        Hub.Settings.CustomDns.Add(new DnsEntry { Name = name, Primary = a, Secondary = b, Group = "custom" });
        Hub.Store.Save();
        _status.Foreground = Res("Green");
        StatusText = L.T($"«{name}» به لیست اضافه شد (اعمال نشد).", $"“{name}” was added to the list (not applied).");
        _group = "all";
        DrawGroupChips();
        DrawRows(true);
    }

    /// <summary>Confirms, sets the DNS and reads it back. Empty addresses mean "automatic". False if not applied.</summary>
    private bool ApplyDns(string name, string primary, string secondary)
    {
        var adapter = Adapter;
        if (adapter is null)
        {
            StatusText = L.T("کارت شبکه‌ی فعالی پیدا نشد.", "No active network adapter was found.");
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
            StatusText = L.T("حالت نمایشی: چیزی ثبت نشد.", "Demo mode: nothing was changed.");
            return false;
        }
        if (!Hub.Elevated)
        {
            if (Ask(L.T("دسترسی ادمین لازمه", "Administrator rights needed"),
                    L.T("برای تغییر دی‌ان‌اس شب‌پا باید با دسترسی ادمین اجرا بشه. الان دوباره اجرا بشه؟", "Shabpa must run as administrator to change DNS. Restart it now?"),
                    L.T("اجرا با ادمین", "Restart as admin")))
                if (Application.Current is App app && !app.Restart(elevated: true, page: "dns"))
                    StatusText = L.T("اجازه‌ی ادمین داده نشد.", "Administrator permission was not given.");
            return false;
        }

        var error = Offload.KeepResponsive(() => DnsConfig.Apply(adapter, primary, secondary)); // netsh: seconds, so the window keeps answering meanwhile
        if (error is not null)
        {
            _status.Foreground = Res("Red");
            StatusText = error switch
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
        StatusText = L.T($"ثبت شد و از ویندوز تایید گرفت: {what}", $"Set and confirmed by Windows: {what}");
        Refresh();
        return true;
    }
}
