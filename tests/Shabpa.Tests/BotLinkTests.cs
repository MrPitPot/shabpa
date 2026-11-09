using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Shabpa.Core;

namespace Shabpa.Tests;

/// <summary>A stand-in for the ShabPa_Bot server: answers the device API the way docs/PROTOCOL.md says.</summary>
internal sealed class FakeBotServer : HttpMessageHandler
{
    public readonly List<(string Path, string Auth, string Body)> Calls = [];
    public readonly Queue<string> Commands = new();
    public int Chats;
    public bool ForgetDevice;
    public int FailEvents;
    public string? DownHost; // requests to this host fail like a cut connection
    private int _codes;
    public int BotBalance;                                  // what the bot holds for the one linked chat
    public bool FeathersWaiting;                            // the bot tells the app in its poll answer that feathers are waiting
    public bool LoseNextTakeAnswer;
    public int? PollEverySeconds;                           // the pace the server asks for in its poll answer (null: it says nothing)
    public Dictionary<string, (int Status, int Feathers)> Codes = [];   // what the bot answers to a feather code                         // the bot takes the feathers, but the answer never arrives
    private readonly Dictionary<string, string> _takes = [];

    public int Count(string path) { lock (Calls) return Calls.Count(c => c.Path == path); }
    public string[] Bodies(string path) { lock (Calls) return [.. Calls.Where(c => c.Path == path).Select(c => c.Body)]; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (DownHost is not null && request.RequestUri!.Host == DownHost) throw new HttpRequestException("connection cut");
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var auth = request.Headers.Authorization?.ToString() ?? "";
        lock (Calls) Calls.Add((path, auth, body));
        if (path != "/api/device/pair" && (ForgetDevice || auth != "Bearer dev1.s3cret"))
        {
            ForgetDevice = false;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        if (path == "/api/device/event" && FailEvents > 0)
        {
            FailEvents--;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        if (path == "/api/device/feathers/redeem")
        {
            var code = JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()!;
            var (status, feathers) = Codes.TryGetValue(code, out var known) ? known : (404, 0);
            if (status == 200) BotBalance += feathers;
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(status == 200 ? $$"""{"ok":true,"feathers":{{feathers}},"balance":{{BotBalance}}}""" : "{}", Encoding.UTF8, "application/json") };
        }
        if (path == "/api/device/feathers/take")
        {
            lock (_takes)
            {
                var id = JsonDocument.Parse(body).RootElement.GetProperty("requestId").GetString()!;
                if (!_takes.TryGetValue(id, out var cached))
                {
                    _takes[id] = cached = $$"""{"ok":true,"taken":{{BotBalance}},"balance":0}""";
                    BotBalance = 0;
                }
                if (LoseNextTakeAnswer) { LoseNextTakeAnswer = false; throw new HttpRequestException("connection cut"); }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cached, Encoding.UTF8, "application/json") };
            }
        }
        string json = path switch
        {
            "/api/device/pair" => """{"deviceId":"dev1","secret":"s3cret","code":"K7QM-4TXE","expiresIn":600}""",
            "/api/device/code" => $$"""{"code":"NEW{{++_codes}}-CODE","expiresIn":600}""",
            "/api/device/poll" => $$"""{"commands":[{{string.Join(',', DrainCommands())}}],"chats":{{Chats}},"serverTime":0{{(FeathersWaiting ? ",\"feathers\":true" : "")}}{{(PollEverySeconds is { } pace ? $",\"pollEvery\":{pace}" : "")}}}""",
            "/api/device/event" => """{"ok":true,"sent":1}""",
            "/api/device/feathers" => $$"""{"chats":[{"ref":"abc123def456","balance":{{BotBalance}},"streak":0,"best":0,"owned":[]}]}""",
            _ => """{"ok":true}""",
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private List<string> DrainCommands()
    {
        lock (Commands)
        {
            var all = Commands.ToList();
            Commands.Clear();
            return all;
        }
    }
}

public class BotLinkTests
{
    private readonly Settings _settings = new();
    private readonly FakeBotServer _server = new();
    private readonly List<string> _ran = [];

    private string? _outbox;

