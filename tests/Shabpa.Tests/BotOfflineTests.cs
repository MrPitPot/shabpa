using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Shabpa.Tests;

/// <summary>The bot link when the server cannot be found (the real log: "No such host is known" every half hour for days).</summary>
[Collection("log")]
public class BotOfflineTests
{
    private sealed class OfflineHandler : HttpMessageHandler
    {
        public volatile bool Down = true;
        public volatile bool Hang;
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (Hang) { await Task.Delay(Timeout.Infinite, ct); }
            if (Down) throw new HttpRequestException("No such host is known. (bot.shabpa.ir:443)", new SocketException((int)SocketError.HostNotFound));
            var path = request.RequestUri!.AbsolutePath;
            var json = path == "/api/device/poll" ? """{"commands":[],"chats":1,"serverTime":0}""" : """{"ok":true}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static (BotLink link, Settings settings) Make(OfflineHandler handler, int waitMs = 15)
    {
        var settings = new Settings { BotEnabled = true, BotDeviceId = "dev1", BotSecretProtected = Secret.Protect("s3cret") };
        var link = new BotLink(() => settings, () => { }, new BotHost { Execute = (_, _) => RemoteReply.Fail("x", "x") }, handler)
        {
            PollEvery = TimeSpan.FromMilliseconds(40), RetryWait = _ => TimeSpan.FromMilliseconds(waitMs),
        };
        return (link, settings);
    }

    private static string WithLog(Action work)
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-botoff-{Guid.NewGuid():N}.log");
        var old = Log.File;
        Log.File = file;
        try
        {
            work();
            return File.Exists(file) ? File.ReadAllText(file) : "";
        }
        finally { Log.File = old; try { File.Delete(file); } catch { /* temp */ } }
    }

    [Fact]
    public void Two_failure_kinds_that_take_turns_are_each_logged_once_per_half_hour()
    {
        var b = new BotBackoff();
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(b.ShouldLog("HttpRequestException/HostNotFound", t));
        Assert.True(b.ShouldLog("TaskCanceledException", t.AddMinutes(1)));
        for (var m = 2; m < 29; m += 3)
        {
            Assert.False(b.ShouldLog("HttpRequestException/HostNotFound", t.AddMinutes(m)));
            Assert.False(b.ShouldLog("TaskCanceledException", t.AddMinutes(m + 1)));
        }
        Assert.True(b.ShouldLog("HttpRequestException/HostNotFound", t.AddMinutes(31)));
    }

    [Fact]
    public void A_name_that_cannot_be_resolved_is_its_own_kind()
    {
        var dns = new HttpRequestException("No such host is known", new SocketException((int)SocketError.HostNotFound));
        var refused = new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused));
        Assert.Equal("HttpRequestException/HostNotFound", BotBackoff.Kind(dns));
        Assert.Equal("HttpRequestException/ConnectionRefused", BotBackoff.Kind(refused));
        Assert.Equal("TaskCanceledException", BotBackoff.Kind(new TaskCanceledException("Timeout of 20 seconds")));
    }

    [Fact]
    public async Task An_unreachable_server_is_logged_once_the_state_changes_once_and_the_link_comes_back_by_itself()
    {
        var handler = new OfflineHandler();
        var (link, _) = Make(handler);
        var changes = new List<(BotState state, int thread)>();
        var testThread = Environment.CurrentManagedThreadId;
        link.Changed += () => { lock (changes) changes.Add((link.State, Environment.CurrentManagedThreadId)); };
        var text = "";
        var start = Stopwatch.StartNew();
        var returnedAfter = 0L;
        var logged = WithLog(() =>
        {
            link.Restart();
            returnedAfter = start.ElapsedMilliseconds;
            Thread.Sleep(700);                 // dozens of failed tries
            handler.Down = false;
            Thread.Sleep(500);
            link.Stop();
        });
        text = logged;
        Assert.True(returnedAfter < 300, $"Restart took {returnedAfter} ms with the server unreachable");
        Assert.True(handler.Calls > 10, $"only {handler.Calls} calls: the loop did not retry");
        Assert.Equal(1, text.Split("bot link: HttpRequestException").Length - 1);   // one line for dozens of tries
        Assert.Contains("reachable again after", text);
        lock (changes)
        {
            Assert.Equal(1, changes.Count(c => c.state == BotState.Error));            // the error state is announced once, not on every try
            Assert.DoesNotContain(changes, c => c.state != BotState.Connecting && c.thread == testThread);   // after the start, never raised on the caller's thread
        }
        await Task.CompletedTask;
    }

    [Fact]
    public void Stopping_a_link_whose_request_hangs_does_not_wait_for_it()
    {
        var handler = new OfflineHandler { Hang = true };
        var (link, _) = Make(handler);
        link.Restart();
        Thread.Sleep(200);
        Assert.True(handler.Calls >= 1);
        var sw = Stopwatch.StartNew();
        link.Dispose();
        Assert.True(sw.ElapsedMilliseconds < 200, $"Dispose took {sw.ElapsedMilliseconds} ms");
    }
}
