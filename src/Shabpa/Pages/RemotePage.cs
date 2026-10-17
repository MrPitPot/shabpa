using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Core;
using Shabpa.Kit;
using Switch = Shabpa.Kit.Switch;

namespace Shabpa.Pages;

/// <summary>«تلگرام»: link this PC to the Shabpa bot with a one-time code, privacy choice, proxy, and what the bot can do.
/// Glass Depth: status card, two numbered steps, switch and proxy cards, all from the shared kit.</summary>
public sealed class RemotePage : PageBase
{
    public override string PageTitle => L.T("کنترل از راه دور", "Remote control");
    public override string PageSubtitle => L.T("با ربات شب‌پا توی تلگرام، از هر جا سیستم رو بخوابون، خاموش کن یا وضعیتش رو ببین.", "With the Shabpa bot in Telegram, sleep, shut down or check on the PC from anywhere.");

    private readonly System.Windows.Threading.DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly StackPanel _statusPanel = new();
    private readonly GlassCard _setupCard = new();
    private readonly TextBlock _codeText = new() { FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Res("Gold"), FontFamily = new FontFamily("Consolas"), FlowDirection = FlowDirection.LeftToRight };
    private readonly Button _copyButton = new() { Style = St("Btn"), Padding = new Thickness(18, 0, 18, 0), Height = 40, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _newCodeButton = new() { Style = St("Btn"), Padding = new Thickness(18, 0, 18, 0), Height = 40 };
    private readonly TextBlock _codeHint = new() { Style = St("Label"), TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Switch _tasksToBot = new();
    private TextBlock _copyLabel = null!;
    private readonly System.Windows.Threading.DispatcherTimer _copied = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private string _shownCode = "", _stateSig = "";
    private readonly TextBox _proxyBox = new() { Style = St("Input"), FlowDirection = FlowDirection.LeftToRight, FontFamily = new FontFamily("Consolas") };

    public RemotePage()
    {
        _proxyBox.Text = Hub.Settings.ProxyUrl;
        _tasksToBot.IsChecked = Hub.Settings.TasksToBot;
        _tasksToBot.Click += OnTasksToBot;
        _copyButton.Content = NetParts.IconText("tasks", L.T("کپی", "Copy"), out _copyLabel);
        _copyButton.Click += OnCopy;
        _copied.Tick += (_, _) =>
        {
            _copied.Stop();
            _copyLabel.Text = L.T("کپی", "Copy");
            Motion.Pop(_copyButton);
        };
        _newCodeButton.Content = NetParts.IconText("refresh", L.T("کد جدید", "New code"), out _);
        _newCodeButton.Click += OnNewCode;

        var root = new NetBlocks(1000, 6);
        root.Add(new GlassCard { Padding = new Thickness(22, 14, 22, 14), Child = _statusPanel }, 0, 0, 12, 0);
        root.Add(BuildSetup(), 1, 0, 12, 1);
        root.Add(BuildPrivacy(), 2, 0, 4, 2);
        root.Add(BuildProxy(), 2, 4, 4, 3);
        root.Add(BuildCapabilities(), 2, 8, 4, 4);
        Content = root;

        Hub.Bot.Changed += () => Dispatcher.BeginInvoke(Refresh);
        // keeps the "code is valid for m:ss" line current
        _ticker.Tick += (_, _) => { if (IsVisible) UpdateCodeHint(); };
        _ticker.Start();
    }

    // ---- layout ----------------------------------------------------------------------------------

    private FrameworkElement BuildSetup()
    {
        var body = new StackPanel();
        body.Children.Add(NetParts.Header("net", "HueA", L.T("وصل کردن به ربات", "Linking to the bot")));

        // step 1
        var one = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        one.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        one.ColumnDefinitions.Add(new ColumnDefinition());
        one.Children.Add(NetParts.StepBadge(1));
        var oneBody = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(oneBody, 1);
        oneBody.Children.Add(new TextBlock { Style = St("H2"), Text = L.T("قدم ۱: ربات شب‌پا رو توی تلگرام باز کن", "Step 1: open the Shabpa bot in Telegram"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var botRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        botRow.ColumnDefinitions.Add(new ColumnDefinition());
        botRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var handle = NetParts.LatinText("@ShabPa_Bot", "Body", 15);
        handle.FontFamily = new FontFamily("Consolas");
        handle.FontWeight = FontWeights.SemiBold;
        handle.VerticalAlignment = VerticalAlignment.Center;
        botRow.Children.Add(handle);
        var open = new Button { Style = St("Btn"), Padding = new Thickness(18, 0, 18, 0), Height = 40, Margin = new Thickness(10, 0, 0, 0), Content = NetParts.IconText("globe", L.T("باز کردن ربات", "Open the bot"), out _) };
        open.Click += OnOpenBot;
        Grid.SetColumn(open, 1);
        botRow.Children.Add(open);
        oneBody.Children.Add(botRow);
        one.Children.Add(oneBody);
        body.Children.Add(one);

        body.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 18, 0, 0) });

        // step 2
        var two = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        two.ColumnDefinitions.Add(new ColumnDefinition());
        two.Children.Add(NetParts.StepBadge(2));
        var twoBody = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(twoBody, 1);
        twoBody.Children.Add(new TextBlock { Style = St("H2"), Text = L.T("قدم ۲: این کد رو برای ربات بفرست", "Step 2: send this code to the bot"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var codeRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var codeBox = new Border { Style = St("Card"), Padding = new Thickness(22, 8, 22, 8), Margin = new Thickness(0, 0, 12, 8), Child = _codeText };
        codeRow.Children.Add(codeBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(_copyButton);
        buttons.Children.Add(_newCodeButton);
        codeRow.Children.Add(buttons);
        twoBody.Children.Add(codeRow);
        twoBody.Children.Add(_codeHint);
        two.Children.Add(twoBody);
        body.Children.Add(two);

        _setupCard.Child = body;
        return _setupCard;
    }

    private FrameworkElement BuildPrivacy()
    {
        var (card, body) = NetParts.Card("shield", "HueB", L.T("حریم خصوصی", "Privacy"));
        body.Children.Add(K.SettingRow(L.T("ربات بتونه عنوان کارهای امروزم رو بهم نشون بده", "Let the bot show me the titles of today's tasks"), _tasksToBot, first: true));
        var tip = K.Text(L.T("پیش‌فرض خاموشه: ربات فقط تعداد کارها رو می‌گه. اگه روشنش کنی، عنوان کارهای امروز از طریق سرور شب‌پا و تلگرام به حساب‌های وصل‌شده می‌رسه.",
            "Off by default: the bot only says how many tasks there are. If you turn it on, the titles of today's tasks travel through the Shabpa server and Telegram to the linked accounts."), "Label");
        tip.TextWrapping = TextWrapping.Wrap;
        tip.LineHeight = 20;
        tip.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(tip);
        return card;
    }

    private FrameworkElement BuildProxy()
    {
        var (card, body) = NetParts.Card("globe", "HueC", L.T("پروکسی", "Proxy"));
        var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_proxyBox);
        var save = new Button { Style = St("Btn"), Content = L.T("ذخیره", "Save"), Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(10, 0, 0, 0) };
        save.Click += OnSaveProxy;
        Grid.SetColumn(save, 1);
        row.Children.Add(save);
        body.Children.Add(row);
        var tip = K.Text(L.T("معمولاً لازم نیست: این کامپیوتر به خود تلگرام وصل نمی‌شه، فقط به سرور شب‌پا. اگه اون هم باز نشد، آدرس پروکسی محلی‌ات رو بنویس؛ مثلاً socks5://127.0.0.1:1080. خالی بذاری از پروکسی خودِ ویندوز استفاده می‌شه.",
            "Usually not needed: this PC never talks to Telegram itself, only to the Shabpa server. If that is blocked too, enter your local proxy, for example socks5://127.0.0.1:1080. Leave it empty to use the Windows proxy."), "Label");
        tip.TextWrapping = TextWrapping.Wrap;
        tip.LineHeight = 20;
        tip.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(tip);
        return card;
    }

    private FrameworkElement BuildCapabilities()
    {
        var (card, body) = NetParts.Card("bolt", "HueD", L.T("از توی تلگرام چه کارهایی می‌شه کرد", "What you can do from Telegram"));
        var text = new TextBlock
        {
            Style = St("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
            Text = L.T("وضعیت سیستم · خواب · هایبرنیت · خاموش · ری‌استارت · قفل صفحه (همین الان یا با تأخیر) · لغو · خالی کردن رم · کم‌نور کردن صفحه · صدا · حالت شب‌خواب. هر دستور برق اول روی صفحه شمارش معکوس نشون می‌ده، و دستوری که بیشتر از ۳ دقیقه توی راه مونده (مثلاً وقتی سیستم خاموش بوده) هیچ‌وقت اجرا نمی‌شه.",
                "PC status · sleep · hibernate · shut down · restart · lock (now or later) · cancel · free RAM · dim the screen · volume · sleep mode. Every power command first shows a countdown on screen, and a command that waited more than 3 minutes (for example while the PC was off) never runs."),
        };
        body.Children.Add(text);
        return card;
    }

    // ---- actions -----------------------------------------------------------------------------------

    private void OnOpenBot(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo($"https://t.me/{BotLink.BotUsername}") { UseShellExecute = true }); }
        catch { /* no browser or Telegram handler */ }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Hub.Bot.Code.Length == 0) return;
        try
        {
            Clipboard.SetText(Hub.Bot.Code);
            // short feedback on the button itself: the label says it was copied for a moment
            _copyLabel.Text = L.T("کپی شد", "Copied");
            Motion.Pop(_copyButton);
            _copied.Stop();
            _copied.Start();
        }
        catch { /* clipboard busy */ }
    }

    private void OnNewCode(object sender, RoutedEventArgs e)
    {
        Hub.Bot.NewCode();
        Refresh();
    }

    private void OnTasksToBot(object sender, RoutedEventArgs e)
    {
        Hub.Settings.TasksToBot = _tasksToBot.IsChecked == true;
        Hub.Store.Save();
    }

    private void OnSaveProxy(object sender, RoutedEventArgs e)
    {
        Hub.Settings.ProxyUrl = _proxyBox.Text.Trim();
        Hub.Store.Save();
        if (sender is FrameworkElement saved) Motion.Pop(saved);
        Hub.Bot.Restart();
        Refresh();
    }

    private void UpdateCodeHint()
    {
        var bot = Hub.Bot;
        if (bot.Code.Length == 0)
        {
            _codeHint.Text = bot.State == BotState.Connecting || bot.State == BotState.WaitingForPair
                ? L.T("در حال گرفتن کد…", "Getting a code…")
                : "";
            return;
        }
        var left = bot.CodeExpires - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            _codeHint.Text = L.T("این کد منقضی شده؛ یه کد جدید می‌گیرم…", "This code has expired; getting a new one…");
            return;
        }
        var time = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        _codeHint.Text = L.T($"این کد فقط یک بار و تا {Fa.Digits(time)} دقیقه‌ی دیگه کار می‌کنه. هر کس این کد رو داشته باشه می‌تونه این سیستم رو کنترل کنه، پس فقط خودت بفرستش.",
            $"The code works once, for the next {time}. Whoever has it can control this PC, so send it yourself only.");
    }

    public override void Refresh()
    {
        var bot = Hub.Bot;
        _codeText.Text = bot.Code.Length > 0 ? bot.Code : "— — — —";
        if (_shownCode != bot.Code)
        {
            if (_shownCode.Length > 0 || bot.Code.Length > 0) Motion.Pop(_codeText);
            _shownCode = bot.Code;
        }
        _copyButton.IsEnabled = bot.Code.Length > 0;
        _newCodeButton.IsEnabled = bot.State is BotState.WaitingForPair or BotState.Connected;
        UpdateCodeHint();

        var stateSig = bot.State + "|" + bot.Chats;
        var stateChanged = _stateSig.Length > 0 && stateSig != _stateSig;
        _stateSig = stateSig;
        _statusPanel.Children.Clear();
        var remove = new Button { Style = St("BtnDanger"), Content = L.T("قطع اتصال", "Disconnect"), Padding = new Thickness(18, 9, 18, 9) };
        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false;
            await Hub.Bot.RemoveAsync();
        };

        switch (bot.State)
        {
            case BotState.Connected:
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var another = new Button { Style = St("Btn"), Content = L.T("وصل کردن یه حساب دیگه", "Link another account"), Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 8, 0) };
                another.Click += (_, _) =>
                {
                    Hub.Bot.NewCode();
                    var hidden = _setupCard.Visibility != Visibility.Visible;
                    _setupCard.Visibility = Visibility.Visible;
                    if (hidden) Motion.Rise(_setupCard, 0, 16, 0.6);
                };
                actions.Children.Add(another);
                actions.Children.Add(remove);
                _statusPanel.Children.Add(Row("chk", "Green", L.T("به ربات وصله", "Linked to the bot"),
                    bot.Chats == 1
                        ? L.T("یه حساب تلگرام این سیستم رو کنترل می‌کنه.", "One Telegram account controls this PC.")
                        : L.T($"{Fa.Digits(bot.Chats.ToString())} حساب تلگرام این سیستم رو کنترل می‌کنن.", $"{bot.Chats} Telegram accounts control this PC."), actions));
                break;
            }
            case BotState.WaitingForPair:
                _statusPanel.Children.Add(Row("clk", "Gold", L.T("فقط کد رو برای ربات بفرست", "Just send the code to the bot"),
                    L.T("‎@ShabPa_Bot رو باز کن و کد پایین رو براش بفرست. همین.", "Open @ShabPa_Bot and send it the code below. That is all."), remove));
                break;
            case BotState.Connecting:
                _statusPanel.Children.Add(Row("refresh", "Accent", L.T("در حال اتصال…", "Connecting…"), L.T("چند لحظه صبر کن.", "One moment."), remove));
                break;
            case BotState.Error:
                _statusPanel.Children.Add(Row("x", "Red", L.T("اتصال برقرار نشد", "Could not connect"), bot.Detail, remove));
                break;
            default:
            {
                var connect = new Button { Style = St("BtnPrimary"), Content = L.T("وصل شدن به ربات", "Connect to the bot"), Padding = new Thickness(20, 9, 20, 9) };
                connect.Click += (_, _) => Hub.Bot.Enable();
                _statusPanel.Children.Add(Row("globe", "Accent", L.T("هنوز به ربات وصل نیست", "Not linked to the bot yet"),
                    L.T("یه کد می‌گیری و برای ‎@ShabPa_Bot می‌فرستی. ربات ساختن و توکن لازم نیست.", "You get a code and send it to @ShabPa_Bot. No bot to create, no token."), connect));
                break;
            }
        }

        // Before linking there is nothing to show yet; once linked, the code card only comes back for another account.
        var setupWasHidden = _setupCard.Visibility != Visibility.Visible;
        _setupCard.Visibility = bot.State == BotState.WaitingForPair || (bot.State == BotState.Connected && bot.Code.Length > 0)
            ? Visibility.Visible : Visibility.Collapsed;
        // the status line swaps with a short rise; the code card comes in when it appears
        if (stateChanged) Motion.Switch(_statusPanel);
        if (setupWasHidden && _setupCard.Visibility == Visibility.Visible && _setupCard.IsLoaded) Motion.Rise(_setupCard, 0, 16, 0.6);
    }
}
