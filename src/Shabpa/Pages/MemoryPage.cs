using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«حافظه» (a tab of «کنترل سیستم»): how full the RAM is, two buttons to free it, and the automatic threshold.
/// Glass Depth: usage ring card, automatic card, two action cards, result line, advice.</summary>
public sealed class MemoryPage : PageBase
{
    private readonly RingGauge _ring = new() { Width = 176, Height = 176, ViewSize = 200, RingRadius = 84, Thickness = 12, Ticks = false, Knob = false };
    private readonly TextBlock _used = new() { FontSize = 44, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, LineHeight = 52, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Text = "—", Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _total = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Foreground = Res("Muted") };
    private readonly TextBlock _free = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("Muted") };
    private readonly TextBlock _cache = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("Muted") };
    private readonly TextBlock _cacheHint = new() { Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _trim, _cacheButton;
    private readonly Border _result = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 11, 16, 11), Margin = new Thickness(8, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _resultText = new() { FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _auto = new();
    private readonly Grid _root = ListUi.PageGrid(4);
    private readonly FrameworkElement _usageCard, _autoCard, _trimCard, _cacheCard, _tip;
    private bool? _wide;
    private bool _first = true;

    public override string PageTitle => L.T("حافظه", "Memory");
    public override string PageSubtitle => L.T("ببین رم چقدر پره و هر وقت خواستی خالی‌اش کن.", "See how full the RAM is and free it up when you want.");

    public MemoryPage()
    {
        _trim = ListUi.Btn("ram", L.T("خالی کردن رم", "Free up RAM"), "BtnPrimary", () => Start(false));
        _cacheButton = ListUi.Btn("ram", L.T("خالی کردن کش رم", "Clear RAM cache"), "BtnPrimary", () => Start(true));

        // usage: ring with the percentage, total, free / cache pills
        var ringBox = new Grid { Width = 176, Height = 176, Margin = new Thickness(0, 0, 28, 0), VerticalAlignment = VerticalAlignment.Center };
        ringBox.Children.Add(_ring);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        mid.Children.Add(_used);
        mid.Children.Add(new TextBlock { Text = L.T("رم در حال استفاده", "RAM in use"), Style = St("Label"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
        ringBox.Children.Add(mid);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = L.T("مصرف حافظه", "Memory usage"), Style = St("H2") });
        _total.Margin = new Thickness(0, 6, 0, 12);
        info.Children.Add(_total);
        var pills = new WrapPanel();
        var freePill = new Border { Style = St("Pill"), Margin = new Thickness(0, 0, 8, 6), Child = _free };
        var cachePill = new Border
        {
            Style = St("Pill"), Margin = new Thickness(0, 0, 8, 6), Child = _cache,
            ToolTip = L.T("حافظه‌ای که ویندوز برای باز شدن سریع‌تر برنامه‌ها نگه داشته؛ هر وقت لازم بشه خودش آزادش می‌کنه.", "Memory Windows keeps so apps reopen faster; it releases it by itself when needed."),
        };
        pills.Children.Add(freePill);
        pills.Children.Add(cachePill);
        info.Children.Add(pills);
        var usage = new Grid();
        usage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        usage.ColumnDefinitions.Add(new ColumnDefinition());
        usage.Children.Add(ringBox);
        Grid.SetColumn(info, 1);
        usage.Children.Add(info);
        _usageCard = new GlassCard { Padding = new Thickness(28, 24, 28, 24), Margin = new Thickness(8), Child = usage };

        // automatic
        var (autoCard, auto) = K.Card(L.T("خودکار", "Automatic"));
        auto.Children.Add(new TextBlock { Text = L.T("وقتی رم از این حد پرتر شد، خودش خالی‌اش کنه (حداکثر هر ۱۰ دقیقه یک بار):", "Free up RAM by itself when usage passes this level (at most once every 10 minutes):"), Style = St("Body"), Margin = new Thickness(0, 6, 0, 12) });
        auto.Children.Add(_auto);
        autoCard.Margin = new Thickness(8);
        _autoCard = autoCard;
        Chips(_auto, [(0, L.T("خاموش", "Off")), (80, L.T("۸۰٪", "80%")), (90, L.T("۹۰٪", "90%")), (95, L.T("۹۵٪", "95%"))], Hub.Settings.AutoTrimPercent, v =>
        {
            Hub.Settings.AutoTrimPercent = v;
            Hub.Store.Save();
        });

        // the two actions
        _trimCard = ActionCard("ram", "HueA", L.T("خالی کردن رم", "Free up RAM"),
            new TextBlock { Text = L.T("حافظه‌ی بلااستفاده‌ی برنامه‌های باز رو از رم بیرون می‌کشه. برنامه‌ها بسته نمی‌شن؛ فقط دفعه‌ی اول که سراغشون بری یه لحظه کندترن.", "Pushes the idle memory of running apps out of RAM. Nothing is closed; apps are just a touch slower the first time you return to them."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) },
            _trim);
        _cacheCard = ActionCard("cpu", "HueC", L.T("خالی کردن کش رم", "Clear RAM cache"), _cacheHint, _cacheButton);

        _result.Child = _resultText;
        _tip = new TextBlock
        {
            Style = St("Tip"), Margin = new Thickness(12, 4, 12, 0),
            Text = L.T("ویندوز خودش رم رو مدیریت می‌کنه و رمِ پر به‌خودی‌خود بد نیست. این دکمه‌ها وقتی به درد می‌خورن که قبل از یه بازی یا برنامه‌ی سنگین بخوای جا باز کنی، یا کش بعد از یه کار سنگین خالی نشده باشه.",
                "Windows manages RAM on its own, and full RAM is not bad in itself. These buttons help when you want room before a game or heavy app, or when the cache did not shrink after heavy work."),
        };

        foreach (var e in new[] { _usageCard, _autoCard, _trimCard, _cacheCard, _result, _tip }) _root.Children.Add(e);
        Content = _root;
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 900);
        ApplyLayout(true);
        Hub.MemoryCleaned += OnCleaned;
    }

    private static GlassCard ActionCard(string icon, string hue, string title, UIElement body, Button button)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new IconBadge(icon, hue, 36, 18, 11));
        head.Children.Add(new TextBlock { Text = title, Style = St("H2"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 16, 0, 0);
        button.Padding = new Thickness(20, 11, 20, 11);
        stack.Children.Add(button);
        return new GlassCard { Margin = new Thickness(8), Child = stack };
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        while (_root.RowDefinitions.Count < 6) _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (wide)
        {
            ListUi.Place(_usageCard, 0, 0, 7);
            ListUi.Place(_autoCard, 0, 7, 5);
            ListUi.Place(_trimCard, 1, 0, 6);
            ListUi.Place(_cacheCard, 1, 6, 6);
            ListUi.Place(_result, 2, 0, 12);
            ListUi.Place(_tip, 3, 0, 12);
        }
        else
        {
            ListUi.Place(_usageCard, 0, 0, 12);
            ListUi.Place(_autoCard, 1, 0, 12);
            ListUi.Place(_trimCard, 2, 0, 12);
            ListUi.Place(_cacheCard, 3, 0, 12);
            ListUi.Place(_result, 4, 0, 12);
            ListUi.Place(_tip, 5, 0, 12);
        }
    }

    public override void Refresh()
    {
        _cacheButton.IsEnabled = Hub.CanLimit && !Hub.Cleaning;
        _trim.IsEnabled = !Hub.Cleaning;
        _cacheHint.Text = L.T("حافظه‌ای که ویندوز «محض احتیاط» از فایل‌ها و برنامه‌های قبلی نگه داشته رو آزاد می‌کنه.", "Releases the memory Windows keeps “just in case” from earlier files and apps.")
                          + (Hub.CanLimit ? "" : " " + L.T("به دسترسی ادمین نیاز داره (دکمه‌اش توی «پایداری پینگ» هست).", "Needs admin rights (the button is on the “Ping stability” page)."));
        Show();
    }

    public override void OnTick(SensorSnapshot s) => Show();

    private void Show()
    {
        var memory = Hub.Memory;
        if (memory.Total <= 0) return;
        var percent = memory.UsedPercent;
        var suffix = L.En ? "%" : "٪";
        Mo.Count(_used, percent, v => Fa.Digits($"{v:0}") + suffix, 700);
        _total.Text = $"{Fa.Bytes(memory.Used)} / {Fa.Bytes(memory.Total)}";
        _ring.ArcBrush = percent >= 90 ? Res("Red") : percent >= 75 ? Res("Gold") : null;
        if (_first)
        {
            _first = false;
            _ring.AnimateTo(percent / 100);
        }
        else _ring.AnimateTo(percent / 100, 700);
        _free.Text = ListUi.Ltr(L.T($"آزاد {Fa.Bytes(memory.Available)}", $"Free {Fa.Bytes(memory.Available)}"));
        _cache.Text = ListUi.Ltr(L.T($"کش {Fa.Bytes(memory.Standby)}", $"Cache {Fa.Bytes(memory.Standby)}"));
    }

    private void Result(string text, string tint)
    {
        var wasHidden = _result.Visibility != Visibility.Visible;
        _result.Visibility = Visibility.Visible;
        _result.Background = Res(tint == "Muted" ? "Line" : tint + "Soft");
        _resultText.Foreground = Res(tint);
        text = ListUi.Ltr(text);
        var changed = _resultText.Text != text;
        _resultText.Text = text;
        if (wasHidden || changed) Mo.Flash(_result);
    }

    private void Start(bool cache)
    {
        _trim.IsEnabled = _cacheButton.IsEnabled = false;
        Result(L.T("در حال خالی کردن…", "Working…"), "Muted");
        Hub.CleanMemory(cache);
    }

    private void OnCleaned(MemoryResult result, bool cache)
    {
        if (result.Error is { } error)
        {
            Result(error == "needs-admin"
                ? L.T("خالی کردن کش به دسترسی ادمین نیاز داره.", "Clearing the cache needs admin rights.")
                : L.T($"انجام نشد: {error}", $"Failed: {error}"), "Red");
        }
        else
        {
            Result(cache
                ? L.T($"کش رم خالی شد؛ {Fa.Bytes(result.Freed)} آزاد شد.", $"RAM cache cleared; {Fa.Bytes(result.Freed)} freed.")
                : L.T($"{Fa.Bytes(result.Freed)} رم آزاد شد ({Fa.Digits(result.Processes.ToString())} برنامه سبک شدن).", $"{Fa.Bytes(result.Freed)} of RAM freed ({result.Processes} processes trimmed)."), "Green");
        }
        Refresh();
    }
}
