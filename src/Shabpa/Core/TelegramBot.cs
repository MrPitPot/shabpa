using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shabpa.Core;

public enum BotCommand { Unknown, Start, Status, Cancel, Sleep, Hibernate, Shutdown, Restart, Lock, Update }

public enum BotState { Off, Connecting, WaitingForPair, Connected, Error }

/// <summary>What the bot is allowed to do to the app.</summary>
public sealed class BotHost
{
    public required Func<string> Status { get; init; }
    /// <summary>Starts the on-screen countdown for an action; returns the countdown length in seconds,
    /// 0 if the action ran immediately, or -1 if another countdown is already showing.</summary>
    public required Func<PowerAction, int> Request { get; init; }
    /// <summary>Cancels the running countdown and clears the queue; returns a description.</summary>
    public required Func<string> Cancel { get; init; }
    /// <summary>Checks for a new version and installs it; returns the first reply (more follow as messages).</summary>
    public Func<string>? Update { get; init; }
    /// <summary>Free text the fixed commands did not recognise; returns a reply, or null if it is not understood either.</summary>
}

/// <summary>Remote control through the user's own Telegram bot (long polling, no server needed).</summary>
public sealed class TelegramBot : IDisposable
{
    private static string Keyboard => L.En
        ? """{"keyboard":[["Status"],["Sleep","Hibernate"],["Shut down","Restart"],["Lock","Cancel"],["Update"]],"resize_keyboard":true}"""
        : """{"keyboard":[["وضعیت"],["خواب","هایبرنیت"],["خاموش","ری‌استارت"],["قفل صفحه","لغو"],["به‌روزرسانی"]],"resize_keyboard":true}""";

    /// <summary>Messages older than this are ignored: a "shut down" sent while the PC was off must not
    /// run hours later when it comes back.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    private readonly Func<Settings> _settings;
    private readonly Action _save;
    private readonly BotHost _host;
    private CancellationTokenSource? _cts;
    private HttpClient? _http;
    private int _wrongCodes;
    private DateTime _pairLockUntil = DateTime.MinValue;

    public BotState State { get; private set; } = BotState.Off;
    public string Detail { get; private set; } = "";
    public string BotName { get; private set; } = "";
    public string PairCode { get; private set; } = NewCode();
    public event Action? Changed;

    public TelegramBot(Func<Settings> settings, Action save, BotHost host)
    {
        _settings = settings;
        _save = save;
        _host = host;
    }

    public static string Protect(string token) =>
        token.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    /// <summary>Bot tokens look like "123456789:AA...".</summary>
    public static bool LooksLikeToken(string token)
    {
        var parts = token.Trim().Split(':');
        return parts.Length == 2 && parts[0].Length >= 6 && parts[0].All(char.IsAsciiDigit) && parts[1].Length >= 30;
    }

    private static string NewCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public static BotCommand Parse(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (text.StartsWith('/'))
        {
            var word = text[1..].Split(' ', '@')[0];
            return word switch
            {
                "start" or "help" => BotCommand.Start,
                "status" => BotCommand.Status,
                "cancel" => BotCommand.Cancel,
                "sleep" => BotCommand.Sleep,
                "hibernate" => BotCommand.Hibernate,
                "shutdown" => BotCommand.Shutdown,
                "restart" or "reboot" => BotCommand.Restart,
                "lock" => BotCommand.Lock,
                "update" => BotCommand.Update,
                _ => BotCommand.Unknown,
            };
        }
        return text switch
        {
            "وضعیت" or "status" => BotCommand.Status,
            "لغو" or "cancel" => BotCommand.Cancel,
            "خواب" or "sleep" => BotCommand.Sleep,
            "هایبرنیت" or "hibernate" => BotCommand.Hibernate,
            "خاموش" or "خاموش کردن" or "shut down" or "shutdown" => BotCommand.Shutdown,
            "ری‌استارت" or "ریستارت" or "ری استارت" or "restart" => BotCommand.Restart,
            "قفل" or "قفل صفحه" or "lock" => BotCommand.Lock,
            "به‌روزرسانی" or "بروزرسانی" or "به روزرسانی" or "آپدیت" or "update" => BotCommand.Update,
            _ => BotCommand.Unknown,
        };
    }

