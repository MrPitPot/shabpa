using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shabpa.Core;

public enum BotState { Off, Connecting, WaitingForPair, Connected, Error }

/// <summary>What the shared bot asked the PC to do, and what the PC answers.</summary>
public sealed record RemoteReply(bool Ok, string Fa, string En)
{
    public static RemoteReply Done(string fa, string en) => new(true, fa, en);
    public static RemoteReply Fail(string fa, string en) => new(false, fa, en);
}

/// <summary>What the bot is allowed to do to the app: it can only ask for the fixed set of commands in
/// docs/PROTOCOL.md, never run anything else.</summary>
public sealed class BotHost
{
    /// <summary>Runs one command (status, power, cancel, ram-clean, dim, volume, sleep-mode, sleep-cancel,
    /// after-download) and returns the answer. Called from a background thread.</summary>
    public required Func<string, JsonElement, RemoteReply> Execute { get; init; }
}

/// <summary>Encrypts small secrets for the current Windows user (device secret, support admin key).</summary>
public static class Secret
{
    public static string Protect(string value) =>
        value.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

/// <summary>The link to @ShabPa_Bot. The app never sees a Telegram token: it asks the project's server for a one-time
/// code, the person sends that code to the bot, and from then on the app polls the server for commands.
/// Only outgoing HTTPS calls are made; nothing on the PC listens.</summary>
public sealed class BotLink : IDisposable
{
    public const string DefaultEndpoint = "https://bot.shabpa.ir";
    public const string BotUsername = "ShabPa_Bot";

    internal TimeSpan PollEvery { get; init; } = TimeSpan.FromSeconds(3);

    private readonly Func<Settings> _settings;
    private readonly Action _save;
    private readonly BotHost _host;
    private readonly Func<DateTimeOffset> _now;
    private readonly HttpMessageHandler? _handler;
    private CancellationTokenSource? _cts;
    private HttpClient? _http;

    public BotState State { get; private set; } = BotState.Off;
    public string Detail { get; private set; } = "";
    /// <summary>The code to send to the bot, like K7QM-4TXE, or empty when there is none to show.</summary>
    public string Code { get; private set; } = "";
    public DateTimeOffset CodeExpires { get; private set; }
    public int Chats { get; private set; }
    public event Action? Changed;

    public BotLink(Func<Settings> settings, Action save, BotHost host, HttpMessageHandler? handler = null, Func<DateTimeOffset>? now = null, string? outboxFile = null)
    {
        _outboxFile = outboxFile;
        _settings = settings;
        _save = save;
        _host = host;
        _handler = handler;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        LoadOutbox();
    }

    private string Endpoint => string.IsNullOrWhiteSpace(_settings().BotEndpoint) ? DefaultEndpoint : _settings().BotEndpoint.TrimEnd('/');
    private bool HasDevice => _settings().BotDeviceId.Length > 0 && Secret.Unprotect(_settings().BotSecretProtected).Length > 0;

