using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;

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
    private readonly CheckBox _diag = new();
    private readonly TextBlock _diagPreview = new(), _viaNote = new(), _sendNote = new(), _adminNote = new();
    private readonly StackPanel _mine = new(), _all = new();
    private readonly PasswordBox _adminKey = new();
    private bool _busy;

    public override string PageTitle => L.T("پشتیبانی", "Support");
    public override string PageSubtitle => L.T("مشکل، پیشنهاد یا سؤالت رو بفرست و جوابش رو همین‌جا ببین.", "Send a problem, an idea or a question and see the answer here.");

    private static SupportClient Client => Hub.I.Support;

    public SupportPage()
    {
        var root = new StackPanel();

        // ---- new ticket -------------------------------------------------------------------------------
        var (card, form) = Ui2.Card(L.T("تیکت جدید", "New ticket"),
            L.T("هر چی می‌نویسی فقط بعد از زدن دکمه‌ی ارسال و تأیید خودت می‌ره. شب‌پا خودش چیزی نمی‌فرسته.",
                "Whatever you write is only sent after you press Send and confirm. Shabpa never sends anything by itself."));
        form.Children.Add(Ui2.Label(L.T("موضوع", "Topic")));
        form.Children.Add(_categoryChips);
        form.Children.Add(Ui2.Label(L.T("عنوان", "Title")));
        Style(_title, 460, false);
        _title.MaxLength = 120;
        form.Children.Add(_title);
        form.Children.Add(Ui2.Label(L.T("توضیح (چی شد؟ انتظارت چی بود؟)", "Details (what happened? what did you expect?)")));
        Style(_body, 460, true);
        _body.MaxLength = 4000;
        form.Children.Add(_body);
        form.Children.Add(Ui2.Label(L.T("راه تماس (اختیاری؛ فقط اگه می‌خوای بیرون از برنامه هم جواب بدم)", "Contact (optional; only if you want an answer outside the app)")));
        Style(_contact, 320, false);
        _contact.MaxLength = 100;
        form.Children.Add(_contact);

        _diag.Style = St("Check");
        _diag.Content = L.T("اطلاعات فنی ساده هم همراهش بره (ورژن برنامه، ویندوز، ادمین بودن)", "Include simple technical details (app version, Windows, admin or not)");
        _diag.Margin = new Thickness(0, 12, 0, 0);
        _diag.Click += (_, _) => UpdateDiag();
        form.Children.Add(_diag);
        _diagPreview.Style = St("Label");
        _diagPreview.TextWrapping = TextWrapping.Wrap;
        _diagPreview.FlowDirection = FlowDirection.LeftToRight;
        _diagPreview.Margin = new Thickness(0, 6, 0, 0);
        form.Children.Add(_diagPreview);

        form.Children.Add(Ui2.Label(L.T("از کجا بفرستم؟", "Send through"), 14, 4));
        form.Children.Add(_viaChips);
        _viaNote.Style = St("Label");
        _viaNote.TextWrapping = TextWrapping.Wrap;
        _viaNote.Margin = new Thickness(0, 4, 0, 0);
        form.Children.Add(_viaNote);

        var send = new Button { Style = St("BtnPrimary"), Content = L.T("ارسال تیکت", "Send ticket"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 14, 0, 0) };
        send.Click += async (_, _) => await SendAsync();
        form.Children.Add(send);
        _sendNote.Style = St("Label");
        _sendNote.TextWrapping = TextWrapping.Wrap;
        _sendNote.Margin = new Thickness(0, 8, 0, 0);
        form.Children.Add(_sendNote);
        root.Children.Add(card);

        // ---- my tickets -------------------------------------------------------------------------------
        var (mineCard, mine) = Ui2.Card(L.T("تیکت‌های من", "My tickets"));
        var refresh = new Button { Style = St("Btn"), Content = L.T("به‌روزرسانی", "Refresh"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        refresh.Click += async (_, _) => await RefreshMineAsync();
        mine.Children.Add(refresh);
        mine.Children.Add(_mine);
        root.Children.Add(mineCard);

        // ---- project owner ----------------------------------------------------------------------------
        var (admin, adminBody) = Ui2.Card(L.T("مدیر پروژه", "Project owner"),
            L.T("اگه صاحب پروژه‌ای، کلید مدیر رو وارد کن تا همه‌ی تیکت‌ها رو ببینی و جواب بدی. کلید فقط روی همین کامپیوتر و رمزگذاری‌شده نگه داشته می‌شه.",
                "If you own the project, enter the admin key to see and answer every ticket. The key is kept only on this PC, encrypted."));
        _adminKey.Padding = new Thickness(10, 7, 10, 7);
        _adminKey.Width = 300;
        _adminKey.HorizontalAlignment = HorizontalAlignment.Left;
        _adminKey.Margin = new Thickness(0, 10, 0, 0);
        _adminKey.Background = Res("Panel2");
        _adminKey.Foreground = Res("Text");
        _adminKey.BorderBrush = Res("Line");
        _adminKey.FlowDirection = FlowDirection.LeftToRight;
        adminBody.Children.Add(_adminKey);
        Style(_endpoint, 420, false);
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
        adminBody.Children.Add(_all);
        root.Children.Add(admin);

        Content = root;

        Chips(_categoryChips, SupportInfo.Categories.Select(c => (c, SupportInfo.CategoryName(c))), _category, v => _category = v);
    }

    private static void Style(TextBox box, double width, bool multiline)
    {
        box.Style = St("Input");
        box.Width = width;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.Padding = new Thickness(10, 7, 10, 7);
        if (!multiline) return;
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.MinHeight = 110;
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

    public override void Refresh()
    {
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
            _sendNote.Foreground = Res("Red");
            _sendNote.Text = L.T("عنوان و توضیح رو کامل‌تر بنویس.", "Please write a fuller title and description.");
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
            _sendNote.Foreground = Res("Red");
            _sendNote.Text = error ?? "";
            return;
        }
        Remember(id, title, "server");
        _title.Text = "";
        _body.Text = "";
        _sendNote.Foreground = Res("Green");
        _sendNote.Text = L.T($"ارسال شد. شماره‌ی تیکت: {id}", $"Sent. Ticket number: {id}");
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

    private void ShowMineLocal()
    {
        _mine.Children.Clear();
        var shown = new HashSet<string>();
        foreach (var t in _fromServer)
        {
            shown.Add(t.Id);
            _mine.Children.Add(TicketRow(t, admin: false));
        }
        foreach (var r in Hub.State.Tickets.Where(r => r.Via == "github" || !shown.Contains(r.Id)).Take(20))
        {
            var github = r.Via == "github";
            var open = github ? IconButton("", L.T("باز کردن ایشوها", "Open issues"), () => OpenUrl(GitHubIssue.IssuesUrl)) : null;
            _mine.Children.Add(Row("", github ? "Gold" : "Accent", r.Title,
                (github ? L.T("گیت‌هاب", "GitHub") : L.T("سرور شب‌پا", "Shabpa server")) + " · " + $"{Fa.RelativeDay(r.Created, DateTime.Now)} {Fa.Clock(r.Created)}", open));
        }
        if (_mine.Children.Count == 0)
            _mine.Children.Add(Ui2.Label(L.T("هنوز تیکتی نفرستادی.", "You have not sent a ticket yet.")));
    }

    private async Task RefreshMineAsync()
    {
        if (!Client.Available) return;
        var (tickets, error) = await Client.MineAsync();
        if (tickets is null)
        {
            _sendNote.Foreground = Res("Gold");
            _sendNote.Text = error ?? "";
            return;
        }
        _fromServer = tickets;
        ShowMineLocal();
    }

    private UIElement TicketRow(TicketInfo t, bool admin)
    {
        var panel = new StackPanel();
        var sub = $"{SupportInfo.StatusName(t.Status)} · {SupportInfo.CategoryName(t.Category)} · #{t.Id}";
        panel.Children.Add(Row("", t.Status == "answered" ? "Green" : t.Status == "closed" ? "Gold" : "Accent", t.Title, sub));
        var text = new TextBlock { Text = t.Body, Style = St("Label"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(54, 0, 0, 4) };
        panel.Children.Add(text);
        if (admin && t.Diag.Length > 0)
            panel.Children.Add(new TextBlock { Text = t.Diag, Style = St("Label"), FontFamily = new FontFamily("Consolas"), FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(54, 0, 0, 4) });
        if (admin && t.Contact.Length > 0)
            panel.Children.Add(new TextBlock { Text = L.T("تماس: ", "Contact: ") + t.Contact, Style = St("Label"), Margin = new Thickness(54, 0, 0, 4) });
        foreach (var reply in t.Replies)
            panel.Children.Add(new Border
            {
                Background = Res(reply.Admin ? "AccentSoft" : "Panel2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(54, 2, 0, 4),
                Child = new TextBlock { Text = (reply.Admin ? L.T("پشتیبانی: ", "Support: ") : "") + reply.Text, Style = St("Body"), TextWrapping = TextWrapping.Wrap },
            });
        if (admin)
        {
            var reply = new TextBox { Style = St("Input"), Width = 360, Padding = new Thickness(8, 6, 8, 6), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(54, 4, 8, 4) };
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            bar.Children.Add(reply);
            foreach (var (status, label) in new[] { ("answered", L.T("جواب بده", "Answer")), ("reviewing", L.T("در حال بررسی", "Reviewing")), ("closed", L.T("ببند", "Close")) })
            {
                var id = t.Id;
                var s = status;
                var button = new Button { Style = St("Btn"), Content = label, Margin = new Thickness(0, 4, 6, 4), VerticalAlignment = VerticalAlignment.Top };
                button.Click += async (_, _) =>
                {
                    var error = await Client.ReplyAsync(id, reply.Text.Trim(), s);
                    _adminNote.Text = error ?? L.T("ثبت شد.", "Saved.");
                    if (error is null) await RefreshAllAsync();
                };
                bar.Children.Add(button);
            }
            panel.Children.Add(bar);
        }
        panel.Children.Add(Divider());
        return panel;
    }

    private async Task SaveAdminAsync()
    {
        var s = Hub.Settings.Support;
        var endpoint = _endpoint.Text.Trim();
        if (endpoint.Length > 0 && !SupportClient.IsValidEndpoint(endpoint))
        {
            _adminNote.Foreground = Res("Red");
            _adminNote.Text = L.T("آدرس باید با https شروع بشه.", "The address must start with https.");
            return;
        }
        s.Endpoint = endpoint;
        if (_adminKey.Password.Length > 0)
        {
            s.AdminKeyProtected = TelegramBot.Protect(_adminKey.Password);
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
        _adminNote.Text = L.T("از حالت مدیر خارج شدی.", "Signed out of owner mode.");
    }

    private async Task RefreshAllAsync()
    {
        if (!Client.IsAdmin || !Client.Available)
        {
            _adminNote.Foreground = Res("Gold");
            _adminNote.Text = Client.Available ? L.T("کلید مدیر وارد نشده.", "No admin key entered.") : L.T("آدرس سرور تنظیم نشده.", "The server address is not set.");
            return;
        }
        var (tickets, error) = await Client.AllAsync();
        _all.Children.Clear();
        if (tickets is null)
        {
            _adminNote.Foreground = Res("Red");
            _adminNote.Text = error ?? "";
            return;
        }
        _adminNote.Foreground = Res("Muted");
        _adminNote.Text = L.T($"{Fa.Digits(tickets.Count.ToString())} تیکت", $"{tickets.Count} tickets");
        foreach (var t in tickets.Take(60)) _all.Children.Add(TicketRow(t, admin: true));
    }
}
