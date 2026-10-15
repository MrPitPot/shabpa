using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Open a game launcher at a chosen time and switch the PC off once its download has finished.</summary>
public sealed class DownloadsPage : PageBase
{
    private readonly StackPanel _launchers = new();
    private readonly WrapPanel _pick = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly WrapPanel _actions = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _hour = new() { Style = St("NumberInput"), Text = "03" };
    private readonly TextBox _minute = new() { Style = St("NumberInput"), Text = "00" };
    private readonly DayPicker _day;
    private readonly TextBlock _when = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _quiet = new() { Style = St("Input"), Width = 70, FlowDirection = FlowDirection.LeftToRight, Text = "10", Padding = new Thickness(10, 6, 10, 6) };
    private readonly TextBlock _note = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _plans = new();
    private readonly Border _planCard = new() { Style = St("Card"), Margin = new Thickness(0, 12, 0, 0) };
    private Border _form = null!;
    private string? _launcher;
    private PowerAction _action = PowerAction.Shutdown;
    private bool _openOnly;
    private readonly WrapPanel _repeat = new() { Margin = new Thickness(0, 6, 0, 0) };
    private Func<List<DayOfWeek>> _repeatDays = () => [];
    private readonly TextBox _threshold = new() { Style = St("Input"), Width = 70, FlowDirection = FlowDirection.LeftToRight, Text = "1", Padding = new Thickness(10, 6, 10, 6) };
    private readonly TextBox _giveUp = new() { Style = St("Input"), Width = 70, FlowDirection = FlowDirection.LeftToRight, Text = "45", Padding = new Thickness(10, 6, 10, 6) };

    public override string PageTitle => L.T("دانلود زمان‌بندی‌شده", "Scheduled download");
    public override string PageSubtitle => L.T("لانچر بازی رو توی ساعت مشخص باز کن و بعد از تموم شدن دانلود، سیستم رو خاموش کن.",
        "Open a game launcher at a set time and switch the PC off when its download is done.");

