using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class HistoryPage : PageBase
{
    private const int DaysShown = 7;

    private List<PowerEvent> _events = [];
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Now);
    private bool _loaded;

    public override string PageTitle => L.T("خواب و بیداری", "Sleep and wake history");
    public override string PageSubtitle => L.T("سیستم کِی روشن و خاموش شده، چرا، و چی جلوی خوابیدنش رو می‌گیره.", "When the PC turned on and off, why, and what keeps it from sleeping.");

    public HistoryPage() => InitializeComponent();

    public override void Refresh()
    {
        if (_loaded) return;
        _loaded = true;
        Load();
    }

    private void OnReload(object sender, RoutedEventArgs e) => Load();

    private void Load()
    {
        SummaryTitle.Text = L.T("در حال خوندن تاریخچه‌ی ویندوز…", "Reading the Windows history…");
        SummaryText.Text = "";
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
        var tint = healthy ? "Green" : "Red";
        SummaryBadge.Background = Res(tint + "Soft");
        SummaryIcon.Foreground = Res(tint);
        SummaryIcon.Text = healthy ? "" : "";
        SummaryTitle.Text = healthy
            ? L.T("این هفته همه‌ی خاموش‌شدن‌ها سالم بوده", "Every shutdown this week was clean")
            : L.T($"این هفته {Fa.Digits(unexpected.ToString())} بار ناگهانی خاموش شده", $"{unexpected} unexpected shutdown(s) this week");
        SummaryText.Text = healthy
            ? L.T("سیستم هیچ بار ناگهانی خاموش نشده.", "The PC never went down unexpectedly.")
            : L.T("قطع برق، هنگ کردن یا نگه‌داشتن دکمه‌ی پاور می‌تونه دلیلش باشه.", "A power cut, a freeze or holding the power button can cause this.");
        SummaryPills.Children.Clear();
        SummaryPills.Children.Add(PillText(L.T($"خاموشی ناگهانی {Fa.Digits(unexpected.ToString())}", $"Unexpected {unexpected}"), healthy ? "Accent" : "Red"));
        var wakePill = PillText(L.T($"بیداری از خواب {Fa.Digits(wakes.ToString())}", $"Wakes {wakes}"), "Accent");
        wakePill.Margin = new Thickness(8, 0, 0, 0);
        SummaryPills.Children.Add(wakePill);

        DayTiles.Children.Clear();
        for (var i = 0; i < DaysShown; i++)
        {
            var day = DateOnly.FromDateTime(now).AddDays(-i);
            var date = day.ToDateTime(TimeOnly.MinValue);
            var hours = onTime.GetValueOrDefault(day).TotalHours;
            var bad = _events.Any(e => e.Kind == PowerEventKind.Unexpected && DateOnly.FromDateTime(e.Time) == day);

            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = Fa.RelativeDay(date, now), FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = Fa.Date(date), FontSize = 11, Foreground = Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 10) });
            var mark = Badge(bad ? "" : "", bad ? "Red" : "Green", 30);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(mark);
            var off = hours < 0.05;
            content.Children.Add(new TextBlock
            {
                Text = off ? L.T("خاموش", "Off") : hours < 1 ? Fa.Duration(TimeSpan.FromMinutes(Math.Round(hours * 60))) : L.T($"{Fa.N(hours, hours < 10 ? 1 : 0)} ساعت", $"{Fa.N(hours, hours < 10 ? 1 : 0)} h"),
                FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
                Foreground = Res("Text"),
            });
            content.Children.Add(new TextBlock { Text = off ? L.T("بود", "all day") : L.T("روشن بود", "powered on"), FontSize = 11, Foreground = Res("Faint"), HorizontalAlignment = HorizontalAlignment.Center });

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
            };
            DayTiles.Children.Add(tile);
        }
        RenderDay();
    }

    private void RenderDay()
    {
        var now = DateTime.Now;
        var date = _selected.ToDateTime(TimeOnly.MinValue);
        DayTitle.Text = $"{Fa.RelativeDay(date, now)}{L.Comma}{Fa.Date(date)}";
        Timeline.Children.Clear();

        var events = _events.Where(e => !e.Hidden && DateOnly.FromDateTime(e.Time) == _selected).ToList();
        if (events.Count == 0)
        {
            Timeline.Children.Add(Txt(L.T("این روز هیچ روشن و خاموش شدنی ثبت نشده.", "No power events were recorded on this day.")));
            return;
        }

        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            var tint = ev.Kind switch
            {
                PowerEventKind.Unexpected => "Red",
                PowerEventKind.Boot or PowerEventKind.Wake => "Green",
                PowerEventKind.Sleep or PowerEventKind.Hibernate => "Accent",
                _ => "Gold",
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            row.Children.Add(new TextBlock { Text = Fa.Clock(ev.Time), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Res("Text"), Margin = new Thickness(0, 4, 0, 0) });

            // dot + connecting line
            var rail = new Grid();
            if (i < events.Count - 1)
                rail.Children.Add(new Rectangle { Width = 2, Fill = Res("Line"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, -4) });
            rail.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Res(tint), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) });
            Grid.SetColumn(rail, 1);
            row.Children.Add(rail);

            var text = new StackPanel { Margin = new Thickness(10, 3, 0, 14) };
            text.Children.Add(new TextBlock { Text = ev.Title, Style = St("H2"), FontSize = 14 });
            if (ev.Detail.Length > 0)
                text.Children.Add(new TextBlock { Text = ev.Detail, Style = St("Label"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 2);
            row.Children.Add(text);

            Timeline.Children.Add(row);
        }
    }

    private void ShowBlockers(SleepBlockers.Result result)
    {
        BlockerPanel.Children.Clear();
        if (result.NeedsAdmin)
        {
            var button = new Button { Style = St("Btn"), Content = L.T("نشون بده", "Show") };
            button.Click += (_, _) =>
            {
                button.IsEnabled = false;
                Task.Run(() =>
                {
                    var elevated = SleepBlockers.ReadElevated();
                    Dispatcher.BeginInvoke(() => ShowBlockers(elevated));
                });
            };
            BlockerPanel.Children.Add(Row("", "Accent", L.T("این فهرست دسترسی ادمین می‌خواد", "This list needs administrator rights"),
                result.Error ?? L.T("ویندوز این اطلاعات رو فقط به ادمین می‌ده. با زدن دکمه، یک بار ازت اجازه می‌گیره.", "Windows only gives this to admins. The button asks for permission once."), button));
            return;
        }

        if (result.Items.Count == 0)
        {
            BlockerPanel.Children.Add(Row("", "Green", L.T("هیچ‌چی جلوی خواب رو نگرفته", "Nothing is keeping it awake"),
                L.T("الان اگه سیستم بی‌استفاده بمونه، طبق تنظیمات ویندوز می‌خوابه.", "If the PC sits idle now, it sleeps according to the Windows settings.")));
            return;
        }

        foreach (var item in result.Items)
        {
            if (BlockerPanel.Children.Count > 0) BlockerPanel.Children.Add(Divider());
            BlockerPanel.Children.Add(Row(item.Kind == "DRIVER" ? "" : "", "Gold", item.Name,
                item.Reason.Length > 0 ? $"{item.CategoryText} · {item.Reason}" : item.CategoryText, PillText(item.KindText, "Accent")));
        }
    }
}
