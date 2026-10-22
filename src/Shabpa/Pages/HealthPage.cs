using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>«سلامت و امنیت»: a plain-language look at disks, updates, services, errors, security settings and startup
/// programs, with a report that is reviewed and redacted before it is shared. It says what it sees; it does not claim to
/// repair anything or to be an antivirus.</summary>
public sealed class HealthPage : TabsPage
{
    public static readonly string[] TabKeys = ["overview", "security", "apps", "drivers", "report"];

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");

    public HealthPage()
    {
        Add("overview", L.T("سلامت سیستم", "System health"), new HealthOverviewPage());
        Add("security", L.T("امنیت و استارتاپ", "Security & startup"), new SecurityPage());
        Add("apps", L.T("به‌روزرسانی برنامه‌ها", "Program updates"), new AppUpdatesPage());
        Add("drivers", L.T("درایورها", "Drivers"), new DriversPage());
        Add("report", L.T("گزارش", "Report"), new HealthReportPage());
    }
}

internal static class HealthUi
{
    /// <summary>One check as a list item: state icon (green tick, amber or red warning, grey for unknown), title, detail and advice.
    /// <paramref name="onIgnore"/> adds an "Ignore" button to a warning; <paramref name="onRestore"/> turns the row into an ignored one with a "Bring back" button.</summary>
    public static FrameworkElement Row(HealthCheck check, bool first = false, Action? onIgnore = null, Action? onRestore = null)
    {
        var (icon, tint) = check.State switch
        {
            CheckState.Good => ("chk", "HueB"),
            CheckState.Attention => ("warn", "HueC"),
            CheckState.Problem => ("warn", "HueE"),
            _ => ("min", "Accent"),
        };
        UIElement? trailing = null;
        if (onRestore is not null) trailing = Small(L.T("برگردوندن", "Bring back"), onRestore);
        else if (onIgnore is not null && check.State != CheckState.Good) trailing = Small(L.T("نادیده گرفتن", "Ignore"), onIgnore);
        return ListUi.Item(icon, tint, check.Title, check.Detail, trailing, first, note: check.Advice.Length > 0 ? check.Advice : null, neutral: onRestore is not null || check.State == CheckState.Unknown);
    }

    private static Button Small(string text, Action click)
    {
        var b = ListUi.Btn(null, text, "Btn", click);
        b.Padding = new Thickness(14, 7, 14, 7);
        return b;
    }

    public static bool IsIgnored(HealthCheck c) => Hub.I.State.HealthIgnored.Contains(c.Id);

    public static void SetIgnored(HealthCheck c, bool ignored)
    {
        var list = Hub.I.State.HealthIgnored;
        if (ignored && !list.Contains(c.Id)) list.Add(c.Id);
        else if (!ignored) list.Remove(c.Id);
        Hub.I.Store.Save();
    }

    /// <summary>The ignored warnings, under a small heading, each with a button to bring it back.</summary>
    public static void AddIgnored(Panel host, Mo.Memo memo, IReadOnlyList<HealthCheck> ignored, Action changed)
    {
        if (ignored.Count == 0) return;
        host.Children.Add(memo.Item("ign-head" + ignored.Count, new TextBlock
        {
            Text = L.T($"نادیده گرفته‌شده ({Fa.Digits(ignored.Count.ToString())})", $"Ignored ({ignored.Count})"),
            Style = K.Style("Label"), Margin = new Thickness(0, 16, 0, 2),
        }));
        var first = true;
        foreach (var c in ignored)
        {
            var captured = c;
            host.Children.Add(memo.Item("ign:" + c.Id + c.State, Row(c, first, onRestore: () => { SetIgnored(captured, false); changed(); })));
            first = false;
        }
    }

    public static string WindowsVersion() => Environment.OSVersion.VersionString;

