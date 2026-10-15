using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

/// <summary>Remote control through Telegram. Two ways in: the shared Shabpa bot, reached through the relay server
/// (server/relay-worker) over one WebSocket, or the user's own bot polled directly with its token. A saved token
/// always wins, so people who set up their own bot before the shared one existed keep exactly what they had.</summary>
public sealed class TelegramBot : IDisposable
{
    private static string Keyboard => L.En
        ? """{"keyboard":[["Status"],["Sleep","Hibernate"],["Shut down","Restart"],["Lock","Cancel"],["Update"]],"resize_keyboard":true}"""
        : """{"keyboard":[["وضعیت"],["خواب","هایبرنیت"],["خاموش","ری‌استارت"],["قفل صفحه","لغو"],["به‌روزرسانی"]],"resize_keyboard":true}""";

    /// <summary>Messages older than this are ignored: a "shut down" sent while the PC was off must not
    /// run hours later when it comes back.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    /// <summary>The official relay's address; empty until it is deployed and this constant (or the setting) is filled in.</summary>
    public const string DefaultRelay = "https://shabpa-relay.amber-garden-174c.workers.dev";

    private readonly Func<Settings> _settings;
    private readonly Action _save;
    private readonly BotHost _host;
    private CancellationTokenSource? _cts;
    private HttpClient? _http;
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private DateTime _linkCodeUntil;
    private int _wrongCodes;
    private DateTime _pairLockUntil = DateTime.MinValue;

    public BotState State { get; private set; } = BotState.Off;
    public string Detail { get; private set; } = "";
    public string BotName { get; private set; } = "";
    public string PairCode { get; private set; } = NewCode();
    public event Action? Changed;

    public sealed record LinkRequest(long Chat, string Name, string Username);

    /// <summary>Shared bot: someone typed this PC's code in Telegram and waits for the user to allow it here.</summary>
    public LinkRequest? Pending { get; private set; }
    public event Action<LinkRequest>? LinkRequested;
    /// <summary>Shared bot: the one-time code (8 characters) the relay issued for linking a Telegram account.</summary>
    public string LinkCode { get; private set; } = "";
    public bool LinkCodeFresh => LinkCode.Length > 0 && DateTime.Now < _linkCodeUntil;
    public string LinkCodeDisplay => LinkCode.Length == 8 ? $"{LinkCode[..4]}-{LinkCode[4..]}" : LinkCode;
    public string LinkUrl => BotName.Length > 0 && LinkCodeFresh ? $"https://t.me/{BotName}?start={LinkCode}" : "";

    public string RelayEndpoint
    {
        get
        {
            var set = _settings().Relay.Endpoint.Trim();
            return (set.Length > 0 ? set : DefaultRelay).TrimEnd('/');
        }
    }

