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

        IsVisibleChanged += (_, _) => OnVisible();

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
        Greeting.Text = Greetings.Pick(now);
        DateText.Text = L.T($"{Fa.DayName(now.DayOfWeek)} {Fa.Date(now)} · ساعت {Fa.Clock(now)}", $"{Fa.DayName(now.DayOfWeek)}, {Fa.Date(now)} · {Fa.Clock(now)}");

        NextPanel.Children.Clear();
        _ruleBar = null;
        _ruleText = null;

        foreach (var cmd in Hub.Queue.Items.OrderBy(c => c.DueAt).Take(4))
        {
            if (NextPanel.Children.Count > 0) NextPanel.Children.Add(Divider());
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            trailing.Children.Add(new TextBlock
            {
                Text = Fa.Countdown(cmd.DueAt - now), Style = St("H2"), Foreground = Res("Gold"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0), Tag = cmd.DueAt,
            });
            var id = cmd.Id;
            var due = cmd.DueAt;
            var action = cmd.Action;
            trailing.Children.Add(IconButton("\uE74D", L.T("حذف", "Delete"), () => DeleteCommand(id, action, due)));
            NextPanel.Children.Add(Row(ActionGlyph(cmd.Action), "Accent", Fa.ActionName(cmd.Action), When(cmd, now), trailing));
        }

        if (Hub.Engine.Rule is { } rule)
        {
            if (NextPanel.Children.Count > 0) NextPanel.Children.Add(Divider());
            _ruleBar = new ProgressBar { Style = St("Bar"), Width = 110, VerticalAlignment = VerticalAlignment.Center };
            var ruleTrailing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ruleTrailing.Children.Add(_ruleBar);
            ruleTrailing.Children.Add(new Border { Width = 10 });
            ruleTrailing.Children.Add(IconButton("", L.T("غیرفعال کردن", "Turn off"), () => DeleteRule(rule.Action)));
            var row = Row("", "Gold", L.T($"خاموشی شرطی: {Fa.ActionName(rule.Action)}", $"Conditional rule: {Fa.ActionName(rule.Action)}"), Hub.Engine.Status.Text, ruleTrailing);
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
        L.T($"{Fa.DayOf(when, now)} ساعت {Fa.Clock(when)}", $"{Fa.DayOf(when, now)} at {Fa.Clock(when)}");

    internal static string When(ScheduledCommand cmd, DateTime now)
    {
        var text = L.T("اجرا: ", "Runs: ") + At(cmd.DueAt, now);
        if (cmd.Repeats) text += L.T($" · تکرار: {Fa.Days(cmd.RepeatDays)}", $" · repeats: {Fa.Days(cmd.RepeatDays)}");
        if (Hub.SourceText(cmd.Source) is { Length: > 0 } source) text += $" · {source}";
        return text;
    }

    public override void OnTick(SensorSnapshot s)
    {
        WattsText.Text = s.TotalWatts > 0 ? $"{s.TotalWatts:0} W" : "";
        ShowLive();

        if (_ruleBar is not null)
        {
            _ruleBar.Value = Hub.Engine.Status.Progress;
            if (_ruleText is not null) _ruleText.Text = Hub.Engine.Status.Text;
        }
    }

    private static string CleanGpuName(string name) =>
        string.Join(' ', name.Replace("(R)", "").Replace("(TM)", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("AMD" or "NVIDIA" or "Radeon" or "GeForce" or "Intel" or "Graphics")));

    private static System.Windows.Media.Brush Tint(string key, double value, double warn, double bad) =>
        Res(value >= bad ? "Red" : value >= warn ? "Gold" : key);

    /// <summary>The four live cards: graphics card, processor, memory and network.</summary>
    private void ShowLive()
    {
        var s = Hub.Demo ? OverlayMetrics.Demo(DateTime.Now) : Hub.Telemetry.Latest;
        var g = s.Gpu;

        // graphics card
        if (g.Name.Length == 0 && !Hub.Demo)
        {
            GpuBig.Text = "—";
            GpuUnit.Text = "";
            GpuSide.Text = "";
            GpuNote.Text = L.T("کارت گرافیک خونده نشد", "Graphics card not read");
        }
        else
        {
            GpuBig.Text = g.TempC is { } t ? $"{t:0}" : g.LoadPercent is { } l0 ? $"{l0:0}" : "—";
            GpuUnit.Text = g.TempC is not null ? "°C" : "%";
            GpuSide.Text = g.TempC is not null && g.LoadPercent is { } l ? $"{l:0}%" : "";
            GpuBig.Foreground = g.TempC is { } temp ? Tint("Green", temp, 75, 88) : Res("Green");
            GpuNote.Text = CleanGpuName(g.Name) + (g.VramTotalMb is { } vram ? $" · {vram / 1024:0} GB" : "");
        }
        GpuBar.Value = g.LoadPercent ?? 0;

        // processor
        var temperature = s.CpuTempC;
        CpuBig.Text = temperature is { } ct ? $"{ct:0}" : $"{s.CpuLoad:0}";
        CpuUnit.Text = temperature is not null ? "°C" : "%";
        CpuSide.Text = temperature is not null ? $"{s.CpuLoad:0}%" : "";
        CpuBig.Foreground = temperature is { } cpuTemp ? Tint("Green", cpuTemp, 75, 90) : Tint("Green", s.CpuLoad, 75, 92);
        CpuBar.Value = s.CpuLoad;
        CpuNote.Text = Telemetry.CpuName() + (s.CpuMHz is { } mhz ? $" · {mhz / 1000:0.0} GHz" : "");

        // memory
        var memory = Hub.Demo ? new MemoryInfo(32L << 30, 14L << 30, 0) : Hub.Memory;
        if (memory.Total > 0)
        {
            RamBig.Text = $"{memory.Used / 1073741824.0:0.0}";
            RamUnit.Text = $"/ {memory.Total / 1073741824.0:0} GB";
            RamSide.Text = "";
            RamBar.Value = memory.UsedPercent;
            RamBig.Foreground = Tint("Green", memory.UsedPercent, 75, 92);
            RamNote.Text = L.T($"{memory.UsedPercent:0}٪ استفاده شده", $"{memory.UsedPercent:0}% used");
        }

        // network
        NetBig.Text = s.DownMbps >= 10 ? $"{s.DownMbps:0}" : $"{s.DownMbps:0.0}";
        NetUnit.Text = "Mb/s ↓";
        NetSide.Text = "";
        NetBar.Value = Math.Min(100, s.DownMbps);
        NetNote.Text = L.T($"آپلود {(s.UpMbps >= 10 ? $"{s.UpMbps:0}" : $"{s.UpMbps:0.0}")} Mb/s", $"Up {(s.UpMbps >= 10 ? $"{s.UpMbps:0}" : $"{s.UpMbps:0.0}")} Mb/s");
        NetNote.FlowDirection = Flow;
    }

    private bool _acquired;

    private void OnVisible()
    {
        if (IsVisible && !_acquired && !Hub.Demo)
        {
            _acquired = true;
            Hub.Telemetry.Acquire();
        }
        else if (!IsVisible && _acquired)
        {
            _acquired = false;
            Hub.Telemetry.Release();
        }
    }

    public override void OnSecond()
    {
        var now = DateTime.Now;
        if (_onSince is { } since) UptimeText.Text = Fa.Duration(now - since);
        ShowLive();
        foreach (var row in NextPanel.Children.OfType<Grid>())
            if (row.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag is DateTime) is { } label)
                label.Text = Fa.Countdown((DateTime)label.Tag - now);
    }

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        var minutes = int.Parse((string)((Button)sender).Tag);
        var now = DateTime.Now;
        var due = now.AddMinutes(minutes);
        var seconds = Hub.Settings.CountdownSeconds;
        if (!Ask(L.T("این زمان‌بندی ثبت بشه؟", "Schedule this?"),
                L.T($"{Fa.ActionSentence(_action)}. {Fa.Digits(seconds.ToString())} ثانیه قبلش شمارش معکوس می‌بینی و می‌تونی لغوش کنی. هر وقت خواستی از لیست «بعدی چیه؟» با دکمه‌ی حذف پاکش کن.",
                    $"Then {Fa.ActionSentence(_action)}. You get a {seconds}-second countdown first and can cancel. Delete it any time from the “What's next?” list."),
                L.T("تایید و ثبت", "Confirm and schedule"),
                detail: L.T($"شروع: {At(due, now)} ({Fa.Duration(due - now)} دیگه)", $"Runs: {At(due, now)} (in {Fa.Duration(due - now)})")))
            return;
        if (Hub.AddCommand(_action, due))
            Hub.Notify(L.T("ثبت شد", "Scheduled"), L.T($"{Fa.ActionSentence(_action)} · {At(due, now)}", $"{Fa.ActionSentence(_action)} · {At(due, now)}"));
        else
            Hub.Notify(L.T("از قبل ثبت شده بود", "Already scheduled"), L.T("همین دستور با همین زمان قبلاً توی لیسته؛ دوباره اضافه نشد.", "The same command at about the same time is already in the list; nothing was added."));
    }

    private void OnAfterDownload(object sender, RoutedEventArgs e)
    {
        if (!Ask(L.T("خاموشی بعد از دانلود فعال بشه؟", "Turn on shutdown-after-download?"),
                L.T($"شب‌پا از همین الان سرعت اینترنت رو زیر نظر می‌گیره. وقتی دانلودی شروع بشه و بعدش ۵ دقیقه سرعت زیر ۱ مگابیت بمونه، {Fa.ActionSentence(_action)} (با شمارش معکوس قابل لغو). تا وقتی دانلودی شروع نشه هیچ اتفاقی نمی‌افته.",
                    $"Shabpa starts watching your connection right now. Once a download has started and the speed then stays under 1 Mbit for 5 minutes, {Fa.ActionSentence(_action)} (with a cancellable countdown). Nothing happens until a download has started."),
                L.T("تایید و فعال کن", "Confirm and turn on"),
                detail: L.T("شروع نظارت: همین الان · هر وقت خواستی از لیست «بعدی چیه؟» غیرفعالش کن", "Watching starts: right now · turn it off any time from the “What's next?” list")))
            return;
        Hub.ActivateRule(new Rule { Kind = RuleKind.Network, ThresholdMbps = 1, HoldMinutes = 5, WaitForStart = true, Action = _action });
    }

    private void OnWhenIdle(object sender, RoutedEventArgs e)
    {
        if (!Ask(L.T("خاموشی بعد از بی‌استفاده موندن فعال بشه؟", "Turn on shutdown-when-idle?"),
                L.T($"اگه ۳۰ دقیقه پشت سر هم موس و کیبورد دست نخوره، {Fa.ActionSentence(_action)} (با شمارش معکوس قابل لغو). با هر حرکت موس شمارش از اول شروع می‌شه.",
                    $"If the mouse and keyboard stay untouched for 30 minutes in a row, {Fa.ActionSentence(_action)} (with a cancellable countdown). Any movement restarts the count."),
                L.T("تایید و فعال کن", "Confirm and turn on"),
                detail: L.T("شروع شمارش: همین الان · هر وقت خواستی از لیست «بعدی چیه؟» غیرفعالش کن", "Counting starts: right now · turn it off any time from the “What's next?” list")))
            return;
        Hub.ActivateRule(new Rule { Kind = RuleKind.Idle, HoldMinutes = 30, Action = _action });
    }

    private void OnLock(object sender, RoutedEventArgs e)
    {
        if (!Ask(L.T("صفحه قفل بشه؟", "Lock the screen?"),
                L.T("صفحه همین الان قفل می‌شه و برای ورود باید رمز یا پین بزنی. برنامه‌ها و دانلودها بازمی‌مونن.", "The screen locks right now and you will need your password or PIN. Apps and downloads keep running."),
                L.T("قفل کن", "Lock now"), detail: L.T("شروع: همین الان", "Starts: right now")))
            return;
        Hub.RequestAction(PowerAction.Lock, "", 0);
    }

    private void DeleteCommand(Guid id, PowerAction action, DateTime due)
    {
        var now = DateTime.Now;
        if (!Ask(L.T("این دستور حذف بشه؟", "Delete this command?"),
                L.T("برای همیشه از لیست پاک می‌شه و اجرا نمی‌شه؛ هیچ شمارش معکوسی هم نمی‌بینی.", "It is removed from the list for good and will not run; no countdown will appear either."),
                L.T("حذف کن", "Delete"), danger: true,
                detail: $"{Fa.ActionName(action)} · {At(due, now)}"))
            return;
        Hub.RemoveCommand(id);
    }

    private void DeleteRule(PowerAction action)
    {
        if (!Ask(L.T("خاموشی شرطی غیرفعال بشه؟", "Turn this rule off?"),
                L.T("دیگه چیزی زیر نظر گرفته نمی‌شه و سیستم به خاطر این قانون چیزی اجرا نمی‌کنه.", "Nothing is watched any more and the PC will not act because of this rule."),
                L.T("غیرفعال کن", "Turn off"), danger: true, detail: Fa.ActionName(action)))
            return;
        Hub.DeactivateRule();
    }

    private void OnFreeRam(object sender, RoutedEventArgs e)
    {
        if (!Ask(L.T("رم خالی بشه؟", "Free up RAM?"),
                L.T("حافظه‌ی بلااستفاده‌ی برنامه‌ها برداشته می‌شه. برنامه‌ها بسته نمی‌شن و چیزی ذخیره‌نشده از بین نمی‌ره؛ فقط ممکنه چند ثانیه کند باشن.",
                    "Unused memory is taken back from apps. Nothing is closed and no unsaved work is lost; apps may just feel slow for a few seconds."),
                L.T("خالی کن", "Free it")))
            return;
        RamButton.IsEnabled = false;
        Hub.CleanMemory(cache: false);
    }
}
