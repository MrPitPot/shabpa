using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>City search with suggestions, the weather now, the next hours and the next days, over an animated
/// backdrop that follows the sky (rain, snow, sun, clouds, stars, fog, lightning).</summary>
public sealed class WeatherPage : PageBase
{
    private readonly WeatherClient _client = new(() => Hub.Settings.ProxyUrl);
    private readonly TextBox _search = new() { Style = St("Input"), Padding = new Thickness(12, 9, 12, 9), FontSize = 14 };
    private readonly StackPanel _suggestions = new();
    private readonly TextBlock _hint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly WeatherScene _scene = new() { Height = 270 };
    private readonly StackPanel _heroText = new() { Margin = new Thickness(26, 20, 26, 20), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _details = new();
    private readonly StackPanel _hours = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _days = new();
    private readonly Border _body = new();
    private readonly TextBlock _footer = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 0) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private CancellationTokenSource? _searchCts;
    private WeatherReport? _report;
    private bool _loading;
    private string? _error;
    private DateTime _lastFetch = DateTime.MinValue;
    private DateTime _lastTick = DateTime.MinValue;

    public override string PageTitle => L.T("آب و هوا", "Weather");
    public override string PageSubtitle => L.T("اسم شهر رو بنویس و از پیشنهادها انتخاب کن؛ هوای الان و روزهای آینده رو ببین.",
        "Type a city and pick it from the suggestions; see the weather now and the days ahead.");

