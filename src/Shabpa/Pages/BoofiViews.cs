using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Boofi drawn from the stacked pictures in Assets/boofi: the owl, then each worn part, in one 240-unit frame.
/// Every picture is flattened once into one frozen bitmap and kept, so a screen with many tiles is one cheap image per tile.</summary>
internal static class BoofiArt
{
    private static readonly ConcurrentDictionary<(string, int), BitmapImage> Layer = new();
    private static readonly ConcurrentDictionary<string, ImageSource> Flat = new();

    private static BitmapImage? Load(string file, int width)
    {
        if (Layer.TryGetValue((file, width), out var hit)) return hit;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri($"pack://application:,,,/Assets/boofi/{file}", UriKind.Absolute);
            img.DecodePixelWidth = width;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return Layer[(file, width)] = img;
        }
        catch { return null; } // no application resources (tests, design time): the picture is simply left out
    }

    private static double _dpi = 1;

    private static double Dpi()
    {
        try { if (Application.Current?.MainWindow is { } w) _dpi = Math.Clamp(VisualTreeHelper.GetDpi(w).DpiScaleX, 1, 2); } catch { }
        return _dpi;
    }

    /// <summary>The flattened picture of the worn parts, <paramref name="w"/> x <paramref name="h"/> device-independent pixels,
    /// centred on <paramref name="focus"/> (frame units). Left-to-right always: the Persian layout must not mirror Boofi.</summary>
    public static Image Make(IReadOnlyDictionary<string, string> worn, (double X, double Y, double W) focus, double w, double h)
    {
        var image = new Image { Width = w, Height = h, Stretch = Stretch.Fill, FlowDirection = FlowDirection.LeftToRight, IsHitTestVisible = false, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var layers = BoofiCatalog.Layers(worn);
        var key = $"{string.Join(',', layers)}|{focus.X}|{focus.Y}|{focus.W}|{w}|{h}";
        if (!Flat.TryGetValue(key, out var source))
        {
            source = Render(layers, focus, w, h);
            if (source is not null) Flat[key] = source;
        }
        image.Source = source;
        return image;
    }

    public static Image Make(IReadOnlyDictionary<string, string> worn, (double X, double Y, double W) focus, double size) => Make(worn, focus, size, size);

    private static ImageSource? Render(List<string> layers, (double X, double Y, double W) focus, double w, double h)
    {
        try
        {
            var s = w / focus.W;                  // device-independent pixels per frame unit
            var side = 240 * s;
            var dpi = Dpi();
            var want = side * dpi;
            var decode = want <= 128 ? 128 : want <= 224 ? 224 : want <= 336 ? 336 : 448;
            var visual = new DrawingVisual();
            var drawn = 0;
            using (var dc = visual.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                var left = w / 2 - (focus.X + 20) * s;
                var top = h / 2 - (focus.Y + 20) * s;
                foreach (var file in layers)
                {
                    if (Load(file, decode) is not { } picture) continue;
                    dc.DrawImage(picture, new Rect(left, top, side, side));
                    drawn++;
                }
                dc.Pop();
            }
            if (drawn == 0) return null;
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * dpi), (int)Math.Ceiling(h * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    public static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static Color Alpha(string hex, byte a) { var c = Color(hex); return System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B); }
    public static Brush Res(string key) => Ui.Live(key);
    public static Brush Rar(string rarity, byte a = 255) => Frozen(new SolidColorBrush(Alpha(BoofiCatalog.RarityColor(rarity), a)));
    public static Brush Frozen(Brush b) { b.Freeze(); return b; }

    /// <summary>Rough waiting time for a price at the usual earning pace (about 15 feathers a day).</summary>
    public static string Eta(int feathers)
    {
        var days = Math.Max(1, (int)Math.Ceiling(feathers / 15.0));
        string text = days < 7 ? L.T($"{Fa.N(days)} روز", days == 1 ? "1 day" : $"{days} days")
            : days < 45 ? L.T($"{Fa.N((int)Math.Round(days / 7.0))} هفته", $"{(int)Math.Round(days / 7.0)} weeks")
            : days < 700 ? L.T($"{Fa.N((int)Math.Round(days / 30.0))} ماه", $"{(int)Math.Round(days / 30.0)} months")
            : L.T($"{Fa.N(days / 365.0, 1)} سال", $"{days / 365.0:0.#} years");
        return L.T("حدود ", "about ") + text;
    }

    public static string DurShort(int feathers) => Eta(feathers).Replace(L.T("حدود ", "about "), "");

    // ---- small controls in the shop's own look (cheap Borders, no effects) -------------------------------------

    public static Border Pill(string text, bool active, Action click, string? dot = null, string? count = null, bool closable = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot is not null) row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Rar(dot), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = text, FontSize = 12.5, FontWeight = active ? FontWeights.Bold : FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, active ? "Accent" : "Text");
        row.Children.Add(label);
        if (count is not null)
        {
            var c = new TextBlock { Text = count, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            c.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(c);
        }
        if (closable)
        {
            var x = new TextBlock { Text = "✕", FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            x.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(x);
        }
        var b = new Border { Child = row, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.2), Cursor = Cursors.Hand };
        b.SetResourceReference(Border.BorderBrushProperty, active ? "Accent" : "Line");
        b.SetResourceReference(Border.BackgroundProperty, active ? "Panel3" : "Panel2");
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    public enum Kind { Primary, Ok, Ghost, Off }

    public static Border Button(string text, Kind kind, Action? click, double height = 34)
    {
        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Child = label, Height = height, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.2), Cursor = kind is Kind.Off or Kind.Ok ? Cursors.Arrow : Cursors.Hand };
        switch (kind)
        {
            case Kind.Primary:
                b.SetResourceReference(Border.BackgroundProperty, "Accent");
                b.BorderBrush = Brushes.Transparent;
                label.Foreground = Brushes.White;
                break;
            case Kind.Ok:
                b.Background = Frozen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 52, 211, 153)));
                b.BorderBrush = Frozen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 52, 211, 153)));
                label.SetResourceReference(TextBlock.ForegroundProperty, "Green");
                break;
            case Kind.Ghost:
                b.Background = Brushes.Transparent;
                b.SetResourceReference(Border.BorderBrushProperty, "Line");
                label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                break;
            default:
                b.Background = Brushes.Transparent;
                b.SetResourceReference(Border.BorderBrushProperty, "Line");
                label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                break;
        }
        if (click is not null && kind is not (Kind.Off or Kind.Ok)) b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    public static TextBlock Text(string text, double size, string brushKey = "Text", FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    public static void Cache(UIElement e) => e.CacheMode = new BitmapCache(Dpi());

    public static string DefaultLabel(string slot) => slot switch
    {
        "hat" => L.T("کلاه نگهبان (پیش‌فرض)", "Guard cap (default)"),
        "lantern" => L.T("فانوس پیش‌فرض", "Default lantern"),
        "fur" => BoofiCatalog.Find(BoofiCatalog.DefaultFur)!.Name,
        _ => L.T("بدون", "None"),
    };
}

