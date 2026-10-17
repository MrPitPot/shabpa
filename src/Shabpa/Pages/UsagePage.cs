using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Per-application internet use over a chosen range: four stat cards, one fixed-height chart and a ranked list.</summary>
public sealed class UsagePage : PageBase
{
    public enum Span { Today, Yesterday, Week, Month, ThisMonth, All }

    private const double ChartHeight = 170;
    private const int ListLimit = 40;

    private static readonly Span[] Spans = [Span.Today, Span.Yesterday, Span.Week, Span.Month, Span.ThisMonth, Span.All];

    private readonly Blocks _root = new();
    private readonly StatCard _down = new(L.T("دانلود", "Downloaded"), "dl", "HueB");
    private readonly StatCard _up = new(L.T("آپلود", "Uploaded"), "ul", "HueA");
    private readonly StatCard _total = new(L.T("جمع", "Total"), "net", "HueC");
    private readonly StatCard _avg = new(L.T("میانگین روزانه", "Daily average"), "ecg", "HueD");
    private readonly GlassCard _chartCard = new() { Padding = new Thickness(24, 20, 24, 18) };
    private readonly StackPanel _list = new();
    private readonly WrapPanel _budgetChips = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly ProgressBar _budgetBar = new() { Style = St("Bar"), Maximum = 1, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _budgetText = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private Span _span = Span.Week;
    private string? _app;
    private DateTime _lastDraw;
    private readonly CMo.Memo _rows;
    // user actions replay the motion; the live refresh every few seconds only updates the numbers quietly
    private bool _animateChart, _animateList, _entering;

    public override string PageTitle => L.T("پایداری پینگ", "Ping stability");
    public override string PageSubtitle => L.T("هر برنامه در هر بازه‌ی زمانی چقدر اینترنت خورده.", "How much internet each app used, over any period.");

    public UsagePage()
    {
        GroupC.EnsureIcons();
        _rows = new CMo.Memo(_list);

        // period selector
        var spans = new Segmented(Spans.Select(SpanName).ToList(), Array.IndexOf(Spans, _span), compact: true) { Margin = new Thickness(0, 0, 0, 2) };
        spans.Changed += i =>
        {
            _span = Spans[i];
            _rows.Reset();
            _animateChart = _animateList = true;
            Draw();
        };
        _root.Add(spans, Blocks.At(0, 0, 12));

        // four numbers
        _root.Add(_down, Blocks.At(1, 0, 3), Blocks.At(1, 0, 6));
        _root.Add(_up, Blocks.At(1, 3, 3), Blocks.At(1, 6, 6));
        _root.Add(_total, Blocks.At(1, 6, 3), Blocks.At(2, 0, 6));
        _root.Add(_avg, Blocks.At(1, 9, 3), Blocks.At(2, 6, 6));
        _root.Add(_chartCard, Blocks.At(2, 0, 12), Blocks.At(3, 0, 12));

        // monthly allowance
        var (budgetCard, budget) = Ui2.Card(L.T("سقف حجم ماهانه", "Monthly allowance"),
            L.T("اگه بسته‌ی اینترنتت حجم ماهانه داره، سقفش رو بذار تا موقع ۸۰٪ و ۱۰۰٪ خبرت کنم.", "If your plan has a monthly allowance, set it and I warn you at 80% and 100%."));
        budget.Children.Add(_budgetChips);
        budget.Children.Add(_budgetBar);
        budget.Children.Add(_budgetText);
        _root.Add(budgetCard, Blocks.At(3, 0, 12), Blocks.At(4, 0, 12));
        Chips(_budgetChips, new double[] { 0, 30, 50, 100, 200, 300, 500, 1000 }.Select(v => (v, v == 0 ? L.T("بدون سقف", "No limit") : $"{Fa.N(v, 0)} {L.T("گیگ", "GB")}")),
            Hub.Settings.MonthlyLimitGb, v =>
            {
                Hub.Settings.MonthlyLimitGb = v;
                Hub.Settings.BudgetWarned = "";
                Hub.Store.Save();
                _animateChart = true;
                Draw();
            });

        // ranked list with the clear button at the end of its title row
        var header = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        var clear = new Button { Style = St("BtnDanger"), Padding = new Thickness(16, 8, 16, 8) };
        var clearRow = new StackPanel { Orientation = Orientation.Horizontal };
        clearRow.Children.Add(new Icon("trash", 16) { Margin = new Thickness(0, 0, 8, 0) });
        clearRow.Children.Add(new TextBlock { Text = L.T("پاک کردن تاریخچه", "Clear history"), VerticalAlignment = VerticalAlignment.Center });
        clear.Content = clearRow;
        clear.Click += OnClear;
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(new TextBlock { Text = L.T("مصرف هر برنامه", "Usage per app"), Style = St("Section"), VerticalAlignment = VerticalAlignment.Center });
        var listBlock = new StackPanel();
        listBlock.Children.Add(header);
        listBlock.Children.Add(new GlassCard { Padding = new Thickness(14, 12, 14, 12), Child = _list, Margin = new Thickness(0, 8, 0, 0) });
        listBlock.Children.Add(new TextBlock
        {
            Style = St("Label"),
            Margin = new Thickness(6, 12, 6, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = L.T("روی هر برنامه بزن تا نمودار فقط برای همون نشون داده بشه. حجم‌ها از لحظه‌ی باز بودن شب‌پا (با دسترسی ادمین) ثبت می‌شن و روی همین سیستم می‌مونن؛ چیزی جایی فرستاده نمی‌شه.",
                "Click an app to chart only that one. Usage is recorded while Shabpa runs as administrator and stays on this PC; nothing is sent anywhere."),
        });
        _root.Add(listBlock, Blocks.At(4, 0, 12), Blocks.At(5, 0, 12));
        Content = _root;
        Loaded += (_, _) =>
        {
            // entrance: cards rise, the numbers count up from zero, the bars grow and the list rows follow
            CMo.Cascade(_root);
            foreach (var card in new[] { _down, _up, _total, _avg }) CMo.ResetBytes(card);
            _budgetBar.Value = 0;
            _rows.Reset();
            _entering = true;
            _animateChart = _animateList = true;
            Draw();
            _entering = false;
        };
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
            Span.ThisMonth => (DateOnly.FromDateTime(Fa.MonthStart(now)), today),
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

        var animate = _animateChart && IsLoaded;
        DrawBudget(now);
        DrawSummary(from, to, down, up, days, animate);
        DrawChart(from, to, animate);
        DrawList(rows, _animateList && IsLoaded);
        _animateChart = _animateList = false;
    }

    private void DrawBudget(DateTime now)
    {
        var limit = Hub.Settings.MonthlyLimitGb;
        var from = DateOnly.FromDateTime(Fa.MonthStart(now));
        var (down, up) = Hub.Usage.Total(from, DateOnly.FromDateTime(now));
        var check = DataBudget.Evaluate(down + up, limit);
        _budgetBar.Visibility = limit > 0 ? Visibility.Visible : Visibility.Collapsed;
        var before = _budgetBar.Value;
        _budgetBar.Value = Math.Clamp(check.Fraction, 0, 1);
        if (_animateChart && IsLoaded) CMo.Fill(_budgetBar, before);
        _budgetBar.Foreground = Res(check.Level == 2 ? "Red" : check.Level == 1 ? "Gold" : "Accent");
        _budgetText.Text = limit > 0
            ? L.T($"این ماه {Fa.N(check.UsedGb, 1)} از {Fa.N(limit, 0)} گیگ مصرف شده ({Fa.N(check.Fraction * 100, 0)}٪).",
                $"This month: {Fa.N(check.UsedGb, 1)} of {Fa.N(limit, 0)} GB used ({Fa.N(check.Fraction * 100, 0)}%).")
            : L.T($"این ماه تا الان {Fa.N(check.UsedGb, 1)} گیگ مصرف شده.", $"This month so far: {Fa.N(check.UsedGb, 1)} GB.");
    }

    private void DrawSummary(DateOnly from, DateOnly to, long down, long up, int days, bool animate)
    {
        // one point per hour for a single day, otherwise one per day: the sparkline of each card
        List<double> d, u;
        if (from == to)
        {
            var (hd, hu) = Hub.Usage.Hourly(from);
            d = hd.Select(x => (double)x).ToList();
            u = hu.Select(x => (double)x).ToList();
        }
        else
        {
            var daily = Hub.Usage.Daily(from, to);
            d = daily.Select(x => (double)x.down).ToList();
            u = daily.Select(x => (double)x.up).ToList();
        }
        var t = d.Zip(u, (x, y) => x + y).ToList();
        CMo.CountBytes(_down, down, d, animate);
        CMo.CountBytes(_up, up, u, animate);
        CMo.CountBytes(_total, down + up, t, animate);
        CMo.CountBytes(_avg, (down + up) / days, t, animate);
    }

    private void DrawChart(DateOnly from, DateOnly to, bool animate)
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
                var shortRange = (to.DayNumber - from.DayNumber) < 8;
                bars.Add((Fa.DayOfMonth(d) == 1 || date == from ? Fa.MonthName(d) + " " + Fa.Digits(Fa.DayOfMonth(d).ToString()) : shortRange ? Fa.Digits(Fa.DayOfMonth(d).ToString()) : "",
                    $"{Fa.DayName(d.DayOfWeek)} {Fa.Date(d)}", down, up));
            }
        }

