using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Per-application internet use over a chosen range, drawn as one fixed-height chart and a ranked list.</summary>
public sealed class UsagePage : PageBase
{
    public enum Span { Today, Yesterday, Week, Month, ThisMonth, All }

    private const double ChartHeight = 150;
    private const int ListLimit = 40;

    private static readonly Span[] Spans = [Span.Today, Span.Yesterday, Span.Week, Span.Month, Span.ThisMonth, Span.All];

    private readonly WrapPanel _spanChips = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly Border _summary = new() { Style = St("Card") };
    private readonly Border _chartCard = new() { Style = St("Card"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel _list = new();
    private Span _span = Span.Week;
    private string? _app;
    private DateTime _lastDraw;

    public override string PageTitle => L.T("پایداری پینگ", "Ping stability");
    public override string PageSubtitle => L.T("هر برنامه در هر بازه‌ی زمانی چقدر اینترنت خورده.", "How much internet each app used, over any period.");

    public UsagePage()
    {
        var stack = new StackPanel();
        stack.Children.Add(_spanChips);
        stack.Children.Add(_summary);
        stack.Children.Add(_chartCard);

        var header = new DockPanel { Margin = new Thickness(4, 18, 4, 8) };
        var clear = new Button { Style = St("BtnDanger"), Content = L.T("پاک کردن تاریخچه", "Clear history") };
        clear.Click += OnClear;
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(new TextBlock { Text = L.T("مصرف هر برنامه", "Usage per app"), Style = St("Section"), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(header);

        var listCard = new Border { Style = St("Card"), Child = _list };
        stack.Children.Add(listCard);
        stack.Children.Add(new TextBlock
        {
            Style = St("Label"),
            Margin = new Thickness(4, 12, 4, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = L.T("روی هر برنامه بزن تا نمودار فقط برای همون نشون داده بشه. حجم‌ها از لحظه‌ی باز بودن شب‌پا (با دسترسی ادمین) ثبت می‌شن و روی همین سیستم می‌مونن؛ چیزی جایی فرستاده نمی‌شه.",
                "Click an app to chart only that one. Usage is recorded while Shabpa runs as administrator and stays on this PC; nothing is sent anywhere."),
        });
        Content = stack;

        Chips(_spanChips, Spans.Select(s => (s, SpanName(s))), _span, v =>
        {
            _span = v;
            Draw();
        });
    }

    private static string SpanName(Span s) => s switch
    {
        Span.Today => L.T("امروز", "Today"),
        Span.Yesterday => L.T("دیروز", "Yesterday"),
        Span.Week => L.T("۷ روز", "7 days"),
        Span.Month => L.T("۳۰ روز", "30 days"),
        Span.ThisMonth => L.T("این ماه", "This month"),
        _ => L.T("همه", "All time"),
    };

    /// <summary>The inclusive date range a span covers.</summary>
    public static (DateOnly from, DateOnly to) Bounds(Span span, DateTime now, DateOnly? first)
    {
        var today = DateOnly.FromDateTime(now);
        return span switch
        {
            Span.Today => (today, today),
            Span.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            Span.Week => (today.AddDays(-6), today),
            Span.Month => (today.AddDays(-29), today),
            Span.ThisMonth => (new DateOnly(now.Year, now.Month, 1), today),
            _ => (first ?? today, today),
        };
    }

    public override void Refresh() => Draw();

    public override void OnSecond()
    {
        // Live data only moves for ranges that include today; a redraw every few seconds is plenty.
        if (DateTime.Now - _lastDraw < TimeSpan.FromSeconds(5)) return;
        Draw();
    }

    private void Draw()
    {
        _lastDraw = DateTime.Now;
        var now = DateTime.Now;
        var usage = Hub.Usage;
        var (from, to) = Bounds(_span, now, usage.FirstDay);
        var rows = usage.Range(from, to);
        var (down, up) = usage.Total(from, to);
        var days = Math.Max(1, to.DayNumber - from.DayNumber + 1);

        DrawSummary(down, up, days);
        DrawChart(from, to);
        DrawList(rows);
    }

    private void DrawSummary(long down, long up, int days)
    {
        var grid = new Grid();
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Cell(int column, string label, string value, string tint)
        {
            var cell = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            cell.Children.Add(new TextBlock { Text = label, Style = St("Label") });
            cell.Children.Add(new TextBlock
            {
                Text = value, FontFamily = (FontFamily)Application.Current.Resources["Num"], FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = Res(tint), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
            });
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        Cell(0, L.T("دانلود", "Downloaded"), Fa.Bytes(down), "Green");
        Cell(1, L.T("آپلود", "Uploaded"), Fa.Bytes(up), "Accent");
        Cell(2, L.T("جمع", "Total"), Fa.Bytes(down + up), "Text");
        Cell(3, L.T("میانگین روزانه", "Daily average"), Fa.Bytes((down + up) / days), "Gold");
        _summary.Child = grid;
    }

    private void DrawChart(DateOnly from, DateOnly to)
    {
        var usage = Hub.Usage;
        var single = from == to;
        List<(string label, string tip, long down, long up)> bars = [];
        if (single && _app is null)
        {
            var (hd, hu) = usage.Hourly(from);
            for (var h = 0; h < 24; h++)
                bars.Add((h % 3 == 0 ? Fa.Digits(h.ToString()) : "", $"{Fa.Clock(h, 0)}", hd[h], hu[h]));
        }
        else
        {
            foreach (var (date, down, up) in usage.Daily(from, to, _app))
            {
                var d = date.ToDateTime(TimeOnly.MinValue);
                bars.Add((Fa.DayOfMonth(d) == 1 || date == from ? Fa.MonthName(d) + " " + Fa.Digits(Fa.DayOfMonth(d).ToString()) : "",
                    $"{Fa.DayName(d.DayOfWeek)} {Fa.Date(d)}", down, up));
            }
        }

        var panel = new StackPanel();
        var title = _app is null
            ? L.T("همه‌ی برنامه‌ها", "All apps")
            : usage.Range(from, to).FirstOrDefault(r => r.Key == _app)?.Title ?? _app;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (_app is not null)
        {
            var all = new Button { Style = St("Btn"), Content = L.T("نمایش همه", "Show all") };
            all.Click += (_, _) => { _app = null; Draw(); };
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
        }
        head.Children.Add(new TextBlock { Text = title + L.Comma + (single ? L.T("به تفکیک ساعت", "by hour") : L.T("به تفکیک روز", "by day")), Style = St("H2") });
        panel.Children.Add(head);

        var max = Math.Max(1, bars.Count == 0 ? 1 : bars.Max(b => b.down + b.up));
        var chart = new Grid { Height = ChartHeight, FlowDirection = FlowDirection.LeftToRight };
        var labels = new Grid { FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < bars.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            labels.ColumnDefinitions.Add(new ColumnDefinition());
            var (label, tip, down, up) = bars[i];

            var track = new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5, 0, 1.5, 0) };
            Grid.SetColumn(track, i);
            chart.Children.Add(track);

            if (down + up > 0)
            {
                var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1.5, 0, 1.5, 0) };
                var upHeight = ChartHeight * up / max;
                var downHeight = ChartHeight * down / max;
                if (up > 0) column.Children.Add(new Border { Background = Res("Accent"), Height = Math.Max(2, upHeight), CornerRadius = new CornerRadius(3, 3, 0, 0) });
                if (down > 0) column.Children.Add(new Border { Background = Res("Green"), Height = Math.Max(2, downHeight), CornerRadius = new CornerRadius(up > 0 ? 0 : 3, up > 0 ? 0 : 3, 0, 0) });
                Grid.SetColumn(column, i);
                chart.Children.Add(column);
            }

            var hit = new Border
            {
                Background = Brushes.Transparent,
                ToolTip = new ToolTip { Content = $"{tip}\n↓ {Fa.Bytes(down)}  ↑ {Fa.Bytes(up)}", FlowDirection = FlowDirection.LeftToRight },
            };
            Grid.SetColumn(hit, i);
            chart.Children.Add(hit);

            if (label.Length > 0)
            {
                var text = new TextBlock { Text = label, Style = St("Label"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), FlowDirection = Flow };
                Grid.SetColumn(text, i);
                Grid.SetColumnSpan(text, Math.Min(6, bars.Count - i));
                labels.Children.Add(text);
            }
        }
        panel.Children.Add(chart);
        panel.Children.Add(labels);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        legend.Children.Add(Dot("Green", L.T("دانلود", "Download")));
        legend.Children.Add(Dot("Accent", L.T("آپلود", "Upload")));
        legend.Children.Add(new TextBlock
        {
            Text = L.T($"بلندترین ستون: {Fa.Bytes(max)}", $"Tallest bar: {Fa.Bytes(max)}"), Style = St("Label"),
            Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(legend);
        _chartCard.Child = panel;
    }

    private static UIElement Dot(string tint, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
        row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Res(tint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(new TextBlock { Text = text, Style = St("Label"), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void DrawList(List<UsageRow> rows)
    {
        _list.Children.Clear();
        if (rows.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = L.T("هنوز مصرفی ثبت نشده. شب‌پا باید با دسترسی ادمین باز باشه تا ترافیک هر برنامه رو ببینه.",
                    "No usage recorded yet. Shabpa needs to run as administrator to see each app's traffic."),
                Style = St("Body"), TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var top = Math.Max(1, rows[0].Total);
        foreach (var row in rows.Take(ListLimit))
        {
            var selected = row.Key == _app;
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = row.Title, Style = St("Body"), Foreground = Res(selected ? "Accent" : "Text"), FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var bar = new Grid { Height = 8, Margin = new Thickness(0, 6, 12, 0), FlowDirection = FlowDirection.LeftToRight };
            bar.Children.Add(new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(4) });
            var share = Math.Clamp((double)row.Total / top, 0.01, 1);
            var fill = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(row.Down, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(row.Up, 0), GridUnitType.Star) });
            var down = new Border { Background = Res("Green"), CornerRadius = new CornerRadius(4, 0, 0, 4) };
            var up = new Border { Background = Res("Accent"), CornerRadius = new CornerRadius(0, 4, 4, 0) };
            Grid.SetColumn(up, 1);
            fill.Children.Add(down);
            fill.Children.Add(up);
            // Width proportional to the biggest app; the Grid fills its parent so use a star column to scale.
            var scaler = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
            fill.HorizontalAlignment = HorizontalAlignment.Stretch;
            scaler.Children.Add(fill);
            bar.Children.Add(scaler);
            left.Children.Add(bar);
            grid.Children.Add(left);

            var numbers = new StackPanel { VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            numbers.Children.Add(new TextBlock
            {
                Text = Fa.Bytes(row.Total), FontFamily = (FontFamily)Application.Current.Resources["Num"], FontWeight = FontWeights.Bold, FontSize = 15,
                Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Right,
            });
            numbers.Children.Add(new TextBlock
            {
                Text = $"↓ {Fa.Bytes(row.Down)}   ↑ {Fa.Bytes(row.Up)}", Style = St("Label"), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right,
            });
            Grid.SetColumn(numbers, 1);
            grid.Children.Add(numbers);

            var key = row.Key;
            grid.MouseLeftButtonUp += (_, _) =>
            {
                _app = _app == key ? null : key;
                Draw();
            };
            _list.Children.Add(grid);
            _list.Children.Add(Divider());
        }
        if (rows.Count > ListLimit)
            _list.Children.Add(new TextBlock { Text = L.T($"و {Fa.Digits((rows.Count - ListLimit).ToString())} برنامه‌ی دیگه با مصرف کمتر.", $"and {rows.Count - ListLimit} more with less use."), Style = St("Label") });
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(L.T("تاریخچه‌ی مصرف همه‌ی برنامه‌ها برای همیشه پاک بشه؟", "Delete the usage history of every app for good?"),
            L.T("شب‌پا", "Shabpa"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No,
            Ui.Flow == FlowDirection.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None);
        if (answer != MessageBoxResult.Yes) return;
        Hub.Usage.Clear();
        _app = null;
        Draw();
    }
}