/// <summary>The feather store, laid out like the prototype: a top bar (balance, active filters, count, sort, filter button),
/// a filter popover, and tiles grouped by rarity.</summary>
internal sealed class BoofiStoreView : StackPanel
{
    private readonly Hub _hub;
    private string _slot = "", _rarity = "", _status = "", _sort = "rarity";
    private readonly WrapPanel _bar = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _body = new();
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, PopupAnimation = PopupAnimation.Fade };
    private readonly StackPanel _popupBody = new();
    private Border? _filterButton;
    private string _sig = "";

    /// <summary>Raised when a tile's «preview» is pressed; the page switches to My Boofi with that part tried on.</summary>
    public event Action<BoofiPart>? PreviewRequested;

    public BoofiStoreView(Hub hub)
    {
        _hub = hub;
        _popup.Child = new Border
        {
            Child = _popupBody, Width = 360, Padding = new Thickness(16), Margin = new Thickness(8), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.5),
            BorderBrush = Ui.Live("Line"), Background = Ui.Live("Panel3"),
        };
        Children.Add(_bar);
        Children.Add(_body);
        Refresh(true);
    }

    private IEnumerable<BoofiPart> Filtered(RewardsEngine e, string slot, string rarity, string status) =>
        BoofiCatalog.All.Where(p => (slot == "" || p.Slot == slot) && (rarity == "" || p.Rarity == rarity)).Where(p => status switch
        {
            "buy" => !e.Owns(p.Id) && e.State.Feathers >= p.Price,
            "own" => e.Owns(p.Id),
            "new" => !e.Owns(p.Id),
            _ => true,
        });

    private int Active => (_slot != "" ? 1 : 0) + (_rarity != "" ? 1 : 0) + (_status != "" ? 1 : 0);

    /// <summary>Redraws only when something it shows has changed (the rewards engine reports changes every minute).</summary>
    public void Refresh(bool force = false)
    {
        var e = _hub.Rewards;
        var sig = $"{e.State.Feathers}|{e.EarnedToday}|{string.Join(',', e.State.Owned)}|{string.Join(',', e.State.Worn.Select(kv => kv.Key + kv.Value))}|{_slot}|{_rarity}|{_status}|{_sort}";
        if (!force && sig == _sig) return;
        _sig = sig;
        DrawBar(e);
        DrawBody(e);
        if (_popup.IsOpen) DrawPopup(e);
    }

    private void Set(Action change)
    {
        change();
        Refresh();
    }

    // ---- top bar -----------------------------------------------------------------------------------------------

    private void DrawBar(RewardsEngine e)
    {
        _bar.Children.Clear();
        var balance = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 6, 14, 6), Margin = new Thickness(0, 0, 10, 6), BorderThickness = new Thickness(1.2) };
        balance.SetResourceReference(Border.BorderBrushProperty, "Line");
        balance.SetResourceReference(Border.BackgroundProperty, "Panel2");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon("feather", 16, BoofiArt.Res("Gold"), 1.8) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(BoofiArt.Text(L.T($"{Fa.N(e.State.Feathers)} پَر", $"{Fa.N(e.State.Feathers)} feathers"), 14, "Gold", FontWeights.Bold));
        if (e.EarnedToday > 0)
            row.Children.Add(BoofiArt.Text(L.T($"  ·  امروز {Fa.N(e.EarnedToday)} پَر گرفتی", $"  ·  {Fa.N(e.EarnedToday)} earned today"), 12, "Muted"));
        balance.Child = row;
        _bar.Children.Add(balance);

        if (_slot != "") _bar.Children.Add(BoofiArt.Pill(BoofiCatalog.SlotName(_slot), true, () => Set(() => _slot = ""), closable: true));
        if (_rarity != "") _bar.Children.Add(BoofiArt.Pill(BoofiCatalog.RarityName(_rarity), true, () => Set(() => _rarity = ""), _rarity, closable: true));
        if (_status != "") _bar.Children.Add(BoofiArt.Pill(StatusName(_status), true, () => Set(() => _status = ""), closable: true));

        var count = Filtered(e, _slot, _rarity, _status).Count();
        var counter = BoofiArt.Text(L.T($"{Fa.N(count)} قطعه", count == 1 ? "1 item" : $"{count} items"), 12.5, "Muted");
        counter.Margin = new Thickness(6, 0, 12, 6);
        _bar.Children.Add(counter);

        var sortOptions = new[] { ("rarity", L.T("دسته‌بندی کمیابی", "By rarity")), ("cheap", L.T("ارزان‌ترین", "Cheapest first")), ("pricey", L.T("گران‌ترین", "Priciest first")) };
        var sort = new DropdownBox(sortOptions, _sort, 170) { Height = 32, Margin = new Thickness(0, 0, 8, 6) };
        sort.Picked += v => Set(() => _sort = v);
        _bar.Children.Add(sort);

        var filter = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1.2), Cursor = Cursors.Hand };
        filter.SetResourceReference(Border.BorderBrushProperty, Active > 0 ? "Accent" : "Line");
        filter.SetResourceReference(Border.BackgroundProperty, "Panel2");
        var frow = new StackPanel { Orientation = Orientation.Horizontal };
        frow.Children.Add(BoofiArt.Text(L.T("فیلتر", "Filter"), 12.5, Active > 0 ? "Accent" : "Text", FontWeights.SemiBold));
        if (Active > 0)
        {
            var badge = new Border { Background = BoofiArt.Res("Accent"), CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Margin = new Thickness(8, 0, 0, 0), Child = new TextBlock { Text = Fa.N(Active), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            frow.Children.Add(badge);
        }
        filter.Child = frow;
        filter.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; if (_popup.IsOpen) { _popup.IsOpen = false; return; } DrawPopup(_hub.Rewards); _popup.IsOpen = true; };
        _filterButton = filter;
        _popup.PlacementTarget = filter;
        _bar.Children.Add(filter);
    }

    private static string StatusName(string s) => s switch
    {
        "buy" => L.T("می‌تونم بخرم", "I can buy"), "own" => L.T("دارم", "Owned"), "new" => L.T("هنوز ندارم", "Not owned"), _ => L.T("همه", "All"),
    };

    // ---- filter popover ----------------------------------------------------------------------------------------

    private void DrawPopup(RewardsEngine e)
    {
        _popupBody.Children.Clear();
        void Section(string title, IEnumerable<Border> pills)
        {
            var t = BoofiArt.Text(title, 12, "Muted", FontWeights.SemiBold);
            t.Margin = new Thickness(2, 0, 0, 6);
            _popupBody.Children.Add(t);
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var p in pills) wrap.Children.Add(p);
            _popupBody.Children.Add(wrap);
        }
        // the numbers next to each choice count what you would get with the other filters kept
        Section(L.T("نوع", "Type"), new[] { BoofiArt.Pill(L.T("همه", "All"), _slot == "", () => Set(() => _slot = ""), count: Fa.N(Filtered(e, "", _rarity, _status).Count())) }
            .Concat(BoofiCatalog.Slots.Select(s => BoofiArt.Pill(BoofiCatalog.SlotName(s), _slot == s, () => Set(() => _slot = s), count: Fa.N(Filtered(e, s, _rarity, _status).Count())))));
        Section(L.T("کمیابی", "Rarity"), new[] { BoofiArt.Pill(L.T("همه", "All"), _rarity == "", () => Set(() => _rarity = "")) }
            .Concat(BoofiCatalog.Rarities.Select(r => BoofiArt.Pill(BoofiCatalog.RarityName(r), _rarity == r, () => Set(() => _rarity = r), r, Fa.N(Filtered(e, _slot, r, _status).Count())))));
        Section(L.T("وضعیت", "Status"), new[] { "", "buy", "own", "new" }.Select(s => BoofiArt.Pill(StatusName(s), _status == s, () => Set(() => _status = s))));
        Section(L.T("مرتب‌سازی", "Sort"), new[] { ("rarity", L.T("کمیابی", "Rarity")), ("cheap", L.T("ارزان‌ترین", "Cheapest")), ("pricey", L.T("گران‌ترین", "Priciest")) }
            .Select(o => BoofiArt.Pill(o.Item2, _sort == o.Item1, () => Set(() => _sort = o.Item1))));

        var found = Filtered(e, _slot, _rarity, _status).Count();
        var footer = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var clear = BoofiArt.Text(L.T("پاک کردن همه", "Clear all"), 12.5, "Muted", FontWeights.SemiBold);
        clear.Cursor = Cursors.Hand;
        clear.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; Set(() => { _slot = ""; _rarity = ""; _status = ""; }); };
        var info = BoofiArt.Text(L.T($"{Fa.N(found)} قطعه پیدا شد", found == 1 ? "1 item found" : $"{found} items found"), 12.5, "Text", FontWeights.SemiBold);
        info.HorizontalAlignment = HorizontalAlignment.Center;
        var show = BoofiArt.Button(L.T("نمایش", "Show"), BoofiArt.Kind.Primary, () => _popup.IsOpen = false, 32);
        show.Padding = new Thickness(18, 0, 18, 0);
        Grid.SetColumn(info, 1); Grid.SetColumn(show, 2);
        footer.Children.Add(clear); footer.Children.Add(info); footer.Children.Add(show);
        _popupBody.Children.Add(footer);
    }

    // ---- tiles -------------------------------------------------------------------------------------------------

    private void DrawBody(RewardsEngine e)
    {
        _body.Children.Clear();
        var list = Filtered(e, _slot, _rarity, _status).ToList();
        if (list.Count == 0)
        {
            var none = BoofiArt.Text(L.T("چیزی با این فیلترها پیدا نشد.", "Nothing matches these filters."), 13, "Muted");
            none.Margin = new Thickness(6, 24, 0, 0);
            _body.Children.Add(none);
            return;
        }
        if (_sort == "rarity")
        {
            foreach (var r in BoofiCatalog.Rarities)
            {
                var group = list.Where(p => p.Rarity == r).OrderBy(p => Array.IndexOf(BoofiCatalog.Slots, p.Slot)).ThenBy(p => p.Price).ToList();
                if (group.Count == 0) continue;
                _body.Children.Add(SectionHeader(r, group));
                _body.Children.Add(TileWrap(group, e));
            }
        }
        else
        {
            var ordered = _sort == "cheap" ? list.OrderBy(p => p.Price) : list.OrderByDescending(p => p.Price);
            _body.Children.Add(TileWrap(ordered.ToList(), e));
        }
    }

    private static FrameworkElement SectionHeader(string rarity, List<BoofiPart> group)
    {
        var lo = group.Min(p => p.Price);
        var hi = group.Max(p => p.Price);
        var head = new DockPanel { Margin = new Thickness(0, 14, 0, 6), LastChildFill = true };
        var dot = new Ellipse { Width = 10, Height = 10, Fill = BoofiArt.Rar(rarity), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Left);
        head.Children.Add(dot);
        var name = new TextBlock { Text = BoofiCatalog.RarityName(rarity), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = BoofiArt.Rar(rarity), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(name, Dock.Left);
        head.Children.Add(name);
        var range = lo == hi ? $"{Fa.N(lo)}" : L.T($"{Fa.N(lo)} تا {Fa.N(hi)}", $"{lo} to {hi}");
        var dur = lo == hi ? BoofiArt.DurShort(lo) : L.T($"{BoofiArt.DurShort(lo)} تا {BoofiArt.DurShort(hi)}", $"{BoofiArt.DurShort(lo)} to {BoofiArt.DurShort(hi)}");
        var meta = BoofiArt.Text(L.T($"{Fa.N(group.Count)} قطعه  ·  {range} پَر  ·  {dur}", $"{group.Count} items  ·  {range} feathers  ·  {dur}"), 12, "Muted");
        meta.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(meta, Dock.Left);
        head.Children.Add(meta);
        var line = new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Center, StrokeThickness = 1, StrokeDashArray = [3, 3], Fill = null };
        line.SetResourceReference(Shape.StrokeProperty, "Line");
        head.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Child = line });
        return head;
    }

    private WrapPanel TileWrap(List<BoofiPart> parts, RewardsEngine e)
    {
        var wrap = new WrapPanel { Margin = new Thickness(-5, 0, -5, 0) };
        foreach (var p in parts) wrap.Children.Add(Tile(p, e));
        return wrap;
    }

    private FrameworkElement Tile(BoofiPart p, RewardsEngine e)
    {
        var owned = e.Owns(p.Id);
        var worn = e.State.Worn.GetValueOrDefault(p.Slot) == p.Id || (p.Id == BoofiCatalog.DefaultFur && !e.State.Worn.ContainsKey("fur"));
        var have = e.State.Feathers;
        var can = have >= p.Price;
        var strong = p.Rarity is "epic" or "legend";

        var root = new Grid { Width = 178 };
        for (var i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = i switch { 0 => new GridLength(128), 1 or 4 => GridLength.Auto, 2 => new GridLength(1, GridUnitType.Star), _ => GridLength.Auto } });

        // art with rarity glow, pills and the owned badge
        var art = new Grid { ClipToBounds = true };
        var artBack = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = BoofiArt.Frozen(new RadialGradientBrush(BoofiArt.Alpha(BoofiCatalog.RarityColor(p.Rarity), (byte)(strong ? 95 : 60)), Colors.Transparent) { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .75, RadiusY = .75 }),
        };
        art.Children.Add(artBack);
        var image = BoofiArt.Make(new Dictionary<string, string> { [p.Slot] = p.Id }, BoofiCatalog.Focus(p.Slot), 178, 128);
        art.Children.Add(image);
        var rar = new Border { Background = BoofiArt.Rar(p.Rarity, 60), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        rar.Child = new TextBlock { Text = BoofiCatalog.RarityName(p.Rarity), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = BoofiArt.Rar(p.Rarity) };
        art.Children.Add(rar);
        var tag = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        tag.SetResourceReference(Border.BackgroundProperty, "Panel3");
        tag.Child = BoofiArt.Text(BoofiCatalog.SlotName(p.Slot), 10.5, "Muted");
        art.Children.Add(tag);
        if (owned)
        {
            var badge = new Border { Background = BoofiArt.Frozen(new SolidColorBrush(Color.FromArgb(200, 22, 120, 84))), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            badge.Child = new TextBlock { Text = worn ? L.T("پوشیده", "Worn") : L.T("داری", "Owned"), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            art.Children.Add(badge);
        }
        if (!owned) { art.Cursor = Cursors.Hand; art.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; PreviewRequested?.Invoke(p); }; art.Background = Brushes.Transparent; }
        Grid.SetRow(art, 0);
        root.Children.Add(art);

        var name = new TextBlock { Text = p.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 6, 2, 0), ToolTip = p.Name };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        Grid.SetRow(name, 1);
        root.Children.Add(name);

        var sub = new TextBlock { Text = p.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, Margin = new Thickness(2, 1, 2, 0), ToolTip = p.Description, VerticalAlignment = VerticalAlignment.Top };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(sub, 2);
        root.Children.Add(sub);

        // price, waiting time and (when it is not affordable yet) how far along you are
        var price = new StackPanel { Margin = new Thickness(2, 4, 2, 6) };
        var prow = new DockPanel { LastChildFill = true };
        string brush = owned ? "Muted" : can ? "Gold" : "Text";
        if (p.Price == 0)
            prow.Children.Add(BoofiArt.Text(L.T("رایگان", "Free"), 14, "Green", FontWeights.Bold));
        else
        {
            var f = new Icon("feather", 14, BoofiArt.Res(brush), 1.8) { Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(f, Dock.Left);
            prow.Children.Add(f);
            var amount = BoofiArt.Text(Fa.N(p.Price), 15, brush, FontWeights.Bold);
            DockPanel.SetDock(amount, Dock.Left);
            prow.Children.Add(amount);
            if (!owned && !can)
            {
                var eta = BoofiArt.Text(BoofiArt.Eta(p.Price - have), 11, "Muted");
                eta.HorizontalAlignment = HorizontalAlignment.Right;
                eta.ToolTip = L.T($"{Fa.N(p.Price - have)} پَر کم داری", $"{p.Price - have} feathers short");
                prow.Children.Add(eta);
            }
        }
        price.Children.Add(prow);
        if (!owned && !can && p.Price > 0)
        {
            var frac = Math.Clamp((double)have / p.Price, 0, 1);
            var track = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
            var back = new Border { CornerRadius = new CornerRadius(2) };
            back.SetResourceReference(Border.BackgroundProperty, "Line");
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(frac, .001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - frac, .001), GridUnitType.Star) });
            var fill = new Border { CornerRadius = new CornerRadius(2), Background = BoofiArt.Rar(p.Rarity) };
            System.Windows.Controls.Grid.SetColumnSpan(back, 2);
            track.Children.Add(back);
            track.Children.Add(fill);
            price.Children.Add(track);
        }
        Grid.SetRow(price, 3);
        root.Children.Add(price);

        BoofiArt.Kind kind;
        string label;
        Action? click;
        if (owned && worn) { kind = BoofiArt.Kind.Ok; label = L.T("پوشیده ✓", "Worn ✓"); click = null; }
        else if (owned) { kind = BoofiArt.Kind.Primary; label = L.T("بپوش", "Wear"); click = () => e.Wear(p.Slot, p.Id); }
        else if (can) { kind = BoofiArt.Kind.Primary; label = L.T("بخر", "Buy"); click = () => Buy(p, e); }
        else { kind = BoofiArt.Kind.Ghost; label = L.T("پیش‌نمایش", "Preview"); click = () => PreviewRequested?.Invoke(p); }
        var button = BoofiArt.Button(label, kind, click);
        Grid.SetRow(button, 4);
        root.Children.Add(button);

        var card = new Border
        {
            Width = 212, Height = 284, Margin = new Thickness(5), Padding = new Thickness(17, 10, 17, 14), CornerRadius = new CornerRadius(18),
            Background = BoofiArt.Rar(p.Rarity, (byte)(strong ? 34 : 20)),
            BorderThickness = new Thickness(strong ? 2 : 1.4),
            BorderBrush = p.Rarity == "season" ? Brushes.Transparent : BoofiArt.Rar(p.Rarity, (byte)(strong ? 230 : 120)),
            Child = root,
        };
        FrameworkElement result = card;
        if (p.Rarity == "season")
        {
            // seasonal parts: dashed border
            var outer = new Grid { Width = 222, Height = 294 };
            var dash = new Rectangle { Margin = new Thickness(6), RadiusX = 18, RadiusY = 18, Stroke = BoofiArt.Rar(p.Rarity, 190), StrokeThickness = 1.6, StrokeDashArray = [4, 3], IsHitTestVisible = false };
            card.Margin = new Thickness(5);
            outer.Children.Add(card);
            outer.Children.Add(dash);
            result = outer;
        }
        BoofiArt.Cache(result); // one cached picture per tile: scrolling only moves bitmaps
        return result;
    }

    private void Buy(BoofiPart p, RewardsEngine engine)
    {
        if (p.Price >= 300 && !ConfirmWindow.Ask(L.T($"«{p.Name}» رو بخری؟", $"Buy “{p.Name}”?"),
                L.T($"{Fa.N(p.Price)} پَر از موجودیت کم می‌شه. سطحت پایین نمی‌آد.", $"{p.Price} feathers will be taken from your balance. Your level does not go down."), L.T("بخر", "Buy"), null, false, null)) return;
        switch (engine.Buy(p.Id))
        {
            case RewardsEngine.BuyResult.Ok:
                _hub.Notify(L.T("خریدی!", "Bought!"), L.T($"«{p.Name}» مال توئه. توی «بوفی من» تنش کن.", $"“{p.Name}” is yours. Put it on in My Boofi."), "rewards");
                break;
            case RewardsEngine.BuyResult.NotEnough:
                _hub.Notify(L.T("پَر کافی نیست", "Not enough feathers"), L.T("با مأموریت‌ها و مدال‌ها پَر جمع کن.", "Collect more with quests and medals."), "rewards");
                break;
        }
    }
}

