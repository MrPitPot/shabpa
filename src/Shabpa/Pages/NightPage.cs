using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«شب و قطعی برق»: the cheap-traffic night window (wake the PC, open the launchers, sleep at the end) and the
/// power-cut standby (warning ten minutes before, hibernate two minutes before). Everything goes through the usual
/// confirmation that names the exact times.</summary>
public sealed class NightPage : PageBase
{
    private readonly StackPanel _root = new();
    private readonly TextBox _fromH = new() { Style = St("NumberInput") }, _fromM = new() { Style = St("NumberInput") };
    private readonly TextBox _toH = new() { Style = St("NumberInput") }, _toM = new() { Style = St("NumberInput") };
    private readonly TextBox _cutH = new() { Style = St("NumberInput"), Text = "14" }, _cutM = new() { Style = St("NumberInput"), Text = "00" };
    private readonly TextBox _cutLen = new() { Style = St("NumberInput"), Text = "2", Width = 64 };
    private readonly StackPanel _launchers = new();
    private readonly WrapPanel _endChips = new();
    private readonly StackPanel _status = new(), _cuts = new(), _cutDays = new();
    private readonly TextBlock _error = new() { Style = St("Label"), Foreground = Res("Red"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly HashSet<string> _picked = [];
    private PowerAction _end = PowerAction.Sleep;
    private Func<List<DayOfWeek>> _days = () => [];

    public override string PageTitle => L.T("شب و قطعی برق", "Night & power cuts");
    public override string PageSubtitle => L.T("اینترنت ارزون شبانه رو از دست نده و برای قطعی برق آماده باش.", "Do not miss the cheap night traffic, and be ready for power cuts.");

    private NightSettings S => Hub.Settings.Overnight;

    public NightPage()
    {
        Content = _root;
        var s = S;
        (_fromH.Text, _fromM.Text, _toH.Text, _toM.Text) = ($"{s.FromHour:00}", $"{s.FromMinute:00}", $"{s.ToHour:00}", $"{s.ToMinute:00}");
        _end = s.EndAction;
        foreach (var k in s.Launchers) _picked.Add(k);
        TimeBoxes.Wire(_fromH, _fromM);
        TimeBoxes.Wire(_toH, _toM);
        TimeBoxes.Wire(_cutH, _cutM);

        // ---- night internet --------------------------------------------------------------------------------------
        var (nCard, n) = K.Card(L.T("اینترنت شبانه", "Night internet"),
            L.T("خیلی از بسته‌ها شب‌ها ترافیک ارزون‌تر یا نیم‌بها دارن. شب‌پا سر شروع بازه سیستم رو از خواب بیدار می‌کنه، لانچر رو باز می‌کنه و دانلود رو ادامه می‌ده، و آخر بازه سیستم رو می‌خوابونه.",
                "Many plans have cheaper or half-price traffic at night. Shabpa wakes the PC when the window starts, opens the launcher and resumes the download, and puts the PC to sleep when the window ends."));
        n.Children.Add(Caption(L.T("بازه‌ی ارزون بسته‌ت (۲۴ ساعته)", "Your cheap window (24-hour)")));
        var times = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        times.Children.Add(Clock(_fromH, _fromM));
        times.Children.Add(new TextBlock { Text = L.T("تا", "to"), Style = St("Label"), Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center });
        times.Children.Add(Clock(_toH, _toM));
        n.Children.Add(times);

        n.Children.Add(Caption(L.T("لانچر دانلود (اولی کامل کار می‌کنه، بقیه فقط باز می‌شن)", "Download launcher (the first one gets full help, the others are only opened)"), 14));
        n.Children.Add(_launchers);
        n.Children.Add(Caption(L.T("آخر بازه سیستم چی بشه؟", "What happens at the end of the window?"), 14));
        n.Children.Add(_endChips);
        var apply = new Button { Content = L.T("فعال کن", "Turn on"), Style = St("BtnPrimary"), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140 };
        apply.Click += (_, _) => ApplyInternet();
        var off = new Button { Content = L.T("خاموش کن", "Turn off"), Style = St("Btn"), Margin = new Thickness(10, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        off.Click += (_, _) => DisableInternet();
        n.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { apply, off } });
        n.Children.Add(_status);
        n.Children.Add(_error);
        n.Children.Add(new TextBlock
        {
            Text = L.T("برای بیدار شدن سیستم از خواب، «بیدارباش» ویندوز (تایمر بیدارکننده) باید روشن باشه؛ توی صفحه‌ی بیدارباش وضعیتش رو می‌بینی. با خاموش‌کردن کامل سیستم بیدار نمی‌شه، پس پایان بازه رو «خواب» بذار.",
                "Waking from sleep needs Windows wake timers to be on; the Wake-up page shows their state. A full shutdown cannot wake the PC, so keep the end of the window on “Sleep”."),
            Style = St("Tip"), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        _root.Children.Add(nCard);

        // ---- power cuts --------------------------------------------------------------------------------------------
        var (cCard, c) = K.Card(L.T("آماده‌باش قطعی برق", "Power-cut standby"),
            L.T("ساعت قطعی برق منطقه‌ات رو بنویس. ۱۰ دقیقه قبلش خبرت می‌کنه، ۲ دقیقه قبلش سیستم هایبرنیت می‌شه که کارت نپره و هارد سالم بمونه، و بعد از برگشتن برق می‌گه چقدر خاموش بوده.",
                "Enter your area's power-cut hours. It warns you 10 minutes before, hibernates the PC 2 minutes before so your work is kept and the drive stays safe, and tells you how long the PC was off once power is back."));
        c.Margin = new Thickness(0);
        cCard.Margin = new Thickness(0, 16, 0, 0);
        c.Children.Add(Caption(L.T("شروع قطعی (۲۴ ساعته) و مدتش", "Cut starts (24-hour) and how long")));
        var cut = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        cut.Children.Add(Clock(_cutH, _cutM));
        cut.Children.Add(new TextBlock { Text = L.T("برای", "for"), Style = St("Label"), Margin = new Thickness(16, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        cut.Children.Add(_cutLen);
        cut.Children.Add(new TextBlock { Text = L.T("ساعت", "hours"), Style = St("Label"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        c.Children.Add(cut);
        c.Children.Add(Caption(L.T("روزها (خالی یعنی هر روز)", "Days (none means every day)"), 14));
        _cutDays.Orientation = Orientation.Horizontal;
        _days = DayToggles(_cutDays);
        c.Children.Add(_cutDays);
        var add = new Button { Content = L.T("افزودن قطعی", "Add the cut"), Style = St("BtnPrimary"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140 };
        add.Click += (_, _) => AddCut();
        c.Children.Add(add);
        c.Children.Add(_cuts);
        _root.Children.Add(cCard);
    }

    private static TextBlock Caption(string text, double top = 0) => new() { Text = text, Style = St("Label"), Margin = new Thickness(0, top, 0, 4), TextWrapping = TextWrapping.Wrap };

    private static StackPanel Clock(TextBox h, TextBox m)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        p.Children.Add(h);
        p.Children.Add(new TextBlock { Text = ":", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 2) });
        p.Children.Add(m);
        return p;
    }

    public override void Refresh()
    {
        // launchers the person already has (found or added by hand on the download page)
        _launchers.Children.Clear();
        var known = Hub.Settings.Launchers;
        if (known.Count == 0)
            _launchers.Children.Add(new TextBlock
            {
                Text = L.T("هنوز لانچری پیدا نشده. از صفحه‌ی «دانلود زمان‌بندی‌شده» دنبال لانچرها بگرد، بعد برگرد اینجا.", "No launcher found yet. Search for launchers on the “Scheduled download” page, then come back."),
                Style = St("Tip"), TextWrapping = TextWrapping.Wrap,
            });
        var first = true;
        foreach (var l in known)
        {
            var sw = new Kit.Switch { IsChecked = _picked.Contains(l.Key) };
            var key = l.Key;
            sw.Checked += (_, _) => _picked.Add(key);
            sw.Unchecked += (_, _) => _picked.Remove(key);
            _launchers.Children.Add(K.SettingRow(l.Name, sw, first));
            first = false;
        }
        Chips(_endChips, new[] { (PowerAction.Sleep, L.T("خواب", "Sleep")), (PowerAction.Hibernate, L.T("هایبرنیت", "Hibernate")), (PowerAction.Shutdown, L.T("خاموش", "Shut down")) }, _end, a => _end = a);

        _status.Children.Clear();
        if (S.InternetOn)
        {
            var next = Schedule.Next(S.FromHour, S.FromMinute, [], DateTime.Now);
            _status.Children.Add(new TextBlock
            {
                Text = L.T($"فعاله: هر شب ساعت {Fa.Clock(S.FromHour, S.FromMinute)} شروع و ساعت {Fa.Clock(S.ToHour, S.ToMinute)} تموم می‌شه. نوبت بعدی: {Fa.RelativeDay(next, DateTime.Now)} {Fa.Clock(next)}.",
                    $"On: every night from {Fa.Clock(S.FromHour, S.FromMinute)} to {Fa.Clock(S.ToHour, S.ToMinute)}. Next: {Fa.RelativeDay(next, DateTime.Now)} {Fa.Clock(next)}."),
                Style = St("Body"), Foreground = Res("Green"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
            });
        }

        _cuts.Children.Clear();
        foreach (var o in S.Outages.OrderBy(o => o.Hour * 60 + o.Minute))
        {
            var id = o.Id;
            var del = IconButton("trash", L.T("حذف", "Delete"), () =>
            {
                if (!Ask(L.T("این قطعی حذف بشه؟", "Delete this power cut?"), L.T("هشدار و هایبرنیت خودکارش هم برداشته می‌شه.", "Its warning and automatic hibernate are removed too."), L.T("حذف کن", "Delete"), danger: true,
                        detail: $"{Fa.Clock(o.Hour, o.Minute)} · {Fa.Days(o.Days)}")) return;
                Hub.Night.RemoveOutage(id);
                Refresh();
            });
            var hours = o.DurationMinutes / 60.0;
            _cuts.Children.Add(Row("bolt", "HueC", L.T($"ساعت {Fa.Clock(o.Hour, o.Minute)} · حدود {Fa.N(hours, hours % 1 == 0 ? 0 : 1)} ساعت", $"{Fa.Clock(o.Hour, o.Minute)} · about {Fa.N(hours, hours % 1 == 0 ? 0 : 1)} h"),
                L.T($"{Fa.Days(o.Days)} · هایبرنیت ساعت {Fa.Clock(Time(o, -2))}", $"{Fa.Days(o.Days)} · hibernates at {Fa.Clock(Time(o, -2))}"), del));
        }
        if (S.Outages.Count > 0) _cuts.Margin = new Thickness(0, 12, 0, 0);
    }

    private static DateTime Time(OutageSlot o, int addMinutes) => DateTime.Today.AddHours(o.Hour).AddMinutes(o.Minute + addMinutes);

    private void ApplyInternet()
    {
        _error.Text = "";
        if (!TimeBoxes.TryRead(_fromH, _fromM, out var fh, out var fm) || !TimeBoxes.TryRead(_toH, _toM, out var th, out var tm))
        {
            _error.Text = L.T("ساعت‌ها باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشن.", "The times must be between 00:00 and 23:59.");
            return;
        }
        var chosen = Hub.Settings.Launchers.Where(l => _picked.Contains(l.Key)).ToList();
        var next = Schedule.Next(fh, fm, [], DateTime.Now);
        if (chosen.Count == 0) { _error.Text = L.T("اول یه لانچر انتخاب کن.", "Pick a launcher first."); return; }
        if (!Ask(L.T("اینترنت شبانه روشن بشه؟", "Turn night internet on?"),
                L.T($"هر شب ساعت {Fa.Clock(fh, fm)} سیستم از خواب بیدار می‌شه و «{chosen[0].Name}» باز می‌شه. ساعت {Fa.Clock(th, tm)} هم «{Fa.ActionName(_end)}» انجام می‌شه (با شمارش معکوس قابل لغو). اولین بار: {Fa.RelativeDay(next, DateTime.Now)} {Fa.Clock(next)}.",
                    $"Every night at {Fa.Clock(fh, fm)} the PC wakes up and “{chosen[0].Name}” opens. At {Fa.Clock(th, tm)} “{Fa.ActionName(_end)}” runs (with a cancellable countdown). First time: {Fa.RelativeDay(next, DateTime.Now)} {Fa.Clock(next)}."),
                L.T("روشن کن", "Turn on"), detail: $"{Fa.Clock(fh, fm)} → {Fa.Clock(th, tm)}")) return;
        var error = Hub.Night.EnableInternet(fh, fm, th, tm, chosen, _end);
        _error.Text = error ?? "";
        Refresh();
    }

    private void DisableInternet()
    {
        if (!S.InternetOn) return;
        if (!Ask(L.T("اینترنت شبانه خاموش بشه؟", "Turn night internet off?"), L.T("بیدارباش و برنامه‌ی دانلود و خاموشی پایان بازه برداشته می‌شن.", "The wake-up, the download plan and the end-of-window action are removed."),
                L.T("خاموش کن", "Turn off"), danger: true)) return;
        Hub.Night.DisableInternet();
        Refresh();
    }

    private void AddCut()
    {
        if (!TimeBoxes.TryRead(_cutH, _cutM, out var h, out var m)) { Notice(L.T("ساعت درست نیست.", "That time is not valid.")); return; }
        if (!double.TryParse(Latin(_cutLen.Text).Replace('٫', '.').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours) || hours is <= 0 or > 12)
        {
            Notice(L.T("مدت قطعی باید بین ۰ تا ۱۲ ساعت باشه.", "The length must be between 0 and 12 hours."));
            return;
        }
        var days = _days();
        var slot = new OutageSlot { Hour = h, Minute = m, DurationMinutes = (int)Math.Round(hours * 60), Days = days };
        if (S.Outages.Any(o => o.Hour == h && o.Minute == m && o.Days.Order().SequenceEqual(days.Order()))) { Notice(L.T("این قطعی رو قبلاً گذاشتی.", "You already have this one.")); return; }
        var hib = Time(slot, -2);
        if (!Ask(L.T("قطعی برق ثبت بشه؟", "Add this power cut?"),
                L.T($"ساعت {Fa.Clock(h, m)} ({Fa.Days(days)}) برق می‌ره. ۱۰ دقیقه قبلش خبر می‌دم و ساعت {Fa.Clock(hib)} سیستم هایبرنیت می‌شه (با شمارش معکوس قابل لغو).",
                    $"Power goes out at {Fa.Clock(h, m)} ({Fa.Days(days)}). You get a warning 10 minutes before and the PC hibernates at {Fa.Clock(hib)} (cancellable countdown)."),
                L.T("ثبت کن", "Add it"), detail: $"{Fa.Clock(h, m)} · {Fa.Days(days)}")) return;
        Hub.Night.AddOutage(slot);
        Refresh();
    }

    private void Notice(string text) => _error.Text = text;
}
