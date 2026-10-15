using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>«سلامت و امنیت»: a plain-language look at disks, updates, services, errors, security settings and startup
/// programs, with a report that is reviewed and redacted before it is shared. It says what it sees; it does not claim to
/// repair anything or to be an antivirus.</summary>
public sealed class HealthPage : TabsPage
{
    public static readonly string[] TabKeys = ["overview", "security", "report"];

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");

    public HealthPage()
    {
        Add("overview", L.T("سلامت سیستم", "System health"), new HealthOverviewPage());
        Add("security", L.T("امنیت و استارتاپ", "Security & startup"), new SecurityPage());
        Add("report", L.T("گزارش", "Report"), new HealthReportPage());
    }
}

internal static class HealthUi
{
    public static FrameworkElement Row(HealthCheck check)
    {
        var (glyph, tint) = check.State switch
        {
            CheckState.Good => ("", "Green"),
            CheckState.Attention => ("", "Gold"),
            CheckState.Problem => ("", "Red"),
            _ => ("", "Muted"),
        };
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
            Background = tint == "Muted" ? (System.Windows.Media.Brush)Application.Current.Resources["Panel2"] : (System.Windows.Media.Brush)Application.Current.Resources[tint + "Soft"],
            Child = new TextBlock
            {
                Text = glyph, Style = (Style)Application.Current.Resources["Icon"], FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)Application.Current.Resources[tint],
            },
        };
        grid.Children.Add(badge);
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = check.Title, Style = (Style)Application.Current.Resources["H2"], FontSize = 14 });
        text.Children.Add(new TextBlock { Text = check.Detail, Style = (Style)Application.Current.Resources["Body"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (check.Advice.Length > 0)
            text.Children.Add(new TextBlock { Text = check.Advice, Style = (Style)Application.Current.Resources["Label"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    public static string WindowsVersion() => Environment.OSVersion.VersionString;
}

public sealed class HealthOverviewPage : PageBase
{
    private readonly StackPanel _checks = new();
    private readonly StackPanel _events = new();
    private readonly TextBlock _status = new() { Style = St("Label"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private bool _busy;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("وضعیت دیسک‌ها، به‌روزرسانی، سرویس‌ها و خطاهای اخیر به زبان ساده. فقط نشون می‌ده؛ ادعا نمی‌کنه چیزی رو تعمیر کرده.",
        "Disks, updates, services and recent errors in plain language. It only reports; it never claims to have repaired anything.");

    public HealthOverviewPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("وضعیت کلی", "Overview"));
        var refresh = new Button { Style = St("BtnPrimary"), Content = L.T("بررسی دوباره", "Check again"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        refresh.Click += (_, _) => Load();
        body.Children.Add(refresh);
        body.Children.Add(_status);
        _checks.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_checks);
        root.Children.Add(card);
        var (eCard, e) = Ui2.Card(L.T("خطاهای ۷ روز اخیر", "Errors in the last 7 days"),
            L.T("از لاگ رویدادهای ویندوز، گروه‌بندی‌شده. بیشترش بی‌خطره؛ فقط مواردی که تکرار می‌شن مهمن.", "From the Windows event log, grouped. Most are harmless; repeated ones matter."));
        e.Children.Add(_events);
        root.Children.Add(eCard);
        Content = root;
    }

    public override void Refresh()
    {
        if (_checks.Children.Count == 0 && !_busy) Load();
    }

    private void Load()
    {
        if (_busy) return;
        _busy = true;
        _status.Text = L.T("در حال بررسی…", "Checking…");
        _ = Task.Run(() =>
        {
            var reg = new WindowsRegistryProbe();
            var list = new List<HealthCheck>();
            list.AddRange(Health.DisksNow());
            list.Add(Health.PendingReboot(reg));
            list.Add(Health.WindowsUpdate(reg, DateTime.UtcNow));
            list.AddRange(Health.Services(reg, Health.ServiceStateNow));
            var events = Health.RecentErrors();
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                Show(list, events);
            });
        });
    }

    private void Show(List<HealthCheck> list, List<EventSummary> events)
    {
        var problems = list.Count(c => c.State == CheckState.Problem);
        var attention = list.Count(c => c.State == CheckState.Attention);
        _status.Text = problems + attention == 0
            ? L.T("همه‌چیز عادی به نظر می‌رسه ✅", "Everything looks normal ✅")
            : L.T($"{Fa.Digits(problems.ToString())} مورد مشکل‌دار و {Fa.Digits(attention.ToString())} مورد نیازمند توجه.", $"{problems} problem(s) and {attention} item(s) that need attention.");
        _checks.Children.Clear();
        foreach (var c in list.OrderByDescending(c => c.State == CheckState.Problem).ThenByDescending(c => c.State == CheckState.Attention)) _checks.Children.Add(HealthUi.Row(c));

        _events.Children.Clear();
        if (events.Count == 0)
            _events.Children.Add(new TextBlock { Text = L.T("خطای مهمی ثبت نشده (یا لاگ خونده نشد).", "No notable errors were recorded (or the log could not be read)."), Style = St("Body"), Margin = new Thickness(0, 10, 0, 0) });
        foreach (var ev in events)
        {
            var grid = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            grid.Children.Add(new TextBlock
            {
                Text = $"{(ev.Critical ? "🔴 " : "")}{ev.Provider} · #{ev.Id} · ×{Fa.Digits(ev.Count.ToString())}", Style = St("H2"), FontSize = 13, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left,
            });
            grid.Children.Add(new TextBlock
            {
                Text = ev.Meaning + (ev.UsuallyHarmless ? L.T(" (معمولاً بی‌خطره)", " (usually harmless)") : ""), Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            });
            grid.Children.Add(new TextBlock { Text = L.T($"آخرین بار: {Fa.DayOf(ev.Last, DateTime.Now)} {Fa.Clock(ev.Last)}", $"Last: {Fa.DayOf(ev.Last, DateTime.Now)} {ev.Last:HH:mm}"), Style = St("Label"), Margin = new Thickness(0, 2, 0, 0) });
            _events.Children.Add(grid);
        }
    }
}

public sealed class SecurityPage : PageBase
{
    private readonly StackPanel _security = new();
    private readonly StackPanel _startup = new();
    private readonly StackPanel _permissions = new();
    private bool _busy;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("چند تنظیم مهم امنیتی ویندوز و برنامه‌هایی که با ویندوز بالا میان. این یه آنتی‌ویروس نیست؛ فقط وضعیت رو نشون می‌ده.",
        "A few key Windows security settings and the programs that start with Windows. This is not an antivirus; it only shows status.");

    public SecurityPage()
    {
        var root = new StackPanel();
        var (sCard, s) = Ui2.Card(L.T("تنظیمات امنیتی ویندوز", "Windows security settings"));
        s.Children.Add(_security);
        root.Children.Add(sCard);

        var (stCard, st) = Ui2.Card(L.T("برنامه‌های شروع ویندوز", "Programs that start with Windows"),
            L.T("اگه برنامه‌ی جدیدی بعد از آخرین بررسی اضافه شده باشه علامت می‌خوره و بهت خبر می‌دم.", "A program added since the last review is marked, and you get a notice."));
        st.Children.Add(_startup);
        root.Children.Add(stCard);

        var (pCard, p) = Ui2.Card(L.T("شب‌پا چه دسترسی‌هایی می‌خواد؟", "What access does Shabpa need?"),
            L.T("بیشتر قابلیت‌ها بدون ادمین کار می‌کنن. این‌ها فقط وقتی لازمن که از اون قابلیت‌ها استفاده کنی.", "Most features work without administrator rights. These are needed only if you use those features."));
        p.Children.Add(_permissions);
        root.Children.Add(pCard);
        Content = root;
    }

    public override void Refresh()
    {
        Load();
        ShowPermissions();
    }

    private void Load()
    {
        if (_busy) return;
        _busy = true;
        _ = Task.Run(() =>
        {
            var reg = new WindowsRegistryProbe();
            var security = Health.Security(reg, Health.ServiceStateNow, Hub.Elevated);
            var startup = Health.Startup(reg, Health.StartupFolders());
            List<StartupChange> changes;
            try { changes = Hub.CheckStartup(announce: false); }
            catch (Exception ex)
            {
                Log.Write($"startup compare: {ex.Message}");
                changes = [];
            }
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                ShowSecurity(security);
                ShowStartup(startup, changes);
            });
        });
    }

    private void ShowSecurity(List<HealthCheck> security)
    {
        _security.Children.Clear();
        foreach (var c in security) _security.Children.Add(HealthUi.Row(c));
    }

    private void ShowStartup(List<StartupItem> items, List<StartupChange> changes)
    {
        _startup.Children.Clear();
        var changed = changes.ToDictionary(c => c.Item.Key, c => c.Kind);
        if (changes.Count > 0)
        {
            var banner = new Border { Background = Res("GoldSoft"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 6) };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = L.T($"{Fa.Digits(changes.Count.ToString())} مورد از آخرین بررسی تازه یا تغییرکرده.", $"{changes.Count} item(s) new or changed since the last review."),
                Style = St("Body"), Foreground = Res("Gold"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            });
            var accept = new Button { Style = St("Btn"), Content = L.T("می‌شناسمشون؛ فهرست رو بپذیر", "I know them; accept this list"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            accept.Click += (_, _) =>
            {
                Hub.AcceptStartup();
                Hub.State.StartupAnnounced.Clear();
                Load();
            };
            sp.Children.Add(accept);
            banner.Child = sp;
            _startup.Children.Add(banner);
        }
        foreach (var item in items.OrderByDescending(i => changed.ContainsKey(i.Key)).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            var title = item.Name + (changed.TryGetValue(item.Key, out var kind) ? "   " + (kind == "new" ? L.T("● تازه", "● new") : L.T("● تغییر کرده", "● changed")) : "");
            text.Children.Add(new TextBlock { Text = title, Style = St("H2"), FontSize = 14, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, Foreground = changed.ContainsKey(item.Key) ? Res("Gold") : Res("Text") });
            text.Children.Add(new TextBlock { Text = item.Command, Style = St("Label"), TextTrimming = TextTrimming.CharacterEllipsis, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = item.Command });
            foreach (var note in Health.Notes(item))
                text.Children.Add(new TextBlock { Text = "⚠ " + note, Style = St("Label"), Foreground = Res("Gold"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            row.Children.Add(text);
            if (item.CanToggle)
            {
                var toggle = new Button { Style = St("Btn"), Content = item.Enabled ? L.T("غیرفعال کردن", "Turn off") : L.T("فعال کردن", "Turn on"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                var captured = item;
                toggle.Click += (_, _) =>
                {
                    var target = !captured.Enabled;
                    if (!ConfirmWindow.Ask(L.T("برنامه‌ی شروع ویندوز", "Startup program"),
                            target ? L.T($"«{captured.Name}» دوباره با ویندوز شروع می‌شه.", $"“{captured.Name}” will start with Windows again.")
                                   : L.T($"«{captured.Name}» دیگه با ویندوز شروع نمی‌شه. پاک نمی‌شه و هر وقت خواستی دوباره روشنش می‌کنی.", $"“{captured.Name}” will no longer start with Windows. It is not removed; you can turn it back on."),
                            target ? L.T("فعال کن", "Turn on") : L.T("غیرفعال کن", "Turn off"))) return;
                    if (Health.SetStartupEnabled(captured.Name, target) is { } error) Hub.Notify(L.T("انجام نشد", "Failed"), error);
                    Load();
                };
                Grid.SetColumn(toggle, 1);
                row.Children.Add(toggle);
            }
            else if (item.Source.StartsWith("HKLM", StringComparison.Ordinal))
            {
                var hint = new TextBlock { Text = L.T("برای تغییر: Task Manager ← Startup", "To change: Task Manager → Startup"), Style = St("Label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                Grid.SetColumn(hint, 1);
                row.Children.Add(hint);
            }
            _startup.Children.Add(row);
        }
        if (items.Count == 0) _startup.Children.Add(new TextBlock { Text = L.T("موردی پیدا نشد.", "None found."), Style = St("Body") });
    }

    private void ShowPermissions()
    {
        _permissions.Children.Clear();
        var admin = Hub.Elevated;
        _permissions.Children.Add(new TextBlock
        {
            Text = admin ? L.T("الان با دسترسی ادمین اجرا شده.", "Running with administrator rights now.") : L.T("الان بدون دسترسی ادمین اجرا شده.", "Running without administrator rights now."),
            Style = St("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6),
        });
        foreach (var (fa, en, needsAdmin, whyFa, whyEn) in new[]
        {
            ("کارها، تمرکز، پشتیبان‌گیری، فایل‌های تکراری", "Tasks, focus, backup, duplicates", false, "فقط به پوشه‌هایی که خودت انتخاب می‌کنی دسترسی داره.", "Only touches folders you choose."),
            ("خاموشی، خواب، زمان‌بندی، تلگرام", "Shutdown, sleep, schedules, Telegram", false, "از خود ویندوز کمک می‌گیره؛ ادمین لازم نیست.", "Uses Windows' own commands; no admin needed."),
            ("فریم‌ریت بازی", "Game frame rate", true, "از رویدادهای گرافیکی ویندوز می‌خونه که فقط ادمین می‌تونه گوش بده.", "Reads Windows graphics events that only administrators may listen to."),
            ("حسگرهای پردازنده و مادربورد", "CPU and board sensors", true, "برای خوندن توان و دمای پردازنده درایور حسگر لازمه.", "Reading CPU power and temperature needs a sensor driver."),
            ("محدودکردن سرعت هر برنامه", "Per-app speed limits", true, "برای کنترل ترافیک شبکه از یه درایور بسته‌گیر استفاده می‌کنه.", "Uses a packet driver to control network traffic."),
            ("خالی کردن کش رم", "Purging the RAM cache", true, "پاک‌کردن کش سیستم فقط با ادمین ممکنه.", "Clearing the system cache needs administrator rights."),
            ("پاک‌سازی خودکار فایل‌ها", "Clearing files", false, "فقط فایل‌هایی که تیک زدی و فقط به سطل بازیافت.", "Only files you tick, and only into the Recycle Bin."),
        })
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = L.T(fa, en), Style = St("Body"), FontWeight = FontWeights.SemiBold });
            t.Children.Add(new TextBlock { Text = L.T(whyFa, whyEn), Style = St("Label"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(t);
            var pill = new Border
            {
                Style = St("Pill"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                Background = needsAdmin ? (admin ? Res("GreenSoft") : Res("GoldSoft")) : Res("Panel2"),
                Child = new TextBlock
                {
                    Text = needsAdmin ? (admin ? L.T("ادمین ✓", "admin ✓") : L.T("نیاز به ادمین", "needs admin")) : L.T("بدون ادمین", "no admin"),
                    FontSize = 11.5, Foreground = needsAdmin ? (admin ? Res("Green") : Res("Gold")) : Res("Muted"),
                },
            };
            Grid.SetColumn(pill, 1);
            row.Children.Add(pill);
            _permissions.Children.Add(row);
        }
    }
}

/// <summary>A readable report of the above, with personal details removed for the person to review before sharing.</summary>
public sealed class HealthReportPage : PageBase
{
    private readonly TextBox _text = new()
    {
        Style = St("Input"), IsReadOnly = false, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 320, MaxHeight = 520,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas"), FlowDirection = FlowDirection.LeftToRight, FontSize = 12,
    };
    private readonly CheckBox _redact = new() { Style = St("Check"), Content = L.T("نام کاربری، نام کامپیوتر، IP و ایمیل حذف بشه", "Remove user name, computer name, IP addresses and e-mail"), IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private string _raw = "";
    private bool _busy;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("یه گزارش متنی برای نشون‌دادن به پشتیبانی. قبل از هر کاری خودت می‌خونیش و اطلاعات شخصی حذف می‌شه. چیزی خودکار فرستاده نمی‌شه.",
        "A text report to show support. You read it first and personal details are removed. Nothing is sent automatically.");

    public HealthReportPage()
    {
        var root = new StackPanel();
        var (card, body) = Ui2.Card(L.T("گزارش", "Report"));
        var make = new Button { Style = St("BtnPrimary"), Content = L.T("ساخت گزارش", "Build the report"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        make.Click += (_, _) => Build();
        body.Children.Add(make);
        _redact.Click += (_, _) => Apply();
        body.Children.Add(_redact);
        body.Children.Add(Ui2.Label(L.T("هر چیزی که نمی‌خوای هم می‌تونی خودت اینجا پاک کنی.", "You can also delete anything you do not want to share right here."), 8, 4));
        _text.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(_text);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var copy = new Button { Style = St("Btn"), Content = L.T("کپی", "Copy"), Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_text.Text);
                _status.Text = L.T("کپی شد.", "Copied.");
            }
            catch (System.Runtime.InteropServices.ExternalException) { _status.Text = L.T("کلیپ‌بورد در دسترس نبود.", "The clipboard was busy."); }
        };
        var save = new Button { Style = St("Btn"), Content = L.T("ذخیره به‌صورت فایل…", "Save as a file…") };
        save.Click += (_, _) => Save();
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        body.Children.Add(buttons);
        body.Children.Add(_status);
        root.Children.Add(card);
        Content = root;
    }

    private void Apply() => _text.Text = _redact.IsChecked == true ? Redactor.Redact(_raw) : _raw;

    private void Build()
    {
        if (_busy) return;
        _busy = true;
        _status.Text = L.T("در حال جمع‌آوری…", "Collecting…");
        _ = Task.Run(() =>
        {
            var reg = new WindowsRegistryProbe();
            var health = new List<HealthCheck>();
            health.AddRange(Health.DisksNow());
            health.Add(Health.PendingReboot(reg));
            health.Add(Health.WindowsUpdate(reg, DateTime.UtcNow));
            health.AddRange(Health.Services(reg, Health.ServiceStateNow));
            var security = Health.Security(reg, Health.ServiceStateNow, Hub.Elevated);
            var events = Health.RecentErrors();
            var startup = Health.Startup(reg, Health.StartupFolders());
            var text = HealthReport.Build(health, events, security, startup, Core.Updater.Current.ToString(3), HealthUi.WindowsVersion(), DateTime.Now);
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                _raw = text;
                Apply();
                _status.Text = L.T("گزارش آماده‌ست. بخونش و هر چی خواستی پاک کن.", "The report is ready. Read it and delete anything you like.");
            });
        });
    }

    private void Save()
    {
        if (_text.Text.Length == 0) return;
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            FileName = $"shabpa-report-{DateTime.Now:yyyyMMdd}.txt", Filter = "Text|*.txt", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, _text.Text);
            _status.Text = L.T("ذخیره شد.", "Saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = L.T("ذخیره نشد.", "Could not save.");
        }
    }
}