    private BotLink Link() => new(() => _settings, () => { }, new BotHost
    {
        Execute = (type, args) =>
        {
            lock (_ran) _ran.Add(type);
            return type == "status" ? RemoteReply.Done("وضعیت", "STATUS") : RemoteReply.Fail("نه", "NO");
        },
    }, _server, outboxFile: _outbox) { PollEvery = TimeSpan.FromMilliseconds(30) };

    /// <summary>A link whose app side keeps what the bot hands over, the way the engine does: one open take per account, closed in the same step the feathers are added.</summary>
    private BotLink FeatherLink(Dictionary<string, string> open, Action<int> added, TimeSpan? every = null) => new(() => _settings, () => { }, new BotHost
    {
        Execute = (_, _) => RemoteReply.Fail("", ""),
        FeatherTicket = (handle, balance) =>
        {
            lock (open)
            {
                if (open.TryGetValue(handle, out var id)) return id;
                if (balance <= 0) return null;
                return open[handle] = Guid.NewGuid().ToString("N");
            }
        },
        FeatherTaken = (handle, ticket, taken) =>
        {
            lock (open)
            {
                if (!open.TryGetValue(handle, out var id) || id != ticket) return;
                open.Remove(handle);
                added(taken);
            }
        },
    }, _server, outboxFile: _outbox) { PollEvery = TimeSpan.FromMilliseconds(30), FeatherEvery = every ?? TimeSpan.FromMilliseconds(60) };

    [Fact]
    public void The_app_polls_at_the_pace_the_server_asks_for_except_while_a_pairing_code_is_shown()
    {
        _server.PollEverySeconds = 1;
        using var link = Link();
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        Assert.NotEqual("", link.Code);
        WaitFor(() => link.NextWait > TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMilliseconds(30), link.NextWait);       // a code is on screen: the person is about to send it, keep the quick rhythm
        _server.Chats = 1;
        WaitFor(() => link.State == BotState.Connected && link.NextWait == TimeSpan.FromSeconds(1));
        _server.PollEverySeconds = null;                                 // a server that says nothing: the app's own rhythm
        WaitFor(() => link.NextWait == TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public void Feathers_the_bot_holds_are_taken_into_the_app_once()
    {
        var open = new Dictionary<string, string>();
        var total = 0;
        _server.BotBalance = 300;
        using var link = FeatherLink(open, n => Interlocked.Add(ref total, n));
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        _server.Chats = 1;
        WaitFor(() => Volatile.Read(ref total) == 300);
        Thread.Sleep(300);                                          // several more rounds: nothing is taken again
        Assert.Equal(300, Volatile.Read(ref total));
        Assert.Equal(1, _server.Count("/api/device/feathers/take"));
        lock (open) Assert.Empty(open);
        _server.BotBalance = 30;                                    // the person claimed more in the bot
        WaitFor(() => Volatile.Read(ref total) == 330);
    }

    [Fact]
    public void When_the_bot_says_feathers_are_waiting_they_are_taken_at_once_not_at_the_next_round()
    {
        var open = new Dictionary<string, string>();
        var total = 0;
        using var link = FeatherLink(open, n => Interlocked.Add(ref total, n), TimeSpan.FromHours(1));
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        _server.Chats = 1;
        WaitFor(() => _server.Count("/api/device/feathers") == 1);       // the first round after linking
        Thread.Sleep(200);
        Assert.Equal(1, _server.Count("/api/device/feathers"));          // and then nothing for an hour...
        _server.BotBalance = 300;                                         // ...until a feather code is used in the bot
        _server.FeathersWaiting = true;
        WaitFor(() => Volatile.Read(ref total) == 300);
    }

    [Fact]
    public async Task A_feather_code_typed_to_boofi_goes_to_the_bot_and_the_feathers_arrive_at_once()
    {
        var open = new Dictionary<string, string>();
        var total = 0;
        _server.Codes["NIGHT100"] = (200, 300);
        _server.Codes["USED1"] = (409, 0);
        _server.Codes["SLOW1"] = (429, 0);
        using var link = FeatherLink(open, n => Interlocked.Add(ref total, n), TimeSpan.FromHours(1));
        Assert.Equal(FeatherCodeStatus.NotLinked, (await link.RedeemCodeAsync("NIGHT100")).Status);      // not linked yet
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        _server.Chats = 1;
        WaitFor(() => link.State == BotState.Connected);
        var ok = await link.RedeemCodeAsync("NIGHT100");
        Assert.Equal((FeatherCodeStatus.Ok, 300), (ok.Status, ok.Feathers));
        Assert.Equal(300, Volatile.Read(ref total));                                                      // in the app already, not at the next round
        Assert.Equal(FeatherCodeStatus.Used, (await link.RedeemCodeAsync("USED1")).Status);
        Assert.Equal(FeatherCodeStatus.Limited, (await link.RedeemCodeAsync("SLOW1")).Status);
        Assert.Equal(FeatherCodeStatus.Unknown, (await link.RedeemCodeAsync("NOPE")).Status);
        Assert.Equal(300, Volatile.Read(ref total));
    }

    [Fact]
    public void An_answer_lost_on_the_way_is_asked_again_and_the_feathers_are_added_exactly_once()
    {
        var open = new Dictionary<string, string>();
        var total = 0;
        _server.BotBalance = 300;
        _server.LoseNextTakeAnswer = true;
        using var link = FeatherLink(open, n => Interlocked.Add(ref total, n));
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        _server.Chats = 1;
        WaitFor(() => Volatile.Read(ref total) == 300);
        Thread.Sleep(300);
        Assert.Equal(300, Volatile.Read(ref total));               // the bot had already emptied its balance; the retry got the same answer
        Assert.True(_server.Count("/api/device/feathers/take") >= 2);
    }

    private static void WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException();
            Thread.Sleep(10);
        }
    }

