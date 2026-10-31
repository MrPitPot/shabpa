using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Boofi drawn from the stacked pictures in Assets/boofi: the owl, then each worn part, in one frame.</summary>
internal static class BoofiArt
{
    private static readonly ConcurrentDictionary<(string, int), BitmapImage> Cache = new();

    private static BitmapImage? Load(string file, int width)
    {
        if (Cache.TryGetValue((file, width), out var hit)) return hit;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri($"pack://application:,,,/Assets/boofi/{file}", UriKind.Absolute);
            img.DecodePixelWidth = width;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return Cache[(file, width)] = img;
        }
        catch { return null; } // no application resources (tests, design time): the picture is simply left out
    }

    /// <summary>A square picture of <paramref name="size"/> pixels showing the part of the frame given by <paramref name="focus"/>.</summary>
    public static FrameworkElement Make(IReadOnlyDictionary<string, string> worn, (double X, double Y, double W) focus, double size)
    {
        var s = size / focus.W; // pixels per unit of the 240-unit frame
        var side = 240 * s;
        var decode = (int)Math.Clamp(Math.Ceiling(side * 1.25), 96, 448);
        var canvas = new Canvas { Width = size, Height = size, ClipToBounds = true, IsHitTestVisible = false };
        foreach (var file in BoofiCatalog.Layers(worn))
        {
            if (Load(file, decode) is not { } source) continue;
            var image = new Image { Source = source, Width = side, Height = side, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(image, (-20 - (focus.X - focus.W / 2)) * s);
            Canvas.SetTop(image, (-20 - (focus.Y - focus.W / 2)) * s);
            canvas.Children.Add(image);
        }
        return canvas;
    }

    public static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static Style St(string key) => (Style)Application.Current.Resources[key];
    public static Brush Res(string key) => Ui.Live(key);
}

/// <summary>The feather store: every part as a tile, with filters. Buying spends feathers; owned parts can be worn straight away.</summary>
internal sealed class BoofiStoreView : StackPanel
{
    private readonly Hub _hub;
    private string _slot = "", _rarity = "", _status = "";
    private readonly StackPanel _slotChips = new(), _rarityChips = new(), _statusChips = new();
    private readonly TextBlock _balance = new() { FontWeight = FontWeights.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _grid = new() { Margin = new Thickness(-5, 10, -5, 0) };

    public BoofiStoreView(Hub hub)
    {
        _hub = hub;
        var top = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        var bal = new Border { Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 8, 14, 8), Child = _balance };
        DockPanel.SetDock(bal, Dock.Left);
        top.Children.Add(bal);
        Children.Add(top);
        Children.Add(_slotChips);
        Children.Add(_rarityChips);
        Children.Add(_statusChips);
        Children.Add(_grid);
        Refresh();
    }

    private static string L2(string fa, string en) => L.T(fa, en);

    public void Refresh()
    {
        var engine = _hub.Rewards;
        _balance.Text = L.T($"پَر تو: {Fa.N(engine.State.Feathers)}", $"Your feathers: {engine.State.Feathers}");
        _balance.Foreground = BoofiArt.Res("Gold");
        PageBase.Chips(_slotChips, new[] { ("", L2("همه‌ی بخش‌ها", "Everything")) }.Concat(BoofiCatalog.Slots.Select(s => (s, BoofiCatalog.SlotName(s)))), _slot, v => { _slot = v; Refresh(); });
        PageBase.Chips(_rarityChips, new[] { ("", L2("همه‌ی کمیابی‌ها", "Any rarity")) }.Concat(BoofiCatalog.Rarities.Select(r => (r, BoofiCatalog.RarityName(r)))), _rarity, v => { _rarity = v; Refresh(); });
        PageBase.Chips(_statusChips, new[] { ("", L2("همه", "All")), ("buy", L2("می‌تونم بخرم", "I can buy")), ("own", L2("خریده‌ام", "Owned")), ("new", L2("هنوز نخریده‌ام", "Not bought")) }, _status, v => { _status = v; Refresh(); });
        _grid.Children.Clear();
        var list = BoofiCatalog.All.Where(p => (_slot == "" || p.Slot == _slot) && (_rarity == "" || p.Rarity == _rarity)).Where(p => _status switch
        {
            "buy" => !engine.Owns(p.Id) && engine.State.Feathers >= p.Price,
            "own" => engine.Owns(p.Id),
            "new" => !engine.Owns(p.Id),
            _ => true,
        })
        .OrderBy(p => Array.IndexOf(BoofiCatalog.Rarities, p.Rarity)).ThenBy(p => Array.IndexOf(BoofiCatalog.Slots, p.Slot)).ThenBy(p => p.Price).ToList();
        foreach (var p in list) _grid.Children.Add(Tile(p, engine));
        if (list.Count == 0)
            _grid.Children.Add(new TextBlock { Text = L2("چیزی با این فیلترها پیدا نشد.", "Nothing matches these filters."), Style = BoofiArt.St("Body"), Margin = new Thickness(8, 20, 0, 0) });
    }

