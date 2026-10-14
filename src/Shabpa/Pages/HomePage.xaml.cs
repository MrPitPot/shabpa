using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class HomePage : PageBase
{
    private PowerAction _action = PowerAction.Shutdown;
    private DateTime? _onSince;
    private ProgressBar? _ruleBar;
    private TextBlock? _ruleText;
    private string _ramNote = "";

    public override string PageTitle => L.T("خانه", "Home");
    public override string PageSubtitle => L.T("یه نگاه سریع به وضعیت سیستم و برنامه‌هایی که در راهن.", "A quick look at the PC and what is coming up.");

    public HomePage()
    {
        InitializeComponent();
        Chips(ActionChips,
            [(PowerAction.Shutdown, L.T("خاموش بشه", "Shut down")), (PowerAction.Sleep, L.T("بخوابه", "Sleep")), (PowerAction.Hibernate, L.T("هایبرنیت بشه", "Hibernate"))],
            _action, a => _action = a);

        Hub.MemoryCleaned += (result, cache) =>
        {
            RamButton.IsEnabled = true;
            if (cache) return;
            _ramNote = L.T($"{Fa.Bytes(result.Freed)} رم آزاد شد.", $"{Fa.Bytes(result.Freed)} of RAM freed.");
            Refresh();
        };

        // When the machine last came on: boot or resume, whichever is later.
        Task.Run(() =>
        {
            var last = PowerHistory.Read(DateTime.Now.AddDays(-14)).LastOrDefault(e => e.TurnsOn);
            Dispatcher.BeginInvoke(() =>
            {
                _onSince = last?.Time ?? DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
                OnSecond();
            });
        });
    }

    public override void Refresh()
    {
        var now = DateTime.Now;
        Greeting.Text = now.Hour switch
        {
            >= 5 and < 12 => L.T("صبح بخیر", "Good morning"),
            >= 12 and < 17 => L.T("ظهر بخیر", "Good afternoon"),
            >= 17 and < 21 => L.T("عصر بخیر", "Good evening"),
            _ => L.T("شب بخیر", "Good night"),
        };
        DateText.Text = L.T($"{Fa.DayName(now.DayOfWeek)} {Fa.Date(now)} · ساعت {Fa.Clock(now)}", $"{Fa.DayName(now.DayOfWeek)}, {Fa.Date(now)} · {Fa.Clock(now)}");

        NextPanel.Children.Clear();
        _ruleBar = null;
        _ruleText = null;

        foreach (var cmd in Hub.Queue.Items.OrderBy(c => c.DueAt).Take(3))
        {
            if (NextPanel.Children.Count > 0) NextPanel.Children.Add(Divider());
            NextPanel.Children.Add(Row(ActionGlyph(cmd.Action), "Accent", Fa.ActionName(cmd.Action), When(cmd, now),
                new TextBlock { Text = Fa.Countdown(cmd.DueAt - now), Style = St("H2"), Foreground = Res("Gold"), VerticalAlignment = VerticalAlignment.Center, Tag = cmd.DueAt }));
        }

        if (Hub.Engine.Rule is { } rule)
        {
            if (NextPanel.Children.Count > 0) NextPanel.Children.Add(Divider());
            _ruleBar = new ProgressBar { Style = St("Bar"), Width = 110, VerticalAlignment = VerticalAlignment.Center };
            var row = Row("", "Gold", L.T($"خاموشی شرطی: {Fa.ActionName(rule.Action)}", $"Conditional rule: {Fa.ActionName(rule.Action)}"), Hub.Engine.Status.Text, _ruleBar);
            _ruleText = ((StackPanel)row.Children[1]).Children.OfType<TextBlock>().LastOrDefault();
            NextPanel.Children.Add(row);
            _ruleBar.Value = Hub.Engine.Status.Progress;
        }

        if (Hub.NextWake(now) is { } wake)
        {
            if (NextPanel.Children.Count > 0) NextPanel.Children.Add(Divider());
            NextPanel.Children.Add(Row("", "Green", L.T("بیدارباش", "Wake-up"), At(wake, now)));
        }

        if (NextPanel.Children.Count == 0)
            NextPanel.Children.Add(new TextBlock
            {
                Text = L.T("هیچ برنامه‌ای در راه نیست. از میان‌برهای پایین یا «کنترل سیستم» یکی بساز.", "Nothing is scheduled. Create something with the shortcuts below or in “System control”."),
                Style = St("Body"),
                Margin = new Thickness(0, 8, 0, 8),
            });

        var today = Hub.Energy.Summarize(now).Days[^1];
        var energy = today.TotalWh > 1
            ? L.T($"امروز تا الان {Fa.N(today.TotalWh / 1000, 2)} کیلووات‌ساعت مصرف شده، حدود {Fa.N((long)(today.TotalWh / 1000 * Hub.Settings.Tariff))} تومان.",
                $"Today so far: {Fa.N(today.TotalWh / 1000, 2)} kWh, about {Fa.N((long)(today.TotalWh / 1000 * Hub.Settings.Tariff))} Toman.")
            : "";
        TodayText.Text = string.Join("  ", new[] { _ramNote, energy }.Where(t => t.Length > 0));
    }

    /// <summary>«فردا ساعت ۰۱:۳۰» / "Tomorrow at 01:30".</summary>
    internal static string At(DateTime when, DateTime now) =>
        L.T($"{Fa.RelativeDay(when, now)} ساعت {Fa.Clock(when)}", $"{Fa.RelativeDay(when, now)} at {Fa.Clock(when)}");

    internal static string When(ScheduledCommand cmd, DateTime now)
    {
        var text = At(cmd.DueAt, now);
        if (cmd.Repeats) text += L.T($" · تکرار: {Fa.Days(cmd.RepeatDays)}", $" · repeats: {Fa.Days(cmd.RepeatDays)}");
        if (Hub.SourceText(cmd.Source) is { Length: > 0 } source) text += $" · {source}";
        return text;
    }

    public override void OnTick(SensorSnapshot s)
    {
        WattsText.Text = s.TotalWatts > 0 ? $"{s.TotalWatts:0} W" : "—";
        WattsSub.Text = L.T($"پردازنده {Fa.N(s.CpuWatts, 0)} · گرافیک {Fa.N(s.GpuWatts, 0)} وات", $"CPU {Fa.N(s.CpuWatts, 0)} · GPU {Fa.N(s.GpuWatts, 0)} W");
        NetText.Text = $"↓ {(s.DownMbps >= 10 ? $"{s.DownMbps:0}" : $"{s.DownMbps:0.0}")} Mb";
        NetSub.Text = L.T($"آپلود {Fa.Speed(s.UpMbps)} مگابیت بر ثانیه", $"Upload {Fa.Speed(s.UpMbps)} Mbit/s");
        CpuText.Text = $"{s.CpuLoad:0}%";
        CpuBar.Value = s.CpuLoad;
        var memory = Hub.Memory;
        RamText.Text = memory.Total > 0 ? $"{memory.UsedPercent:0}%" : "—";
        RamBar.Value = memory.UsedPercent;

        if (_ruleBar is not null)
        {
            _ruleBar.Value = Hub.Engine.Status.Progress;
            if (_ruleText is not null) _ruleText.Text = Hub.Engine.Status.Text;
        }
    }

    public override void OnSecond()
    {
        var now = DateTime.Now;
        if (_onSince is { } since) UptimeText.Text = Fa.Duration(now - since);
        foreach (var row in NextPanel.Children.OfType<Grid>())
            if (row.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag is DateTime) is { } label)
                label.Text = Fa.Countdown((DateTime)label.Tag - now);
    }

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        var minutes = int.Parse((string)((Button)sender).Tag);
        Hub.AddCommand(_action, DateTime.Now.AddMinutes(minutes));
    }

    private void OnAfterDownload(object sender, RoutedEventArgs e) =>
        Hub.ActivateRule(new Rule { Kind = RuleKind.Network, ThresholdMbps = 1, HoldMinutes = 5, WaitForStart = true, Action = _action });

    private void OnWhenIdle(object sender, RoutedEventArgs e) =>
        Hub.ActivateRule(new Rule { Kind = RuleKind.Idle, HoldMinutes = 30, Action = _action });

    private void OnLock(object sender, RoutedEventArgs e) => Hub.RequestAction(PowerAction.Lock, "", 0);

    private void OnFreeRam(object sender, RoutedEventArgs e)
    {
        RamButton.IsEnabled = false;
        Hub.CleanMemory(cache: false);
    }
}