    private static long Exp(int seconds) => DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds();

    [Fact]
    public void Nothing_is_sent_until_the_person_chooses_to_connect()
    {
        using var link = Link();
        link.Restart();
        Thread.Sleep(100);
        Assert.Equal(BotState.Off, link.State);
        Assert.Empty(_server.Calls);
    }

    [Fact]
    public void Connecting_registers_the_pc_shows_a_code_and_keeps_the_secret_encrypted()
    {
        using var link = Link();
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        Assert.Equal("K7QM-4TXE", link.Code);
        Assert.Equal("dev1", _settings.BotDeviceId);
        Assert.DoesNotContain("s3cret", _settings.BotSecretProtected);
        Assert.Equal("s3cret", Secret.Unprotect(_settings.BotSecretProtected));
        Assert.Equal(1, _server.Count("/api/device/pair"));
    }

    [Fact]
    public void A_linked_chat_runs_commands_and_gets_the_answer()
    {
        using var link = Link();
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        _server.Chats = 1;
        lock (_server.Commands) _server.Commands.Enqueue($$"""{"id":"c1","type":"status","args":{},"exp":{{Exp(120)}}}""");
        WaitFor(() => _server.Count("/api/device/result") == 1);
        Assert.Equal(BotState.Connected, link.State);
        Assert.Equal("", link.Code); // used, so no longer shown
        using var result = JsonDocument.Parse(_server.Bodies("/api/device/result")[0]);
        Assert.Equal("c1", result.RootElement.GetProperty("id").GetString());
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("STATUS", result.RootElement.GetProperty("textEn").GetString());
    }

    [Fact]
    public void A_command_that_waited_too_long_never_runs()
    {
        using var link = Link();
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        _server.Chats = 1;
        lock (_server.Commands)
        {
            _server.Commands.Enqueue($$"""{"id":"old","type":"power","args":{"action":"shutdown"},"exp":{{Exp(-5)}}}""");
            _server.Commands.Enqueue($$"""{"id":"new","type":"status","args":{},"exp":{{Exp(120)}}}""");
        }
        WaitFor(() => _server.Count("/api/device/result") == 1);
        lock (_ran) Assert.Equal(["status"], _ran);
    }

    [Fact]
    public void A_pc_the_server_forgot_registers_again()
    {
        using var link = Link();
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        _server.ForgetDevice = true;
        WaitFor(() => _server.Count("/api/device/pair") == 2);
        WaitFor(() => link.Code == "K7QM-4TXE" && _settings.BotDeviceId == "dev1");
    }

    [Fact]
    public async Task Disconnecting_unlinks_on_the_server_and_forgets_the_secret()
    {
        using var link = Link();
        link.Enable();
        WaitFor(() => link.State == BotState.WaitingForPair);
        await link.RemoveAsync();
        Assert.Equal(1, _server.Count("/api/device/unlink"));
        Assert.Equal(BotState.Off, link.State);
        Assert.False(_settings.BotEnabled);
        Assert.Equal("", _settings.BotDeviceId);
        Assert.Equal("", _settings.BotSecretProtected);
    }

