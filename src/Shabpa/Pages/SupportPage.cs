using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Support: send a ticket through GitHub or through the project's own server, follow the answers, and (for the
/// project owner, with the admin key) read and answer every ticket.</summary>
public sealed class SupportPage : PageBase
{
    private string _category = "bug";
    private string _via = "github";
    private readonly WrapPanel _categoryChips = new();
    private readonly WrapPanel _viaChips = new();
    private readonly TextBox _title = new(), _body = new(), _contact = new(), _endpoint = new();
    private readonly Kit.Switch _diag = new();
    private readonly TextBlock _diagPreview = new(), _viaNote = new(), _sendNote = new(), _adminNote = new();
    private readonly StackPanel _mine = new(), _all = new();
    private readonly WrapPanel _mineFilter = new(), _allFilter = new();
    private string _mineFilterKey = "all", _allFilterKey = "all";
    private List<TicketInfo> _allTickets = [];
    private readonly PasswordBox _adminKey = new();
    private bool _busy;

    // layout: ticket form and "my tickets" side by side, the owner card under them (one column when the window is narrow)
    private readonly Grid _root = new() { Margin = new Thickness(-8, 0, -8, 0) };
    private FrameworkElement _ticketCard = null!, _mineCard = null!, _adminCard = null!;
    private bool? _wide;

    // rows already shown, so only a new ticket rises in (the page entrance handles the first draw)
    private readonly HashSet<string> _seen = [];
    private bool _primed;

    public override string PageTitle => L.T("پشتیبانی", "Support");
    public override string PageSubtitle => L.T("مشکل، پیشنهاد یا سؤالت رو بفرست و جوابش رو همین‌جا ببین.", "Send a problem, an idea or a question and see the answer here.");

    private static SupportClient Client => Hub.I.Support;

    public SupportPage()
    {
        for (var i = 0; i < 3; i++) _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < 12; i++) _root.ColumnDefinitions.Add(new ColumnDefinition());

        // ---- new ticket -------------------------------------------------------------------------------
        var (card, form) = Ui2.Card(L.T("تیکت جدید", "New ticket"),
            L.T("هر چی می‌نویسی فقط بعد از زدن دکمه‌ی ارسال و تأیید خودت می‌ره. شب‌پا خودش چیزی نمی‌فرسته.",
                "Whatever you write is only sent after you press Send and confirm. Shabpa never sends anything by itself."));
        form.Children.Add(Ui2.Label(L.T("موضوع", "Topic")));
        form.Children.Add(_categoryChips);
        form.Children.Add(Ui2.Label(L.T("عنوان", "Title")));
        StyleBox(_title, 0, false);
        _title.MaxLength = 120;
        form.Children.Add(_title);
        form.Children.Add(Ui2.Label(L.T("توضیح (چی شد؟ انتظارت چی بود؟)", "Details (what happened? what did you expect?)")));
        StyleBox(_body, 0, true);
        _body.MaxLength = 4000;
        form.Children.Add(_body);
        form.Children.Add(Ui2.Label(L.T("راه تماس (اختیاری؛ فقط اگه می‌خوای بیرون از برنامه هم جواب بدم)", "Contact (optional; only if you want an answer outside the app)")));
        StyleBox(_contact, 0, false);
        _contact.MaxLength = 100;
        form.Children.Add(_contact);