    public void Restart()
    {
        Stop();
        var token = Unprotect(_settings().TelegramTokenProtected);
        if (token.Length == 0)
        {
            Set(BotState.Off, "");
            return;
        }

        var handler = new HttpClientHandler();
        var proxy = _settings().ProxyUrl.Trim();
        if (proxy.Length > 0)
        {
            try { handler.Proxy = new WebProxy(proxy); handler.UseProxy = true; }
            catch { Set(BotState.Error, L.T("آدرس پروکسی درست نیست.", "The proxy address is not valid.")); return; }
        }
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        _cts = new CancellationTokenSource();
        Set(BotState.Connecting, L.T("در حال اتصال به تلگرام…", "Connecting to Telegram…"));
        _ = Task.Run(() => Poll(token, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _http?.Dispose();
        _http = null;
    }

    public void Unpair()
    {
        _settings().TelegramChatId = 0;
        _settings().TelegramUser = "";
        PairCode = NewCode();
        _save();
        if (State == BotState.Connected) Set(BotState.WaitingForPair, "");
        else Changed?.Invoke();
    }

    private void Set(BotState state, string detail)
    {
        State = state;
        Detail = detail;
        Changed?.Invoke();
    }

    private async Task Poll(string token, CancellationToken ct)
    {
        var api = $"https://api.telegram.org/bot{token}/";
        long offset = 0;
        var failures = 0;
        var http = _http!;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (BotName.Length == 0 || State is BotState.Connecting or BotState.Error)
                {
                    using var me = JsonDocument.Parse(await http.GetStringAsync(api + "getMe", ct));
                    BotName = me.RootElement.GetProperty("result").GetProperty("username").GetString() ?? "";
                    Set(_settings().TelegramChatId == 0 ? BotState.WaitingForPair : BotState.Connected, "");
                }

                var json = await http.GetStringAsync($"{api}getUpdates?timeout=25&offset={offset}&allowed_updates=%5B%22message%22%5D", ct);
                failures = 0;
                using var doc = JsonDocument.Parse(json);
                foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
                {
                    offset = update.GetProperty("update_id").GetInt64() + 1;
                    if (!update.TryGetProperty("message", out var msg)) continue;
                    if (!msg.TryGetProperty("text", out var textEl)) continue;
                    var chat = msg.GetProperty("chat");
                    if (chat.GetProperty("type").GetString() != "private") continue;
                    var sent = DateTimeOffset.FromUnixTimeSeconds(msg.GetProperty("date").GetInt64());
                    var reply = Handle(chat.GetProperty("id").GetInt64(), textEl.GetString() ?? "",
                        msg.TryGetProperty("from", out var from) && from.TryGetProperty("first_name", out var fn) ? fn.GetString() ?? "" : "",
                        DateTimeOffset.UtcNow - sent > StaleAfter);
                    if (reply is not null) await Send(http, api, chat.GetProperty("id").GetInt64(), reply, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
            {
                Set(BotState.Error, L.T("توکن ربات درست نیست.", "The bot token is not valid."));
                return;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1) Log.Write($"telegram: {ex.GetType().Name}: {ex.Message}");
                if (failures >= 2) Set(BotState.Error, L.T("به تلگرام دسترسی نیست. اینترنت یا پروکسی رو چک کن؛ خودم دوباره تلاش می‌کنم.", "Telegram is unreachable. Check the internet or proxy; I will keep retrying."));
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Returns the reply text, or null to stay silent.</summary>
    internal string? Handle(long chatId, string text, string firstName, bool stale)
    {
        var settings = _settings();
        if (settings.TelegramChatId != chatId)
        {
            if (settings.TelegramChatId != 0) return L.T("این ربات به حساب دیگه‌ای وصله.", "This bot is linked to another account.");
            if (stale) return null;
            if (DateTime.Now < _pairLockUntil) return null; // too many wrong codes: silent for a while
            var candidate = text.Trim().Split(' ')[^1];
            if (candidate == PairCode)
            {
                settings.TelegramChatId = chatId;
                settings.TelegramUser = firstName;
                _wrongCodes = 0;
                PairCode = NewCode();
                _save();
                Set(BotState.Connected, "");
                return L.T("وصل شد ✅\nاز این به بعد می‌تونی سیستمت رو از همین‌جا کنترل کنی.", "Connected ✅\nYou can now control your PC from here.");
            }
            // A few wrong guesses burn the code so it cannot be brute-forced.
            if (++_wrongCodes >= 5)
            {
                _wrongCodes = 0;
                PairCode = NewCode();
                _pairLockUntil = DateTime.Now.AddMinutes(10);
                Changed?.Invoke();
            }
            return L.T("برای اتصال، کد ۶ رقمی‌ای که توی شب‌پا می‌بینی رو بفرست.", "To connect, send the 6-digit code shown in Shabpa.");
        }

        var command = Parse(text);
        if (stale && command is not (BotCommand.Start or BotCommand.Status or BotCommand.Update))
            return L.T("این پیام قدیمیه و اجرا نشد. اگه هنوز لازمه دوباره بفرست.", "That message is old and was not run. Send it again if you still need it.");

        switch (command)
        {
            case BotCommand.Start: return L.T("شب‌پا آماده‌ست. از دکمه‌های پایین استفاده کن.", "Shabpa is ready. Use the buttons below.");
            case BotCommand.Status: return _host.Status();
            case BotCommand.Cancel: return _host.Cancel();
            case BotCommand.Update: return _host.Update?.Invoke() ?? L.T("به‌روزرسانی در دسترس نیست.", "Updates are not available.");
            case BotCommand.Unknown: return L.T("متوجه نشدم. از دکمه‌های پایین استفاده کن.", "I did not get that. Use the buttons below.");
        }

        var action = command switch
        {
            BotCommand.Sleep => PowerAction.Sleep,
            BotCommand.Hibernate => PowerAction.Hibernate,
            BotCommand.Shutdown => PowerAction.Shutdown,
            BotCommand.Restart => PowerAction.Restart,
            _ => PowerAction.Lock,
        };
        var seconds = _host.Request(action);
        if (seconds < 0) return L.T("الان یه شمارش معکوس دیگه روی صفحه‌ست. اول «لغو» رو بفرست یا صبر کن تموم بشه.", "Another countdown is already on screen. Send “Cancel” first or wait for it to finish.");
        return seconds == 0
            ? L.T($"{Fa.ActionName(action)} انجام شد.", $"{Fa.ActionName(action)}: done.")
            : L.T($"تا {Fa.Digits(seconds.ToString())} ثانیه‌ی دیگه {Fa.ActionSentence(action)}. برای انصراف «لغو» رو بفرست.", $"In {seconds} seconds {Fa.ActionSentence(action)}. Send “Cancel” to stop it.");
    }

    private static async Task Send(HttpClient http, string api, long chatId, string text, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["text"] = text,
            ["reply_markup"] = Keyboard,
        });
        using var response = await http.PostAsync(api + "sendMessage", content, ct);
    }

    /// <summary>Pushes a message to the paired chat, e.g. "rule fired". Silent if not connected.</summary>
    public void Notify(string text)
    {
        var chat = _settings().TelegramChatId;
        var token = Unprotect(_settings().TelegramTokenProtected);
        if (chat == 0 || token.Length == 0 || _http is not { } http || _cts is not { } cts) return;
        _ = Task.Run(async () =>
        {
            try { await Send(http, $"https://api.telegram.org/bot{token}/", chat, text, cts.Token); }
            catch { /* best effort */ }
        });
    }

    public void Dispose() => Stop();
}
