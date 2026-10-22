using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>City search with suggestions, the weather now, the next hours and the next days, over an animated
/// backdrop that follows the sky (rain, snow, sun, clouds, stars, fog, lightning).</summary>
public sealed class WeatherPage : PageBase
{
    /// <summary>Percent sign: the Arabic one in Persian, the plain one in English.</summary>
    private static string Pct => L.En ? "%" : "٪";

    private readonly WeatherClient _client = new(() => Hub.Settings.ProxyUrl);
    private readonly TextBox _search = new() { Style = St("Input"), Padding = new Thickness(10, 5, 10, 5), FontSize = 13 };
    private readonly StackPanel _suggestions = new();
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly WeatherScene _scene = new() { MinHeight = 190, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _tempText = new() { FontSize = 54, FontWeight = FontWeights.Light, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight };
    private string _drawSig = "";
    private readonly StackPanel _heroText = new() { Margin = new Thickness(26, 16, 26, 16), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _details = new();
    private readonly StackPanel _hours = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _days = new();
    private readonly Border _body = new();
    private FrameworkElement _detailCard = null!, _hourCard = null!, _dayCard = null!, _cityCard = null!;
    private readonly WrapPanel _tiles = new();

    private static readonly Brush Scrim = MakeScrim();

    private static Brush MakeScrim()
    {
        // weather art content: a light black veil so the white text stays readable on bright skies
        var b = new SolidColorBrush(Color.FromArgb(0x3A, 0, 0, 0));
        b.Freeze();
        return b;
    }
    private readonly TextBlock _footer = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 0) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private CancellationTokenSource? _searchCts;
    private WeatherReport? _report;
    private bool _loading;
    private string? _error;
    private DateTime _lastFetch = DateTime.MinValue;
    private DateTime _lastTick = DateTime.MinValue;

    public override string PageTitle => L.T("آب و هوا", "Weather");
    public override string PageSubtitle => L.T("چند شهر رو نگه دار و کنار هم ببین؛ روی هر کدوم بزنی جزئیاتش باز می‌شه.",
        "Keep a few cities and see them side by side; click one to open its details.");

    public WeatherPage()
    {
        var root = new NetBlocks(1000, 7);

        var (searchCard, search) = NetParts.Card("globe", "HueA", L.T("شهر", "City"));
        _search.ToolTip = L.T("اسم شهر به فارسی یا انگلیسی", "City name in Persian or English");
        search.Children.Add(new Border { Margin = new Thickness(0, 12, 0, 0), Child = _search });
        search.Children.Add(_suggestions);
        search.Children.Add(_hint);
        root.Add(searchCard, 0, 0, 12, 0);

        var (cityCard, cityBody) = NetParts.Card("cloud", "HueA", L.T("شهرهای من", "My cities"));
        _tiles.Margin = new Thickness(-4, 10, -4, 0);
        _tiles.FlowDirection = Flow;
        cityBody.Children.Add(_tiles);
        _cityCard = cityCard;
        root.Add(cityCard, 1, 0, 12, 1);

        // the hero is art, not chrome: it keeps its own sky colours, clipped to the card's rounded corners
        var heroBorder = new GlassCard { Padding = new Thickness(0), Margin = new Thickness(0) };
        var heroGrid = new Grid();
        heroGrid.Children.Add(_scene);
        // soft dark scrim under the text (replaces the old text shadow, which was a blur effect)
        heroGrid.Children.Add(new Border { IsHitTestVisible = false, Background = Scrim });
        heroGrid.Children.Add(_heroText);
        heroGrid.SizeChanged += (_, e) => heroGrid.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 17, 17);
        heroBorder.Child = heroGrid;
        _body.Child = heroBorder;
        root.Add(_body, 2, 0, 12, 2);

        var (detailCard, detail) = NetParts.Card("cloud", "HueB", L.T("جزئیات", "Details"));
        detail.Children.Add(_details);
        _detailCard = detailCard;
        root.Add(detailCard, 3, 0, 12, 3);

        var (hourCard, hour) = NetParts.Card("clk", "HueC", L.T("ساعت‌های آینده", "Next hours"));
        var hourScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _hours, Margin = new Thickness(0, 12, 0, 0), FlowDirection = Flow,
        };
        // the wheel scrolls the strip sideways (a horizontal-only ScrollViewer otherwise swallows it); at either end the page takes over
        Kit.SmoothScroll.SetHorizontalWheel(hourScroll, true);
        hour.Children.Add(hourScroll);
        _hourCard = hourCard;
        root.Add(hourCard, 4, 0, 12, 4);

