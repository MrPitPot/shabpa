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
    private readonly StackPanel _manual = new();
    private readonly StackPanel _teach = new();
    private readonly TextBlock _teachNote = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _planCard = new() { Style = St("Card"), Margin = new Thickness(0, 12, 0, 0) };
    private Border _form = null!;
    private string? _launcher;
    private PowerAction _action = PowerAction.Shutdown;
    private bool _openOnly;
    private readonly CheckBox _wakeUp = new() { Style = St("Check"), IsChecked = true, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
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
            L.T("با زدن دکمه، شب‌پا لیست برنامه‌های نصب‌شده، میانبرهای منوی استارت، رجیستری و پوشه‌های رایج روی همه‌ی درایوها رو برای پیدا کردن لانچرها (Steam، Epic، Riot و…) می‌گرده. چیزی از داخل لانچرها خونده یا جایی فرستاده نمی‌شه.",
                "When you press the button, Shabpa checks the installed-programs list, start-menu shortcuts, the registry and the usual folders on every drive for launchers (Steam, Epic, Riot…). Nothing is read from inside them or sent anywhere."));
        var find = new Button { Style = St("BtnPrimary"), Content = L.T("پیدا کردن لانچرها", "Find launchers"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 12, 0, 0) };
        find.Click += (_, _) => Scan();
        scan.Children.Add(find);
        scan.Children.Add(_launchers);
        scan.Children.Add(_manual);
        root.Children.Add(scanCard);

        // ---- teach by hand -------------------------------------------------------------------------
        var (teachCard, teach) = Ui2.Card(L.T("یادگیری دستی", "Teach by hand"),
            L.T("اگه لانچر خودش دانلود رو شروع نمی‌کنه، یک بار نشونش بده چه کلیک‌هایی بزنه (مثلاً برو به Downloads و دکمه‌ی ادامه). شب‌پا فقط جای کلیک‌ها و فاصله‌ی زمانی‌شون رو توی پنجره‌ی همون لانچر یاد می‌گیره و موقع زمان‌بندی تکرارشون می‌کنه.",
                "If a launcher does not start its download by itself, show it once which clicks to make (for example open Downloads and press resume). Shabpa only learns where and when you click inside that launcher's window and repeats it when a plan starts."));
        teach.Children.Add(_teach);
        teach.Children.Add(_teachNote);
        root.Children.Add(teachCard);

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
        _wakeUp.Content = L.T("اگه کامپیوتر خواب بود، یه دقیقه قبل بیدارش کن", "If the PC is asleep, wake it a minute before");
        _wakeUp.ToolTip = L.T("از تایمر بیدارباش ویندوز استفاده می‌کنه. اگه کار نکرد، توی گزینه‌های برق ویندوز «Allow wake timers» رو روشن کن.",
            "Uses a Windows wake timer. If it does not work, enable “Allow wake timers” in Windows power options.");
        form.Children.Add(_wakeUp);
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

    private bool _scanning;

    private void Scan()
    {
        if (_scanning) return;
        if (Hub.Demo)
        {
            Hub.Settings.Launchers = DemoLaunchers();
            Hub.Settings.LaunchersScanned = true;
            Refresh();
            return;
        }
        // reading the installed-programs list and every shortcut can take seconds: the window keeps answering meanwhile
        _scanning = true;
        _note.Text = L.T("دارم می‌گردم…", "Searching…");
        var before = Hub.Settings.Launchers.ToList();
        _ = Task.Run(LauncherDetector.Detect).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            _scanning = false;
            _note.Text = "";
            if (t.IsCompletedSuccessfully)
            {
                Hub.Settings.Launchers = LauncherDetector.Merge(t.Result, before, System.IO.File.Exists);
                Hub.Settings.LaunchersScanned = true;
                Hub.Store.Save();
            }
            Refresh();
        }));
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
            LauncherKey = launcher.Key, LauncherName = launcher.Name, ExePath = launcher.ExePath, Manual = launcher.Manual, StartAt = at, Action = _action,
            QuietMinutes = quiet, RepeatDays = repeat, ThresholdMbps = threshold, GiveUpMinutes = giveUp, OpenOnly = _openOnly, WakeUp = _wakeUp.IsChecked == true,
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
            {
                var key = l.Key;
                UIElement? remove = l.Manual ? IconButton("\uE74D", L.T("حذف از فهرست", "Remove from the list"), () => RemoveManual(key)) : null;
                _launchers.Children.Add(Row("\uE7FC", "Green", l.Name + (l.Manual ? L.T(" (دستی)", " (added by hand)") : ""), l.ExePath, remove));
            }

        ShowManual();
        ShowTeach();
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

    // ---- launchers added by hand ----------------------------------------------------------------

    private void ShowManual()
    {
        _manual.Children.Clear();
        var missing = LauncherDetector.KnownNames.Where(k => Known.All(l => l.Key != k.key)).ToList();
        _manual.Children.Add(Ui2.Label(Hub.Settings.LaunchersScanned && missing.Count > 0
            ? L.T("پیدا نشدن: ", "Not found: ") + string.Join("، ", missing.Select(m => m.name))
            : L.T("لانچرت توی فهرست نیست؟ فایل اجراییش (.exe) رو خودت نشون بده.", "Your launcher is not listed? Point at its program file (.exe) yourself."), 14, 6));
        var pick = new Button { Style = St("Btn"), Content = L.T("افزودن دستی لانچر…", "Add a launcher by hand…"), HorizontalAlignment = HorizontalAlignment.Left };
        pick.Click += (_, _) => AddManual();
        _manual.Children.Add(pick);
    }

    private void AddManual()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("فایل اجرایی لانچر رو انتخاب کن", "Pick the launcher's program file"),
            Filter = L.T("برنامه (*.exe)|*.exe", "Program (*.exe)|*.exe"), CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        if (LauncherDetector.Reject(dialog.FileName) is { } why)
        {
            _note.Text = why;
            return;
        }
        var info = LauncherDetector.FromFile(dialog.FileName);
        if (!Ask(L.T("این لانچر اضافه بشه؟", "Add this launcher?"), $"{info.Name}\n{info.ExePath}", L.T("اضافه کن", "Add"))) return;
        Hub.Settings.Launchers.RemoveAll(l => l.Key == info.Key || string.Equals(l.ExePath, info.ExePath, StringComparison.OrdinalIgnoreCase));
        Hub.Settings.Launchers.Add(info);
        Hub.Settings.LaunchersScanned = true;
        Hub.Store.Save();
        _note.Text = L.T($"«{info.Name}» اضافه شد.", $"“{info.Name}” was added.");
        Refresh();
    }

    private void RemoveManual(string key)
    {
        Hub.Settings.Launchers.RemoveAll(l => l.Key == key && l.Manual);
        Hub.Store.Save();
        Refresh();
    }

    // ---- teaching clicks ------------------------------------------------------------------------

    private void ShowTeach()
    {
        _teach.Children.Clear();
        if (Known.Count == 0)
        {
            _teach.Children.Add(Ui2.Label(L.T("اول لانچرها رو پیدا کن.", "Find the launchers first."), 12, 0));
            return;
        }
        foreach (var l in Known)
        {
            var launcher = l;
            var macro = Hub.MacroFor(l.Key);
            var learn = new Button { Style = St("Btn"), Content = macro is null ? L.T("یاد بده", "Teach") : L.T("دوباره یاد بده", "Teach again"), Margin = new Thickness(8, 0, 0, 0) };
            learn.Click += (_, _) => StartLesson(launcher);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            if (macro is not null)
            {
                var test = new Button { Style = St("Btn"), Content = L.T("امتحان کن", "Try it"), Margin = new Thickness(8, 0, 0, 0) };
                test.Click += (_, _) => TryLesson(launcher);
                panel.Children.Add(test);
            }
            panel.Children.Add(learn);
            if (macro is not null)
                panel.Children.Add(IconButton("\uE74D", L.T("فراموش کن", "Forget"), () => { Hub.RemoveMacro(launcher.Key); Refresh(); }));
            var sub = macro is null
                ? L.T("چیزی یاد داده نشده", "Nothing taught yet")
                : L.T($"{Fa.Digits(macro.Steps.Count.ToString())} کلیک · {Fa.RelativeDay(macro.Recorded, DateTime.Now)} {Fa.Clock(macro.Recorded)}", $"{macro.Steps.Count} clicks · {Fa.RelativeDay(macro.Recorded, DateTime.Now)} {Fa.Clock(macro.Recorded)}");
            _teach.Children.Add(Row("\uE7C9", macro is null ? "Gold" : "Green", l.Name, sub, panel));
        }
    }

    private void StartLesson(LauncherInfo launcher)
    {
        if (Hub.MacroBusy) return;
        var admin = Hub.Elevated
            ? ""
            : L.T("\n\nاگه این لانچر با دسترسی ادمین اجرا می‌شه، شب‌پا هم باید با ادمین اجرا بشه وگرنه کلیک‌هاش اثر نمی‌کنه.",
                "\n\nIf this launcher runs as administrator, Shabpa must run as administrator too or its clicks have no effect.");
        var message = L.T($"شب‌پا «{launcher.Name}» رو باز می‌کنه و پنجره‌ی شب‌پا کوچک می‌شه. بعدش همون کارهایی رو توی لانچر بکن که برای شروع دانلود می‌کنی و آخرش «پایان و ذخیره» رو بزن.\n\nموقع یادگیری فقط جای کلیک‌های ماوس توی پنجره‌ی همون لانچر و فاصله‌ی زمانی‌شون ذخیره می‌شه؛ هیچ کلید کیبورد، متن، رمز یا عکسی از صفحه ذخیره نمی‌شه و چیزی هم از کامپیوتر بیرون نمی‌ره.{admin}",
            $"Shabpa opens “{launcher.Name}” and its own window gets out of the way. Then do in the launcher what you do to start the download, and press “Finish and save”.\n\nOnly the places and timing of mouse clicks inside that launcher's window are kept; no key, text, password or screen picture is stored and nothing leaves this PC.{admin}");
        if (!ConfirmWindow.AskAlways(L.T("شروع یادگیری؟", "Start teaching?"), message, L.T("شروع کن", "Start"))) return;
        try
        {
            LauncherDetector.Open(launcher.ExePath);
        }
        catch (Exception ex)
        {
            _teachNote.Text = ex.Message;
            return;
        }
        if (!Hub.StartMacroLesson(launcher, count =>
        {
            Application.Current.MainWindow?.Show();
            if (Application.Current.MainWindow is { } w) { w.WindowState = WindowState.Normal; w.Activate(); }
            _teachNote.Text = count > 0
                ? L.T($"{Fa.Digits(count.ToString())} کلیک یاد گرفته شد. «امتحان کن» رو بزن تا ببینی درست کار می‌کنه.", $"{count} clicks learned. Press “Try it” to see that it works.")
                : L.T("چیزی ذخیره نشد.", "Nothing was saved.");
            Refresh();
        }))
        {
            _teachNote.Text = L.T("نتونستم ضبط رو شروع کنم.", "Could not start recording.");
            return;
        }
        if (Application.Current.MainWindow is { } main) main.WindowState = WindowState.Minimized;
    }

    private void TryLesson(LauncherInfo launcher)
    {
        if (Hub.MacroBusy || Hub.MacroFor(launcher.Key) is not { } macro) return;
        if (!ConfirmWindow.AskAlways(L.T("امتحان کلیک‌ها؟", "Try the clicks?"),
                L.T($"«{launcher.Name}» باز می‌شه و شب‌پا کلیک‌های یادگرفته‌شده رو ماوس‌وار انجام می‌ده. ماوس رو دست نزن؛ با Esc می‌تونی هر لحظه متوقفش کنی.",
                    $"“{launcher.Name}” opens and Shabpa repeats the clicks with the mouse. Leave the mouse alone; Esc stops it at any moment."), L.T("امتحان کن", "Try")))
            return;
        try
        {
            LauncherDetector.Open(launcher.ExePath);
        }
        catch (Exception ex)
        {
            _teachNote.Text = ex.Message;
            return;
        }
        if (Application.Current.MainWindow is { } main) main.WindowState = WindowState.Minimized;
        Hub.PlayMacro(macro, launcher.Name, auto: false, outcome => _teachNote.Text = MacroPlayer.Describe(outcome));
    }
}
