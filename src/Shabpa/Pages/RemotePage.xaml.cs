using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class RemotePage : PageBase
{
    public override string PageTitle => L.T("کنترل از راه دور", "Remote control");
    public override string PageSubtitle => L.T("از توی تلگرام، از هر جا سیستم رو بخوابون، خاموش کن یا وضعیتش رو ببین.", "From Telegram, sleep, shut down or check on the PC from anywhere.");

    private bool _showPersonal;
    private bool _adding;

    public RemotePage()
    {
        InitializeComponent();
        ProxyBox.Text = Hub.Settings.ProxyUrl;
        Hub.Bot.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    private async void OnSaveToken(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Text.Trim();
        if (!TelegramBot.LooksLikeToken(token))
        {
            TokenHint.Text = L.T("این شبیه توکن ربات نیست. توکن چیزی شبیه 123456789:AAH... هست.", "That does not look like a bot token. A token looks like 123456789:AAH...");
            TokenHint.Foreground = Res("Red");
            return;
        }
        // Moving to a personal bot: the shared relay no longer needs to know this PC.
        await Hub.Bot.ForgetSharedAsync();
        Hub.Settings.TelegramTokenProtected = TelegramBot.Protect(token);
        Hub.Settings.TelegramChatId = 0;
        Hub.Settings.TelegramUser = "";
        Hub.Store.Save();
        TokenBox.Text = "";
        Hub.Bot.Restart();
        Refresh();
    }

    private void OnSaveProxy(object sender, RoutedEventArgs e)
    {
        Hub.Settings.ProxyUrl = ProxyBox.Text.Trim();
        Hub.Store.Save();
        Hub.Bot.Restart();
        Refresh();
    }

    private void RemoveToken()
    {
        Hub.Settings.TelegramTokenProtected = "";
        Hub.Settings.TelegramChatId = 0;
        Hub.Settings.TelegramUser = "";
        Hub.Store.Save();
        Hub.Bot.Restart();
        Refresh();
    }

    private void OnOpenTelegram(object sender, RoutedEventArgs e)
    {
        var url = Hub.Bot.LinkUrl;
        if (url.Length == 0) { Hub.Bot.RequestCode(); return; }
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { LinkHint.Text = L.T($"تلگرام باز نشد. این آدرس رو توی گوشی باز کن: {url}", $"Could not open Telegram. Open this on your phone: {url}"); }
    }

    private void OnNewCode(object sender, RoutedEventArgs e) => Hub.Bot.RequestCode();

    private void OnTogglePersonal(object sender, RoutedEventArgs e)
    {
        _showPersonal = !_showPersonal;
        Refresh();
    }

    public override void Refresh()
    {
        var bot = Hub.Bot;
        if (bot.Shared)
        {
            RefreshShared(bot);
            return;
        }
        SharedPanel.Visibility = Visibility.Collapsed;
        SetupHeader.Text = L.T("راه‌اندازی", "Setup");
        var hasToken = Hub.Settings.TelegramTokenProtected.Length > 0;
        var name = bot.BotName.Length > 0 ? $"‎@{bot.BotName}" : L.T("ربات", "the bot");

        TokenHint.Foreground = Res("Muted");
        TokenHint.Text = hasToken
            ? L.T("توکن ذخیره شده (رمزنگاری‌شده، فقط روی همین حساب ویندوز). برای عوض کردنش، توکن جدید رو وارد کن.", "A token is saved (encrypted, for this Windows account only). Enter a new one to replace it.")
            : L.T("توکن فقط روی همین سیستم و به‌صورت رمزنگاری‌شده نگه داشته می‌شه.", "The token is kept on this PC only, encrypted.");
        CodeText.Text = bot.State == BotState.WaitingForPair ? bot.PairCode : "— — —";

        StatusPanel.Children.Clear();
        var remove = new Button { Style = St("BtnDanger"), Content = L.T("حذف ربات", "Remove bot") };
        remove.Click += (_, _) => RemoveToken();

        switch (bot.State)
        {
            case BotState.Connected:
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var unpair = new Button { Style = St("Btn"), Content = L.T("قطع اتصال حساب", "Unlink account"), Margin = new Thickness(0, 0, 8, 0) };
                unpair.Click += (_, _) => Hub.Bot.Unpair();
                actions.Children.Add(unpair);
                actions.Children.Add(remove);
                var user = Hub.Settings.TelegramUser;
                StatusPanel.Children.Add(Row("", "Green", L.T("ربات تلگرام وصله", "The Telegram bot is connected"),
                    user.Length > 0
                        ? L.T($"{name} آماده‌ست و به حساب «{user}» وصله.", $"{name} is ready and linked to “{user}”.")
                        : L.T($"{name} آماده‌ست.", $"{name} is ready."), actions));
                break;
            case BotState.WaitingForPair:
                StatusPanel.Children.Add(Row("", "Gold", L.T("ربات آماده‌ست؛ فقط حسابت رو وصل کن", "The bot is ready; just link your account"),
                    L.T($"توی تلگرام {name} رو باز کن و کد ۶ رقمی قدم ۳ رو براش بفرست.", $"Open {name} in Telegram and send it the 6-digit code from step 3."), remove));
                break;
            case BotState.Connecting:
                StatusPanel.Children.Add(Row("", "Accent", L.T("در حال اتصال به تلگرام…", "Connecting to Telegram…"), L.T("چند لحظه صبر کن.", "One moment."), remove));
                break;
            case BotState.Error:
                StatusPanel.Children.Add(Row("", "Red", L.T("اتصال برقرار نشد", "Could not connect"), bot.Detail, remove));
                break;
            default:
                StatusPanel.Children.Add(Row("", "Accent", L.T("هنوز رباتی وصل نیست", "No bot connected yet"),
                    L.T("با سه قدم پایین، ربات تلگرام خودت رو وصل کن. رایگانه و سرور لازم نداره.", "Connect your own Telegram bot with the three steps below. It is free and needs no server.")));
                break;
        }

        // Once paired there is nothing left to set up.
        SetupPanel.Visibility = bot.State == BotState.Connected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshShared(TelegramBot bot)
    {
        var name = bot.BotName.Length > 0 ? $"‎@{bot.BotName}" : L.T("ربات شب‌پا", "the Shabpa bot");
        var linked = bot.LinkedChats;
        if (bot.State == BotState.WaitingForPair || _adding)
        {
            // An expired code is useless; ask for a new one as soon as the card is on screen.
            if (!bot.LinkCodeFresh && bot.State is BotState.WaitingForPair or BotState.Connected) bot.RequestCode();
        }
        if (linked.Count == 0) _adding = false;
        LinkCodeText.Text = bot.LinkCodeFresh ? bot.LinkCodeDisplay : "— — — —";
        OpenTelegram.IsEnabled = bot.LinkUrl.Length > 0;

        StatusPanel.Children.Clear();
        if (bot.Pending is { } request)
        {
            var who = request.Username.Length > 0 ? $"{request.Name} (‎@{request.Username})" : request.Name;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var allow = new Button { Style = St("BtnPrimary"), Content = L.T("اجازه بده", "Allow"), Margin = new Thickness(0, 0, 8, 0) };
            allow.Click += (_, _) => { _adding = false; bot.ApproveLink(); };
            var deny = new Button { Style = St("BtnDanger"), Content = L.T("رد کن", "Decline") };
            deny.Click += (_, _) => bot.RejectLink();
            buttons.Children.Add(allow);
            buttons.Children.Add(deny);
            StatusPanel.Children.Add(Row("", "Gold", L.T($"«{who}» می‌خواد به این سیستم وصل بشه", $"“{who}” wants to link to this PC"),
                L.T("اگه خودت این کد رو نفرستادی، رد کن.", "If you did not send the code yourself, decline."), buttons));
        }

        switch (bot.State)
        {
            case BotState.Connected:
                var add = new Button { Style = St("Btn"), Content = L.T("افزودن حساب دیگه", "Add another account") };
                add.Click += (_, _) => { _adding = !_adding; Refresh(); };
                StatusPanel.Children.Add(Row("", "Green", L.T("به ربات شب‌پا وصلی", "Linked to the Shabpa bot"),
                    L.T($"توی تلگرام به {name} دستور بده.", $"Send commands to {name} in Telegram."), add));
                foreach (var chat in linked.ToList())
                {
                    var unlink = new Button { Style = St("BtnDanger"), Content = L.T("قطع اتصال", "Unlink") };
                    unlink.Click += (_, _) =>
                    {
                        if (!bot.Unlink(chat.Id)) LinkHint.Text = L.T("الان به سرور شب‌پا وصل نیستم؛ بعداً دوباره امتحان کن.", "Not connected to the Shabpa server right now; try again later.");
                    };
                    StatusPanel.Children.Add(Row("", "Accent", chat.Name.Length > 0 ? chat.Name : L.T("حساب تلگرام", "Telegram account"), "", unlink));
                }
                break;
            case BotState.WaitingForPair:
                StatusPanel.Children.Add(Row("", "Gold", L.T("ربات شب‌پا آماده‌ست؛ فقط حسابت رو وصل کن", "The Shabpa bot is ready; just link your account"),
                    L.T($"دکمه‌ی «باز کردن در تلگرام» رو بزن، یا کد پایین رو برای {name} بفرست.", $"Press “Open in Telegram”, or send the code below to {name}.")));
                break;
            case BotState.Error:
                StatusPanel.Children.Add(Row("", "Red", L.T("اتصال برقرار نشد", "Could not connect"), bot.Detail));
                break;
            default:
                StatusPanel.Children.Add(Row("", "Accent", L.T("در حال اتصال به سرور شب‌پا…", "Connecting to the Shabpa server…"), bot.Detail.Length > 0 ? bot.Detail : L.T("چند لحظه صبر کن.", "One moment.")));
                break;
        }

        SharedPanel.Visibility = bot.Pending is null && (bot.State == BotState.WaitingForPair || (_adding && bot.State == BotState.Connected))
            ? Visibility.Visible : Visibility.Collapsed;
        PersonalToggle.Visibility = linked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PersonalToggle.Content = _showPersonal ? L.T("پنهان کردن ربات شخصی", "Hide the personal bot setup") : L.T("پیشرفته: استفاده از ربات شخصی خودم", "Advanced: use my own bot instead");
        SetupHeader.Text = L.T("ربات شخصی (پیشرفته)", "Personal bot (advanced)");
        SetupPanel.Visibility = _showPersonal && linked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TokenHint.Foreground = Res("Muted");
        TokenHint.Text = L.T("با ربات شخصی، پیام‌ها مستقیم بین سیستمت و تلگرام می‌رن و از سرور شب‌پا رد نمی‌شن. سیستم باید به تلگرام دسترسی داشته باشه.", "With a personal bot, messages go straight between this PC and Telegram, not through the Shabpa server. The PC needs to reach Telegram.");
        CodeText.Text = "— — —";
    }
}
