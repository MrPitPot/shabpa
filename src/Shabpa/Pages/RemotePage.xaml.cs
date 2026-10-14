using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class RemotePage : PageBase
{
    public override string PageTitle => L.T("کنترل از راه دور", "Remote control");
    public override string PageSubtitle => L.T("با ربات تلگرام خودت، از هر جا سیستم رو بخوابون، خاموش کن یا وضعیتش رو ببین.", "With your own Telegram bot, sleep, shut down or check on the PC from anywhere.");

    public RemotePage()
    {
        InitializeComponent();
        ProxyBox.Text = Hub.Settings.ProxyUrl;
        Hub.Bot.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    private void OnSaveToken(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Text.Trim();
        if (!TelegramBot.LooksLikeToken(token))
        {
            TokenHint.Text = L.T("این شبیه توکن ربات نیست. توکن چیزی شبیه 123456789:AAH... هست.", "That does not look like a bot token. A token looks like 123456789:AAH...");
            TokenHint.Foreground = Res("Red");
            return;
        }
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

    public override void Refresh()
    {
        var bot = Hub.Bot;
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
}
