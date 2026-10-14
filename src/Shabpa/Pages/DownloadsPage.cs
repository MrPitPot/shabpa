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
    private readonly TextBox _time = new() { Style = St("Input"), Width = 90, FlowDirection = FlowDirection.LeftToRight, Text = "03:00", Padding = new Thickness(10, 6, 10, 6) };
    private readonly TextBox _quiet = new() { Style = St("Input"), Width = 70, FlowDirection = FlowDirection.LeftToRight, Text = "10", Padding = new Thickness(10, 6, 10, 6) };
    private readonly TextBlock _note = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _plans = new();
    private readonly Border _planCard = new() { Style = St("Card"), Margin = new Thickness(0, 12, 0, 0) };
    private Border _form = null!;
    private string? _launcher;
    private PowerAction _action = PowerAction.Shutdown;

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
        form.Children.Add(Ui2.Label(L.T("ساعت شروع (۲۴ ساعته، مثلاً ۰۳:۰۰)", "Start time (24-hour, e.g. 03:00)")));
        form.Children.Add(new StackPanel { Children = { _time }, HorizontalAlignment = HorizontalAlignment.Left });
        form.Children.Add(Ui2.Label(L.T("وقتی دانلود تموم شد", "When the download finishes")));
        form.Children.Add(_actions);
        form.Children.Add(Ui2.Label(L.T("دانلود وقتی تموم‌شده حساب می‌شه که اینترنت این‌قدر دقیقه آروم بمونه", "Finished means the connection stayed quiet for this many minutes")));
        form.Children.Add(new StackPanel { Children = { _quiet }, HorizontalAlignment = HorizontalAlignment.Left });
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
            (PowerAction.Shutdown, L.T("خاموش کردن", "Shut down")),
            (PowerAction.Sleep, L.T("خواب", "Sleep")),
            (PowerAction.Hibernate, L.T("هایبرنیت", "Hibernate")),
        }, _action, a => _action = a);
    }

    private static List<LauncherInfo> Known => Hub.Settings.Launchers;

    private static string When(DateTime t) => $"{Fa.RelativeDay(t, DateTime.Now)} {Fa.Date(t)} {Fa.Clock(t)}";

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
        var parts = Latin(_time.Text).Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h is < 0 or > 23 || m is < 0 or > 59)
        {
            _note.Text = L.T("ساعت رو مثل ۰۳:۰۰ بنویس.", "Write the time like 03:00.");
            return;
        }
        var now = DateTime.Now;
        var at = now.Date.AddHours(h).AddMinutes(m);
        if (at <= now.AddMinutes(1)) at = at.AddDays(1);
        var quiet = Int(_quiet, 1, 120, 10);

        var message = L.T($"«{launcher.Name}» {When(at)} باز می‌شه و بعد از تموم شدن دانلود (وقتی {Fa.Digits(quiet.ToString())} دقیقه اینترنت آروم بمونه) «{Fa.ActionName(_action)}» انجام می‌شه.",
            $"“{launcher.Name}” opens {When(at)}; once the download is done ({quiet} quiet minutes) the PC will “{Fa.ActionName(_action)}”.");
        if (!Ask(L.T("این زمان‌بندی ثبت بشه؟", "Add this plan?"), message, L.T("ثبت", "Add"))) return;

        _note.Text = Hub.AddDownloadPlan(launcher, at, _action, quiet)
            ? L.T("ثبت شد.", "Added.")
            : L.T("همین زمان‌بندی قبلاً ثبت شده بود.", "That plan already exists.");
    }

    public override void Refresh()
    {
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
            var sub = plan.State == PlanState.Running
                ? L.T("در حال دانلود؛ منتظر تموم شدنش هستم.", "Running; waiting for it to finish.")
                : L.T($"شروع: {When(plan.StartAt)} · بعدش: {Fa.ActionName(plan.Action)}", $"Starts {When(plan.StartAt)} · then: {Fa.ActionName(plan.Action)}");
            _plans.Children.Add(Row("\uE896", plan.State == PlanState.Running ? "Green" : "Gold", plan.LauncherName, sub, delete));
        }
    }
}
