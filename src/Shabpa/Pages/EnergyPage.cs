using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«مصرف برق» in the Glass Depth design: three stat cards (live power with a sparkline, 30-day total with its
/// measured share, cost), the tariff, the 30-day bar chart and the split by mode. Numbers and formulas are unchanged.</summary>
public sealed class EnergyPage : PageBase
{
    private DateTime _lastChart;
    private bool _drawnOnce;
    private int _ticks;
    private bool _liveShown, _modesDrawn;
    private double _totalLast, _costLast;
    private readonly List<double> _watts = [];

    // stat cards
    private readonly TextBlock _liveValue = new(), _liveUnit = new(), _liveSub = new();
    private readonly TextBlock _totalValue = new(), _totalUnit = new(), _coverage = new();
    private readonly TextBlock _costValue = new(), _costUnit = new(), _costSub = new();
    private readonly Sparkline _spark = new() { Height = 34, Margin = new Thickness(0, 8, 0, 0), Line = Res("HueB"), Opacity = 0.8 };
    private readonly ProgressBar _meter = new() { Style = St("Bar"), Maximum = 100, Margin = new Thickness(0, 12, 0, 4) };
    private readonly Grid _stats = new() { Margin = new Thickness(-8, -8, -8, 8) };
    private readonly List<FrameworkElement> _statCards = [];
    private bool? _wide;

    // tariff
    private readonly TextBox _tariffBox = new() { Style = St("Input"), Width = 110, TextAlignment = TextAlignment.Center, FlowDirection = FlowDirection.LeftToRight, FontWeight = FontWeights.SemiBold };
    private readonly Grid _tariffGrid = new();
    private readonly StackPanel _tariffControls = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private bool? _tariffWide;

    // chart + modes
    private readonly TextBlock _chartHint = new() { Style = St("Label"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _chart = new() { Height = 150, FlowDirection = FlowDirection.LeftToRight };
    private readonly Grid _chartLabels = new() { Height = 20, Margin = new Thickness(0, 8, 0, 0), FlowDirection = FlowDirection.LeftToRight };
    private readonly StackPanel _modes = new();
    private readonly TextBlock _note = new() { Style = St("Tip"), Margin = new Thickness(4, 0, 4, 8) };

    public override string PageTitle => L.T("مصرف برق", "Energy use");
    public override string PageSubtitle => L.T("سیستمت چقدر برق می‌کشه و آخر ماه چقدر روی قبض میاد.", "How much power the PC draws and what it adds to the bill.");

    public EnergyPage()
    {
        // ---- three stat cards ------------------------------------------------------------------------------------
        for (var i = 0; i < 6; i++) _stats.ColumnDefinitions.Add(new ColumnDefinition());
        var live = Stat("bolt", "HueB", L.T("مصرف لحظه‌ای", "Power right now"), _liveValue, _liveUnit, _liveSub, "HueB", live: true);
        _liveValue.Text = "—";
        _liveUnit.Text = "W";
        _liveUnit.FlowDirection = FlowDirection.LeftToRight;
        ((StackPanel)live.Child).Children.Add(_spark);
        var total = Stat("ecg", "HueA", L.T("مصرف ۳۰ روز گذشته", "Last 30 days"), _totalValue, _totalUnit, _coverage, null);
        _totalValue.Text = "—";
        _totalUnit.Text = "kWh";
        _totalUnit.FlowDirection = FlowDirection.LeftToRight;
        var totalBody = (StackPanel)total.Child;
        totalBody.Children.Insert(2, _meter);
        var cost = Stat("star", "HueC", L.T("هزینه‌ی این ۳۰ روز", "Cost of these 30 days"), _costValue, _costUnit, _costSub, "Gold");
        cost.Background = Res("HeroGradient");
        _costValue.Text = "—";
        foreach (var card in new[] { live, total, cost })
        {
            card.Margin = new Thickness(8);
            _statCards.Add(card);
            _stats.Children.Add(card);
        }
        ApplyLayout(true);

        // ---- tariff ------------------------------------------------------------------------------------------------
        _tariffBox.Text = Hub.Settings.Tariff.ToString("0.##");
        _tariffGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _tariffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tariffTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tariffTexts.Children.Add(new TextBlock { Text = L.T("تعرفه‌ی برق", "Electricity price"), Style = St("H2") });
        tariffTexts.Children.Add(new TextBlock { Text = L.T("نرخ هر کیلووات‌ساعت رو از قبض خودت وارد کن تا هزینه دقیق بشه.", "Enter the price per kWh from your bill for an accurate cost."), Style = St("Body"), Margin = new Thickness(0, 2, 0, 0) });
        _tariffGrid.Children.Add(tariffTexts);
        _tariffControls.Children.Add(_tariffBox);
        _tariffControls.Children.Add(new TextBlock { Text = L.T("تومان / kWh", "Toman / kWh"), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 14, 0) });
        var save = new Button { Style = St("BtnPrimary"), Height = 42, Padding = new Thickness(24, 0, 24, 0) };
        save.Content = PowerUi.ButtonContent("chk", new TextBlock { Text = L.T("ذخیره", "Save"), FontWeight = FontWeights.Bold });
        save.Click += OnSaveTariff;
        _tariffControls.Children.Add(save);
        Grid.SetColumn(_tariffControls, 1);
        _tariffGrid.Children.Add(_tariffControls);
        var tariffCard = new GlassCard { Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(22, 16, 22, 16), Child = _tariffGrid };

        // ---- 30-day chart ------------------------------------------------------------------------------------------
        var chartCard = new GlassCard { Margin = new Thickness(0, 0, 0, 16) };
        var chartBody = new StackPanel();
        var chartHead = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        chartHead.ColumnDefinitions.Add(new ColumnDefinition());
        chartHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chartHead.Children.Add(new TextBlock { Text = L.T("۳۰ روز گذشته", "Last 30 days"), Style = St("H2") });
        Grid.SetColumn(_chartHint, 1);
        chartHead.Children.Add(_chartHint);
        chartBody.Children.Add(chartHead);
        chartBody.Children.Add(_chart);
        chartBody.Children.Add(_chartLabels);
        chartCard.Child = chartBody;

        // ---- by mode -----------------------------------------------------------------------------------------------
        var (modeCard, modeBody) = PowerUi.Card(L.T("مصرف به تفکیک حالت", "Usage by mode"));
        modeBody.Children.Add(_modes);

        var root = new StackPanel();
        root.Children.Add(_stats);
        root.Children.Add(tariffCard);
        root.Children.Add(chartCard);
        root.Children.Add(modeCard);
        root.Children.Add(_note);
        Content = root;

        SizeChanged += (_, e) =>
        {
            ApplyLayout(e.NewSize.Width > 900);
            ApplyTariffLayout(e.NewSize.Width > 700);
        };
        ApplyTariffLayout(true);

        if (Hub.Demo)
        {
            var rnd = new Random(11);
            double w = 140;
            for (var i = 0; i < 24; i++) _watts.Add(w = Math.Clamp(w + (rnd.NextDouble() - 0.5) * 40, 70, 260));
        }
    }

    /// <summary>One stat card: tinted icon + label, big value with a small unit, a sub line.</summary>
    private static GlassCard Stat(string icon, string hue, string label, TextBlock value, TextBlock unit, TextBlock sub, string? valueBrush, bool live = false)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new IconBadge(icon, hue);
        head.Children.Add(badge);
        var name = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        if (live)
        {
            var dot = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            dot.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Res("Green"), VerticalAlignment = VerticalAlignment.Center });
            dot.Children.Add(new TextBlock { Text = L.T("زنده", "Live"), Foreground = Res("Green"), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0) });
            Grid.SetColumn(dot, 2);
            head.Children.Add(dot);
        }

        value.FontSize = 32;
        value.FontWeight = FontWeights.Bold;
        value.FlowDirection = FlowDirection.LeftToRight;
        value.LineHeight = 38;
        value.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        value.Foreground = Res(valueBrush ?? "Text");
        unit.FontSize = 15;
        unit.FontWeight = FontWeights.Medium;
        unit.Foreground = Res("Muted");
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(8, 0, 0, 6);
        var val = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        val.Children.Add(value);
        val.Children.Add(unit);

        sub.Style = St("Label");
        sub.FontSize = 11.5;
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 2, 0, 0);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(val);
        body.Children.Add(sub);
        var card = new GlassCard { Padding = new Thickness(20, 18, 20, 18), Child = body, Lift = true };
        PowerUi.HoverBadge(card, badge);
        return card;
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        _stats.RowDefinitions.Clear();
        if (!wide) for (var r = 0; r < 2; r++) _stats.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // wide: three across; narrow: two on top, the cost card full width below (six columns)
        if (wide) for (var i = 0; i < 3; i++) PowerUi.Place(_statCards[i], 0, i * 2, 2);
        else
        {
            PowerUi.Place(_statCards[0], 0, 0, 3);
            PowerUi.Place(_statCards[1], 0, 3, 3);
            PowerUi.Place(_statCards[2], 1, 0, 6);
        }
    }

    private void ApplyTariffLayout(bool wide)
    {
        if (_tariffWide == wide) return;
        _tariffWide = wide;
        _tariffGrid.RowDefinitions.Clear();
        if (wide)
        {
            Grid.SetRow(_tariffControls, 0);
            Grid.SetColumn(_tariffControls, 1);
            Grid.SetColumnSpan(_tariffControls, 1);
            _tariffControls.Margin = new Thickness(0);
        }
        else
        {
            _tariffGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _tariffGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_tariffControls, 1);
            Grid.SetColumn(_tariffControls, 0);
            Grid.SetColumnSpan(_tariffControls, 2);
            _tariffControls.Margin = new Thickness(0, 14, 0, 0);
        }
    }

    private void OnSaveTariff(object sender, RoutedEventArgs e)
    {
        Hub.Settings.Tariff = Number(_tariffBox, 0, 1_000_000, Hub.Settings.Tariff);
        _tariffBox.Text = Hub.Settings.Tariff.ToString("0.##");
        Hub.Store.Save();
        Motion.Pop(_tariffBox);
        Refresh();
    }

    public override void OnTick(SensorSnapshot s)
    {
        if (s.TotalWatts <= 0) _liveValue.Text = "—";
        else if (!_liveShown)
        {
            _liveShown = true;
            PowerUi.Count(_liveValue, 0, s.TotalWatts, v => Fa.N(v, 0));
        }
        else _liveValue.Text = Fa.N(s.TotalWatts, 0);
        _liveSub.Text = L.T($"پردازنده {Fa.N(s.CpuWatts, 0)} · گرافیک {Fa.N(s.GpuWatts, 0)} · بقیه {Fa.N(Hub.Settings.BaseWatts, 0)} وات",
            $"CPU {Fa.N(s.CpuWatts, 0)} · GPU {Fa.N(s.GpuWatts, 0)} · rest {Fa.N(Hub.Settings.BaseWatts, 0)} W");
        // the sparkline takes a new point every third tick
        if (s.TotalWatts > 0 && _ticks++ % 3 == 0)
        {
            _watts.Add(s.TotalWatts);
            if (_watts.Count > 24) _watts.RemoveAt(0);
            _spark.Set(_watts);
        }
        _note.Text = (s.CpuMeasured
                        ? L.T("توان پردازنده از حسگر خودِ پردازنده خونده می‌شه.", "CPU power is read from the processor's own sensor.")
                        : L.T("ویندوز روی این سیستم حسگر توان پردازنده رو در اختیار نمی‌ذاره، پس از روی میزان بار تخمین زده می‌شه.", "Windows does not expose a CPU power sensor on this PC, so it is estimated from the load."))
                    + " " + L.T("توان گرافیک از روی میزان بار و بقیه‌ی قطعات با یه عدد ثابت تخمین زده می‌شن؛ این عددها رو می‌تونی توی «تنظیمات» با سیستم خودت دقیق کنی. مانیتور حساب نشده.",
                        "GPU power is estimated from its load and everything else with a fixed figure; tune both in Settings. The monitor is not included.");
        // Today's bar grows slowly; redrawing once a minute is plenty.
        if (DateTime.Now - _lastChart > TimeSpan.FromMinutes(1)) Refresh();
    }

    public override void Refresh()
    {
        _lastChart = DateTime.Now;
        var summary = Hub.Energy.Summarize(DateTime.Now);
        var tariff = Hub.Settings.Tariff;

        PowerUi.Count(_totalValue, _totalLast, summary.TotalKwh, v => Fa.N(v, 1));
        _totalLast = summary.TotalKwh;
        PowerUi.Fill(_meter, Math.Clamp(summary.Coverage * 100, 0, 100));
        _coverage.Text = summary.Coverage < 0.005
            ? L.T("اندازه‌گیری تازه شروع شده.", "Measurement has only just started.")
            : L.T($"{Fa.N(summary.Coverage * 100, 0)}٪ این ۳۰ روز اندازه‌گیری شده.", $"{Fa.N(summary.Coverage * 100, 0)}% of these 30 days was measured.");
        var costNow = (double)(long)Math.Round(summary.TotalKwh * tariff);
        PowerUi.Count(_costValue, _costLast, costNow, v => Fa.N((long)Math.Round(v)));
        _costLast = costNow;
        _costUnit.Text = L.T("تومان", "Toman");
        _costSub.Text = L.T($"با تعرفه‌ی {Fa.N((long)tariff)} تومان", $"At {Fa.N((long)tariff)} Toman per kWh");

        DrawChart(summary);

        _modes.Children.Clear();
        var max = Math.Max(0.001, Math.Max(summary.GamingKwh, Math.Max(summary.ActiveKwh, summary.IdleKwh)));
        var first = !_modesDrawn;
        _modesDrawn = true;
        _modes.Children.Add(ModeRow(L.T("گیمینگ", "Gaming"), "HueC", summary.GamingKwh, max, first));
        _modes.Children.Add(Divider());
        _modes.Children.Add(ModeRow(L.T("در حال استفاده", "In use"), "HueA", summary.ActiveKwh, max, first));
        _modes.Children.Add(Divider());
        _modes.Children.Add(ModeRow(L.T("روشن و بی‌استفاده", "On but idle"), "HueE", summary.IdleKwh, max, first));
    }

    private static Grid ModeRow(string name, string tint, double kwh, double max, bool animate = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Res(tint), VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(new TextBlock { Text = name, Style = St("H2"), FontSize = 13.5, Margin = new Thickness(10, 0, 0, 0) });
        grid.Children.Add(label);

        var bar = new ProgressBar { Style = St("Bar"), Maximum = max, Value = animate ? 0 : kwh, Foreground = Res(tint), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);
        if (animate) PowerUi.Fill(bar, kwh, 1000);

        var value = new TextBlock { Text = $"{kwh:0.0} kWh", Style = St("H2"), FontSize = 13.5, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private void DrawChart(EnergySummary summary)
    {
        _chart.Children.Clear();
        _chart.ColumnDefinitions.Clear();
        _chartLabels.Children.Clear();
        _chartLabels.ColumnDefinitions.Clear();

        var days = summary.Days;
        var max = Math.Max(1, days.Max(d => d.TotalWh));
        var measured = days.Where(d => d.TotalWh > 0).ToList();
        var average = measured.Count > 0 ? measured.Average(d => d.TotalWh) : 0;
        _chartHint.Text = measured.Count > 0 ? L.T($"میانگین روزانه {Fa.N(average / 1000, 2)} kWh", $"Daily average {Fa.N(average / 1000, 2)} kWh") : "";
        const double height = 150;
        var animate = !_drawnOnce && Motion.Enabled;
        _drawnOnce = true;

        for (var i = 0; i < days.Count; i++)
        {
            _chart.ColumnDefinitions.Add(new ColumnDefinition());
            _chartLabels.ColumnDefinitions.Add(new ColumnDefinition());
            var day = days[i];
            var date = day.Date.ToDateTime(TimeOnly.MinValue);

            var track = new Border { Background = Res("Line"), CornerRadius = new CornerRadius(5), Margin = new Thickness(2, 0, 2, 0) };
            Grid.SetColumn(track, i);
            _chart.Children.Add(track);

            if (day.TotalWh > 0)
            {
                var bar = new Border
                {
                    // Days well above the average stand out in gold.
                    Background = Res(day.TotalWh > average * 1.4 && measured.Count > 3 ? "Gold" : "AccentBar"),
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(2, 0, 2, 0),
                    Height = Math.Max(4, height * day.TotalWh / max),
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                Grid.SetColumn(bar, i);
                _chart.Children.Add(bar);
                if (animate)
                {
                    // one-time grow from the baseline (transform only), staggered a little per day
                    var scale = new ScaleTransform(1, 0);
                    bar.RenderTransformOrigin = new Point(0.5, 1);
                    bar.RenderTransform = scale;
                    var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 14), EasingFunction = Fx.Out,
                        FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
                    };
                    anim.Completed += (_, _) => bar.RenderTransform = Transform.Identity;
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }
            }

            var cost = Fa.N((long)(day.TotalWh / 1000 * Hub.Settings.Tariff));
            var tip = $"{Fa.DayName(date.DayOfWeek)} {Fa.Date(date)}\n" + L.T($"{Fa.N(day.TotalWh / 1000, 2)} کیلووات‌ساعت · {cost} تومان", $"{Fa.N(day.TotalWh / 1000, 2)} kWh · {cost} Toman");
            var hit = new Border { Background = Brushes.Transparent, ToolTip = new ToolTip { Content = tip, FlowDirection = Flow } };
            Grid.SetColumn(hit, i);
            _chart.Children.Add(hit);

            // Label the first bar and the first day of each month.
            if (i == 0 || Fa.DayOfMonth(date) == 1)
            {
                var label = new TextBlock { Text = Fa.MonthName(date), Style = St("Label"), FontSize = 11, FlowDirection = Flow, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) };
                Grid.SetColumn(label, i);
                Grid.SetColumnSpan(label, Math.Min(6, days.Count - i));
                _chartLabels.Children.Add(label);
            }
        }
    }
}