        _diag.Content = new TextBlock
        {
            Text = L.T("اطلاعات فنی ساده هم همراهش بره (ورژن برنامه، ویندوز، ادمین بودن)", "Include simple technical details (app version, Windows, admin or not)"),
            TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 6, 0, 6),
        };
        _diag.HorizontalAlignment = HorizontalAlignment.Stretch;
        _diag.MinHeight = 44;
        _diag.Click += (_, _) => UpdateDiag();
        form.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 14, 0, 0) });
        form.Children.Add(_diag);
        _diagPreview.Style = St("Label");
        _diagPreview.TextWrapping = TextWrapping.Wrap;
        _diagPreview.FlowDirection = FlowDirection.LeftToRight;
        _diagPreview.FontFamily = new FontFamily("Consolas");
        _diagPreview.Margin = new Thickness(0);
        form.Children.Add(_diagPreview);

        form.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 6, 0, 0) });
        form.Children.Add(Ui2.Label(L.T("از کجا بفرستم؟", "Send through"), 10, 8));
        form.Children.Add(_viaChips);
        _viaNote.Style = St("Label");
        _viaNote.TextWrapping = TextWrapping.Wrap;
        _viaNote.Margin = new Thickness(0, 4, 0, 0);
        form.Children.Add(_viaNote);

        var send = new Button { Style = St("BtnPrimary"), Content = L.T("ارسال تیکت", "Send ticket"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(26, 10, 26, 10), Margin = new Thickness(0, 16, 0, 0) };
        send.Click += async (_, _) => await SendAsync();
        form.Children.Add(send);
        _sendNote.Style = St("Label");
        _sendNote.TextWrapping = TextWrapping.Wrap;
        _sendNote.Margin = new Thickness(0, 8, 0, 0);
        form.Children.Add(_sendNote);
        _ticketCard = card;

        // ---- my tickets -------------------------------------------------------------------------------
        var (mineCard, mine) = Ui2.Card(L.T("تیکت‌های من", "My tickets"));
        var refresh = new Button { Style = St("Btn"), Content = L.T("به‌روزرسانی", "Refresh"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        refresh.Click += async (_, _) => await RefreshMineAsync();
        mine.Children.Add(refresh);
        _mineFilter.Margin = new Thickness(0, 12, 0, 2);
        mine.Children.Add(_mineFilter);
        mine.Children.Add(_mine);
        FilterChips(_mineFilter, _mineFilterKey, v => { _mineFilterKey = v; ShowMineLocal(); });
        _mineCard = mineCard;

        // ---- project owner ----------------------------------------------------------------------------
        var (admin, adminBody) = Ui2.Card(L.T("مدیر پروژه", "Project owner"),
            L.T("اگه صاحب پروژه‌ای، کلید مدیر رو وارد کن تا همه‌ی تیکت‌ها رو ببینی و جواب بدی. کلید فقط روی همین کامپیوتر و رمزگذاری‌شده نگه داشته می‌شه.",
                "If you own the project, enter the admin key to see and answer every ticket. The key is kept only on this PC, encrypted."));
        _adminKey.Style = St("InputPass");
        _adminKey.Width = 360;
        _adminKey.HorizontalAlignment = HorizontalAlignment.Left;
        _adminKey.Margin = new Thickness(0, 12, 0, 0);
        _adminKey.FlowDirection = FlowDirection.LeftToRight;
        adminBody.Children.Add(_adminKey);
        StyleBox(_endpoint, 460, false);
        _endpoint.FlowDirection = FlowDirection.LeftToRight;
        _endpoint.Margin = new Thickness(0, 8, 0, 0);
        adminBody.Children.Add(Ui2.Label(L.T("آدرس سرور (اختیاری؛ اگه توی برنامه ثابت نشده)", "Server address (optional; if not built in)"), 10, 2));
        adminBody.Children.Add(_endpoint);
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var save = new Button { Style = St("BtnPrimary"), Content = L.T("ذخیره و ورود", "Save and sign in"), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += async (_, _) => await SaveAdminAsync();
        var forget = new Button { Style = St("Btn"), Content = L.T("خروج مدیر", "Sign out"), Margin = new Thickness(0, 0, 8, 0) };
        forget.Click += (_, _) => ForgetAdmin();
        var issues = new Button { Style = St("Btn"), Content = L.T("باز کردن ایشوهای گیت‌هاب", "Open GitHub issues") };
        issues.Click += (_, _) => OpenUrl(GitHubIssue.IssuesUrl);
        row.Children.Add(save);
        row.Children.Add(forget);
        row.Children.Add(issues);
        adminBody.Children.Add(row);
        _adminNote.Style = St("Label");
        _adminNote.TextWrapping = TextWrapping.Wrap;
        _adminNote.Margin = new Thickness(0, 8, 0, 0);
        adminBody.Children.Add(_adminNote);
        _allFilter.Margin = new Thickness(0, 8, 0, 2);
        adminBody.Children.Add(_allFilter);
        adminBody.Children.Add(_all);
        FilterChips(_allFilter, _allFilterKey, v => { _allFilterKey = v; ShowAll(); });
        _adminCard = admin;
        _adminCard.Visibility = OwnerVisible(Client.IsAdmin, Environment.GetEnvironmentVariable("SHABPA_OWNER")) ? Visibility.Visible : Visibility.Collapsed;

        foreach (var block in new[] { _ticketCard, _mineCard, _adminCard })
        {
            block.Margin = new Thickness(8);
            _root.Children.Add(block);
        }
        Content = _root;
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width > 900);
        ApplyLayout(true);

        Chips(_categoryChips, SupportInfo.Categories.Select(c => (c, SupportInfo.CategoryName(c))), _category, v => _category = v);
    }

    private static void Place(UIElement e, int row, int column, int span)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        if (wide)
        {
            Place(_ticketCard, 0, 0, 7);
            Place(_mineCard, 0, 7, 5);
            Place(_adminCard, 1, 0, 12);
        }
        else
        {
            Place(_ticketCard, 0, 0, 12);
            Place(_mineCard, 1, 0, 12);
            Place(_adminCard, 2, 0, 12);
        }
    }

    /// <param name="width">0 = fill the card; otherwise a fixed width at the start side.</param>
    private static void StyleBox(TextBox box, double width, bool multiline)
    {
        box.Style = St("Input");
        if (width > 0) box.Width = width;
        box.HorizontalAlignment = width > 0 ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        box.Padding = new Thickness(12, 8, 12, 8);
        if (!multiline) return;
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.MinHeight = 120;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser registered */ }
    }

    private void UpdateDiag()
    {
        _diagPreview.Text = _diag.IsChecked == true
            ? L.T("این متن همراه تیکت می‌ره:\n", "This text goes with the ticket:\n") + SupportInfo.Diagnostics(Hub.Elevated)
            : "";
    }

    /// <summary>The owner card is for the project owner only: it shows on a PC that is already signed in as admin, or when
    /// the environment variable SHABPA_OWNER=1 is set (how the owner opens it the first time). Everyone else never sees it.</summary>
    public static bool OwnerVisible(bool signedInAsAdmin, string? ownerVariable) => signedInAsAdmin || ownerVariable == "1";

    public override void Refresh()    {
        var s = Hub.Settings.Support;
        if (_contact.Text.Length == 0) _contact.Text = s.Contact;
        _endpoint.Text = s.Endpoint;
        var options = new List<(string, string)> { ("github", L.T("گیت‌هاب", "GitHub")) };
        if (Client.Available) options.Add(("server", L.T("سرور شب‌پا", "Shabpa server")));
        if (_via == "server" && !Client.Available) _via = "github";
        Chips(_viaChips, options, _via, v =>
        {
            _via = v;
            UpdateViaNote();
        });
        UpdateViaNote();
        UpdateDiag();
        ShowMineLocal();
        _adminNote.Text = Client.IsAdmin
            ? L.T("وارد شدی. تیکت‌ها رو با «به‌روزرسانی» تازه کن.", "Signed in. Refresh to load tickets.")
            : "";
        if (Client.IsAdmin && _all.Children.Count == 0) _ = RefreshAllAsync();
        if (Client.Available) _ = RefreshMineAsync();
    }

    /// <summary>Sets a status line and lets it rise in, so a result does not just appear.</summary>
    private static void Say(TextBlock note, string brush, string text)
    {
        note.Foreground = Res(brush);
        note.Text = text;
        if (text.Length > 0) Motion.Rise(note, 0, 8, 0.45);
    }

    private void UpdateViaNote()
    {
        _viaNote.Text = _via == "github"
            ? L.T("یه صفحه‌ی گیت‌هاب با متنت پر شده باز می‌شه و خودت اون‌جا «Submit» رو می‌زنی. حساب گیت‌هاب لازمه و تیکت عمومی می‌شه، پس رمز یا اطلاعات شخصی ننویس.",
                "A GitHub page opens with your text filled in and you press Submit there. It needs a GitHub account and the ticket is public, so do not write passwords or personal details.")
            : L.T("مستقیم به سرور پروژه می‌ره و خصوصیه. بدون حساب گیت‌هاب؛ جواب رو همین‌جا می‌بینی.",
                "Goes straight to the project's server and stays private. No GitHub account needed; you see the answer here.");
    }

    private async Task SendAsync()
    {
        if (_busy) return;
        var title = _title.Text.Trim();
        var body = _body.Text.Trim();
        if (title.Length < 3 || body.Length < 5)
        {
            Say(_sendNote, "Red", L.T("عنوان و توضیح رو کامل‌تر بنویس.", "Please write a fuller title and description."));
            return;
        }
        var diag = _diag.IsChecked == true ? SupportInfo.Diagnostics(Hub.Elevated) : "";
        var contact = _contact.Text.Trim();
        var where = _via == "github"
            ? L.T("یه صفحه‌ی گیت‌هاب با این متن باز می‌شه (عمومی).", "A GitHub page opens with this text (public).")
            : L.T("این متن به سرور پشتیبانی پروژه فرستاده می‌شه (خصوصی).", "This text is sent to the project's support server (private).");
        var detail = $"{SupportInfo.CategoryName(_category)}\n{title}\n\n{body}" + (contact.Length > 0 ? $"\n\n{L.T("تماس", "Contact")}: {contact}" : "")
            + (diag.Length > 0 ? $"\n\n{diag}" : "");
        if (!Ask(L.T("تیکت فرستاده بشه؟", "Send this ticket?"), where, L.T("ارسال", "Send"), detail: detail)) return;

        Hub.Settings.Support.Contact = contact;
        _sendNote.Foreground = Res("Muted");
        if (_via == "github")
        {
            var text = body + (contact.Length > 0 ? $"\n\n{L.T("تماس", "Contact")}: {contact}" : "") + (diag.Length > 0 ? $"\n\n---\n{diag}" : "");
            OpenUrl(GitHubIssue.NewIssueUrl(title, text, _category));
            Remember($"gh-{DateTime.Now:yyyyMMddHHmmss}", title, "github");
            _sendNote.Text = L.T("صفحه‌ی گیت‌هاب باز شد؛ اون‌جا «Submit new issue» رو بزن.", "The GitHub page opened; press “Submit new issue” there.");
            return;
        }

        _busy = true;
        _sendNote.Text = L.T("در حال ارسال…", "Sending…");
        var (id, error) = await Client.SendAsync(title, body, _category, contact, diag);
        _busy = false;
        if (id is null)
        {
            Say(_sendNote, "Red", error ?? "");
            return;
        }
        Remember(id, title, "server");
        _title.Text = "";
        _body.Text = "";
        Say(_sendNote, "Green", L.T($"ارسال شد. شماره‌ی تیکت: {id}", $"Sent. Ticket number: {id}"));
        await RefreshMineAsync();
    }

    private void Remember(string id, string title, string via)
    {
        var list = Hub.State.Tickets;
        list.Insert(0, new TicketRecord { Id = id, Title = title, Created = DateTime.Now, Via = via });
        if (list.Count > 40) list.RemoveRange(40, list.Count - 40);
        Hub.Store.Save();
        ShowMineLocal();
    }

    private List<TicketInfo> _fromServer = [];

    private static void FilterChips(Panel panel, string selected, Action<string> pick) =>
        Chips(panel, SupportInfo.Filters.Select(f => (f, SupportInfo.FilterName(f))), selected, pick);

    private void ShowMineLocal()
    {
        _mine.Children.Clear();
        var shown = new HashSet<string>();
        var index = 0;
        foreach (var t in _fromServer.OrderByDescending(t => t.Created))
        {
            shown.Add(t.Id);
            if (!SupportInfo.Matches(_mineFilterKey, t.Status)) continue;
            Add(_mine, TicketRow(t, admin: false), "s" + t.Id, index++);
        }
        // tickets sent through GitHub have no status here; they only show under "all"
        foreach (var r in Hub.State.Tickets.Where(r => (r.Via == "github" && _mineFilterKey == "all") || (r.Via != "github" && !shown.Contains(r.Id) && _mineFilterKey is "all" or "open")).Take(20))
        {
            var github = r.Via == "github";
            var record = r;
            var card = ListUi.Nested(new Thickness(0, 8, 0, 0));
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = r.Title, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Res("Text") });
            texts.Children.Add(new TextBlock
            {
                Text = (github ? L.T("گیت‌هاب", "GitHub") : L.T("سرور شب‌پا", "Shabpa server")) + " · " + $"{Fa.RelativeDay(r.Created, DateTime.Now)} {Fa.Clock(r.Created)}",
                Style = St("Label"), Margin = new Thickness(0, 3, 0, 0),
            });
            line.Children.Add(texts);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (github) actions.Children.Add(IconButton("globe", L.T("باز کردن ایشوها", "Open issues"), () => OpenUrl(GitHubIssue.IssuesUrl)));
            actions.Children.Add(IconButton("trash", L.T("حذف از این فهرست", "Remove from this list"), () =>
            {
                if (!Ask(L.T("حذف از فهرست؟", "Remove from the list?"),
                        github ? L.T("فقط از فهرست همین برنامه پاک می‌شه؛ ایشوی گیت‌هاب سر جاش می‌مونه.", "It is only removed from this app's list; the GitHub issue stays.")
                               : L.T("از فهرست این برنامه پاک می‌شه.", "It is removed from this app's list."), L.T("حذف", "Remove"), danger: true)) return;
                Hub.State.Tickets.Remove(record);
                Hub.Store.Save();
                ShowMineLocal();
            }));
            Grid.SetColumn(actions, 1);
            line.Children.Add(actions);
            card.Child = line;
            Add(_mine, card, "l" + r.Id, index++);
        }
        if (_mine.Children.Count == 0) _mine.Children.Add(Empty(_mineFilterKey == "all" ? L.T("هنوز تیکتی نفرستادی.", "You have not sent a ticket yet.") : L.T("تیکتی با این وضعیت نیست.", "No ticket with this status.")));
        _primed = true;
    }

    /// <summary>Adds a ticket row; one that was not on screen before springs in (the first draw is left to the page entrance).</summary>
    private void Add(Panel list, UIElement row, string key, int index)
    {
        list.Children.Add(row);
        if (!_seen.Add(key) || !_primed || row is not FrameworkElement fe) return;
        Motion.Rise(fe, Math.Min(index, 4), 14, 0.55);
    }

    private static UIElement Empty(string text)
    {
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 12) };
        box.Children.Add(new IconBadge("life", "HueA", 46, 22, 15) { HorizontalAlignment = HorizontalAlignment.Center });
        box.Children.Add(new TextBlock { Text = text, Style = St("Label"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        return box;
    }

    private async Task RefreshMineAsync()
    {
        if (!Client.Available) return;
        var (tickets, error) = await Client.MineAsync();
        if (tickets is null)
        {
            Say(_sendNote, "Gold", error ?? "");
            return;
        }
        _fromServer = tickets;
        ShowMineLocal();
    }

    /// <summary>One ticket as a card: status and topic pills, the title, the text (long ones fold), the replies as bubbles and the actions.</summary>
    private UIElement TicketRow(TicketInfo t, bool admin)
    {
        var card = ListUi.Nested(new Thickness(0, 8, 0, 0));
        var panel = new StackPanel();
        var head = new WrapPanel();
        var status = ListUi.Pill(SupportInfo.StatusName(t.Status), SupportInfo.StatusTint(t.Status));
        status.Margin = new Thickness(0, 0, 6, 4);
        var topic = ListUi.PillMuted(SupportInfo.CategoryName(t.Category));
        topic.Margin = new Thickness(0, 0, 6, 4);
        head.Children.Add(status);
        head.Children.Add(topic);
        var when = t.Created.Kind == DateTimeKind.Utc ? t.Created.ToLocalTime() : t.Created;
        head.Children.Add(new TextBlock
        {
            Text = $"#{t.Id} · {Fa.RelativeDay(when, DateTime.Now)} {Fa.Clock(when)}", Style = St("Label"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4), FlowDirection = FlowDirection.LeftToRight,
        });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock { Text = t.Title, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Res("Text"), Margin = new Thickness(0, 2, 0, 4) });

        // long text folds to a few lines with a toggle
        var body = new TextBlock { Text = t.Body, Style = St("Body"), TextWrapping = TextWrapping.Wrap };
        if (t.Body.Length > 180 || t.Body.Count(c => c == '\n') > 3)
        {
            body.MaxHeight = 66;
            body.TextTrimming = TextTrimming.CharacterEllipsis;
            var more = new Button { Style = St("Btn"), Content = L.T("ادامه…", "More…"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 4, 0, 0) };
            more.Click += (_, _) =>
            {
                var open = double.IsPositiveInfinity(body.MaxHeight);
                body.MaxHeight = open ? 66 : double.PositiveInfinity;
                body.TextTrimming = open ? TextTrimming.CharacterEllipsis : TextTrimming.None;
                more.Content = open ? L.T("ادامه…", "More…") : L.T("کمتر", "Less");
            };
            panel.Children.Add(body);
            panel.Children.Add(more);
        }
        else panel.Children.Add(body);

        if (admin && t.Diag.Length > 0)
            panel.Children.Add(new TextBlock { Text = t.Diag, Style = St("Label"), FontFamily = new FontFamily("Consolas"), FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (admin && t.Contact.Length > 0)
            panel.Children.Add(new TextBlock { Text = L.T("تماس: ", "Contact: ") + t.Contact, Style = St("Label"), Margin = new Thickness(0, 4, 0, 0) });
        foreach (var reply in t.Replies)
        {
            var mine = reply.Admin != admin;
            panel.Children.Add(new Border
            {
                Background = Res(reply.Admin ? "AccentSoft" : "Panel2"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(mine ? 0 : 28, 8, mine ? 28 : 0, 0),
                Child = new TextBlock { Text = (reply.Admin ? L.T("پشتیبانی: ", "Support: ") : L.T("تو: ", "You: ")) + reply.Text, Style = St("Body"), TextWrapping = TextWrapping.Wrap },
            });
        }

        var id = t.Id;
        var bar = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (admin)
        {
            var reply = new TextBox { Style = St("Input"), Width = 320, Padding = new Thickness(10, 7, 10, 7), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 6) };
            bar.Children.Add(reply);
            foreach (var (st, label) in new[] { ("answered", L.T("جواب بده", "Answer")), ("reviewing", L.T("در حال بررسی", "Reviewing")), ("closed", L.T("ببند", "Close")) })
            {
                var s2 = st;
                var button = new Button { Style = St("Btn"), Content = label, Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top };
                button.Click += async (_, _) =>
                {
                    var error = await Client.ReplyAsync(id, reply.Text.Trim(), s2);
                    _adminNote.Text = error ?? L.T("ثبت شد.", "Saved.");
                    if (error is null) await RefreshAllAsync();
                };
                bar.Children.Add(button);
            }
        }
        else if (t.Status != "closed")
        {
            var close = new Button { Style = St("Btn"), Content = L.T("بستن تیکت", "Close ticket"), Margin = new Thickness(0, 0, 6, 0) };
            close.Click += async (_, _) =>
            {
                if (!Ask(L.T("تیکت بسته بشه؟", "Close this ticket?"), L.T("تیکت بسته می‌شه ولی می‌مونه و جواب‌ها رو می‌تونی ببینی.", "The ticket is closed but stays, and you can still read the replies."), L.T("ببند", "Close"))) return;
                var error = await Client.CloseAsync(id);
                if (error is not null) { Say(_sendNote, "Red", error); return; }
                await RefreshMineAsync();
            };
            bar.Children.Add(close);
        }
        var delete = new Button { Style = St("Btn"), Content = L.T("حذف", "Delete"), Margin = new Thickness(0, 0, 6, 6) };
        delete.Click += async (_, _) =>
        {
            if (!Ask(L.T("تیکت حذف بشه؟", "Delete this ticket?"),
                    admin ? L.T("برای همیشه از سرور پاک می‌شه و برای صاحبش هم از بین می‌ره.", "It is removed from the server for good and disappears for its sender too.")
                          : L.T("برای همیشه از سرور و از این فهرست پاک می‌شه.", "It is removed from the server and from this list for good."), L.T("حذف", "Delete"), danger: true)) return;
            var error = admin ? await Client.AdminDeleteAsync(id) : await Client.DeleteAsync(id);
            if (error is not null) { Say(admin ? _adminNote : _sendNote, "Red", error); return; }
            var local = Hub.State.Tickets.FirstOrDefault(r => r.Id == id);
            if (local is not null) { Hub.State.Tickets.Remove(local); Hub.Store.Save(); }
            if (admin) await RefreshAllAsync(); else await RefreshMineAsync();
        };
        bar.Children.Add(delete);
        panel.Children.Add(bar);
        card.Child = panel;
        return card;
    }

    private async Task SaveAdminAsync()
    {
        var s = Hub.Settings.Support;
        var endpoint = _endpoint.Text.Trim();
        if (endpoint.Length > 0 && !SupportClient.IsValidEndpoint(endpoint))
        {
            Say(_adminNote, "Red", L.T("آدرس باید با https شروع بشه.", "The address must start with https."));
            return;
        }
        s.Endpoint = endpoint;
        if (_adminKey.Password.Length > 0)
        {
            s.AdminKeyProtected = Secret.Protect(_adminKey.Password);
            _adminKey.Clear();
        }
        Hub.Store.Save();
        _adminNote.Foreground = Res("Muted");
        Refresh();
        await RefreshAllAsync();
    }

    private void ForgetAdmin()
    {
        Hub.Settings.Support.AdminKeyProtected = "";
        Hub.Store.Save();
        _all.Children.Clear();
        _allTickets = [];
        _adminNote.Text = L.T("از حالت مدیر خارج شدی.", "Signed out of owner mode.");
    }

    private async Task RefreshAllAsync()
    {
        if (!Client.IsAdmin || !Client.Available)
        {
            Say(_adminNote, "Gold", Client.Available ? L.T("کلید مدیر وارد نشده.", "No admin key entered.") : L.T("آدرس سرور تنظیم نشده.", "The server address is not set."));
            return;
        }
        var (tickets, error) = await Client.AllAsync();
        if (tickets is null)
        {
            _all.Children.Clear();
            Say(_adminNote, "Red", error ?? "");
            return;
        }
        _allTickets = tickets;
        Say(_adminNote, "Muted", L.T($"{Fa.Digits(tickets.Count.ToString())} تیکت", $"{tickets.Count} tickets"));
        ShowAll();
    }

    private void ShowAll()
    {
        _all.Children.Clear();
        var n = 0;
        foreach (var t in _allTickets.Where(t => SupportInfo.Matches(_allFilterKey, t.Status)).OrderByDescending(t => t.Created).Take(60)) Add(_all, TicketRow(t, admin: true), "a" + t.Id, n++);
    }
}
