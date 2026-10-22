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
    private int _codes;

    public int Count(string path) { lock (Calls) return Calls.Count(c => c.Path == path); }
    public string[] Bodies(string path) { lock (Calls) return [.. Calls.Where(c => c.Path == path).Select(c => c.Body)]; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
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
        string json = path switch
        {
            "/api/device/pair" => """{"deviceId":"dev1","secret":"s3cret","code":"K7QM-4TXE","expiresIn":600}""",
            "/api/device/code" => $$"""{"code":"NEW{{++_codes}}-CODE","expiresIn":600}""",
            "/api/device/poll" => $$"""{"commands":[{{string.Join(',', DrainCommands())}}],"chats":{{Chats}},"serverTime":0}""",
            "/api/device/event" => """{"ok":true,"sent":1}""",
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