        var panel = new StackPanel();
        var title = _app is null
            ? L.T("همه‌ی برنامه‌ها", "All apps")
            : usage.Range(from, to).FirstOrDefault(r => r.Key == _app)?.Title ?? _app;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        if (_app is not null)
        {
            var all = new Button { Style = St("Btn"), Content = L.T("نمایش همه", "Show all"), Padding = new Thickness(14, 6, 14, 6) };
            all.Click += (_, _) => { _app = null; _animateChart = true; Draw(); };
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
        }
        head.Children.Add(new TextBlock { Text = title + L.Comma + (single ? L.T("به تفکیک ساعت", "by hour") : L.T("به تفکیک روز", "by day")), Style = St("H2"), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);

        var max = Math.Max(1, bars.Count == 0 ? 1 : bars.Max(b => b.down + b.up));
        // a handful of bars get room between them and a label each; many bars sit close like a histogram
        var few = bars.Count <= 8;
        var gap = few ? 7.0 : 2.0;
        var chart = new Grid { Height = ChartHeight, FlowDirection = FlowDirection.LeftToRight };
        var labels = new Grid { FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 8, 0, 0) };
        for (var i = 0; i < bars.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            labels.ColumnDefinitions.Add(new ColumnDefinition());
            var (label, tip, down, up) = bars[i];

            var track = new Border { Background = Res("Line"), CornerRadius = new CornerRadius(5), Margin = new Thickness(gap, 0, gap, 0) };
            Grid.SetColumn(track, i);
            chart.Children.Add(track);

            if (down + up > 0)
            {
                var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(gap, 0, gap, 0) };
                var upHeight = ChartHeight * up / max;
                var downHeight = ChartHeight * down / max;
                if (up > 0) column.Children.Add(new Border { Background = Res("HueA"), Height = Math.Max(2, upHeight), CornerRadius = new CornerRadius(5, 5, 0, 0) });
                if (down > 0) column.Children.Add(new Border { Background = Res("HueB"), Height = Math.Max(2, downHeight), CornerRadius = up > 0 ? new CornerRadius(0) : new CornerRadius(5, 5, 0, 0) });
                Grid.SetColumn(column, i);
                chart.Children.Add(column);
                if (animate) CMo.Grow(column, i, delayMs: _entering ? 380 : 0);
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
                var text = new TextBlock
                {
                    Text = label, Style = St("Label"), FontSize = 11.5, Foreground = Res("Faint"), FlowDirection = Flow,
                    HorizontalAlignment = few ? HorizontalAlignment.Center : HorizontalAlignment.Left, Margin = new Thickness(few ? 0 : 2, 0, 0, 0),
                };
                Grid.SetColumn(text, i);
                Grid.SetColumnSpan(text, few ? 1 : Math.Min(6, bars.Count - i));
                labels.Children.Add(text);
                if (animate) CMo.Appear(text, i / 3, 6, 0.5);
            }
        }
        panel.Children.Add(chart);
        panel.Children.Add(labels);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        legend.Children.Add(Dot("HueB", L.T("دانلود", "Download")));
        legend.Children.Add(Dot("HueA", L.T("آپلود", "Upload")));
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

    private void DrawList(List<UsageRow> rows, bool animate)
    {
        _list.Children.Clear();
        _rows.Begin();
        if (rows.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = L.T("هنوز مصرفی ثبت نشده. شب‌پا باید با دسترسی ادمین باز باشه تا ترافیک هر برنامه رو ببینه.",
                    "No usage recorded yet. Shabpa needs to run as administrator to see each app's traffic."),
                Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 8, 10, 8),
            });
            _rows.End();
            return;
        }

        var top = Math.Max(1, rows[0].Total);
        var order = 0;
        foreach (var row in rows.Take(ListLimit))
        {
            var selected = row.Key == _app;
            var shell = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 1, 0, 1), Cursor = System.Windows.Input.Cursors.Hand,
                Background = selected ? Res("AccentSoft") : Brushes.Transparent,
            };
            if (!selected)
            {
                shell.MouseEnter += (_, _) => shell.Background = Res("Line");
                shell.MouseLeave += (_, _) => shell.Background = Brushes.Transparent;
            }
            Fx.Attach(shell, nudge: 2, press: 0.985);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = row.Title, Style = St("Body"), Foreground = Res(selected ? "Accent" : "Text"), FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var bar = new Grid { Height = 8, Margin = new Thickness(0, 6, 12, 0), FlowDirection = FlowDirection.LeftToRight };
            bar.Children.Add(new Border { Background = Res("Line"), CornerRadius = new CornerRadius(4) });
            var share = Math.Clamp((double)row.Total / top, 0.01, 1);
            var fill = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(row.Down, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(row.Up, 0), GridUnitType.Star) });
            var down = new Border { Background = Res("HueB"), CornerRadius = new CornerRadius(4, 0, 0, 4) };
            var up = new Border { Background = Res("HueA"), CornerRadius = new CornerRadius(0, 4, 4, 0) };
            Grid.SetColumn(up, 1);
            fill.Children.Add(down);
            fill.Children.Add(up);
            // width proportional to the biggest app: a star column scales the whole bar
            var scaler = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            scaler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
            scaler.Children.Add(fill);
            bar.Children.Add(scaler);
            if (animate && _list.IsLoaded) CMo.Grow(fill, order, vertical: false, seconds: 0.8, delayMs: 120);
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
            shell.Child = grid;

            var key = row.Key;
            shell.MouseLeftButtonUp += (_, _) =>
            {
                _app = _app == key ? null : key;
                Draw();
            };
            _list.Children.Add(_rows.Item(row.Key, shell));
            order++;
        }
        _rows.End();
        if (rows.Count > ListLimit)
            _list.Children.Add(new TextBlock { Text = L.T($"و {Fa.Digits((rows.Count - ListLimit).ToString())} برنامه‌ی دیگه با مصرف کمتر.", $"and {rows.Count - ListLimit} more with less use."), Style = St("Label"), Margin = new Thickness(12, 8, 12, 4) });
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.AskAlways(L.T("پاک کردن تاریخچه‌ی مصرف", "Clear usage history"),
                L.T("تاریخچه‌ی مصرف همه‌ی برنامه‌ها برای همیشه پاک بشه؟", "Delete the usage history of every app for good?"),
                L.T("پاک کن", "Delete"), danger: true)) return;
        Hub.Usage.Clear();
        _app = null;
        Draw();
    }
}
