using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class WakePage : PageBase
{
    private readonly Func<List<DayOfWeek>> _days;
    private bool _checked;

    public override string PageTitle => L.T("بیدارباش", "Wake-up");
    public override string PageSubtitle => L.T("سیستم رو شب بخوابون و صبح قبل از اینکه بهش برسی آماده باشه.", "Put the PC to sleep at night and have it ready before you get to it in the morning.");

    public WakePage()
    {
        InitializeComponent();
        _days = DayToggles(DayPanel);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (!int.TryParse(Latin(HourBox.Text), out var hour) || hour is < 0 or > 23 ||
            !int.TryParse(Latin(MinuteBox.Text), out var minute) || minute is < 0 or > 59)
        {
            ShowError(L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59."));
            return;
        }
        var days = _days();
        if (Hub.State.WakeSchedules.Any(w => w.Hour == hour && w.Minute == minute && w.Days.Order().SequenceEqual(days.Order())))
        {
            ShowError(L.T("همین بیدارباش رو قبلاً ساختی.", "You already have this wake-up."));
            return;
        }
        if (Hub.AddWake(new WakeSchedule { Hour = hour, Minute = minute, Days = days }) is { } error)
            ShowError(L.T($"ویندوز این زمان‌بندی رو ثبت نکرد: {error}", $"Windows did not accept this schedule: {error}"));
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    public override void Refresh()
    {
        ListPanel.Children.Clear();
        var now = DateTime.Now;
        foreach (var w in Hub.State.WakeSchedules.OrderBy(w => Schedule.Next(w.Hour, w.Minute, w.Days, now)))
        {
            if (ListPanel.Children.Count > 0) ListPanel.Children.Add(Divider());
            var next = Schedule.Next(w.Hour, w.Minute, w.Days, now);
            var id = w.Id;
            ListPanel.Children.Add(Row("", "Green",
                L.T($"ساعت {Fa.Clock(w.Hour, w.Minute)} · {Fa.Days(w.Days)}", $"{Fa.Clock(w.Hour, w.Minute)} · {Fa.Days(w.Days)}"),
                L.T($"بعدی: {Fa.RelativeDay(next, now)} {Fa.Date(next)}، {Fa.Duration(next - now)} دیگه", $"Next: {Fa.RelativeDay(next, now)}, {Fa.Date(next)}, in {Fa.Duration(next - now)}"),
                IconButton("", L.T("حذف", "Remove"), () => Hub.RemoveWake(id))));
        }
        if (ListPanel.Children.Count == 0)
            ListPanel.Children.Add(new TextBlock { Text = L.T("هنوز بیدارباشی نساخته‌ای.", "No wake-ups yet."), Style = St("Body"), Margin = new Thickness(0, 8, 0, 8) });

        if (!_checked)
        {
            _checked = true;
            CheckWakeTimers();
        }
    }

    /// <summary>Wake tasks silently do nothing when the power plan has wake timers turned off.</summary>
    private void CheckWakeTimers()
    {
        Task.Run(() =>
        {
            var enabled = WakeScheduler.WakeTimersEnabled();
            Dispatcher.BeginInvoke(() => ShowWakeTimers(enabled));
        });
    }

    private void ShowWakeTimers(bool? enabled)
    {
        TimerPanel.Children.Clear();
        TimerCard.Visibility = Visibility.Visible;
        switch (enabled)
        {
            case true:
                TimerPanel.Children.Add(Row("", "Green", L.T("تایمر بیداری ویندوز روشنه", "Windows wake timers are on"), L.T("همه‌چی برای بیدارباش آماده‌ست.", "Everything is ready for wake-ups.")));
                break;
            case false:
                var fix = new Button { Style = St("BtnPrimary"), Content = L.T("روشنش کن", "Turn it on") };
                fix.Click += (_, _) =>
                {
                    fix.IsEnabled = false;
                    Task.Run(() =>
                    {
                        var ok = Hub.Demo || WakeScheduler.EnableWakeTimers();
                        var now = ok ? WakeScheduler.WakeTimersEnabled() : false;
                        Dispatcher.BeginInvoke(() =>
                        {
                            ShowWakeTimers(now);
                            if (now != true)
                                ShowError(L.T("ویندوز اجازه نداد. از Control Panel ← Power Options ← Change advanced power settings ← Sleep ← Allow wake timers روشنش کن.",
                                    "Windows refused. Turn it on under Control Panel → Power Options → Change advanced power settings → Sleep → Allow wake timers."));
                        });
                    });
                };
                TimerPanel.Children.Add(Row("", "Gold", L.T("تایمر بیداری ویندوز خاموشه", "Windows wake timers are off"),
                    L.T("تا وقتی «Allow wake timers» توی تنظیمات برق ویندوز خاموش باشه، سیستم سر ساعت بیدار نمی‌شه.", "While “Allow wake timers” is off in the Windows power settings, the PC will not wake on time."), fix));
                break;
            default:
                TimerCard.Visibility = Visibility.Collapsed;
                break;
        }
    }
}
