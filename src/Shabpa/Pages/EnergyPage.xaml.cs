using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class EnergyPage : PageBase
{
    private DateTime _lastChart;

    public override string PageTitle => L.T("مصرف برق", "Energy use");
    public override string PageSubtitle => L.T("سیستمت چقدر برق می‌کشه و آخر ماه چقدر روی قبض میاد.", "How much power the PC draws and what it adds to the bill.");

    public EnergyPage()
    {
        InitializeComponent();
        TariffBox.Text = Hub.Settings.Tariff.ToString("0.##");
    }

    private void OnSaveTariff(object sender, RoutedEventArgs e)
    {
        Hub.Settings.Tariff = Number(TariffBox, 0, 1_000_000, Hub.Settings.Tariff);
        TariffBox.Text = Hub.Settings.Tariff.ToString("0.##");
        Hub.Store.Save();
        Refresh();
    }

    public override void OnTick(SensorSnapshot s)
    {
        LiveText.Text = s.TotalWatts > 0 ? $"{s.TotalWatts:0} W" : "—";
        LiveSub.Text = L.T($"پردازنده {Fa.N(s.CpuWatts, 0)} · گرافیک {Fa.N(s.GpuWatts, 0)} · بقیه {Fa.N(Hub.Settings.BaseWatts, 0)} وات",
            $"CPU {Fa.N(s.CpuWatts, 0)} · GPU {Fa.N(s.GpuWatts, 0)} · rest {Fa.N(Hub.Settings.BaseWatts, 0)} W");
        Note.Text = (s.CpuMeasured
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

        TotalText.Text = $"{summary.TotalKwh:0.0} kWh";
        CoverageText.Text = summary.Coverage < 0.005
            ? L.T("اندازه‌گیری تازه شروع شده.", "Measurement has only just started.")
            : L.T($"{Fa.N(summary.Coverage * 100, 0)}٪ این ۳۰ روز اندازه‌گیری شده.", $"{Fa.N(summary.Coverage * 100, 0)}% of these 30 days was measured.");
        CostText.Text = L.T($"{Fa.N((long)Math.Round(summary.TotalKwh * tariff))} تومان", $"{Fa.N((long)Math.Round(summary.TotalKwh * tariff))} Toman");
        CostSub.Text = L.T($"با تعرفه‌ی {Fa.N((long)tariff)} تومان", $"At {Fa.N((long)tariff)} Toman per kWh");

        DrawChart(summary);

        ModePanel.Children.Clear();
        var max = Math.Max(0.001, Math.Max(summary.GamingKwh, Math.Max(summary.ActiveKwh, summary.IdleKwh)));
        ModePanel.Children.Add(ModeRow(L.T("گیمینگ", "Gaming"), "Gold", summary.GamingKwh, max));
        ModePanel.Children.Add(Divider());
        ModePanel.Children.Add(ModeRow(L.T("در حال استفاده", "In use"), "Accent", summary.ActiveKwh, max));
        ModePanel.Children.Add(Divider());
        ModePanel.Children.Add(ModeRow(L.T("روشن و بی‌استفاده", "On but idle"), "Red", summary.IdleKwh, max));
    }

    private static Grid ModeRow(string name, string tint, double kwh, double max)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Res(tint), VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(new TextBlock { Text = name, Style = St("H2"), FontSize = 13.5, Margin = new Thickness(10, 0, 0, 0) });
        grid.Children.Add(label);

        var bar = new ProgressBar { Style = St("Bar"), Maximum = max, Value = kwh, Foreground = Res(tint), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);

        var value = new TextBlock { Text = $"{kwh:0.0} kWh", Style = St("H2"), FontSize = 13.5, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private void DrawChart(EnergySummary summary)
    {
        Chart.Children.Clear();
        Chart.ColumnDefinitions.Clear();
        ChartLabels.Children.Clear();
        ChartLabels.ColumnDefinitions.Clear();

        var days = summary.Days;
        var max = Math.Max(1, days.Max(d => d.TotalWh));
        var measured = days.Where(d => d.TotalWh > 0).ToList();
        var average = measured.Count > 0 ? measured.Average(d => d.TotalWh) : 0;
        ChartHint.Text = measured.Count > 0 ? L.T($"میانگین روزانه {Fa.N(average / 1000, 2)} kWh", $"Daily average {Fa.N(average / 1000, 2)} kWh") : "";
        const double height = 150;

        for (var i = 0; i < days.Count; i++)
        {
            Chart.ColumnDefinitions.Add(new ColumnDefinition());
            ChartLabels.ColumnDefinitions.Add(new ColumnDefinition());
            var day = days[i];
            var date = day.Date.ToDateTime(TimeOnly.MinValue);

            var track = new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(4), Margin = new Thickness(2, 0, 2, 0) };
            Grid.SetColumn(track, i);
            Chart.Children.Add(track);

            if (day.TotalWh > 0)
            {
                var bar = new Border
                {
                    // Days well above the average stand out in gold.
                    Background = Res(day.TotalWh > average * 1.4 && measured.Count > 3 ? "Gold" : "Accent"),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2, 0, 2, 0),
                    Height = Math.Max(4, height * day.TotalWh / max),
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                Grid.SetColumn(bar, i);
                Chart.Children.Add(bar);
            }

            var cost = Fa.N((long)(day.TotalWh / 1000 * Hub.Settings.Tariff));
            var tip = $"{Fa.DayName(date.DayOfWeek)} {Fa.Date(date)}\n" + L.T($"{Fa.N(day.TotalWh / 1000, 2)} کیلووات‌ساعت · {cost} تومان", $"{Fa.N(day.TotalWh / 1000, 2)} kWh · {cost} Toman");
            var hit = new Border { Background = System.Windows.Media.Brushes.Transparent, ToolTip = new ToolTip { Content = tip, FlowDirection = Flow } };
            Grid.SetColumn(hit, i);
            Chart.Children.Add(hit);

            // Label the first bar and the first day of each month.
            if (i == 0 || Fa.DayOfMonth(date) == 1)
            {
                var label = new TextBlock { Text = Fa.MonthName(date), Style = St("Label"), FontSize = 11, FlowDirection = Flow, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) };
                Grid.SetColumn(label, i);
                Grid.SetColumnSpan(label, Math.Min(6, days.Count - i));
                ChartLabels.Children.Add(label);
            }
        }
    }
}