    /// <summary>Two-column page grid: <paramref name="wideSpans"/> is the column span of each block (rows by <paramref name="wideRows"/>).</summary>
    public static void Layout(Grid grid, bool wide, (FrameworkElement block, int row, int col, int span)[] wideCells, (FrameworkElement block, int row)[] narrowRows)
    {
        while (grid.RowDefinitions.Count < Math.Max(wideCells.Max(c => c.row), narrowRows.Max(r => r.row)) + 1) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (wide) foreach (var (block, row, col, span) in wideCells) ListUi.Place(block, row, col, span);
        else foreach (var (block, row) in narrowRows) ListUi.Place(block, row, 0, 12);
    }
}

public sealed class HealthOverviewPage : PageBase
{
    private readonly StackPanel _checks = new();
    private readonly StackPanel _events = new();
    private readonly TextBlock _status = new() { Style = St("Body"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly RingGauge _ring = new() { Width = 176, Height = 176, ViewSize = 200, RingRadius = 84, Thickness = 12, Ticks = false, Knob = false };
    private readonly TextBlock _score = new() { FontSize = 46, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight, LineHeight = 54, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Text = "—", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StatusBadge _verdict = new(L.T("در حال بررسی…", "Checking…"), "Accent", "ecg") { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly StatCard _good = new(L.T("سالم", "Healthy"), "chk", "HueB", meter: true);
    private readonly StatCard _attention = new(L.T("نیازمند توجه", "Needs attention"), "warn", "HueC", meter: true);
    private readonly StatCard _problem = new(L.T("مشکل‌دار", "Problems"), "warn", "HueE", meter: true);
    private readonly StatCard _errors = new(L.T("خطاهای ۷ روز اخیر", "Errors, last 7 days"), "alarm", "HueD", meter: true);
    private readonly Button _scan;
    private readonly Grid _grid = ListUi.PageGrid(3);
    private readonly StackPanel _left = new();
    private FrameworkElement _checksCard = null!, _eventsCard = null!;
    private bool? _wide;
    private bool _busy;
    private bool _first = true;
    private Mo.Memo _checkMemo, _eventMemo;
    private string _verdictKey = "";

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("وضعیت دیسک‌ها، به‌روزرسانی، سرویس‌ها و خطاهای اخیر به زبان ساده. فقط نشون می‌ده؛ ادعا نمی‌کنه چیزی رو تعمیر کرده.",
        "Disks, updates, services and recent errors in plain language. It only reports; it never claims to have repaired anything.");
    public override UIElement? HeaderAccessory => _scan;

    public HealthOverviewPage()
    {
        _scan = ListUi.Btn("ecg", L.T("بررسی دوباره", "Check again"), "BtnPrimary", Load);
        _checkMemo = new Mo.Memo(_checks);
        _eventMemo = new Mo.Memo(_events);

        // score card: ring + verdict
        var ringBox = new Grid { Width = 176, Height = 176, Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
        ringBox.Children.Add(_ring);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        mid.Children.Add(_score);
        mid.Children.Add(new TextBlock { Text = L.T("امتیاز سلامت", "Health score"), Style = St("Label"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
        ringBox.Children.Add(mid);
        var verdictBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        verdictBox.Children.Add(_verdict);
        verdictBox.Children.Add(_status);
        var score = new Grid();
        score.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        score.ColumnDefinitions.Add(new ColumnDefinition());
        score.Children.Add(ringBox);
        Grid.SetColumn(verdictBox, 1);
        score.Children.Add(verdictBox);
        var scoreCard = new GlassCard { Padding = new Thickness(26, 22, 26, 22), Margin = new Thickness(8), Child = score };

        // the four counters (2 x 2 under the score)
        var counters = new Grid { Margin = new Thickness(0) };
        counters.ColumnDefinitions.Add(new ColumnDefinition());
        counters.ColumnDefinitions.Add(new ColumnDefinition());
        counters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        counters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var cards = new[] { _good, _attention, _problem, _errors };
        for (var i = 0; i < cards.Length; i++)
        {
            cards[i].Margin = new Thickness(8);
            Grid.SetRow(cards[i], i / 2);
            Grid.SetColumn(cards[i], i % 2);
            counters.Children.Add(cards[i]);
            cards[i].Set("—", L.T("مورد", "items"));
        }
        _errors.Meter!.Visibility = Visibility.Collapsed;
        _left.Children.Add(scoreCard);
        _left.Children.Add(counters);

        var (card, body) = Ui2.Card(L.T("وضعیت کلی", "Overview"));
        card.Margin = new Thickness(8);
        _checks.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_checks);
        _checksCard = card;
        var (eCard, e) = Ui2.Card(L.T("خطاهای ۷ روز اخیر", "Errors in the last 7 days"),
            L.T("از لاگ رویدادهای ویندوز، گروه‌بندی‌شده. بیشترش بی‌خطره؛ فقط مواردی که تکرار می‌شن مهمن.", "From the Windows event log, grouped. Most are harmless; repeated ones matter."));
        eCard.Margin = new Thickness(8);
        _events.Margin = new Thickness(0, 6, 0, 0);
        e.Children.Add(_events);
        _eventsCard = eCard;

        _grid.Children.Add(_left);
        _grid.Children.Add(_checksCard);
        _grid.Children.Add(_eventsCard);
        Content = _grid;
        SizeChanged += (_, args) => ApplyLayout(args.NewSize.Width > 900);
        ApplyLayout(true);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        HealthUi.Layout(_grid, wide,
            [(_left, 0, 0, 5), (_checksCard, 0, 5, 7), (_eventsCard, 1, 0, 12)],
            [(_left, 0), (_checksCard, 1), (_eventsCard, 2)]);
    }

    public override void Refresh()
    {
        if (_checks.Children.Count == 0 && !_busy) Load();
    }

    private void Load()
    {
        if (_busy) return;
        _busy = true;
        Mo.Say(_status, L.T("در حال بررسی…", "Checking…"));
        _verdict.Set(L.T("در حال بررسی…", "Checking…"), "Accent", "ecg");
        _verdictKey = "";
        _scan.IsEnabled = false;
        _checkMemo = new Mo.Memo(_checks);
        _eventMemo = new Mo.Memo(_events);
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
                _scan.IsEnabled = true;
                Show(list, events);
            });
        });
    }

    private List<HealthCheck> _lastList = [];
    private List<EventSummary> _lastEvents = [];

    private void Show(List<HealthCheck> all, List<EventSummary> events)
    {
        _lastList = all;
        _lastEvents = events;
        var list = all.Where(c => !HealthUi.IsIgnored(c)).ToList();
        var ignored = all.Where(HealthUi.IsIgnored).ToList();
        var problems = list.Count(c => c.State == CheckState.Problem);
        var attention = list.Count(c => c.State == CheckState.Attention);
        var good = list.Count(c => c.State == CheckState.Good);
        Mo.Say(_status, problems + attention == 0
            ? L.T("همه‌چیز عادی به نظر می‌رسه ✅", "Everything looks normal ✅")
            : L.T($"{Fa.Digits(problems.ToString())} مورد مشکل‌دار و {Fa.Digits(attention.ToString())} مورد نیازمند توجه.", $"{problems} problem(s) and {attention} item(s) that need attention."));

        // score: derived only from the states reported below (100 minus 20 per problem and 8 per item needing attention)
        var score = Math.Clamp(100 - problems * 20 - attention * 8, 0, 100);
        Mo.CountInt(_score, score, 1100);
        _ring.ArcBrush = score >= 70 ? null : score >= 40 ? Res("Gold") : Res("Red");
        if (_first) { _first = false; _ring.AnimateTo(score / 100.0); }
        else _ring.AnimateTo(score / 100.0, 1000);
        if (score >= 90) _verdict.Set(L.T("عالی", "Excellent"), "Green", "chk");
        else if (score >= 70) _verdict.Set(L.T("خوب", "Good"), "Green", "chk");
        else if (score >= 40) _verdict.Set(L.T("نیازمند توجه", "Needs attention"), "Gold", "warn");
        else _verdict.Set(L.T("مشکل‌دار", "Problem"), "Red", "warn");
        var verdictNow = score >= 90 ? "a" : score >= 70 ? "b" : score >= 40 ? "c" : "d";
        if (verdictNow != _verdictKey) { _verdictKey = verdictNow; Motion.Pop(_verdict); }

        var total = Math.Max(1, list.Count);
        void Counter(StatCard card, int n, string unit, double? meter)
        {
            var before = card.Meter?.Value ?? 0;
            card.Set(Fa.Digits(n.ToString()), unit, meter: meter);
            Mo.CountInt(card.Value, n, 900);
            if (meter is not null && card.Meter is not null) Mo.Fill(card.Meter, before, 900);
        }
        Counter(_good, good, L.T("مورد", "items"), 100.0 * good / total);
        Counter(_attention, attention, L.T("مورد", "items"), 100.0 * attention / total);
        Counter(_problem, problems, L.T("مورد", "items"), 100.0 * problems / total);
        Counter(_errors, events.Count, L.T("گروه", "groups"), null);

        _checks.Children.Clear();
        var first = true;
        foreach (var c in list.OrderByDescending(c => c.State == CheckState.Problem).ThenByDescending(c => c.State == CheckState.Attention))
        {
            var captured = c;
            _checks.Children.Add(_checkMemo.Item(c.Title + c.State, HealthUi.Row(c, first, onIgnore: () => { HealthUi.SetIgnored(captured, true); Show(_lastList, _lastEvents); })));
            first = false;
        }
        HealthUi.AddIgnored(_checks, _checkMemo, ignored, () => Show(_lastList, _lastEvents));
        _checkMemo.End();

        _events.Children.Clear();
        if (events.Count == 0)
            _events.Children.Add(new TextBlock { Text = L.T("خطای مهمی ثبت نشده (یا لاگ خونده نشد).", "No notable errors were recorded (or the log could not be read)."), Style = St("Body"), Margin = new Thickness(0, 6, 0, 0) });
        var firstEvent = true;
        foreach (var ev in events)
        {
            _events.Children.Add(_eventMemo.Item($"{ev.Provider}#{ev.Id}", ListUi.Item("alarm", ev.Critical ? "HueE" : "HueC",
                $"{ev.Provider} · #{ev.Id} · ×{Fa.Digits(ev.Count.ToString())}",
                ev.Meaning + (ev.UsuallyHarmless ? L.T(" (معمولاً بی‌خطره)", " (usually harmless)") : ""), null, firstEvent,
                note: L.T($"آخرین بار: {Fa.DayOf(ev.Last, DateTime.Now)} {Fa.Clock(ev.Last)}", $"Last: {Fa.DayOf(ev.Last, DateTime.Now)} {ev.Last:HH:mm}"), titleLtr: true)));
            firstEvent = false;
        }
        _eventMemo.End();
    }
}

public sealed class SecurityPage : PageBase
{
    private readonly StackPanel _security = new();
    private readonly StackPanel _startup = new();
    private readonly StackPanel _permissions = new();
    private readonly Grid _grid = ListUi.PageGrid(2);
    private FrameworkElement _sCard = null!, _stCard = null!, _pCard = null!;
    private bool? _wide;
    private bool _busy;
    private Mo.Memo _secMemo = null!, _startMemo = null!, _permMemo = null!;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("چند تنظیم مهم امنیتی ویندوز و برنامه‌هایی که با ویندوز بالا میان. این یه آنتی‌ویروس نیست؛ فقط وضعیت رو نشون می‌ده.",
        "A few key Windows security settings and the programs that start with Windows. This is not an antivirus; it only shows status.");

    public SecurityPage()
    {
        _secMemo = new Mo.Memo(_security);
        _startMemo = new Mo.Memo(_startup);
        _permMemo = new Mo.Memo(_permissions);
        var (sCard, s) = Ui2.Card(L.T("تنظیمات امنیتی ویندوز", "Windows security settings"));
        _security.Margin = new Thickness(0, 6, 0, 0);
        s.Children.Add(_security);
        _sCard = sCard;

        var (stCard, st) = Ui2.Card(L.T("برنامه‌های شروع ویندوز", "Programs that start with Windows"),
            L.T("اگه برنامه‌ی جدیدی بعد از آخرین بررسی اضافه شده باشه علامت می‌خوره و بهت خبر می‌دم.", "A program added since the last review is marked, and you get a notice."));
        _startup.Margin = new Thickness(0, 6, 0, 0);
        st.Children.Add(_startup);
        _stCard = stCard;

        var (pCard, p) = Ui2.Card(L.T("شب‌پا چه دسترسی‌هایی می‌خواد؟", "What access does Shabpa need?"),
            L.T("بیشتر قابلیت‌ها بدون ادمین کار می‌کنن. این‌ها فقط وقتی لازمن که از اون قابلیت‌ها استفاده کنی.", "Most features work without administrator rights. These are needed only if you use those features."));
        _permissions.Margin = new Thickness(0, 6, 0, 0);
        p.Children.Add(_permissions);
        _pCard = pCard;

        foreach (var c in new[] { _sCard, _stCard, _pCard })
        {
            c.Margin = new Thickness(8);
            _grid.Children.Add(c);
        }
        Content = _grid;
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 900);
        ApplyLayout(true);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        while (_grid.RowDefinitions.Count < 3) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (wide)
        {
            ListUi.Place(_sCard, 0, 0, 5);
            ListUi.Place(_stCard, 0, 5, 7);
            ListUi.Place(_pCard, 1, 0, 12);
        }
        else
        {
            ListUi.Place(_sCard, 0, 0, 12);
            ListUi.Place(_stCard, 1, 0, 12);
            ListUi.Place(_pCard, 2, 0, 12);
        }
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
        // the startup comparison touches saved state, so it is done here on the UI thread (a few registry reads)
        List<StartupChange> changes;
        try { changes = Hub.CheckStartup(announce: false); }
        catch (Exception ex)
        {
            Log.Write($"startup compare: {ex.Message}");
            changes = [];
        }
        _ = Task.Run(() =>
        {
            var reg = new WindowsRegistryProbe();
            var security = Health.Security(reg, Health.ServiceStateNow, Hub.Elevated, Health.AntivirusNow());
            var startup = Health.Startup(reg, Health.StartupFolders());
            Dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                ShowSecurity(security);
                ShowStartup(startup, changes);
            });
        });
    }