    public bool RelayAvailable => SupportClient.IsValidEndpoint(RelayEndpoint);
    /// <summary>True when the shared bot is in use: no personal token, and a relay to talk to.</summary>
    public bool Shared => _settings().TelegramTokenProtected.Length == 0 && RelayAvailable;
    public IReadOnlyList<RelayChat> LinkedChats => _settings().Relay.Chats;

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
            if (!RelayAvailable)
            {
                Set(BotState.Off, "");
                return;
            }
            try { _ = Proxy(); }
            catch { Set(BotState.Error, L.T("آدرس پروکسی درست نیست.", "The proxy address is not valid.")); return; }
            _cts = new CancellationTokenSource();
            Set(BotState.Connecting, L.T("در حال اتصال به سرور شب‌پا…", "Connecting to the Shabpa server…"));
            _ = Task.Run(() => RelayLoop(_cts.Token));
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
        _ws?.Abort();
        _ws = null;
        Pending = null;
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
        return Command(text, stale);
    }

    /// <summary>Shared bot: the relay already checked the link, but a chat this PC never approved still gets nothing.</summary>
    internal string? HandleShared(long chatId, string text, bool stale) =>
        _settings().Relay.Chats.Any(c => c.Id == chatId) ? Command(text, stale) : null;

    /// <summary>Runs one command from an already trusted chat; returns the reply text, or null to stay silent.</summary>
    private string? Command(string text, bool stale)
    {
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
        if (Shared)
        {
            if (LinkedChats.Count > 0) RelaySend(new JsonObject { ["t"] = "notify", ["text"] = text });
            return;
        }
        var chat = _settings().TelegramChatId;
        var token = Unprotect(_settings().TelegramTokenProtected);
        if (chat == 0 || token.Length == 0 || _http is not { } http || _cts is not { } cts) return;
        _ = Task.Run(async () =>
        {
            try { await Send(http, $"https://api.telegram.org/bot{token}/", chat, text, cts.Token); }
            catch { /* best effort */ }
        });
    }

    // ---- Shared bot through the relay ----

    private WebProxy? Proxy()
    {
        var proxy = _settings().ProxyUrl.Trim();
        return proxy.Length == 0 ? null : new WebProxy(proxy);
    }

    private HttpClient RelayHttp()
    {
        var handler = new HttpClientHandler();
        if (Proxy() is { } proxy) { handler.Proxy = proxy; handler.UseProxy = true; }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Shabpa");
        return http;
    }

    private async Task Register(CancellationToken ct)
    {
        using var http = RelayHttp();
        using var response = await http.PostAsync(RelayEndpoint + "/v1/devices", null, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var relay = _settings().Relay;
        relay.DeviceId = doc.RootElement.GetProperty("device").GetString() ?? "";
        relay.SecretProtected = Protect(doc.RootElement.GetProperty("secret").GetString() ?? "");
        relay.Chats.Clear();
        _save();
    }

    private async Task RelayLoop(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var relay = _settings().Relay;
                if (relay.DeviceId.Length != 32 || Unprotect(relay.SecretProtected).Length != 64) await Register(ct);

                using var ws = new ClientWebSocket();
                if (Proxy() is { } proxy) ws.Options.Proxy = proxy;
                ws.Options.CollectHttpResponseDetails = true;
                ws.Options.KeepAliveInterval = TimeSpan.Zero; // our own "ping" text is answered without waking the server
                ws.Options.SetRequestHeader("Authorization", $"Bearer {relay.DeviceId}.{Unprotect(relay.SecretProtected)}");
                var url = new Uri("wss://" + RelayEndpoint["https://".Length..] + "/v1/connect");
                try { await ws.ConnectAsync(url, ct); }
                catch (WebSocketException) when (ws.HttpStatusCode == HttpStatusCode.Unauthorized)
                {
                    // The server no longer knows this PC (removed, or silent for 90 days): register again next round.
                    relay.DeviceId = "";
                    relay.SecretProtected = "";
                    relay.Chats.Clear();
                    _save();
                    throw;
                }

                _ws = ws;
                failures = 0;
                RelaySend(new JsonObject { ["t"] = "hello", ["label"] = Environment.MachineName });
                using var pings = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = PingLoop(pings.Token);
                await Receive(ws, ct);
                pings.Cancel();
                if (!ct.IsCancellationRequested) Set(BotState.Connecting, L.T("اتصال به سرور شب‌پا قطع شد؛ دوباره وصل می‌شم…", "Lost the Shabpa server; reconnecting…"));
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1) Log.Write($"relay: {ex.GetType().Name}: {ex.Message}");
                if (failures >= 2) Set(BotState.Error, L.T("به سرور شب‌پا دسترسی نیست. اینترنت یا پروکسی رو چک کن؛ خودم دوباره تلاش می‌کنم.", "The Shabpa server is unreachable. Check the internet or proxy; I will keep retrying."));
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task PingLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                await SendRaw("ping");
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task Receive(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new System.IO.MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, result.Count);
            if (message.Length > 64 * 1024) return;
            if (!result.EndOfMessage) continue;
            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            if (text == "pong") continue;
            try { OnRelay(text); }
            catch (Exception ex) { Log.Write($"relay message: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private bool Linked => _settings().Relay.Chats.Count > 0;

    private void SetLinkState()
    {
        Set(Linked ? BotState.Connected : BotState.WaitingForPair, "");
        if (!Linked && !LinkCodeFresh) RequestCode();
    }

    /// <summary>One event from the relay.</summary>
    internal void OnRelay(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var m = doc.RootElement;
        var chat = m.TryGetProperty("chat", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0;
        var relay = _settings().Relay;
        switch (Str(m, "t"))
        {
            case "ready":
                BotName = Str(m, "bot");
                var ids = m.TryGetProperty("chats", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt64()).ToHashSet()
                    : [];
                var changed = relay.Chats.RemoveAll(x => !ids.Contains(x.Id)) > 0;
                foreach (var id in ids.Where(id => relay.Chats.All(x => x.Id != id)))
                {
                    relay.Chats.Add(new RelayChat { Id = id });
                    changed = true;
                }
                if (changed) _save();
                SetLinkState();
                break;
            case "code":
                LinkCode = Str(m, "code");
                var ttl = m.TryGetProperty("ttl", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 600;
                _linkCodeUntil = DateTime.Now.AddSeconds(Math.Max(30, ttl - 30));
                if (Str(m, "bot") is { Length: > 0 } bot) BotName = bot;
                Changed?.Invoke();
                break;
            case "pair":
                if (chat == 0) break;
                Pending = new LinkRequest(chat, Str(m, "name"), Str(m, "username"));
                Changed?.Invoke();
                LinkRequested?.Invoke(Pending);
                break;
            case "msg":
                var stale = !m.TryGetProperty("date", out var d) || d.ValueKind != JsonValueKind.Number
                    || DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(d.GetInt64()) > StaleAfter;
                if (HandleShared(chat, Str(m, "text"), stale) is { } reply)
                    RelaySend(new JsonObject { ["t"] = "send", ["chat"] = chat, ["text"] = reply, ["keyboard"] = Keyboard });
                break;
            case "unlinked":
                if (relay.Chats.RemoveAll(x => x.Id == chat) > 0) _save();
                SetLinkState();
                break;
            case "error":
                Log.Write("relay: " + Str(m, "error"));
                break;
        }
    }

    private DateTime _codeAskedAt = DateTime.MinValue;

    /// <summary>Asks the relay for a fresh one-time link code (at most once every few seconds; the server caps it too).</summary>
    public void RequestCode()
    {
        if (DateTime.Now - _codeAskedAt < TimeSpan.FromSeconds(5)) return;
        _codeAskedAt = DateTime.Now;
        RelaySend(new JsonObject { ["t"] = "code" });
    }

    public void ApproveLink()
    {
        if (Pending is not { } p) return;
        Pending = null;
        var relay = _settings().Relay;
        relay.Chats.RemoveAll(x => x.Id == p.Chat);
        relay.Chats.Add(new RelayChat { Id = p.Chat, Name = p.Name.Length > 0 ? p.Name : p.Username });
        _save();
        LinkCode = "";
        RelaySend(new JsonObject
        {
            ["t"] = "pair-ok",
            ["chat"] = p.Chat,
            ["text"] = L.T("وصل شد ✅\nاز این به بعد می‌تونی سیستمت رو از همین‌جا کنترل کنی.", "Connected ✅\nYou can now control your PC from here."),
            ["keyboard"] = Keyboard,
        });
        Set(BotState.Connected, "");
    }

    public void RejectLink()
    {
        if (Pending is not { } p) return;
        Pending = null;
        RelaySend(new JsonObject { ["t"] = "pair-no", ["chat"] = p.Chat, ["text"] = L.T("این درخواست روی سیستم رد شد.", "The request was declined on the PC.") });
        Changed?.Invoke();
    }

    private bool RelayOpen => RelayTap is not null || _ws is { State: WebSocketState.Open };

    /// <summary>Unlinks one Telegram account from this PC (shared bot). False while the server is out of reach,
    /// because the server's list is the one that counts and would bring the account back on the next connect.</summary>
    public bool Unlink(long chat)
    {
        if (!RelayOpen) return false;
        RelaySend(new JsonObject { ["t"] = "unlink", ["chat"] = chat, ["text"] = L.T("اتصال این حساب از روی سیستم قطع شد.", "This account was unlinked on the PC.") });
        if (_settings().Relay.Chats.RemoveAll(x => x.Id == chat) > 0) _save();
        SetLinkState();
        return true;
    }

    /// <summary>Before switching to a personal bot: asks the relay to delete everything it holds about this PC.</summary>
    public async Task ForgetSharedAsync()
    {
        var relay = _settings().Relay;
        if (relay.DeviceId.Length == 0) return;
        if (RelayOpen)
        {
            try { await SendRaw(new JsonObject { ["t"] = "forget" }.ToJsonString()).WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { /* the server drops silent devices after 90 days anyway */ }
        }
        relay.DeviceId = "";
        relay.SecretProtected = "";
        relay.Chats.Clear();
        _save();
    }

    /// <summary>Lets tests see what would go to the relay.</summary>
    internal Action<string>? RelayTap;

    private void RelaySend(JsonObject payload)
    {
        var text = payload.ToJsonString();
        if (RelayTap is { } tap) tap(text);
        else _ = SendRaw(text);
    }

    private async Task SendRaw(string text)
    {
        if (_ws is not { State: WebSocketState.Open } ws) return;
        await _sendLock.WaitAsync();
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception ex) { Log.Write($"relay send: {ex.GetType().Name}: {ex.Message}"); }
        finally { _sendLock.Release(); }
    }

    public void Dispose() => Stop();
}