    public DownloadsPage()
    {
        var root = new StackPanel();

        var (scanCard, scan) = Ui2.Card(L.T("لانچرهای نصب‌شده", "Installed launchers"),
            L.T("با زدن دکمه، شب‌پا فقط چند مسیر شناخته‌شده و رجیستری رو برای پیدا کردن لانچرها (Steam، Epic، Riot و…) می‌گرده. چیزی از داخل لانچرها خونده یا جایی فرستاده نمی‌شه.",
                "When you press the button, Shabpa only checks a few well-known folders and registry values for launchers (Steam, Epic, Riot…). Nothing is read from inside them or sent anywhere."));
        var find = new Button { Style = St("BtnPrimary"), Content = L.T("پیدا کردن لانچرها", "Find launchers"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 12, 0, 0) };
        find.Click += (_, _) => Scan();
        scan.Children.Add(find);
        scan.Children.Add(_launchers);
        root.Children.Add(scanCard);

        // ---- new plan ------------------------------------------------------------------------------
        var (formCard, form) = Ui2.Card(L.T("زمان‌بندی جدید", "New plan"),
            L.T("اول دانلودت رو توی خود لانچر توی صف بذار (یا ادامه‌ش بده). شب‌پا توی ساعت مشخص لانچر رو باز می‌کنه و تا قطع شدن دانلود صبر می‌کنه.",
                "First put your download in the launcher's queue. At the chosen time Shabpa opens the launcher and waits until the download stops."));
        _form = formCard;
        form.Children.Add(Ui2.Label(L.T("لانچر", "Launcher")));
        form.Children.Add(_pick);
        TimeBoxes.Wire(_hour, _minute);
        _day = new DayPicker(() => TimeBoxes.TryRead(_hour, _minute, out var h, out var m) ? (h, m) : null);
        _day.Changed += ShowWhen;
        _hour.TextChanged += (_, _) => ShowWhen();
        _minute.TextChanged += (_, _) => ShowWhen();
        form.Children.Add(Ui2.Label(L.T("روز شروع", "Start day")));
        form.Children.Add(new StackPanel { Children = { _day.View }, HorizontalAlignment = HorizontalAlignment.Left });
        form.Children.Add(Ui2.Label(L.T("ساعت شروع (۲۴ ساعته)", "Start time (24-hour)")));
        form.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                _hour,
                new TextBlock { Text = ":", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) },
                _minute,
            },
        });
        form.Children.Add(_when);
        form.Children.Add(Ui2.Label(L.T("تکرار هفتگی (اگه روزی انتخاب کنی، هر هفته همون روزها و همین ساعت تکرار می‌شه؛ خالی = فقط یک بار)",
            "Weekly repeat (pick days to repeat every week at this time; none = once)")));
        form.Children.Add(_repeat);
        _repeatDays = DayToggles(_repeat, ShowWhen);
        form.Children.Add(Ui2.Label(L.T("وقتی دانلود تموم شد", "When the download finishes")));
        form.Children.Add(_actions);
        form.Children.Add(Ui2.Label(L.T("دانلود وقتی تموم‌شده حساب می‌شه که اینترنت این‌قدر دقیقه آروم بمونه", "Finished means the connection stayed quiet for this many minutes")));
        form.Children.Add(new StackPanel { Children = { _quiet }, HorizontalAlignment = HorizontalAlignment.Left });
        form.Children.Add(Ui2.Label(L.T("سرعت دانلودِ «آروم» (مگابیت بر ثانیه)؛ پایین‌تر از این یعنی دانلود تموم شده یا متوقفه",
            "\"Quiet\" download speed (Mbit/s); below this the download counts as done or paused")));
        form.Children.Add(new StackPanel { Children = { _threshold }, HorizontalAlignment = HorizontalAlignment.Left });
        form.Children.Add(Ui2.Label(L.T("اگه این‌قدر دقیقه بعد از باز شدن لانچر دانلودی شروع نشد، منصرف شو و خاموش نکن",
            "If no download starts this many minutes after opening the launcher, give up and do not switch off")));
        form.Children.Add(new StackPanel { Children = { _giveUp }, HorizontalAlignment = HorizontalAlignment.Left });
        var add = new Button { Style = St("BtnPrimary"), Content = L.T("ثبت زمان‌بندی", "Add plan"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 14, 0, 0) };
        add.Click += (_, _) => AddPlan();
        form.Children.Add(add);
        form.Children.Add(_note);
        root.Children.Add(formCard);

        _planCard.Child = _plans;
        root.Children.Add(_planCard);
        Content = root;

        Chips(_actions, new[]
        {
            ("shutdown", L.T("خاموش کردن", "Shut down")),
            ("sleep", L.T("خواب", "Sleep")),
            ("hibernate", L.T("هایبرنیت", "Hibernate")),
            ("restart", L.T("ری‌استارت", "Restart")),
            ("open", L.T("فقط باز کردن لانچر", "Only open the launcher")),
        }, "shutdown", key =>
        {
            _openOnly = key == "open";
            _action = key switch { "sleep" => PowerAction.Sleep, "hibernate" => PowerAction.Hibernate, "restart" => PowerAction.Restart, _ => PowerAction.Shutdown };
        });
        ShowWhen();
    }

    public override void OnSecond()
    {
        if (DateTime.Now.Second == 0) ShowWhen();
    }

    private static List<LauncherInfo> Known => Hub.Settings.Launchers;

    private static string When(DateTime t) => $"{Fa.RelativeDay(t, DateTime.Now)} {Fa.Date(t)} {Fa.Clock(t)}";

    /// <summary>Spells out the chosen moment under the picker, or why it cannot be used.</summary>
    private void ShowWhen()
    {
        var now = DateTime.Now;
        _day.Update(now);
        var days = _repeatDays();
        if (days.Count > 0 && TimeBoxes.TryRead(_hour, _minute, out var rh, out var rm))
        {
            var next = Schedule.Next(rh, rm, days, now);
            _when.Foreground = Res("Muted");
            _when.Text = L.T($"هر هفته: {Fa.Days(days)} ساعت {Fa.Clock(rh, rm)}. اولین بار: {When(next)}",
                $"Every week: {Fa.Days(days)} at {Fa.Clock(rh, rm)}. First run: {When(next)}");
            return;
        }
        var at = _day.Value(now);
        _when.Foreground = Res(at is { } t && t > now.AddMinutes(1) ? "Muted" : "Red");
        _when.Text = at is not { } a
            ? L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59.")
            : a <= now.AddMinutes(1)
                ? L.T("این زمان گذشته؛ یه روز یا ساعت بعدتر انتخاب کن.", "That time has passed; pick a later day or time.")
                : L.T($"شروع: {When(a)} ({Fa.Duration(a - now)} دیگه)", $"Starts {When(a)} (in {Fa.Duration(a - now)})");
    }

    private void Scan()
    {
        Hub.Settings.Launchers = Hub.Demo ? DemoLaunchers() : LauncherDetector.Detect();
        Hub.Settings.LaunchersScanned = true;
        Hub.Store.Save();
        Refresh();
    }

    private static List<LauncherInfo> DemoLaunchers() =>
    [
        new() { Key = "steam", Name = "Steam", ExePath = @"C:\Program Files (x86)\Steam\steam.exe", Process = "steam" },
        new() { Key = "epic", Name = "Epic Games", ExePath = @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", Process = "epicgameslauncher" },
        new() { Key = "riot", Name = "Riot Client", ExePath = @"C:\Riot Games\Riot Client\RiotClientServices.exe", Process = "riotclientservices" },
    ];

    private void AddPlan()
    {
        var launcher = Known.FirstOrDefault(l => l.Key == _launcher);
        if (launcher is null)
        {
            _note.Text = L.T("اول لانچرها رو پیدا کن و یکی رو انتخاب کن.", "Find the launchers first and pick one.");
            return;
        }
        var now = DateTime.Now;
        if (!TimeBoxes.TryRead(_hour, _minute, out var hour, out var minute))
        {
            _note.Text = L.T("ساعت باید بین ۰۰:۰۰ تا ۲۳:۵۹ باشه.", "The time must be between 00:00 and 23:59.");
            return;
        }
        var repeat = _repeatDays();
        var at = repeat.Count > 0 ? Schedule.Next(hour, minute, repeat, now) : _day.Value(now) ?? now;
        if (at <= now.AddMinutes(1))
        {
            _note.Text = L.T("این زمان گذشته؛ یه روز یا ساعت بعدتر انتخاب کن.", "That time has passed; pick a later day or time.");
            return;
        }
        var quiet = Int(_quiet, 1, 120, 10);
        var threshold = Number(_threshold, 0.1, 1000, 1);
        var giveUp = Int(_giveUp, 5, 600, 45);

        var every = repeat.Count > 0 ? L.T($" و هر هفته ({Fa.Days(repeat)}) تکرار می‌شه", $" and repeats every week ({Fa.Days(repeat)})") : "";
        var message = _openOnly
            ? L.T($"«{launcher.Name}» {When(at)} فقط باز می‌شه{every}. هیچ خاموشی‌ای در کار نیست.", $"“{launcher.Name}” only opens {When(at)}{every}. Nothing is switched off.")
            : L.T($"«{launcher.Name}» {When(at)} باز می‌شه{every} و بعد از تموم شدن دانلود (وقتی {Fa.Digits(quiet.ToString())} دقیقه اینترنت زیر {Fa.Digits(threshold.ToString("0.#"))} مگابیت بمونه) «{Fa.ActionName(_action)}» انجام می‌شه. اگه {Fa.Digits(giveUp.ToString())} دقیقه دانلودی شروع نشه، خاموش نمی‌شه.",
                $"“{launcher.Name}” opens {When(at)}{every}; once the download is done ({quiet} minutes below {threshold:0.#} Mbit/s) the PC will “{Fa.ActionName(_action)}”. If no download starts within {giveUp} minutes nothing is switched off.");
        if (!Ask(L.T("این زمان‌بندی ثبت بشه؟", "Add this plan?"), message, L.T("ثبت", "Add"))) return;

        var plan = new DownloadPlan
        {
            LauncherKey = launcher.Key, LauncherName = launcher.Name, ExePath = launcher.ExePath, StartAt = at, Action = _action,
            QuietMinutes = quiet, RepeatDays = repeat, ThresholdMbps = threshold, GiveUpMinutes = giveUp, OpenOnly = _openOnly,
        };
        _note.Text = Hub.AddDownloadPlan(plan)
            ? L.T("ثبت شد.", "Added.")
            : L.T("همین زمان‌بندی قبلاً ثبت شده بود.", "That plan already exists.");
    }

    public override void Refresh()
    {
        ShowWhen();
        _launchers.Children.Clear();
        if (!Hub.Settings.LaunchersScanned)
            _launchers.Children.Add(Ui2.Label(L.T("هنوز جستجو نشده.", "Not searched yet.")));
        else if (Known.Count == 0)
            _launchers.Children.Add(Ui2.Label(L.T("لانچری پیدا نشد.", "No launcher found.")));
        else
            foreach (var l in Known)
                _launchers.Children.Add(Row("\uE7FC", "Green", l.Name, l.ExePath));

        if (_launcher is null || Known.All(l => l.Key != _launcher)) _launcher = Known.FirstOrDefault()?.Key;
        Chips(_pick, Known.Select(l => (l.Key, l.Name)), _launcher ?? "", v => _launcher = v);
        _form.Visibility = Known.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _plans.Children.Clear();
        _plans.Children.Add(new TextBlock { Text = L.T("زمان‌بندی‌های ثبت‌شده", "Planned downloads"), Style = St("H2") });
        var plans = Hub.State.DownloadPlans;
        _planCard.Visibility = plans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var plan in plans.OrderBy(p => p.StartAt))
        {
            var id = plan.Id;
            var delete = IconButton("\uE74D", L.T("حذف", "Delete"), () =>
            {
                var p = Hub.State.DownloadPlans.FirstOrDefault(x => x.Id == id);
                if (p is null) return;
                if (Ask(L.T("این زمان‌بندی حذف بشه؟", "Delete this plan?"), $"{p.LauncherName} · {When(p.StartAt)}", L.T("حذف", "Delete"), danger: true))
                    Hub.RemoveDownloadPlan(id);
            });
            var then = plan.OpenOnly ? L.T("فقط باز کردن", "open only") : Fa.ActionName(plan.Action);
            var weekly = plan.Repeats ? L.T($" · هر هفته: {Fa.Days(plan.RepeatDays)}", $" · weekly: {Fa.Days(plan.RepeatDays)}") : "";
            var sub = plan.State == PlanState.Running
                ? L.T("در حال دانلود؛ منتظر تموم شدنش هستم.", "Running; waiting for it to finish.")
                : L.T($"شروع: {When(plan.StartAt)} · بعدش: {then}{weekly}", $"Starts {When(plan.StartAt)} · then: {then}{weekly}");
            _plans.Children.Add(Row("\uE896", plan.State == PlanState.Running ? "Green" : "Gold", plan.LauncherName, sub, delete));
        }
    }
}