    private List<HealthCheck> _lastSecurity = [];

    private void ShowSecurity(List<HealthCheck> all)
    {
        _lastSecurity = all;
        _security.Children.Clear();
        _secMemo.Begin();
        var first = true;
        foreach (var c in all.Where(c => !HealthUi.IsIgnored(c)))
        {
            var captured = c;
            _security.Children.Add(_secMemo.Item(c.Title + c.State, HealthUi.Row(c, first, onIgnore: () => { HealthUi.SetIgnored(captured, true); ShowSecurity(_lastSecurity); })));
            first = false;
        }
        HealthUi.AddIgnored(_security, _secMemo, all.Where(HealthUi.IsIgnored).ToList(), () => ShowSecurity(_lastSecurity));
        _secMemo.End();
    }

    private void ShowStartup(List<StartupItem> items, List<StartupChange> changes)
    {
        _startup.Children.Clear();
        _startMemo.Begin();
        var changed = changes.ToDictionary(c => c.Item.Key, c => c.Kind);
        if (changes.Count > 0)
        {
            var banner = new Border { Background = Res("GoldSoft"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 4, 0, 8) };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = L.T($"{Fa.Digits(changes.Count.ToString())} مورد از آخرین بررسی تازه یا تغییرکرده.", $"{changes.Count} item(s) new or changed since the last review."),
                Style = St("Body"), Foreground = Res("Gold"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            });
            var accept = ListUi.Btn("chk", L.T("می‌شناسمشون؛ فهرست رو بپذیر", "I know them; accept this list"), "Btn", () =>
            {
                Hub.AcceptStartup();
                Hub.State.StartupAnnounced.Clear();
                Load();
            });
            accept.HorizontalAlignment = HorizontalAlignment.Left;
            accept.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(accept);
            banner.Child = sp;
            _startup.Children.Add(_startMemo.Item("banner", banner));
        }
        var first = true;
        foreach (var item in items.OrderByDescending(i => changed.ContainsKey(i.Key)).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var isChanged = changed.TryGetValue(item.Key, out var kind);
            var title = item.Name + (isChanged ? "   " + (kind == "new" ? L.T("● تازه", "● new") : L.T("● تغییر کرده", "● changed")) : "");
            UIElement? trailing = null;
            if (item.CanToggle)
            {
                var captured = item;
                var toggle = ListUi.Btn(null, item.Enabled ? L.T("غیرفعال کردن", "Turn off") : L.T("فعال کردن", "Turn on"), "Btn", () =>
                {
                    var target = !captured.Enabled;
                    if (!ConfirmWindow.Ask(L.T("برنامه‌ی شروع ویندوز", "Startup program"),
                            target ? L.T($"«{captured.Name}» دوباره با ویندوز شروع می‌شه.", $"“{captured.Name}” will start with Windows again.")
                                   : L.T($"«{captured.Name}» دیگه با ویندوز شروع نمی‌شه. پاک نمی‌شه و هر وقت خواستی دوباره روشنش می‌کنی.", $"“{captured.Name}” will no longer start with Windows. It is not removed; you can turn it back on."),
                            target ? L.T("فعال کن", "Turn on") : L.T("غیرفعال کن", "Turn off"))) return;
                    if (Health.SetStartupEnabled(captured.Name, target) is { } error) Hub.Notify(L.T("انجام نشد", "Failed"), error);
                    Load();
                });
                toggle.Padding = new Thickness(14, 7, 14, 7);
                trailing = toggle;
            }
            else if (item.Source.StartsWith("HKLM", StringComparison.Ordinal))
            {
                var how = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 170 };
                how.Children.Add(new TextBlock { Text = L.T("برای تغییر:", "To change:"), Style = St("Label") });
                how.Children.Add(new TextBlock { Text = "Task Manager → Startup", Style = St("Label"), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap });
                trailing = how;
            }
            var row = ListUi.Item("bolt", isChanged ? "HueC" : "HueA", title, item.Command, trailing, first, trimSub: true, subLtr: true, titleTint: isChanged ? "Gold" : null, titleLtr: true);
            foreach (var note in Health.Notes(item))
                ListUi.AddText(row, new TextBlock { Text = "⚠ " + note, Style = St("Label"), Foreground = Res("Gold"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            _startup.Children.Add(_startMemo.Item(item.Key + item.Enabled + isChanged, row));
            first = false;
        }
        _startMemo.End();
        if (items.Count == 0) _startup.Children.Add(new TextBlock { Text = L.T("موردی پیدا نشد.", "None found."), Style = St("Body") });
    }

    private void ShowPermissions()
    {
        _permissions.Children.Clear();
        _permMemo.Begin();
        var admin = Hub.Elevated;
        var status = new StatusBadge(admin ? L.T("الان با دسترسی ادمین اجرا شده.", "Running with administrator rights now.") : L.T("الان بدون دسترسی ادمین اجرا شده.", "Running without administrator rights now."),
            admin ? "Green" : "Accent", admin ? "lock" : "user") { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
        _permissions.Children.Add(_permMemo.Item("status", status));
        var first = true;
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
            var pill = needsAdmin
                ? ListUi.Pill(admin ? L.T("ادمین", "admin") : L.T("نیاز به ادمین", "needs admin"), admin ? "Green" : "Gold")
                : ListUi.PillMuted(L.T("بدون ادمین", "no admin"));
            // the tick is a drawn mark (never a text glyph, which the bidi algorithm may reorder), always left-short / right-long
            if (needsAdmin && admin && pill.Child is TextBlock pillText)
            {
                pill.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new Icon("chk", 12, Res("Green"), 2.2) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) }, pillText } };
            }
            _permissions.Children.Add(_permMemo.Item(en + admin, ListUi.Item(needsAdmin ? "lock" : "chk", needsAdmin ? (admin ? "HueB" : "HueC") : "HueA", L.T(fa, en), L.T(whyFa, whyEn), pill, first)));
            first = false;
        }
        _permMemo.End();
    }
}

