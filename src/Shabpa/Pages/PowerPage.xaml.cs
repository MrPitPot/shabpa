using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class PowerPage : PageBase
{
    private enum Mode { Now, After, At }

    private readonly Func<List<DayOfWeek>> _days;
    private readonly DayPicker _day;
    private PowerAction _action = PowerAction.Shutdown;
    private Mode _mode = Mode.Now;
    private bool _ready;

    public override string PageTitle => L.T("دستور برق", "Power command");
    public override string PageSubtitle => L.T("همین حالا، چند دقیقه‌ی دیگه یا هر شب سر یه ساعت مشخص.", "Right now, in a few minutes, or every night at a set time.");

    public PowerPage()
    {
        InitializeComponent();
        _days = DayToggles(DayPanel, UpdateSummary);
        TimeBoxes.Wire(AtHour, AtMinute);
        _day = new DayPicker(() => TimeBoxes.TryRead(AtHour, AtMinute, out var h, out var m) ? (h, m) : null);
        _day.Changed += UpdateSummary;
        DayHost.Content = _day.View;
        Chips(ModeChips, [(Mode.Now, L.T("همین حالا", "Right now")), (Mode.After, L.T("چند وقت دیگه", "After a while")), (Mode.At, L.T("سر ساعت", "At a time"))], _mode, m =>
        {
            _mode = m;
            UpdateSummary();
        });
        _ready = true;
        UpdateSummary();
    }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        _action = Enum.Parse<PowerAction>((string)((RadioButton)sender).Tag);
        UpdateSummary();
    }

    private void OnInput(object sender, TextChangedEventArgs e) => UpdateSummary();

    private void OnQuick(object sender, RoutedEventArgs e)
    {
        var minutes = int.Parse((string)((Button)sender).Tag);
        AfterHours.Text = (minutes / 60).ToString();
        AfterMinutes.Text = (minutes % 60).ToString();
    }

    /// <summary>The moment the current form describes, or null if the input is not usable.</summary>
    private DateTime? Due(DateTime now)
    {
        switch (_mode)
        {
            case Mode.After:
                var span = TimeSpan.FromHours(Int(AfterHours, 0, 99, 0)) + TimeSpan.FromMinutes(Int(AfterMinutes, 0, 59, 0));
                return span > TimeSpan.Zero ? now + span : null;
            case Mode.At:
                if (!TimeBoxes.TryRead(AtHour, AtMinute, out var h, out var m)) return null;
                var days = _days();
                if (days.Count > 0) return Schedule.Next(h, m, days, now);
                // A one-time command runs on the picked day; a moment already gone is not moved to tomorrow.
                return _day.Value(now) is { } at && at > now ? at : null;
            default:
                return now;
        }
    }

    private void UpdateSummary()
    {
        if (!_ready) return;
        AfterPanel.Visibility = _mode == Mode.After ? Visibility.Visible : Visibility.Collapsed;
        AtPanel.Visibility = _mode == Mode.At ? Visibility.Visible : Visibility.Collapsed;
        DayRow.Visibility = _days().Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var now = DateTime.Now;
        _day.Update(now);
        var due = Due(now);
        Go.IsEnabled = due is not null;
        Go.Content = _mode == Mode.Now ? Fa.ActionName(_action) : L.T("زمان‌بندی کن", "Schedule it");

        if (due is null)
            Summary.Text = _mode == Mode.After
                ? L.T("یه مدت زمان وارد کن.", "Enter a duration.")
                : TimeBoxes.TryRead(AtHour, AtMinute, out _, out _)
                    ? L.T("این زمان گذشته؛ یه روز یا ساعت بعدتر انتخاب کن.", "That time has passed; pick a later day or time.")
                    : L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59.");
        else if (_mode == Mode.Now)
            Summary.Text = L.T($"{Fa.ActionSentence(_action)}؛ قبلش ۵ ثانیه فرصت لغو داری.", $"Then {Fa.ActionSentence(_action)}; you get 5 seconds to cancel first.");
        else
        {
            var days = _mode == Mode.At ? _days() : [];
            var repeat = days.Count > 0 ? L.T($" و بعد از اون {Fa.Days(days)} تکرار می‌شه", $", then repeats {Fa.Days(days)}") : "";
            Summary.Text = L.T(
                $"{Fa.DayOf(due.Value, now)} ساعت {Fa.Clock(due.Value)} ({Fa.Duration(due.Value - now)} دیگه) {Fa.ActionSentence(_action)}{repeat}.",
                $"{Fa.DayOf(due.Value, now)} at {Fa.Clock(due.Value)} (in {Fa.Duration(due.Value - now)}) {Fa.ActionSentence(_action)}{repeat}.");
        }
    }

    private void OnGo(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        if (Due(now) is not { } due) return;
        if (_mode == Mode.Now)
        {
            if (!Ask(L.T("همین الان اجرا بشه؟", "Run it right now?"),
                    L.T($"{Fa.ActionSentence(_action)}. بعد از تایید ۵ ثانیه شمارش معکوس می‌بینی و هنوز می‌تونی لغوش کنی. کارهای ذخیره‌نشده رو ذخیره کن.",
                        $"Then {Fa.ActionSentence(_action)}. After you confirm there is a 5-second countdown you can still cancel. Save your work first."),
                    L.T("تایید و اجرا", "Confirm and run"), danger: _action is PowerAction.Shutdown or PowerAction.Restart,
                    detail: L.T("شروع: همین الان", "Starts: right now")))
                return;
            Hub.RequestAction(_action, L.T("دستور مستقیم", "Direct command"), 5);
            return;
        }

        var days = _mode == Mode.At ? _days() : [];
        var repeat = days.Count > 0 ? L.T($" · تکرار: {Fa.Days(days)}", $" · repeats: {Fa.Days(days)}") : "";
        if (!Ask(L.T("این زمان‌بندی ثبت بشه؟", "Schedule this?"),
                L.T($"{Fa.ActionSentence(_action)}. {Fa.Digits(Hub.Settings.CountdownSeconds.ToString())} ثانیه قبلش شمارش معکوس می‌بینی و می‌تونی لغوش کنی. از لیست پایین هر وقت خواستی حذفش کن.",
                    $"Then {Fa.ActionSentence(_action)}. You get a {Hub.Settings.CountdownSeconds}-second countdown first and can cancel. Delete it from the list below whenever you like."),
                L.T("تایید و ثبت", "Confirm and schedule"),
                detail: L.T($"شروع: {HomePage.At(due, now)} ({Fa.Duration(due - now)} دیگه){repeat}", $"Runs: {HomePage.At(due, now)} (in {Fa.Duration(due - now)}){repeat}")))
            return;
        if (!Hub.AddCommand(_action, due, days.Count > 0 ? days : null))
            Hub.Notify(L.T("از قبل ثبت شده بود", "Already scheduled"), L.T("همین دستور با همین زمان قبلاً توی لیسته؛ دوباره اضافه نشد.", "The same command at about the same time is already in the list; nothing was added."));
    }

    public override void Refresh()
    {
        UpdateSummary();
        QueuePanel.Children.Clear();
        var now = DateTime.Now;
        foreach (var cmd in Hub.Queue.Items.OrderBy(c => c.DueAt))
        {
            if (QueuePanel.Children.Count > 0) QueuePanel.Children.Add(Divider());
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            trailing.Children.Add(new TextBlock { Text = Fa.Countdown(cmd.DueAt - now), Style = St("H2"), Foreground = Res("Gold"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Tag = cmd.DueAt });
            var id = cmd.Id;
            var due = cmd.DueAt;
            var action = cmd.Action;
            trailing.Children.Add(IconButton("\uE74D", L.T("حذف", "Remove"), () =>
            {
                if (Ask(L.T("این دستور حذف بشه؟", "Delete this command?"),
                        L.T("برای همیشه از لیست پاک می‌شه و اجرا نمی‌شه؛ هیچ شمارش معکوسی هم نمی‌بینی.", "It is removed from the list for good and will not run; no countdown will appear either."),
                        L.T("حذف کن", "Delete"), danger: true, detail: $"{Fa.ActionName(action)} · {HomePage.At(due, DateTime.Now)}"))
                    Hub.RemoveCommand(id);
            }));
            QueuePanel.Children.Add(Row(ActionGlyph(cmd.Action), "Accent", Fa.ActionName(cmd.Action), HomePage.When(cmd, now), trailing));
        }
        if (QueuePanel.Children.Count == 0)
            QueuePanel.Children.Add(new TextBlock { Text = L.T("دستوری در صف نیست.", "Nothing is queued."), Style = St("Body"), Margin = new Thickness(0, 8, 0, 8) });
    }

    public override void OnSecond()
    {
        var now = DateTime.Now;
        foreach (var row in QueuePanel.Children.OfType<Grid>())
            if (row.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag is DateTime) is { } label)
                label.Text = Fa.Countdown((DateTime)label.Tag - now);
        if (_mode != Mode.Now && now.Second % 15 == 0) UpdateSummary();
    }
}
