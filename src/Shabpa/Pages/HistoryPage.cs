using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«خواب و بیداری» (a tab of «کنترل سیستم»): the week's power events, a day timeline, and what keeps the PC awake.</summary>
public sealed class HistoryPage : PageBase
{
    private const int DaysShown = 7;

    private List<PowerEvent> _events = [];
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Now);
    private bool _loaded;

    private readonly Blocks _root = new();
    private readonly IconBadge _summaryBadge = new("chk", "Green", 48, 24, 14);
    private readonly TextBlock _summaryTitle = new() { FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summaryText = new() { Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly StackPanel _summaryPills = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid _dayTiles = new() { Columns = DaysShown, Margin = new Thickness(-5, 0, -5, 0) };
    private readonly TextBlock _dayTitle = new() { Style = St("H2"), Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _timeline = new();
    private readonly StackPanel _blockerPanel = new();

    public override string PageTitle => L.T("خواب و بیداری", "Sleep and wake history");
    public override string PageSubtitle => L.T("سیستم کِی روشن و خاموش شده، چرا، و چی جلوی خوابیدنش رو می‌گیره.", "When the PC turned on and off, why, and what keeps it from sleeping.");

    public HistoryPage()
    {
        GroupC.EnsureIcons();
        _summaryTitle.Foreground = Res("Text");

        // summary: badge, headline, pills, reload
        var summary = new GlassCard { Padding = new Thickness(24, 20, 24, 20) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_summaryBadge);
        var texts = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_summaryTitle);
        texts.Children.Add(_summaryText);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var end = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        end.Children.Add(_summaryPills);
        var reload = new Button { Style = St("Btn"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(16, 8, 16, 8) };
        var reloadRow = new StackPanel { Orientation = Orientation.Horizontal };
        var reloadIcon = new Icon("refresh", 16) { Margin = new Thickness(0, 0, 8, 0) };
        reloadRow.Children.Add(reloadIcon);
        reloadRow.Children.Add(new TextBlock { Text = L.T("بازبینی مجدد", "Refresh"), VerticalAlignment = VerticalAlignment.Center });
        reload.Content = reloadRow;
        reload.Click += (_, _) =>
        {
            CMo.Spin(reloadIcon);
            Load();
        };
        end.Children.Add(reload);
        Grid.SetColumn(end, 2);
        grid.Children.Add(end);
        summary.Child = grid;
        // on a narrow page the pills and the button drop under the headline
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bool? stacked = null;
        summary.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 780;
            if (stacked == narrow) return;
            stacked = narrow;
            Grid.SetRow(end, narrow ? 1 : 0);
            Grid.SetColumn(end, narrow ? 0 : 2);
            Grid.SetColumnSpan(end, narrow ? 3 : 1);
            end.Margin = narrow ? new Thickness(0, 14, 0, 0) : new Thickness(0);
        };
        _root.Add(summary, Blocks.At(0, 0, 12));

        // the last seven days as tiles
        var week = new StackPanel();
        week.Children.Add(new TextBlock { Text = L.T("هفته‌ی گذشته", "The past week"), Style = St("Section"), Margin = new Thickness(0, 6, 0, 10) });
        week.Children.Add(_dayTiles);
        _root.Add(week, Blocks.At(1, 0, 12));

        // day timeline
        var dayBody = new StackPanel();
        dayBody.Children.Add(_dayTitle);
        dayBody.Children.Add(_timeline);
        _root.Add(new GlassCard { Padding = new Thickness(24, 20, 24, 12), Child = dayBody }, Blocks.At(2, 0, 7));

        // what keeps the PC awake
        var blockBody = new StackPanel();
        blockBody.Children.Add(new TextBlock { Text = L.T("چی نمی‌ذاره سیستم بخوابه", "What keeps the PC awake"), Style = St("H2"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        blockBody.Children.Add(_blockerPanel);
        _root.Add(new GlassCard { Padding = new Thickness(24, 20, 24, 14), Child = blockBody }, Blocks.At(2, 7, 5));

        Content = _root;
        CMo.CascadeOnLoad(this, _root);
    }

    public override void Refresh()
    {
        if (_loaded) return;
        _loaded = true;
        Load();
    }

    private void Load()
    {
        _summaryTitle.Text = L.T("در حال خوندن تاریخچه‌ی ویندوز…", "Reading the Windows history…");
        _summaryText.Text = "";
        Task.Run(() =>
        {
            var from = DateTime.Now.Date.AddDays(-(DaysShown - 1));
            var events = PowerHistory.Read(from);
            var blockers = SleepBlockers.Read();
            Dispatcher.BeginInvoke(() =>
            {
                _events = events;
                Render(from);
                ShowBlockers(blockers);
            });
        });
    }

    private void Render(DateTime from)
    {
        var now = DateTime.Now;
        var onTime = PowerHistory.OnTime(_events, from, now);
        var unexpected = _events.Count(e => e.Kind == PowerEventKind.Unexpected);
        var wakes = _events.Count(e => e.Kind == PowerEventKind.Wake);

        var healthy = unexpected == 0;
        _summaryBadge.Tint(healthy ? "Green" : "Red");
        if (_summaryBadge.Child is Icon icon) icon.Glyph = healthy ? "chk" : "warn";
        if (_summaryBadge.IsLoaded) Motion.Pop(_summaryBadge);
        _summaryTitle.Text = healthy
            ? L.T("این هفته همه‌ی خاموش‌شدن‌ها سالم بوده", "Every shutdown this week was clean")
            : L.T($"این هفته {Fa.Digits(unexpected.ToString())} بار ناگهانی خاموش شده", $"{unexpected} unexpected shutdown(s) this week");
        _summaryText.Text = healthy
            ? L.T("سیستم هیچ بار ناگهانی خاموش نشده.", "The PC never went down unexpectedly.")
            : L.T("قطع برق، هنگ کردن یا نگه‌داشتن دکمه‌ی پاور می‌تونه دلیلش باشه.", "A power cut, a freeze or holding the power button can cause this.");
        _summaryPills.Children.Clear();
        _summaryPills.Children.Add(PillText(L.T($"خاموشی ناگهانی {Fa.Digits(unexpected.ToString())}", $"Unexpected {unexpected}"), healthy ? "Accent" : "Red"));
        var wakePill = PillText(L.T($"بیداری از خواب {Fa.Digits(wakes.ToString())}", $"Wakes {wakes}"), "Accent");
        wakePill.Margin = new Thickness(8, 0, 0, 0);
        _summaryPills.Children.Add(wakePill);
        if (_summaryPills.IsLoaded)
        {
            CMo.Flash(_summaryTitle);
            CMo.Flash(_summaryText);
            CMo.Appear(_summaryPills.Children[0] as FrameworkElement ?? _summaryPills, 0, 8, 0.45);
            CMo.Appear(wakePill, 1, 8, 0.45);
        }

        _dayTiles.Children.Clear();
        for (var i = 0; i < DaysShown; i++)
        {
            var day = DateOnly.FromDateTime(now).AddDays(-i);
            var date = day.ToDateTime(TimeOnly.MinValue);
            var hours = onTime.GetValueOrDefault(day).TotalHours;
            var bad = _events.Any(e => e.Kind == PowerEventKind.Unexpected && DateOnly.FromDateTime(e.Time) == day);

            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = Fa.RelativeDay(date, now), FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = Fa.Date(date), FontSize = 11.5, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis });
            var mark = new IconBadge(bad ? "warn" : "chk", bad ? "HueE" : "HueB", 32, 17, 10) { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(mark);
            var off = hours < 0.05;
            content.Children.Add(new TextBlock
            {
                Text = off ? L.T("خاموش", "Off") : hours < 1 ? Fa.Duration(TimeSpan.FromMinutes(Math.Round(hours * 60))) : L.T($"{Fa.N(hours, hours < 10 ? 1 : 0)} ساعت", $"{Fa.N(hours, hours < 10 ? 1 : 0)} h"),
                FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
                Foreground = Res("Text"),
            });
            content.Children.Add(new TextBlock { Text = off ? L.T("بود", "all day") : L.T("روشن بود", "powered on"), FontSize = 11.5, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center });

            var tile = new RadioButton
            {
                Style = St("Tile"),
                GroupName = "historyDay",
                Margin = new Thickness(5),
                Padding = new Thickness(6, 14, 6, 14),
                Content = content,
                IsChecked = day == _selected,
            };
            tile.Checked += (_, _) =>
            {
                _selected = day;
                RenderDay();
                if (tile.IsLoaded) Motion.Pop(tile);
            };
            _dayTiles.Children.Add(tile);
            if (_dayTiles.IsLoaded) CMo.Appear(tile, i, 14, 0.55);
        }
        RenderDay(animate: false);
    }

    private static (string icon, string tint) Look(PowerEventKind kind) => kind switch
    {
        PowerEventKind.Unexpected => ("warn", "HueE"),
        PowerEventKind.Boot => ("pw", "HueB"),
        PowerEventKind.Wake => ("sun", "HueB"),
        PowerEventKind.Sleep or PowerEventKind.Hibernate => ("moon", "HueA"),
        PowerEventKind.Restart => ("refresh", "HueC"),
        _ => ("pw", "HueC"),
    };

    private void RenderDay(bool animate = true)
    {
        var now = DateTime.Now;
        var date = _selected.ToDateTime(TimeOnly.MinValue);
        _dayTitle.Text = $"{Fa.RelativeDay(date, now)}{L.Comma}{Fa.Date(date)}";
        _timeline.Children.Clear();

        var events = _events.Where(e => !e.Hidden && DateOnly.FromDateTime(e.Time) == _selected).ToList();
        if (events.Count == 0)
        {
            var none = new TextBlock { Text = L.T("این روز هیچ روشن و خاموش شدنی ثبت نشده.", "No power events were recorded on this day."), Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            _timeline.Children.Add(none);
            if (animate && _timeline.IsLoaded) CMo.Appear(none, 0, 8, 0.45);
            return;
        }

        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            var (icon, tint) = Look(ev.Kind);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            row.Children.Add(new TextBlock
            {
                Text = Fa.Clock(ev.Time), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Res("Text"), Margin = new Thickness(0, 5, 0, 0),
                FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = Ui.Flow == FlowDirection.RightToLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            });

            // badge + connecting line to the next event
            var rail = new Grid();
            if (i < events.Count - 1)
                rail.Children.Add(new Rectangle { Width = 2, Fill = Res("Line"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 34, 0, -4) });
            rail.Children.Add(new IconBadge(icon, tint, 30, 16, 9) { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetColumn(rail, 1);
            row.Children.Add(rail);

            var text = new StackPanel { Margin = new Thickness(12, 3, 0, 16) };
            text.Children.Add(new TextBlock { Text = ev.Title, Style = St("H2"), FontSize = 14 });
            if (ev.Detail.Length > 0)
                text.Children.Add(new TextBlock { Text = ev.Detail, Style = St("Label"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 2);
            row.Children.Add(text);

            _timeline.Children.Add(row);
            if (animate && _timeline.IsLoaded) CMo.Appear(row, i, 10, 0.5);
        }
    }

    private void ShowBlockers(SleepBlockers.Result result)
    {
        _blockerPanel.Children.Clear();
        if (result.NeedsAdmin)
        {
            var button = new Button { Style = St("Btn"), Content = L.T("نشون بده", "Show"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            button.Click += (_, _) =>
            {
                button.IsEnabled = false;
                Task.Run(() =>
                {
                    var elevated = SleepBlockers.ReadElevated();
                    Dispatcher.BeginInvoke(() => ShowBlockers(elevated));
                });
            };
            var row = Row("lock", "HueA", L.T("این فهرست دسترسی ادمین می‌خواد", "This list needs administrator rights"),
                result.Error ?? L.T("ویندوز این اطلاعات رو فقط به ادمین می‌ده. با زدن دکمه، یک بار ازت اجازه می‌گیره.", "Windows only gives this to admins. The button asks for permission once."));
            _blockerPanel.Children.Add(row);
            _blockerPanel.Children.Add(button);
            if (_blockerPanel.IsLoaded) { CMo.Appear(row, 0); CMo.Appear(button, 1); }
            return;
        }

        if (result.Items.Count == 0)
        {
            var clear = Row("chk", "HueB", L.T("هیچ‌چی جلوی خواب رو نگرفته", "Nothing is keeping it awake"),
                L.T("الان اگه سیستم بی‌استفاده بمونه، طبق تنظیمات ویندوز می‌خوابه.", "If the PC sits idle now, it sleeps according to the Windows settings."));
            _blockerPanel.Children.Add(clear);
            if (_blockerPanel.IsLoaded) CMo.Appear(clear, 0);
            return;
        }

        var n = 0;
        foreach (var item in result.Items)
        {
            if (_blockerPanel.Children.Count > 0) _blockerPanel.Children.Add(Divider());
            var row = Row(item.Kind == "DRIVER" ? "cpu" : "bolt", "HueC", item.Name,
                item.Reason.Length > 0 ? $"{item.CategoryText} · {item.Reason}" : item.CategoryText, PillText(item.KindText, "Accent"));
            _blockerPanel.Children.Add(row);
            if (_blockerPanel.IsLoaded) CMo.Appear(row, n++);
        }
    }
}