    public WeatherPage()
    {
        var root = new StackPanel();

        var (searchCard, search) = Ui2.Card(L.T("شهر", "City"));
        _search.ToolTip = L.T("اسم شهر به فارسی یا انگلیسی", "City name in Persian or English");
        search.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = _search });
        search.Children.Add(_suggestions);
        search.Children.Add(_hint);
        root.Children.Add(searchCard);

        var heroBorder = new Border { CornerRadius = new CornerRadius(18), ClipToBounds = true, Margin = new Thickness(0, 12, 0, 0), BorderBrush = Res("Line"), BorderThickness = new Thickness(1) };
        var heroGrid = new Grid();
        heroGrid.Children.Add(_scene);
        heroGrid.Children.Add(_heroText);
        heroBorder.Child = heroGrid;
        _body.Child = heroBorder;
        root.Children.Add(_body);

        var (detailCard, detail) = Ui2.Card(L.T("جزئیات", "Details"));
        detail.Children.Add(_details);
        root.Children.Add(detailCard);

        var (hourCard, hour) = Ui2.Card(L.T("ساعت‌های آینده", "Next hours"));
        hour.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _hours, Margin = new Thickness(0, 10, 0, 0), FlowDirection = Flow,
        });
        root.Children.Add(hourCard);

        var (dayCard, day) = Ui2.Card(L.T("روزهای آینده", "Next days"));
        day.Children.Add(_days);
        root.Children.Add(dayCard);
        root.Children.Add(_footer);

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

    private WeatherCity? Selected => Hub.Settings.WeatherCity;

    public override void Refresh()
    {
        if (Selected is null)
        {
            _hint.Text = L.T("هنوز شهری انتخاب نشده؛ اسمش رو بالا بنویس.", "No city picked yet; type its name above.");
            _body.Visibility = Visibility.Collapsed;
            return;
        }
        _body.Visibility = Visibility.Visible;
        if (_report is null || !_report.City.Title.Equals(Selected.Title) || DateTime.Now - _lastFetch > TimeSpan.FromMinutes(15))
            _ = Load();
        else
            Draw();
    }

    public override void OnSecond()
    {
        // Quietly refresh every 15 minutes while the page is on screen.
        if (!IsVisible || Selected is null || _loading) return;
        if (DateTime.Now - _lastFetch > TimeSpan.FromMinutes(15) && DateTime.Now - _lastTick > TimeSpan.FromSeconds(30))
        {
            _lastTick = DateTime.Now;
            _ = Load();
        }
    }

    private async Task Suggest()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var text = _search.Text.Trim();
        var persian = text.Any(c => c is >= '؀' and <= 'ۿ');
        _hint.Text = L.T("در حال جستجو…", "Searching…");
        try
        {
            var found = Hub.Demo ? DemoCities(text) : await _client.SearchAsync(text, persian || !L.En, ct);
            if (ct.IsCancellationRequested) return;
            _suggestions.Children.Clear();
            _hint.Text = found.Count == 0 ? L.T("شهری با این اسم پیدا نشد.", "No place found with that name.") : "";
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
                button.Click += async (_, _) =>
                {
                    Hub.Settings.WeatherCity = pick;
                    Hub.Store.Save();
                    _suggestions.Children.Clear();
                    _search.Text = "";
                    _hint.Text = "";
                    _body.Visibility = Visibility.Visible;
                    _report = null;
                    await Load();
                };
                _suggestions.Children.Add(button);
            }
        }
        catch (OperationCanceledException)
        {
            // a newer search replaced this one
        }
        catch (Exception ex)
        {
            Log.Write($"weather search: {ex.Message}");
            _hint.Text = L.T("به سرویس هواشناسی وصل نشد. اینترنت یا پروکسی رو چک کن.", "Could not reach the weather service. Check the internet or proxy.");
        }
    }

    private async Task Load()
    {
        if (_loading || Selected is not { } city) return;
        _loading = true;
        _error = null;
        _footer.Text = L.T("در حال دریافت اطلاعات…", "Loading…");
        try
        {
            _report = Hub.Demo ? DemoReport(city) : await _client.ForecastAsync(city);
            _lastFetch = DateTime.Now;
        }
        catch (Exception ex)
        {
            Log.Write($"weather: {ex.Message}");
            _error = L.T("دریافت آب و هوا انجام نشد. اینترنت یا پروکسی رو چک کن؛ کمی بعد دوباره امتحان می‌شه.", "Could not load the weather. Check the internet or proxy; it will retry shortly.");
            _lastFetch = DateTime.Now - TimeSpan.FromMinutes(12); // retry in about 3 minutes
        }
        finally
        {
            _loading = false;
        }
        Draw();
    }

    private static string Temp(double t) => Fa.Digits(Math.Round(t).ToString("0", System.Globalization.CultureInfo.InvariantCulture)) + "°";

    private static string Num(double v, string format = "0") => Fa.Digits(v.ToString(format, System.Globalization.CultureInfo.InvariantCulture));

    private void Draw()
    {
        if (_report is null)
        {
            _footer.Text = _error ?? "";
            return;
        }
        var r = _report;
        var now = r.Now;
        _scene.Set(WeatherCodes.SkyOf(now.Code), now.IsDay);

        var white = Brushes.White;
        TextBlock T(string text, double size, FontWeight? weight = null, double opacity = 1) => new()
        {
            Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = white, Opacity = opacity,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black },
            TextWrapping = TextWrapping.Wrap,
        };
        _heroText.Children.Clear();
        _heroText.Children.Add(T(r.City.Title, 22, FontWeights.SemiBold));
        var temp = T(Temp(now.Temp), 66, FontWeights.Light);
        temp.FlowDirection = FlowDirection.LeftToRight;
        temp.TextAlignment = L.En ? TextAlignment.Left : TextAlignment.Right;
        _heroText.Children.Add(temp);
        _heroText.Children.Add(T($"{WeatherCodes.Emoji(now.Code, now.IsDay)}  {WeatherCodes.Text(now.Code)}", 20));
        var today = r.Days.FirstOrDefault();
        var extra = L.T($"احساس واقعی {Temp(now.Feels)}", $"Feels like {Temp(now.Feels)}");
        if (today is not null) extra += L.T($"   ·   بیشینه {Temp(today.Max)}   کمینه {Temp(today.Min)}", $"   ·   High {Temp(today.Max)}   Low {Temp(today.Min)}");
        _heroText.Children.Add(T(extra, 14, opacity: 0.92));
        _heroText.FlowDirection = Flow;

        // details
        _details.Children.Clear();
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 10, -4, 0), FlowDirection = Flow };
        void Tile(string icon, string label, string value)
        {
            var box = new Border { Style = St("Card"), Margin = new Thickness(4), Padding = new Thickness(14, 10, 14, 10) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = $"{icon} {label}", Style = St("Label") });
            // Values mix digits with Latin units (km/h, hPa): lay them out left-to-right so the order stays readable.
            stack.Children.Add(new TextBlock
            {
                Text = value, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = L.En ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            });
            box.Child = stack;
            grid.Children.Add(box);
        }
        Tile("🌡️", L.T("احساس واقعی", "Feels like"), Temp(now.Feels));
        Tile("💧", L.T("رطوبت", "Humidity"), Num(now.Humidity) + "٪");
        Tile("💨", L.T($"باد ({WeatherCodes.Compass(now.WindDir)})", $"Wind ({WeatherCodes.Compass(now.WindDir)})"), $"{Num(now.WindKmh)} km/h");
        Tile("🧭", L.T("فشار", "Pressure"), $"{Num(now.PressureHpa)} hPa");
        Tile("☀️", L.T("پرتو فرابنفش", "UV index"), $"{Num(now.Uv, "0.#")} · {UvLevel(now.Uv)}");
        Tile("☁️", L.T("پوشش ابر", "Cloud cover"), Num(now.CloudPercent) + "٪");
        if (today?.Sunrise is { } sr) Tile("🌅", L.T("طلوع", "Sunrise"), Fa.Clock(sr));
        if (today?.Sunset is { } ss) Tile("🌇", L.T("غروب", "Sunset"), Fa.Clock(ss));
        _details.Children.Add(grid);

        // hours
        _hours.Children.Clear();
        foreach (var h in r.Hours)
        {
            var item = new Border
            {
                Width = 74, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(6, 10, 6, 10), CornerRadius = new CornerRadius(12),
                Background = Res("Panel2"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1),
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = Fa.Clock(h.Time), Style = St("Label"), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = WeatherCodes.Emoji(h.Code, h.IsDay), FontSize = 22, Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = Temp(h.Temp), FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock
            {
                Text = h.RainChance > 0 ? $"💧{Num(h.RainChance)}٪" : " ", FontSize = 11, Foreground = Res("Muted"), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            });
            item.Child = stack;
            _hours.Children.Add(item);
        }

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
            Put(new TextBlock { Text = WeatherCodes.Emoji(d.Code), FontSize = 22, VerticalAlignment = VerticalAlignment.Center }, 1);
            Put(new TextBlock { Text = WeatherCodes.Text(d.Code), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, 2);
            Put(new TextBlock
            {
                Text = d.RainChance > 0 ? $"💧 {Num(d.RainChance)}٪" : "", Style = St("Label"), VerticalAlignment = VerticalAlignment.Center,
            }, 3);
            Put(new TextBlock { Text = Temp(d.Min), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center }, 4);

            // a bar from the day's low to its high, placed inside the week's overall range
            var track = new Grid { Height = 8, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            track.Children.Add(new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(4) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, (d.Min - lo) / span), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, (d.Max - d.Min) / span), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, (hi - d.Max) / span), GridUnitType.Star) });
            var fill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new LinearGradientBrush(Color.FromRgb(0x5B, 0xC0, 0xFF), Color.FromRgb(0xFF, 0xB2, 0x4D), 0),
            };
            Grid.SetColumn(fill, 1);
            track.Children.Add(fill);
            Put(track, 5);
            Put(new TextBlock { Text = Temp(d.Max), Style = St("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center }, 6);
            _days.Children.Add(row);
            if (++index < r.Days.Count) _days.Children.Add(new Border { Height = 1, Background = Res("Line"), Opacity = 0.5 });
        }

        _footer.Text = (_error is null ? "" : _error + "\n")
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

    private static WeatherReport DemoReport(WeatherCity city)
    {
        var now = DateTime.Now;
        var hours = Enumerable.Range(0, 24).Select(i => new WeatherHour(now.Date.AddHours(now.Hour + i), 22 - Math.Abs(12 - (now.Hour + i) % 24) * 0.8, i % 7 == 3 ? 61 : 2, i % 7 == 3 ? 60 : 5, (now.Hour + i) % 24 is >= 6 and < 19)).ToList();
        int[] codes = [0, 2, 3, 61, 63, 71, 95, 45];
        var days = Enumerable.Range(0, 8).Select(i => new WeatherDay(now.Date.AddDays(i), 18 + (i * 3) % 9, 6 + (i * 2) % 7, codes[i % codes.Length], i % 3 == 0 ? 5 : 55, i % 3 == 0 ? 0 : 3.4, 18, 6,
            now.Date.AddDays(i).AddHours(6.5), now.Date.AddDays(i).AddHours(17.8))).ToList();
        return new WeatherReport(city, now, new WeatherNow(21, 19.5, 42, 14, 290, 1015, 35, 0, true, 2, 6), hours, days, WeatherClient.SourceName);
    }
}