        var (dayCard, day) = NetParts.Card("alarm", "HueD", L.T("روزهای آینده", "Next days"));
        _days.Margin = new Thickness(0, 6, 0, 0);
        day.Children.Add(_days);
        _dayCard = dayCard;
        root.Add(dayCard, 5, 0, 12, 5);
        _footer.Margin = new Thickness(0);
        root.Add(_footer, 6, 0, 12, 6);
        _detailCard.Visibility = _hourCard.Visibility = _dayCard.Visibility = Visibility.Collapsed;

        Content = root;

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await Suggest();
        };
        _search.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            if (_search.Text.Trim().Length < 2)
            {
                _suggestions.Children.Clear();
                return;
            }
            _debounce.Start();
        };
    }

    private string Footer { set => NetFx.Say(_footer, value, false); }
    private string Hint
    {
        set
        {
            NetFx.Say(_hint, value, false);
            _hint.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---- the saved cities: tiles, a per-city cache and a bounded, lazy fetch ----------------------

    private readonly Dictionary<string, (WeatherReport Report, DateTime At)> _cache = [];
    private readonly Dictionary<string, DateTime> _failed = [];
    private readonly HashSet<string> _inflight = [];
    private readonly Dictionary<string, CityTile> _tileOf = [];
    private readonly SemaphoreSlim _gate = new(2);
    private string? _viewKey;

    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(3);

    private static Settings Cfg => Hub.Settings;

    /// <summary>The city whose full details are open: the clicked tile, else the default.</summary>
    private WeatherCity? Selected =>
        (_viewKey is null ? null : Cfg.WeatherFavorites.FirstOrDefault(c => WeatherCities.Key(c) == _viewKey)) ?? Cfg.WeatherCity;

    private string SelectedKey => Selected is { } c ? WeatherCities.Key(c) : "";

    private bool Due(string key) =>
        !_inflight.Contains(key)
        && (!_cache.TryGetValue(key, out var hit) || DateTime.Now - hit.At > FreshFor)
        && (!_failed.TryGetValue(key, out var failed) || DateTime.Now - failed > RetryAfter);

    public override void Refresh()
    {
        WeatherCities.Sanitize(Cfg);
        ReconcileTiles();
        if (Selected is null)
        {
            Hint = L.T("هنوز شهری انتخاب نشده؛ اسمش رو بالا بنویس.", "No city picked yet; type its name above.");
            _body.Visibility = _cityCard.Visibility = Visibility.Collapsed;
            _detailCard.Visibility = _hourCard.Visibility = _dayCard.Visibility = Visibility.Collapsed;
            Footer = "";
            return;
        }
        _body.Visibility = _cityCard.Visibility = Visibility.Visible;
        ShowSelected();
        FetchDue();
    }

    public override void OnSecond()
    {
        // Quietly refresh every 15 minutes while the page is on screen; the other cities follow lazily.
        if (!IsVisible || Selected is null) return;
        if (DateTime.Now - _lastTick < TimeSpan.FromSeconds(30)) return;
        _lastTick = DateTime.Now;
        FetchDue();
    }

    /// <summary>Fetches whatever is stale or missing: the open city first, then the rest (two at a time).</summary>
    private void FetchDue()
    {
        if (Selected is { } open && Due(SelectedKey)) _ = Fetch(open);
        foreach (var c in Cfg.WeatherFavorites.ToList())
            if (Due(WeatherCities.Key(c))) _ = Fetch(c);
    }

    private async Task Fetch(WeatherCity city)
    {
        var key = WeatherCities.Key(city);
        if (!_inflight.Add(key)) return;
        if (key == SelectedKey && !_cache.ContainsKey(key)) Footer = L.T("در حال دریافت اطلاعات…", "Loading…");
        try
        {
            await _gate.WaitAsync();
            try
            {
                // the request itself is async I/O: the UI thread is never blocked while it waits
                var report = Hub.Demo ? DemoReport(city) : await _client.ForecastAsync(city);
                _cache[key] = (report, DateTime.Now);
                _failed.Remove(key);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Write($"weather: {ex.Message}");
            _failed[key] = DateTime.Now;
        }
        finally
        {
            _inflight.Remove(key);
        }
        if (_tileOf.TryGetValue(key, out var tile)) tile.Update(_cache.TryGetValue(key, out var hit) ? hit.Report : null, _failed.ContainsKey(key));
        if (key == SelectedKey) ShowSelected();
    }

    /// <summary>Draws the open city from the cache (or says why it cannot).</summary>
    private void ShowSelected()
    {
        var key = SelectedKey;
        _error = _failed.ContainsKey(key)
            ? L.T("دریافت آب و هوا انجام نشد. اینترنت یا پروکسی رو چک کن؛ کمی بعد دوباره امتحان می‌شه.", "Could not load the weather. Check the internet or proxy; it will retry shortly.")
            : null;
        if (_cache.TryGetValue(key, out var hit))
        {
            _report = hit.Report;
            Draw();
            return;
        }
        _report = null;
        _detailCard.Visibility = _hourCard.Visibility = _dayCard.Visibility = Visibility.Collapsed;
        _drawSig = "";
        Footer = _error ?? L.T("در حال دریافت اطلاعات…", "Loading…");
    }

    private void ReconcileTiles()
    {
        var wanted = Cfg.WeatherFavorites.ToList();
        var keys = wanted.Select(WeatherCities.Key).ToList();
        foreach (var gone in _tileOf.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            _tiles.Children.Remove(_tileOf[gone].Root);
            _tileOf.Remove(gone);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var key = keys[i];
            if (!_tileOf.TryGetValue(key, out var tile))
            {
                tile = new CityTile(this, wanted[i]);
                _tileOf[key] = tile;
                _tiles.Children.Add(tile.Root);
                if (_cache.TryGetValue(key, out var hit)) tile.Update(hit.Report, false);
            }
            var at = _tiles.Children.IndexOf(tile.Root);
            if (at != i)
            {
                _tiles.Children.RemoveAt(at);
                _tiles.Children.Insert(i, tile.Root);
            }
            tile.SetState(key == SelectedKey, WeatherCities.Same(wanted[i], Cfg.WeatherCity), animate: false);
        }
    }

    private void Choose(WeatherCity city)
    {
        _viewKey = WeatherCities.Key(city);
        foreach (var (key, tile) in _tileOf)
            tile.SetState(key == _viewKey, Cfg.WeatherCity is { } d && WeatherCities.Key(d) == key, animate: true);
        ShowSelected();
        if (Due(_viewKey)) _ = Fetch(city);
    }

    private void MakeDefault(WeatherCity city)
    {
        if (!WeatherCities.SetDefault(Cfg, city)) return;
        Hub.Store.Save();
        ReconcileTiles();
    }

    private void RemoveCity(WeatherCity city)
    {
        var key = WeatherCities.Key(city);
        if (!WeatherCities.Remove(Cfg, city)) return;
        Hub.Store.Save();
        _cache.Remove(key);
        _failed.Remove(key);
        if (_viewKey == key) _viewKey = null;
        Refresh();
    }

    private void AddCity(WeatherCity pick)
    {
        WeatherCities.Sanitize(Cfg);
        var known = Cfg.WeatherFavorites.Any(c => WeatherCities.Same(c, pick));
        if (!known && !WeatherCities.Add(Cfg, pick))
        {
            Hint = L.T($"حداکثر {Fa.Digits(WeatherCities.MaxCities.ToString())} شهر؛ اول یکی از شهرها رو حذف کن.", $"At most {WeatherCities.MaxCities} cities; remove one first.");
            return;
        }
        Hub.Store.Save();
        _suggestions.Children.Clear();
        _search.Text = "";
        Hint = "";
        _viewKey = WeatherCities.Key(pick);
        Refresh();
    }

    private async Task Suggest()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var text = _search.Text.Trim();
        var persian = text.Any(c => c is >= '؀' and <= 'ۿ');
        Hint = L.T("در حال جستجو…", "Searching…");
        try
        {
            var found = Hub.Demo ? DemoCities(text) : await _client.SearchAsync(text, persian || !L.En, ct);
            if (ct.IsCancellationRequested) return;
            _suggestions.Children.Clear();
            Hint = found.Count == 0 ? L.T("شهری با این اسم پیدا نشد.", "No place found with that name.") : "";
            foreach (var city in found)
            {
                var pick = city;
                var button = new Button
                {
                    Style = St("Btn"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0),
                    Padding = new Thickness(14, 9, 14, 9),
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = pick.Name, FontWeight = FontWeights.SemiBold, Style = St("Body") },
                            new TextBlock { Text = pick.Subtitle, Style = St("Label") },
                        },
                    },
                };
                button.Click += (_, _) => AddCity(pick);
                _suggestions.Children.Add(button);
            }
            NetFx.RiseChildren(_suggestions, 6, 8, 0.4);
        }
        catch (OperationCanceledException)
        {
            // a newer search replaced this one
        }
        catch (Exception ex)
        {
            Log.Write($"weather search: {ex.Message}");
            Hint = L.T("به سرویس هواشناسی وصل نشد. اینترنت یا پروکسی رو چک کن.", "Could not reach the weather service. Check the internet or proxy.");
        }
    }

    /// <summary>One saved city: compact (name, icon, temperature, high/low) until clicked, then wider with the condition and its actions.</summary>
    private sealed class CityTile
    {
        private const double Compact = 150, Open = 250;
        private readonly WeatherPage _page;
        private readonly WeatherCity _city;
        private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Style = St("Body") };
        private readonly TextBlock _star = new() { Text = "★", Foreground = Res("Accent"), FontSize = 13, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
        private readonly TextBlock _temp = new() { FontSize = 24, FontWeight = FontWeights.Light, Foreground = Res("Text"), FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
        private readonly ContentControl _icon = new() { IsTabStop = false };
        private readonly TextBlock _range = new() { Style = St("Label"), FontSize = 11, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _cond = new() { Style = St("Label"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, ClipToBounds = true, MaxHeight = 0, Opacity = 0, Margin = new Thickness(0, 0, 0, 0) };
        private readonly Button _default;
        private readonly Border _frame;
        private bool _selected;

        public Border Root => _frame;

        public CityTile(WeatherPage page, WeatherCity city)
        {
            _page = page;
            _city = city;
            _name.Text = city.Name;
            _name.ToolTip = city.Title;
            _frame = new Border
            {
                Width = Compact, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(14),
                Background = Res("Line"), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
                FlowDirection = Flow,
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(_star);
            head.Children.Add(_name);
            var main = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
            _icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.Margin = new Thickness(0, 0, 8, 0);
            main.Children.Add(_icon);
            main.Children.Add(_temp);
            _default = new Button { Style = St("Btn"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), Content = L.T("پیش‌فرض", "Make default") };
            _default.Click += (_, _) => _page.MakeDefault(_city);
            var remove = new Button { Style = St("Btn"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, Content = L.T("حذف", "Remove") };
            remove.Click += (_, _) => _page.RemoveCity(_city);
            _actions.Children.Add(_default);
            _actions.Children.Add(remove);
            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(main);
            stack.Children.Add(_range);
            stack.Children.Add(_cond);
            var actionsHost = new Border { Child = _actions, Margin = new Thickness(0, 8, 0, 0) };
            _cond.Visibility = Visibility.Collapsed;
            actionsHost.Visibility = Visibility.Collapsed;
            _host = actionsHost;
            stack.Children.Add(actionsHost);
            _frame.Child = stack;
            _frame.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled) return;
                _page.Choose(_city);
            };
            Update(null, false);
        }

        private readonly Border _host;

        public void Update(WeatherReport? r, bool failed)
        {
            if (r is null)
            {
                _icon.Content = ColorEmoji.Icon(failed ? "📡" : "⏳", 26);
                _temp.Text = "—";
                _range.Text = failed ? L.T("آفلاین", "Offline") : L.T("در حال دریافت…", "Loading…");
                _cond.Text = "";
                return;
            }
            var now = r.Now;
            _icon.Content = ColorEmoji.Icon(WeatherCodes.Emoji(now.Code, now.IsDay), 26);
            _temp.Text = Temp(now.Temp);
            var today = r.Days.FirstOrDefault();
            _range.Text = today is null ? "" : $"↑{Temp(today.Max)}  ↓{Temp(today.Min)}" + (failed ? "  ·  " + L.T("قدیمی", "stale") : "");
            _cond.Text = WeatherCodes.Text(now.Code);
        }

        public void SetState(bool selected, bool isDefault, bool animate)
        {
            _star.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
            _default.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
            _frame.BorderBrush = selected ? Res("Accent") : Brushes.Transparent;
            _frame.Background = selected ? Res("AccentSoft") : Res("Line");
            if (selected == _selected && animate) return;
            _selected = selected;
            var width = selected ? Open : Compact;
            var height = selected ? 34.0 : 0.0;
            var tween = animate && Motion.Enabled;
            if (selected)
            {
                _cond.Visibility = _host.Visibility = Visibility.Visible;
            }
            if (!tween)
            {
                _frame.BeginAnimation(WidthProperty, null);
                _frame.Width = width;
                _actions.BeginAnimation(MaxHeightProperty, null);
                _actions.BeginAnimation(OpacityProperty, null);
                _actions.MaxHeight = height;
                _actions.Opacity = selected ? 1 : 0;
                if (!selected) _cond.Visibility = _host.Visibility = Visibility.Collapsed;
                return;
            }
            var span = TimeSpan.FromMilliseconds(selected ? 320 : 240);
            _frame.BeginAnimation(WidthProperty, new DoubleAnimation(width, span) { EasingFunction = Fx.Out });
            _actions.BeginAnimation(MaxHeightProperty, new DoubleAnimation(height, span) { EasingFunction = Fx.Out });
            var fade = new DoubleAnimation(selected ? 1 : 0, span);
            if (!selected)
                fade.Completed += (_, _) =>
                {
                    if (!_selected) _cond.Visibility = _host.Visibility = Visibility.Collapsed;
                };
            _actions.BeginAnimation(OpacityProperty, fade);
        }
    }

    private static string Temp(double t) => Fa.Digits(Math.Round(t).ToString("0", System.Globalization.CultureInfo.InvariantCulture)) + "°";

    private static string Num(double v, string format = "0") => Fa.Digits(v.ToString(format, System.Globalization.CultureInfo.InvariantCulture));

    private void Draw()
    {
        if (_report is null)
        {
            Footer = _error ?? "";
            return;
        }
        var r = _report;
        var sig = r.City.Title + "|" + r.Fetched.Ticks;
        var fresh = sig != _drawSig;
        _drawSig = sig;
        var cards = new[] { _detailCard, _hourCard, _dayCard };
        var wasHidden = cards.Select(c => c.Visibility != Visibility.Visible).ToArray();
        foreach (var c in cards) c.Visibility = Visibility.Visible;
        var now = r.Now;
        _scene.Set(WeatherCodes.SkyOf(now.Code), now.IsDay);

        var white = Brushes.White;
        TextBlock T(string text, double size, FontWeight? weight = null, double opacity = 1) => new()
        {
            Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = white, Opacity = opacity,
            TextWrapping = TextWrapping.Wrap,
        };
        _heroText.Children.Clear();
        _heroText.Children.Add(T(r.City.Title, 22, FontWeights.SemiBold));
        _tempText.TextAlignment = L.En ? TextAlignment.Left : TextAlignment.Right;
        NetFx.Count(_tempText, Math.Round(now.Temp), Temp, 700);
        _heroText.Children.Add(_tempText);
        var condition = T("", 22);
        ColorEmoji.Fill(condition, $"{WeatherCodes.Emoji(now.Code, now.IsDay)}  {WeatherCodes.Text(now.Code)}");
        _heroText.Children.Add(condition);
        var today = r.Days.FirstOrDefault();
        string Lt(double t) => "\u200E" + Temp(t) + "\u200E";
        var extra = L.T($"احساس واقعی {Lt(now.Feels)}", $"Feels like {Lt(now.Feels)}");
        if (today is not null) extra += L.T($"   ·   بیشینه {Lt(today.Max)}   کمینه {Lt(today.Min)}", $"   ·   High {Lt(today.Max)}   Low {Lt(today.Min)}");
        _heroText.Children.Add(T(extra, 14, opacity: 0.92));
        _heroText.FlowDirection = Flow;

        // details
        _details.Children.Clear();
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 10, -4, 0), FlowDirection = Flow };
        grid.SizeChanged += (_, e) => grid.Columns = e.NewSize.Width > 720 ? 4 : 2;
        void Tile(string icon, string label, string value)
        {
            var box = new Border { Style = St("Card"), Margin = new Thickness(4), Padding = new Thickness(14, 10, 14, 10) };
            Fx.Attach(box, 2, 0, 1, 1);
            var stack = new StackPanel();
            var caption = new TextBlock { Style = St("Label") };
            ColorEmoji.Fill(caption, $"{icon} {label}");
            stack.Children.Add(caption);
            // Values mix digits with Latin units (km/h, hPa): lay them out left-to-right so the order stays readable.
            stack.Children.Add(new TextBlock
            {
                Text = value, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            box.Child = stack;
            grid.Children.Add(box);
        }
        Tile("🌡️", L.T("احساس واقعی", "Feels like"), Temp(now.Feels));
        Tile("💧", L.T("رطوبت", "Humidity"), Num(now.Humidity) + Pct);
        Tile("💨", L.T($"باد ({WeatherCodes.Compass(now.WindDir)})", $"Wind ({WeatherCodes.Compass(now.WindDir)})"), $"{Num(now.WindKmh)} km/h");
        Tile("🧭", L.T("فشار", "Pressure"), $"{Num(now.PressureHpa)} hPa");
        Tile("☀️", L.T("پرتو فرابنفش", "UV index"), $"{Num(now.Uv, "0.#")} · {UvLevel(now.Uv)}");
        Tile("☁️", L.T("پوشش ابر", "Cloud cover"), Num(now.CloudPercent) + Pct);
        if (today?.Sunrise is { } sr) Tile("🌅", L.T("طلوع", "Sunrise"), Fa.Clock(sr));
        if (today?.Sunset is { } ss) Tile("🌇", L.T("غروب", "Sunset"), Fa.Clock(ss));
        _details.Children.Add(grid);
        if (fresh) NetFx.RiseChildren(grid, 8, 12, 0.55);

        // hours
        _hours.Children.Clear();
        foreach (var h in r.Hours)
        {
            var item = new Border
            {
                Width = 74, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(6, 10, 6, 10), CornerRadius = new CornerRadius(12),
                Background = Res("Line"),
            };
            Fx.Attach(item, 3, 0, 1, 1);
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = Fa.Clock(h.Time), Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center });
            var hourIcon = ColorEmoji.Icon(WeatherCodes.Emoji(h.Code, h.IsDay), 22);
            ((FrameworkElement)hourIcon).Margin = new Thickness(0, 6, 0, 6);
            ((FrameworkElement)hourIcon).HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(hourIcon);
            stack.Children.Add(new TextBlock { Text = Temp(h.Temp), FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight });
            var chance = new TextBlock { FontSize = 11, Foreground = Res("Muted"), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            ColorEmoji.Fill(chance, h.RainChance > 0 ? $"💧{Num(h.RainChance)}{Pct}" : " ");
            stack.Children.Add(chance);
            item.Child = stack;
            _hours.Children.Add(item);
        }
        if (fresh) NetFx.RiseChildren(_hours, 14, 10, 0.5);

        // days
        _days.Children.Clear();
        var lo = r.Days.Count > 0 ? r.Days.Min(d => d.Min) : 0;
        var hi = r.Days.Count > 0 ? r.Days.Max(d => d.Max) : 1;
        var span = Math.Max(1, hi - lo);
        var index = 0;
        foreach (var d in r.Days)
        {
            var row = new Grid { Margin = new Thickness(0, 7, 0, 7), FlowDirection = Flow };
            foreach (var w in new[] { 120.0, 44.0, 150.0, 70.0, 46.0, 1.0, 46.0 })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 1.0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            void Put(UIElement e, int col)
            {
                Grid.SetColumn(e, col);
                row.Children.Add(e);
            }
            var title = index == 0 ? L.T("امروز", "Today") : Fa.DayName(d.Date.DayOfWeek);
            var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameBox.Children.Add(new TextBlock { Text = title, Style = St("Body"), FontWeight = FontWeights.SemiBold });
            nameBox.Children.Add(new TextBlock { Text = Fa.Date(d.Date), Style = St("Label"), FontSize = 11 });
            Put(nameBox, 0);
            var dayIcon = (FrameworkElement)ColorEmoji.Icon(WeatherCodes.Emoji(d.Code), 22);
            dayIcon.VerticalAlignment = VerticalAlignment.Center;
            Put(dayIcon, 1);
            Put(new TextBlock { Text = WeatherCodes.Text(d.Code), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, 2);
            var rainBox = new TextBlock { Style = St("Label"), VerticalAlignment = VerticalAlignment.Center };
            ColorEmoji.Fill(rainBox, d.RainChance > 0 ? $"💧 {Num(d.RainChance)}{Pct}" : "");
            Put(rainBox, 3);
            Put(new TextBlock { Text = Temp(d.Min), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center }, 4);

            // a bar from the day's low to its high, placed inside the week's overall range
            var track = new Grid { Height = 8, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            track.Children.Add(new Border { Background = Res("Line"), CornerRadius = new CornerRadius(4) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, (d.Min - lo) / span), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, (d.Max - d.Min) / span), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, (hi - d.Max) / span), GridUnitType.Star) });
            var fill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new LinearGradientBrush(K.ColorOf("HueA"), K.ColorOf("HueC"), 0),
            };
            if (fresh && Motion.Enabled)
            {
                // the range bar grows out from its start
                var grow = new ScaleTransform(1, 1);
                fill.RenderTransformOrigin = new Point(0, 0.5);
                fill.RenderTransform = grow;
                grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = Fx.Out, FillBehavior = FillBehavior.Stop });
            }
            Grid.SetColumn(fill, 1);
            track.Children.Add(fill);
            Put(track, 5);
            Put(new TextBlock { Text = Temp(d.Max), Style = St("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center }, 6);
            _days.Children.Add(row);
            if (++index < r.Days.Count) _days.Children.Add(new Border { Height = 1, Background = Res("Line"), Opacity = 0.5 });
        }
        if (fresh) NetFx.RiseChildren(_days, 16, 10, 0.5);
        if (fresh) Motion.Rise(_heroText, 0, 14, 0.7);
        // cards that were hidden until the first report arrived come in like the page's own blocks
        for (var i = 0; i < cards.Length; i++)
            if (wasHidden[i]) Motion.Rise(cards[i], i + 1);

        Footer = (_error is null ? "" : _error + "\n")
            + L.T($"منبع: {r.Source} (سرویس متن‌باز و رایگان با اتصال امن HTTPS؛ داده‌ی مدل‌های سازمان‌های هواشناسی ملی) · آخرین به‌روزرسانی {Fa.Clock(r.Fetched)}",
                $"Source: {r.Source} (free open-source service over HTTPS, using national weather agencies' models) · updated {Fa.Clock(r.Fetched)}");
    }

    private static string UvLevel(double uv) => uv switch
    {
        < 3 => L.T("کم", "Low"),
        < 6 => L.T("متوسط", "Moderate"),
        < 8 => L.T("زیاد", "High"),
        < 11 => L.T("خیلی زیاد", "Very high"),
        _ => L.T("شدید", "Extreme"),
    };

    // ---- demo data (screenshots / --demo): no network --------------------------------------------

    private static List<WeatherCity> DemoCities(string text) =>
    [
        new() { Name = text, Region = "تهران", Country = "ایران", Latitude = 35.69, Longitude = 51.42 },
    ];

    /// <summary>Test aid (--weather-code N [--weather-night]): which sky the demo report shows.</summary>
    public static int? DemoCode { get; set; }
    public static bool DemoNight { get; set; }

    private static WeatherReport DemoReport(WeatherCity city)
    {
        var now = DateTime.Now;
        var shift = Math.Abs((int)(city.Latitude * 10)) % 9 - 4; // demo: each city a little different
        var hours = Enumerable.Range(0, 24).Select(i => new WeatherHour(now.Date.AddHours(now.Hour + i), 22 - Math.Abs(12 - (now.Hour + i) % 24) * 0.8, i % 7 == 3 ? 61 : 2, i % 7 == 3 ? 60 : 5, (now.Hour + i) % 24 is >= 6 and < 19)).ToList();
        int[] codes = [0, 2, 3, 61, 63, 71, 95, 45];
        var days = Enumerable.Range(0, 8).Select(i => new WeatherDay(now.Date.AddDays(i), 18 + (i * 3) % 9, 6 + (i * 2) % 7, codes[i % codes.Length], i % 3 == 0 ? 5 : 55, i % 3 == 0 ? 0 : 3.4, 18, 6,
            now.Date.AddDays(i).AddHours(6.5), now.Date.AddDays(i).AddHours(17.8))).ToList();
        return new WeatherReport(city, now, new WeatherNow(21 + shift, 19.5 + shift, 42, 14, 290, 1015, 35, 0, !DemoNight, DemoCode ?? 2, 6), hours, days, WeatherClient.SourceName);
    }
}