/// <summary>«بوفی من»: the stage with Boofi as dressed now (or with a part tried on), six slot tiles, the parts of the chosen slot
/// (owned ones can be worn, the others previewed) and three saved looks.</summary>
internal sealed class BoofiWardrobeView : Grid
{
    private readonly Hub _hub;
    private string _slot = "hat";
    private BoofiPart? _try;
    private readonly Border _stage = new() { Width = 430, CornerRadius = new CornerRadius(22), Padding = new Thickness(14, 12, 14, 14), VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1.2) };
    private readonly WrapPanel _slots = new() { Margin = new Thickness(-4, 0, -4, 8) };
    private readonly WrapPanel _grid = new() { Margin = new Thickness(-4, 0, -4, 0) };
    private string _sig = "";

    public BoofiWardrobeView(Hub hub)
    {
        _hub = hub;
        _stage.SetResourceReference(Border.BorderBrushProperty, "Line");
        _stage.SetResourceReference(Border.BackgroundProperty, "Panel2");
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        var right = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        right.Children.Add(_slots);
        right.Children.Add(_grid);
        SetColumn(_stage, 0); SetColumn(right, 1);
        Children.Add(_stage);
        Children.Add(right);
        Refresh(true);
    }

    /// <summary>Tries a part on (from the shop's «preview»).</summary>
    public void Preview(BoofiPart p)
    {
        _try = p;
        _slot = p.Slot;
        Refresh(true);
    }

    private IReadOnlyDictionary<string, string> Look(RewardsEngine e)
    {
        if (_try is null) return e.State.Worn;
        var d = new Dictionary<string, string>(e.State.Worn) { [_try.Slot] = _try.Id };
        return d;
    }

    public void Refresh(bool force = false)
    {
        var e = _hub.Rewards;
        var sig = $"{e.State.Feathers}|{string.Join(',', e.State.Owned)}|{string.Join(',', e.State.Worn.Select(kv => kv.Key + kv.Value))}|{_slot}|{_try?.Id}|{e.State.ActivePreset}|{string.Join('/', e.State.Presets.Select(x => x.Count))}";
        if (!force && sig == _sig) return;
        _sig = sig;
        DrawStage(e);
        DrawSlots(e);
        DrawParts(e);
    }

    private void DrawStage(RewardsEngine e)
    {
        var look = Look(e);
        var stack = new StackPanel();
        stack.Children.Add(BoofiArt.Text(L.T("بوفی", "Boofi"), 20, "Text", FontWeights.Bold));
        var hint = BoofiArt.Text(_try is null ? L.T("یکی از بخش‌های سمت دیگه رو انتخاب کن و قطعه‌ای رو تنش کن.", "Pick a slot on the other side and put a part on him.") : L.T($"داری «{_try.Name}» رو امتحان می‌کنی.", $"You are trying on “{_try.Name}”."), 12, "Muted");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 2, 0, 4);
        stack.Children.Add(hint);
        var art = BoofiArt.Make(look, BoofiCatalog.Whole, 330, 330);
        art.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(art);

        if (_try is { } t)
        {
            var bar = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 4, 0, 8), BorderThickness = new Thickness(1.2), BorderBrush = BoofiArt.Rar(t.Rarity, 180), Background = BoofiArt.Rar(t.Rarity, 30) };
            var row = new DockPanel();
            var cancel = BoofiArt.Button(L.T("لغو", "Cancel"), BoofiArt.Kind.Ghost, () => { _try = null; Refresh(true); }, 30);
            cancel.Padding = new Thickness(12, 0, 12, 0);
            DockPanel.SetDock(cancel, Dock.Left);
            row.Children.Add(cancel);
            if (!e.Owns(t.Id))
            {
                var can = e.State.Feathers >= t.Price;
                var buy = BoofiArt.Button(can ? L.T($"بخر · {Fa.N(t.Price)}", $"Buy · {t.Price}") : L.T($"{Fa.N(t.Price - e.State.Feathers)} پَر کم داری", $"{t.Price - e.State.Feathers} short"), can ? BoofiArt.Kind.Primary : BoofiArt.Kind.Off, () =>
                {
                    if (e.Buy(t.Id) == RewardsEngine.BuyResult.Ok) { _hub.Notify(L.T("خریدی!", "Bought!"), L.T($"«{t.Name}» مال توئه.", $"“{t.Name}” is yours."), "rewards"); e.Wear(t.Slot, t.Id); _try = null; Refresh(true); }
                }, 30);
                buy.Padding = new Thickness(12, 0, 12, 0);
                buy.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(buy, Dock.Left);
                row.Children.Add(buy);
            }
            else
            {
                var wear = BoofiArt.Button(L.T("بپوش", "Wear"), BoofiArt.Kind.Primary, () => { e.Wear(t.Slot, t.Id); _try = null; Refresh(true); }, 30);
                wear.Padding = new Thickness(12, 0, 12, 0);
                wear.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(wear, Dock.Left);
                row.Children.Add(wear);
            }
            row.Children.Add(BoofiArt.Text(L.T("امتحان", "Try-on") + " · " + t.Name, 12.5, "Text", FontWeights.SemiBold));
            bar.Child = row;
            stack.Children.Add(bar);
        }

        // what is worn now: six small cells with the rarity colour on the edge
        var worn = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 8) };
        foreach (var slot in BoofiCatalog.Slots)
        {
            var id = look.GetValueOrDefault(slot);
            var part = id is null ? null : BoofiCatalog.Find(id);
            var cell = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(3), BorderThickness = new Thickness(3, 1, 1, 1) };
            cell.SetResourceReference(Border.BackgroundProperty, "Panel3");
            if (part is null) cell.SetResourceReference(Border.BorderBrushProperty, "Line"); else cell.BorderBrush = BoofiArt.Rar(part.Rarity);
            var col = new StackPanel();
            col.Children.Add(BoofiArt.Text(BoofiCatalog.SlotName(slot), 10.5, "Muted"));
            var n = BoofiArt.Text(part?.Name ?? BoofiArt.DefaultLabel(slot), 12.5, part is null ? "Muted" : "Text", FontWeights.SemiBold);
            n.TextTrimming = TextTrimming.CharacterEllipsis;
            col.Children.Add(n);
            cell.Child = col;
            worn.Children.Add(cell);
        }
        stack.Children.Add(worn);

        // three saved looks
        var presets = new WrapPanel();
        for (var i = 0; i < 3; i++)
        {
            var idx = i;
            var saved = e.Preset(i) is not null;
            var active = e.State.ActivePreset == i;
            var pill = BoofiArt.Pill(L.T($"ست {Fa.N(i + 1)}", $"Set {i + 1}") + (saved ? "" : " +"), active, () => { _try = null; e.UsePreset(idx); Refresh(true); });
            pill.ToolTip = saved ? L.T("این ست رو بپوش؛ از این به بعد تغییرها توش ذخیره می‌شه.", "Put this set on; changes are saved into it from now on.") : L.T("لباس فعلی رو توی این ست ذخیره کن.", "Save the current look into this set.");
            presets.Children.Add(pill);
        }
        var off = BoofiArt.Pill(L.T("درآوردن همه", "Take all off"), false, () => { _try = null; foreach (var s in e.State.Worn.Keys.ToList()) e.Wear(s, null); Refresh(true); });
        presets.Children.Add(off);
        stack.Children.Add(presets);
        _stage.Child = stack;
    }

    private void DrawSlots(RewardsEngine e)
    {
        _slots.Children.Clear();
        foreach (var slot in BoofiCatalog.Slots)
        {
            var id = e.State.Worn.GetValueOrDefault(slot);
            var part = id is null ? null : BoofiCatalog.Find(id);
            var selected = slot == _slot;
            var col = new StackPanel { Width = 104 };
            var art = BoofiArt.Make(e.State.Worn, BoofiCatalog.Focus(slot) is var f ? (f.X, f.Y, f.W * 1.1) : BoofiCatalog.Whole, 92, 70);
            art.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(art);
            col.Children.Add(new TextBlock { Text = BoofiCatalog.SlotName(slot), FontSize = 12.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) }.With(t => t.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Accent" : "Text")));
            var n = BoofiArt.Text(part?.Name ?? BoofiArt.DefaultLabel(slot), 10.5, "Muted");
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.TextTrimming = TextTrimming.CharacterEllipsis;
            col.Children.Add(n);
            var b = new Border { Child = col, Margin = new Thickness(4), Padding = new Thickness(6, 6, 6, 8), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(selected ? 2 : 1.2), Cursor = Cursors.Hand };
            b.SetResourceReference(Border.BorderBrushProperty, selected ? "Accent" : "Line");
            b.SetResourceReference(Border.BackgroundProperty, selected ? "Panel3" : "Panel2");
            var s2 = slot;
            b.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; _slot = s2; if (_try is not null && _try.Slot != s2) _try = null; Refresh(true); };
            BoofiArt.Cache(b);
            _slots.Children.Add(b);
        }
    }

    private void DrawParts(RewardsEngine e)
    {
        _grid.Children.Clear();
        var wornId = e.State.Worn.GetValueOrDefault(_slot);
        if (_slot != "fur") _grid.Children.Add(Cell(null, wornId is null, e));
        foreach (var p in BoofiCatalog.All.Where(p => p.Slot == _slot).OrderByDescending(p => e.Owns(p.Id)).ThenBy(p => Array.IndexOf(BoofiCatalog.Rarities, p.Rarity)).ThenBy(p => p.Price))
            _grid.Children.Add(Cell(p, p.Id == wornId || (_slot == "fur" && wornId is null && p.Id == BoofiCatalog.DefaultFur), e));
    }

    private FrameworkElement Cell(BoofiPart? p, bool on, RewardsEngine e)
    {
        var owned = p is null || e.Owns(p.Id);
        var trying = p is not null && _try?.Id == p.Id;
        var body = new StackPanel { Width = 128 };
        if (p is null)
        {
            var none = new Border { Height = 84, Child = new TextBlock { Text = "∅", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.With(t => t.SetResourceReference(TextBlock.ForegroundProperty, "Muted")) };
            body.Children.Add(none);
            body.Children.Add(BoofiArt.Text(BoofiArt.DefaultLabel(_slot), 11.5, "Text", FontWeights.SemiBold).With(t => { t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 4, 0, 0); }));
        }
        else
        {
            var art = BoofiArt.Make(new Dictionary<string, string> { [p.Slot] = p.Id }, BoofiCatalog.Focus(p.Slot), 128, 84);
            if (!owned) art.Opacity = .72;
            body.Children.Add(art);
            body.Children.Add(BoofiArt.Text(p.Name, 12, "Text", FontWeights.SemiBold).With(t => { t.TextAlignment = TextAlignment.Center; t.TextTrimming = TextTrimming.CharacterEllipsis; t.Margin = new Thickness(0, 4, 0, 0); }));
            var status = on ? L.T("پوشیده ✓", "Worn ✓") : owned ? BoofiCatalog.RarityName(p.Rarity) : trying ? L.T("در حال امتحان", "Trying") : L.T($"پیش‌نمایش · {Fa.N(p.Price)}", $"Preview · {p.Price}");
            var st = new TextBlock { Text = status, FontSize = 10.5, TextAlignment = TextAlignment.Center, Foreground = on ? BoofiArt.Res("Green") : BoofiArt.Rar(p.Rarity), Margin = new Thickness(0, 1, 0, 0) };
            body.Children.Add(st);
        }
        var color = p is null ? "common" : p.Rarity;
        var border = new Border
        {
            Margin = new Thickness(4), Padding = new Thickness(8, 8, 8, 8), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand, Child = body,
            BorderThickness = new Thickness(on || trying ? 2.4 : 1.2),
            BorderBrush = on ? BoofiArt.Res("Gold") : trying ? BoofiArt.Res("Accent") : p is null ? Brushes.Transparent : BoofiArt.Rar(color, 120),
            Background = p is null ? Brushes.Transparent : BoofiArt.Rar(color, (byte)(on ? 48 : 22)),
        };
        if (p is null)
        {
            var dash = new Grid();
            dash.Children.Add(border);
            dash.Children.Add(new Rectangle { Margin = new Thickness(4), RadiusX = 14, RadiusY = 14, StrokeThickness = 1.4, StrokeDashArray = [4, 3], IsHitTestVisible = false }.With(r => r.SetResourceReference(Shape.StrokeProperty, "Line")));
            border.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; _try = null; e.Wear(_slot, null); Refresh(true); };
            BoofiArt.Cache(dash);
            return dash;
        }
        border.MouseLeftButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            if (owned) { _try = null; e.Wear(_slot, p.Id); Refresh(true); }
            else { _try = p; Refresh(true); }
        };
        BoofiArt.Cache(border);
        return border;
    }
}

internal static class UiExt
{
    public static T With<T>(this T item, Action<T> act) { act(item); return item; }
}