/// <summary>A readable report of the above, with personal details removed for the person to review before sharing.</summary>
public sealed class HealthReportPage : PageBase
{
    private readonly TextBox _text = new()
    {
        Style = St("Input"), IsReadOnly = false, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 320, MaxHeight = 520,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas"), FlowDirection = FlowDirection.LeftToRight, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Top,
    };
    private readonly CheckBox _redact = new Switch { IsChecked = true };
    private readonly TextBlock _status = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private string _raw = "";
    private bool _busy;

    public override string PageTitle => L.T("سلامت و امنیت", "Health & security");
    public override string PageSubtitle => L.T("یه گزارش متنی برای نشون‌دادن به پشتیبانی. قبل از هر کاری خودت می‌خونیش و اطلاعات شخصی حذف می‌شه. چیزی خودکار فرستاده نمی‌شه.",
        "A text report to show support. You read it first and personal details are removed. Nothing is sent automatically.");

    public HealthReportPage()
    {
        var root = ListUi.PageGrid(1);
        var (card, body) = Ui2.Card(L.T("گزارش", "Report"));
        card.Margin = new Thickness(8);
        ListUi.Place(card, 0, 0, 12);
        var make = ListUi.Btn("tasks", L.T("ساخت گزارش", "Build the report"), "BtnPrimary", Build);
        make.HorizontalAlignment = HorizontalAlignment.Left;
        make.Margin = new Thickness(0, 14, 0, 4);
        body.Children.Add(make);
        _redact.Click += (_, _) => Apply();
        body.Children.Add(K.SettingRow(L.T("نام کاربری، نام کامپیوتر، IP و ایمیل حذف بشه", "Remove user name, computer name, IP addresses and e-mail"), _redact, first: true));
        body.Children.Add(Ui2.Label(L.T("هر چیزی که نمی‌خوای هم می‌تونی خودت اینجا پاک کنی.", "You can also delete anything you do not want to share right here."), 8, 6));
        body.Children.Add(_text);
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var copy = ListUi.Btn("copy", L.T("کپی", "Copy"), "Btn", () =>
        {
            try
            {
                Clipboard.SetText(_text.Text);
                Mo.Say(_status, L.T("کپی شد.", "Copied."));
            }
            catch (System.Runtime.InteropServices.ExternalException) { Mo.Say(_status, L.T("کلیپ‌بورد در دسترس نبود.", "The clipboard was busy.")); }
        });
        copy.Margin = new Thickness(0, 0, 8, 0);
        var save = ListUi.Btn("dl", L.T("ذخیره به‌صورت فایل…", "Save as a file…"), "Btn", Save);
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
        Mo.Say(_status, L.T("در حال جمع‌آوری…", "Collecting…"));
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
                Mo.Flash(_text);
                Mo.Say(_status, L.T("گزارش آماده‌ست. بخونش و هر چی خواستی پاک کن.", "The report is ready. Read it and delete anything you like."));
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
            Mo.Say(_status, L.T("ذخیره شد.", "Saved."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Mo.Say(_status, L.T("ذخیره نشد.", "Could not save."));
        }
    }
}
