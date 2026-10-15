using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;

namespace Shabpa.Pages;

public partial class RemotePage : PageBase
{
    public override string PageTitle => L.T("کنترل از راه دور", "Remote control");
    public override string PageSubtitle => L.T("با ربات شب‌پا توی تلگرام، از هر جا سیستم رو بخوابون، خاموش کن یا وضعیتش رو ببین.", "With the Shabpa bot in Telegram, sleep, shut down or check on the PC from anywhere.");

    private readonly System.Windows.Threading.DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };

    public RemotePage()
    {
        InitializeComponent();
        ProxyBox.Text = Hub.Settings.ProxyUrl;
        TasksToBotCheck.IsChecked = Hub.Settings.TasksToBot;
        Hub.Bot.Changed += () => Dispatcher.BeginInvoke(Refresh);
        // keeps the "code is valid for m:ss" line current
        _ticker.Tick += (_, _) => { if (IsVisible) UpdateCodeHint(); };
        _ticker.Start();
    }

    private void OnOpenBot(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo($"https://t.me/{BotLink.BotUsername}") { UseShellExecute = true }); }
        catch { /* no browser or Telegram handler */ }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Hub.Bot.Code.Length == 0) return;
        try { Clipboard.SetText(Hub.Bot.Code); } catch { /* clipboard busy */ }
    }

    private void OnNewCode(object sender, RoutedEventArgs e)
    {
        Hub.Bot.NewCode();
        Refresh();
    }

    private void OnTasksToBot(object sender, RoutedEventArgs e)
    {
        Hub.Settings.TasksToBot = TasksToBotCheck.IsChecked == true;
        Hub.Store.Save();
    }

    private void OnSaveProxy(object sender, RoutedEventArgs e)
    {
        Hub.Settings.ProxyUrl = ProxyBox.Text.Trim();
        Hub.Store.Save();
        Hub.Bot.Restart();
        Refresh();
    }

    private void UpdateCodeHint()
    {
        var bot = Hub.Bot;
        if (bot.Code.Length == 0)
        {
            CodeHint.Text = bot.State == BotState.Connecting || bot.State == BotState.WaitingForPair
                ? L.T("در حال گرفتن کد…", "Getting a code…")
                : "";
            return;
        }
        var left = bot.CodeExpires - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            CodeHint.Text = L.T("این کد منقضی شده؛ یه کد جدید می‌گیرم…", "This code has expired; getting a new one…");
            return;
        }
        var time = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        CodeHint.Text = L.T($"این کد فقط یک بار و تا {Fa.Digits(time)} دقیقه‌ی دیگه کار می‌کنه. هر کس این کد رو داشته باشه می‌تونه این سیستم رو کنترل کنه، پس فقط خودت بفرستش.",
            $"The code works once, for the next {time}. Whoever has it can control this PC, so send it yourself only.");
    }

    public override void Refresh()
    {
        var bot = Hub.Bot;
        CodeText.Text = bot.Code.Length > 0 ? bot.Code : "— — — —";
        CopyButton.IsEnabled = bot.Code.Length > 0;
        NewCodeButton.IsEnabled = bot.State is BotState.WaitingForPair or BotState.Connected;
        UpdateCodeHint();

        StatusPanel.Children.Clear();
        var remove = new Button { Style = St("BtnDanger"), Content = L.T("قطع اتصال", "Disconnect") };
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
                var another = new Button { Style = St("Btn"), Content = L.T("وصل کردن یه حساب دیگه", "Link another account"), Margin = new Thickness(0, 0, 8, 0) };
                another.Click += (_, _) => { Hub.Bot.NewCode(); SetupPanel.Visibility = Visibility.Visible; };
                actions.Children.Add(another);
                actions.Children.Add(remove);
                StatusPanel.Children.Add(Row("", "Green", L.T("به ربات وصله", "Linked to the bot"),
                    bot.Chats == 1
                        ? L.T("یه حساب تلگرام این سیستم رو کنترل می‌کنه.", "One Telegram account controls this PC.")
                        : L.T($"{Fa.Digits(bot.Chats.ToString())} حساب تلگرام این سیستم رو کنترل می‌کنن.", $"{bot.Chats} Telegram accounts control this PC."), actions));
                break;
            }
            case BotState.WaitingForPair:
                StatusPanel.Children.Add(Row("", "Gold", L.T("فقط کد رو برای ربات بفرست", "Just send the code to the bot"),
                    L.T("‎@ShabPa_Bot رو باز کن و کد پایین رو براش بفرست. همین.", "Open @ShabPa_Bot and send it the code below. That is all."), remove));
                break;
            case BotState.Connecting:
                StatusPanel.Children.Add(Row("", "Accent", L.T("در حال اتصال…", "Connecting…"), L.T("چند لحظه صبر کن.", "One moment."), remove));
                break;
            case BotState.Error:
                StatusPanel.Children.Add(Row("", "Red", L.T("اتصال برقرار نشد", "Could not connect"), bot.Detail, remove));
                break;
            default:
            {
                var connect = new Button { Style = St("BtnPrimary"), Content = L.T("وصل شدن به ربات", "Connect to the bot") };
                connect.Click += (_, _) => Hub.Bot.Enable();
                StatusPanel.Children.Add(Row("", "Accent", L.T("هنوز به ربات وصل نیست", "Not linked to the bot yet"),
                    L.T("یه کد می‌گیری و برای ‎@ShabPa_Bot می‌فرستی. ربات ساختن و توکن لازم نیست.", "You get a code and send it to @ShabPa_Bot. No bot to create, no token."), connect));
                break;
            }
        }

        // Before linking there is nothing to show yet; once linked, the code card only comes back for another account.
        SetupPanel.Visibility = bot.State == BotState.WaitingForPair || (bot.State == BotState.Connected && bot.Code.Length > 0)
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