    [Fact]
    public void Another_account_gets_a_new_code_while_linked()
    {
        using var link = Link();
        _server.Chats = 1;
        link.Enable();
        WaitFor(() => link.State == BotState.Connected);
        link.NewCode();
        WaitFor(() => link.Code.StartsWith("NEW", StringComparison.Ordinal));
    }

    [Fact]
    public void Old_token_settings_are_gone_from_the_model() =>
        Assert.Null(typeof(Settings).GetProperty("TelegramTokenProtected"));

    [Fact]
    public void Secrets_are_encrypted_at_rest()
    {
        var stored = Secret.Protect("FAKE-SECRET-for-tests");
        Assert.DoesNotContain("FAKE-SECRET", stored);
        Assert.Equal("FAKE-SECRET-for-tests", Secret.Unprotect(stored));
        Assert.Equal("", Secret.Unprotect("not-base64!"));
    }

    private string TempOutbox() => _outbox = Path.Combine(Path.GetTempPath(), "shabpa-test-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void A_game_report_still_arrives_when_the_main_address_is_cut()
    {
        _server.DownHost = new Uri(BotLink.DefaultEndpoint).Host; // bot.shabpa.ir is blocked on this network
        using var link = Link();
        _server.Chats = 1;
        link.Enable();
        WaitFor(() => link.State == BotState.Connected);
        link.Notify("game", "fa", "en", "<b>x</b>");
        WaitFor(() => _server.Count("/api/device/event") == 1 && link.Pending == 0); // delivered through the second address
    }

    [Fact]
    public void A_game_report_reaches_the_server_once()
    {
        using var link = Link();
        _server.Chats = 1;
        link.Enable();
        WaitFor(() => link.State == BotState.Connected);
        link.Notify("game", "fa", "en", "<b>x</b>");
        WaitFor(() => _server.Count("/api/device/event") == 1 && link.Pending == 0);
        Thread.Sleep(150);
        Assert.Equal(1, _server.Count("/api/device/event"));
        Assert.Contains("\"eventId\"", _server.Bodies("/api/device/event")[0]);
        Assert.Contains("\"at\"", _server.Bodies("/api/device/event")[0]); // the server says when a queued report really happened
    }

    [Fact]
    public void A_report_that_failed_is_retried_and_not_duplicated()
    {
        using var link = Link();
        _server.Chats = 1;
        _server.FailEvents = 2;
        link.Enable();
        WaitFor(() => link.State == BotState.Connected);
        link.Notify("game", "fa", "en");
        WaitFor(() => link.Pending == 0);
        Assert.Equal(3, _server.Count("/api/device/event"));
        var ids = _server.Bodies("/api/device/event").Select(b => JsonDocument.Parse(b).RootElement.GetProperty("eventId").GetString()).Distinct().ToList();
        Assert.Single(ids);
    }

    [Fact]
    public void Queued_reports_survive_a_restart_and_the_queue_is_bounded()
    {
        TempOutbox();
        try
        {
            _server.Chats = 1;
            _server.FailEvents = 1000;
            using (var link = Link())
            {
                link.Enable();
                WaitFor(() => link.State == BotState.Connected);
                for (var i = 0; i < BotLink.OutboxMax + 10; i++) link.Notify("game", "r" + i);
                Assert.Equal(BotLink.OutboxMax, link.Pending);
            }
            _server.FailEvents = 0;
            using var again = Link();
            Assert.Equal(BotLink.OutboxMax, again.Pending);
            again.Restart();
            WaitFor(() => again.Pending == 0);
            var ok = _server.Bodies("/api/device/event").Select(b => JsonDocument.Parse(b).RootElement.GetProperty("eventId").GetString()).Distinct().Count();
            Assert.True(ok >= BotLink.OutboxMax);
        }
        finally { if (_outbox is not null) File.Delete(_outbox); }
    }

    [Fact]
    public void Reports_are_skipped_when_the_link_is_off()
    {
        using var link = Link();
        link.Notify("game", "fa");
        Assert.Equal(0, link.Pending);
    }
}