    private FrameworkElement Tile(BoofiPart p, RewardsEngine engine)
    {
        var color = BoofiArt.Color(BoofiCatalog.RarityColor(p.Rarity));
        var owned = engine.Owns(p.Id);
        var worn = engine.State.Worn.GetValueOrDefault(p.Slot) == p.Id || (p.Id == BoofiCatalog.DefaultFur && !engine.State.Worn.ContainsKey("fur"));
        var body = new StackPanel();
        var art = new Border
        {
            Width = 180, Height = 150, CornerRadius = new CornerRadius(14), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center,
            Background = new RadialGradientBrush(Color.FromArgb(70, color.R, color.G, color.B), Colors.Transparent) { Center = new Point(.5, .45), GradientOrigin = new Point(.5, .45), RadiusX = .7, RadiusY = .7 },
            Child = BoofiArt.Make(new Dictionary<string, string> { [p.Slot] = p.Id }, BoofiCatalog.Focus(p.Slot), 150),
        };
        body.Children.Add(art);
        var pill = new Border { Background = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        pill.Child = new TextBlock { Text = $"{BoofiCatalog.SlotName(p.Slot)} · {BoofiCatalog.RarityName(p.Rarity)}", FontSize = 11, Foreground = new SolidColorBrush(color) };
        body.Children.Add(pill);
        body.Children.Add(new TextBlock { Text = p.Name, Style = BoofiArt.St("H2"), FontSize = 14, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        body.Children.Add(new TextBlock { Text = p.Note, Style = BoofiArt.St("Label"), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 30, Margin = new Thickness(0, 2, 0, 0), ToolTip = p.Note });
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
        Button action;
        if (owned)
        {
            action = new Button { Style = BoofiArt.St(worn ? "Btn" : "BtnPrimary"), Content = worn ? L2("پوشیده", "Worn") : L2("بپوش", "Wear"), IsEnabled = !worn, Padding = new Thickness(14, 5, 14, 5) };
            action.Click += (_, _) => { engine.Wear(p.Slot, p.Id); };
            row.Children.Add(new TextBlock { Text = L2("خریده‌ای", "Owned"), Foreground = BoofiArt.Res("Green"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        }
        else
        {
            var can = engine.State.Feathers >= p.Price;
            action = new Button { Style = BoofiArt.St(can ? "BtnPrimary" : "Btn"), Content = L2("بخر", "Buy"), IsEnabled = can, Padding = new Thickness(14, 5, 14, 5), ToolTip = can ? null : L.T($"{Fa.N(p.Price - engine.State.Feathers)} پَر کم داری", $"{p.Price - engine.State.Feathers} feathers short") };
            action.Click += (_, _) => Buy(p, engine);
            row.Children.Add(new TextBlock { Text = Fa.N(p.Price), FontWeight = FontWeights.Bold, Foreground = BoofiArt.Res(can ? "Gold" : "Muted"), VerticalAlignment = VerticalAlignment.Center, FontSize = 15 });
            row.Children.Add(new Icon("feather", 14, BoofiArt.Res(can ? "Gold" : "Muted"), 1.8) { Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        DockPanel.SetDock(action, Dock.Left);
        row.Children.Add(action);
        body.Children.Add(row);
        return new Border
        {
            Width = 204, Margin = new Thickness(5), Padding = new Thickness(10), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(p.Rarity is "legend" or "epic" ? 230 : 130), color.R, color.G, color.B)),
            Background = new SolidColorBrush(Color.FromArgb(22, color.R, color.G, color.B)),
            Effect = p.Rarity is "legend" or "epic" ? new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 18, ShadowDepth = 0, Opacity = .35 } : null,
            Child = body,
        };
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

/// <summary>«بوفی من»: Boofi as dressed now, and the parts that are owned, slot by slot.</summary>
internal sealed class BoofiWardrobeView : Grid
{
    private readonly Hub _hub;
    private string _slot = "hat";
    private readonly Border _stage = new() { Width = 400, CornerRadius = new CornerRadius(24), Padding = new Thickness(10, 10, 10, 14), VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
    private readonly StackPanel _slotChips = new();
    private readonly WrapPanel _grid = new() { Margin = new Thickness(-5, 10, -5, 0) };

    public BoofiWardrobeView(Hub hub)
    {
        _hub = hub;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        var right = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        right.Children.Add(_slotChips);
        right.Children.Add(_grid);
        SetColumn(_stage, 0); SetColumn(right, 1);
        Children.Add(_stage);
        Children.Add(right);
        Refresh();
    }

    public void Refresh()
    {
        var engine = _hub.Rewards;
        var worn = engine.State.Worn;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = L.T("بوفی", "Boofi"), Style = BoofiArt.St("H1"), FontSize = 20, Margin = new Thickness(6, 0, 0, 4) });
        var art = BoofiArt.Make(worn, BoofiCatalog.Whole, 380);
        stack.Children.Add(art);
        foreach (var slot in BoofiCatalog.Slots)
        {
            var id = worn.GetValueOrDefault(slot);
            var part = id is null ? null : BoofiCatalog.Find(id);
            var line = new DockPanel { Margin = new Thickness(6, 2, 6, 2) };
            line.Children.Add(new TextBlock { Text = BoofiCatalog.SlotName(slot), Style = BoofiArt.St("Label"), Width = 70 });
            line.Children.Add(new TextBlock
            {
                Text = part?.Name ?? (slot == "hat" ? L.T("کلاه نگهبان (پیش‌فرض)", "Guard cap (default)") : slot == "lantern" ? L.T("فانوس پیش‌فرض", "Default lantern") : slot == "fur" ? BoofiCatalog.Find(BoofiCatalog.DefaultFur)!.Name : "—"),
                Foreground = part is null ? BoofiArt.Res("Muted") : new SolidColorBrush(BoofiArt.Color(BoofiCatalog.RarityColor(part.Rarity))), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Children.Add(line);
        }
        var clear = new Button { Style = BoofiArt.St("Btn"), Content = L.T("درآوردن همه", "Take everything off"), Margin = new Thickness(6, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = worn.Count > 0 };
        clear.Click += (_, _) => { foreach (var s in worn.Keys.ToList()) engine.Wear(s, null); };
        stack.Children.Add(clear);
        _stage.Child = stack;

        PageBase.Chips(_slotChips, BoofiCatalog.Slots.Select(s => (s, BoofiCatalog.SlotName(s))), _slot, v => { _slot = v; Refresh(); });
        _grid.Children.Clear();
        var wornId = worn.GetValueOrDefault(_slot);
        if (_slot != "fur") _grid.Children.Add(Card(null, wornId is null, engine));
        foreach (var p in BoofiCatalog.All.Where(p => p.Slot == _slot && engine.Owns(p.Id)).OrderBy(p => Array.IndexOf(BoofiCatalog.Rarities, p.Rarity)).ThenBy(p => p.Price))
            _grid.Children.Add(Card(p, p.Id == wornId || (_slot == "fur" && wornId is null && p.Id == BoofiCatalog.DefaultFur), engine));
        if (BoofiCatalog.All.Count(p => p.Slot == _slot && engine.Owns(p.Id)) == (_slot == "fur" ? 1 : 0))
            _grid.Children.Add(new TextBlock { Text = L.T("هنوز چیزی از این بخش نخریده‌ای. از تب فروشگاه سر بزن.", "You have nothing from this slot yet. Visit the Shop tab."), Style = BoofiArt.St("Body"), Margin = new Thickness(8, 14, 0, 0) });
    }

    private FrameworkElement Card(BoofiPart? p, bool on, RewardsEngine engine)
    {
        var color = p is null ? Colors.Gray : BoofiArt.Color(BoofiCatalog.RarityColor(p.Rarity));
        var body = new StackPanel { Width = 130 };
        if (p is null)
            body.Children.Add(new Border { Width = 100, Height = 90, HorizontalAlignment = HorizontalAlignment.Center, Child = new TextBlock { Text = "∅", FontSize = 34, Foreground = BoofiArt.Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        else
            body.Children.Add(new Border { Width = 110, Height = 100, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, Child = BoofiArt.Make(new Dictionary<string, string> { [p.Slot] = p.Id }, BoofiCatalog.Focus(p.Slot), 100) });
        body.Children.Add(new TextBlock { Text = p?.Name ?? L.T("بدون", "None"), Style = BoofiArt.St("Label"), FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
        var border = new Border
        {
            Margin = new Thickness(5), Padding = new Thickness(8), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(on ? 2.5 : 1.2), Cursor = System.Windows.Input.Cursors.Hand, Child = body,
            BorderBrush = on ? BoofiArt.Res("Gold") : new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B)),
            Background = new SolidColorBrush(Color.FromArgb(on ? (byte)48 : (byte)22, color.R, color.G, color.B)),
        };
        border.MouseLeftButtonUp += (_, _) => engine.Wear(_slot, p?.Id);
        return border;
    }
}