    /// <summary>Starts (or restarts) the link. Nothing is sent anywhere until the person chose to connect.</summary>
    public void Restart()
    {
        Stop();
        if (!_settings().BotEnabled)
        {
            Code = "";
            Chats = 0;
            Set(BotState.Off, "");
            return;
        }
        HttpMessageHandler handler;
        if (_handler is not null) handler = _handler;
        else
        {
            var real = new HttpClientHandler();
            var proxy = _settings().ProxyUrl.Trim();
            if (proxy.Length > 0)
            {
                try { real.Proxy = new WebProxy(proxy); real.UseProxy = true; }
                catch { Set(BotState.Error, L.T("آدرس پروکسی درست نیست.", "The proxy address is not valid.")); return; }
            }
            handler = real;
        }
        _http = new HttpClient(handler, disposeHandler: _handler is null) { Timeout = TimeSpan.FromSeconds(20) };
        _cts = new CancellationTokenSource();
        Set(BotState.Connecting, L.T("در حال اتصال…", "Connecting…"));
        var token = _cts.Token;
        _ = Task.Run(() => Loop(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _http?.Dispose();
        _http = null;
    }

    /// <summary>Switches the link on from the page; the first call registers this PC with the server.</summary>
    public void Enable()
    {
        _settings().BotEnabled = true;
        _save();
        Restart();
    }

    /// <summary>Forgets this PC on the server and here: every linked chat loses control.</summary>
    public async Task RemoveAsync()
    {
        var http = _http;
        var cts = _cts;
        if (http is not null && HasDevice && cts is not null)
        {
            try { await Send(http, HttpMethod.Post, "/api/device/unlink", null, cts.Token); }
            catch { /* the device secret is dropped below either way, so the server can no longer be reached from here */ }
        }
        Stop();
        var s = _settings();
        s.BotEnabled = false;
        s.BotDeviceId = "";
        s.BotSecretProtected = "";
        Code = "";
        Chats = 0;
        _save();
        Set(BotState.Off, "");
    }

    /// <summary>Asks for a fresh code (the old one is no longer needed once linked, or has expired).</summary>
    public void NewCode()
    {
        Code = "";
        _forceCode = true;
    }

    private volatile bool _forceCode;

    private void Set(BotState state, string detail)
    {
        State = state;
        Detail = detail;
        Changed?.Invoke();
    }

    private async Task<JsonDocument?> Send(HttpClient http, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, Endpoint + path);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (path != "/api/device/pair")
        {
            var secret = Secret.Unprotect(_settings().BotSecretProtected);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{_settings().BotDeviceId}.{secret}");
        }
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException();
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        return text.Length == 0 ? null : JsonDocument.Parse(text);
    }

    private async Task Loop(CancellationToken ct)
    {
        var http = _http!;
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!HasDevice) await Pair(http, ct);
                else if (_forceCode) await FreshCode(http, ct);
                else if (Code.Length > 0 && _now() > CodeExpires)
                {
                    // An expired code is replaced while nobody is linked yet; once linked it is simply dropped.
                    if (Chats == 0) await FreshCode(http, ct);
                    else
                    {
                        Code = "";
                        Changed?.Invoke();
                    }
                }

                using var poll = await Send(http, HttpMethod.Get, "/api/device/poll", null, ct);
                failures = 0;
                _ = Task.Run(() => FlushAsync());
                if (poll is null) continue;
                var root = poll.RootElement;
                var chats = root.TryGetProperty("chats", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                if (chats != Chats || State is BotState.Connecting or BotState.Error)
                {
                    if (chats > Chats) Code = ""; // the code was just used
                    Chats = chats;
                    if (chats == 0 && Code.Length == 0) await FreshCode(http, ct);
                    Set(chats == 0 ? BotState.WaitingForPair : BotState.Connected, "");
                }
                if (root.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
                    foreach (var command in commands.EnumerateArray()) await Run(http, command, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // The server no longer knows this PC (removed, or its storage was reset): register again.
                var s = _settings();
                s.BotDeviceId = "";
                s.BotSecretProtected = "";
                Code = "";
                _save();
            }
            catch (Exception ex)
            {
                failures++;
                if (failures == 1) Log.Write($"bot link: {ex.GetType().Name}: {ex.Message}");
                if (failures >= 2) Set(BotState.Error, L.T("به سرور شب‌پا وصل نشد. اینترنت یا پروکسی رو چک کن؛ خودم دوباره تلاش می‌کنم.", "Could not reach the Shabpa server. Check the internet or proxy; I will keep retrying."));
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            try { await Task.Delay(PollEvery, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task Pair(HttpClient http, CancellationToken ct)
    {
        var name = Environment.MachineName;
        using var doc = await Send(http, HttpMethod.Post, "/api/device/pair",
            new { name = name.Length > 40 ? name[..40] : name, version = typeof(BotLink).Assembly.GetName().Version?.ToString(3) ?? "" }, ct);
        var root = doc!.RootElement;
        var s = _settings();
        s.BotDeviceId = root.GetProperty("deviceId").GetString() ?? "";
        s.BotSecretProtected = Secret.Protect(root.GetProperty("secret").GetString() ?? "");
        _save();
        ReadCode(root);
    }

    private async Task FreshCode(HttpClient http, CancellationToken ct)
    {
        _forceCode = false;
        using var doc = await Send(http, HttpMethod.Post, "/api/device/code", null, ct);
        if (doc is not null) ReadCode(doc.RootElement);
    }

    private void ReadCode(JsonElement root)
    {
        Code = root.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
        var seconds = root.TryGetProperty("expiresIn", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 600;
        CodeExpires = _now().AddSeconds(seconds);
        Changed?.Invoke();
    }

    private async Task Run(HttpClient http, JsonElement command, CancellationToken ct)
    {
        var id = command.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
        var type = command.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        // A command that waited too long (the PC was off, the network was down) must never run by surprise.
        if (command.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.GetInt64() < _now().ToUnixTimeSeconds()) return;
        if (type is "linked" or "unlinked" or "")
        {
            Changed?.Invoke();
            return;
        }
        var args = command.TryGetProperty("args", out var a) ? a : default;
        RemoteReply reply;
        try { reply = _host.Execute(type, args); }
        catch (Exception ex)
        {
            Log.Write($"bot command {type}: {ex.GetType().Name}: {ex.Message}");
            reply = RemoteReply.Fail("انجام نشد.", "That did not work.");
        }
        await Send(http, HttpMethod.Post, "/api/device/result", new { id, ok = reply.Ok, text = Cut(reply.Fa), textEn = Cut(reply.En) }, ct);
    }

    private static string Cut(string text) => text.Length <= 1500 ? text : text[..1500];

    private sealed record Outgoing(string Id, long At, string Kind, string Text, string TextEn, string? Html);

    internal const int OutboxMax = 50;
    internal static readonly TimeSpan OutboxMaxAge = TimeSpan.FromHours(12);
    private readonly string? _outboxFile;
    private readonly List<Outgoing> _outbox = [];
    private readonly object _outboxGate = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);

    private void LoadOutbox()
    {
        if (_outboxFile is null || !File.Exists(_outboxFile)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<Outgoing>>(File.ReadAllText(_outboxFile));
            if (list is not null) lock (_outboxGate) _outbox.AddRange(list.Where(o => o.Id.Length > 0).Take(OutboxMax));
        }
        catch (Exception ex) { Log.Write($"bot event: outbox unreadable, starting empty ({ex.GetType().Name})"); }
    }

    private void SaveOutbox()
    {
        if (_outboxFile is null) return;
        try
        {
            string json;
            lock (_outboxGate) json = JsonSerializer.Serialize(_outbox);
            Directory.CreateDirectory(Path.GetDirectoryName(_outboxFile)!);
            File.WriteAllText(_outboxFile, json);
        }
        catch { /* the in-memory queue still works */ }
    }

    /// <summary>Number of events waiting to be delivered.</summary>
    internal int Pending { get { lock (_outboxGate) return _outbox.Count; } }

    /// <summary>Pushes an event to the linked chats; the server applies each person's notification switches.
    /// kind is one of game, budget, update, warning. The event is queued (bounded, saved to disk) and kept until the
    /// server accepted it, so a network blip or a restart does not lose it. Every outcome is written to shabpa.log.</summary>
    public void Notify(string kind, string text, string? textEn = null, string? html = null)
    {
        if (!_settings().BotEnabled) { Log.Write($"bot event {kind}: skipped (bot link is off)"); return; }
        if (_settings().BotDeviceId.Length == 0) { Log.Write($"bot event {kind}: skipped (this PC is not registered with the bot yet)"); return; }
        if (State == BotState.WaitingForPair && Chats == 0) { Log.Write($"bot event {kind}: skipped (no Telegram chat is linked)"); return; }
        var item = new Outgoing(Guid.NewGuid().ToString("N"), _now().ToUnixTimeSeconds(), kind, Cut(text), Cut(textEn ?? text), html is null ? null : GameMessage.Fit(html));
        lock (_outboxGate)
        {
            _outbox.Add(item);
            while (_outbox.Count > OutboxMax) { Log.Write($"bot event {_outbox[0].Kind}: dropped (queue full)"); _outbox.RemoveAt(0); }
        }
        SaveOutbox();
        Log.Write($"bot event {kind}: queued ({item.Id[..8]})");
        _ = Task.Run(() => FlushAsync());
    }

    /// <summary>Sends the queued events in order; stops at the first transient failure and tries again on the next poll.</summary>
    internal async Task FlushAsync()
    {
        if (_http is not { } http || _cts is not { } cts) return;
        if (!await _flushing.WaitAsync(0)) return;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                Outgoing? next;
                lock (_outboxGate) next = _outbox.Count > 0 ? _outbox[0] : null;
                if (next is null) return;
                if (_now().ToUnixTimeSeconds() - next.At > OutboxMaxAge.TotalSeconds)
                {
                    Log.Write($"bot event {next.Kind}: dropped (older than {OutboxMaxAge.TotalHours:0} h, {next.Id[..8]})");
                    Remove(next);
                    continue;
                }
                try
                {
                    using var reply = await Send(http, HttpMethod.Post, "/api/device/event", new
                    {
                        kind = next.Kind, text = next.Text, textEn = next.TextEn, html = next.Html, parseMode = next.Html is null ? null : "HTML", eventId = next.Id,
                    }, cts.Token);
                    var sent = reply is not null && reply.RootElement.TryGetProperty("sent", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : -1;
                    Remove(next);
                    Log.Write(sent == 0
                        ? $"bot event {next.Kind}: server accepted but delivered to 0 chats (muted, quiet hours or Telegram refused) ({next.Id[..8]})"
                        : $"bot event {next.Kind}: sent ({next.Id[..8]})");
                }
                catch (HttpRequestException ex) when (ex.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError && ex.StatusCode != HttpStatusCode.RequestTimeout && ex.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    // the server refuses this event for good (bad request): retrying would only repeat it
                    Remove(next);
                    Log.Write($"bot event {next.Kind}: failed, not retried ({(int)ex.StatusCode!.Value}) ({next.Id[..8]})");
                }
                catch (UnauthorizedAccessException)
                {
                    Log.Write($"bot event {next.Kind}: failed (device unknown to the server, will retry after re-pairing) ({next.Id[..8]})");
                    return;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    Log.Write($"bot event {next.Kind}: failed, will retry ({ex.GetType().Name}: {ex.Message}) ({next.Id[..8]})");
                    return;
                }
            }
        }
        finally { _flushing.Release(); }
    }

    private void Remove(Outgoing item)
    {
        lock (_outboxGate) _outbox.RemoveAll(o => o.Id == item.Id);
        SaveOutbox();
    }

    public void Dispose() => Stop();
}
