using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class PowerPage : PageBase
{
    private enum Mode { Now, After, At }

    private readonly Func<List<DayOfWeek>> _days;
    private PowerAction _action = PowerAction.Shutdown;
    private Mode _mode = Mode.Now;
    private bool _ready;

    public override string PageTitle => L.T("دستور برق", "Power command");
    public override string PageSubtitle => L.T("همین حالا، چند دقیقه‌ی دیگه یا هر شب سر یه ساعت مشخص.", "Right now, in a few minutes, or every night at a set time.");

    public PowerPage()
    {
        InitializeComponent();
        _days = DayToggles(DayPanel, UpdateSummary);
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
                if (!int.TryParse(Latin(AtHour.Text), out var h) || !int.TryParse(Latin(AtMinute.Text), out var m)) return null;
                if (h is < 0 or > 23 || m is < 0 or > 59) return null;
                return Schedule.Next(h, m, _days(), now);
            default:
                return now;
        }
    }

    private void UpdateSummary()
    {
        if (!_ready) return;
        AfterPanel.Visibility = _mode == Mode.After ? Visibility.Visible : Visibility.Collapsed;
        AtPanel.Visibility = _mode == Mode.At ? Visibility.Visible : Visibility.Collapsed;

        var now = DateTime.Now;
        var due = Due(now);
        Go.IsEnabled = due is not null;
        Go.Content = _mode == Mode.Now ? Fa.ActionName(_action) : L.T("زمان‌بندی کن", "Schedule it");

        if (due is null)
            Summary.Text = _mode == Mode.After
                ? L.T("یه مدت زمان وارد کن.", "Enter a duration.")
                : L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59.");
        else if (_mode == Mode.Now)
            Summary.Text = L.T($"{Fa.ActionSentence(_action)}؛ قبلش ۵ ثانیه فرصت لغو داری.", $"Then {Fa.ActionSentence(_action)}; you get 5 seconds to cancel first.");
        else
        {
            var days = _mode == Mode.At ? _days() : [];
            var repeat = days.Count > 0 ? L.T($" و بعد از اون {Fa.Days(days)} تکرار می‌شه", $", then repeats {Fa.Days(days)}") : "";
            Summary.Text = L.T(
                $"{Fa.RelativeDay(due.Value, now)} ساعت {Fa.Clock(due.Value)} ({Fa.Duration(due.Value - now)} دیگه) {Fa.ActionSentence(_action)}{repeat}.",
                $"{Fa.RelativeDay(due.Value, now)} at {Fa.Clock(due.Value)} (in {Fa.Duration(due.Value - now)}) {Fa.ActionSentence(_action)}{repeat}.");
        }
    }

    private void OnGo(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        if (Due(now) is not { } due) return;
        if (_mode == Mode.Now)
            Hub.RequestAction(_action, L.T("دستور مستقیم", "Direct command"), 5);
        else
            Hub.AddCommand(_action, due, _mode == Mode.At ? _days() : null);
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
            trailing.Children.Add(IconButton("", L.T("حذف", "Remove"), () => Hub.RemoveCommand(id)));
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
