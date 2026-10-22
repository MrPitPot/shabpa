using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Tests;

public class ButtonClickGuardTests
{
    private static string Src(params string[] parts) => Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shabpa", .. parts]));

    /// <summary>Marking the preview "mouse button up" of a button as handled also marks the bubbling one, so the button never sees the
    /// release: no Click, and it keeps the mouse captured. (This froze the Playtime page: the trash icons did nothing and the whole
    /// window stopped answering clicks.) A button already marks its own release as handled, so nothing underneath opens anyway.</summary>
    [Fact]
    public void No_code_swallows_the_release_of_a_button()
    {
        var rule = new Regex(@"Preview(MouseLeftButtonUp|MouseUp)\s*\+=\s*\(\s*\w+\s*,\s*(\w+)\s*\)\s*=>\s*\2\.Handled\s*=\s*true");
        var offenders = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => rule.IsMatch(File.ReadAllText(f))).ToList();
        Assert.Empty(offenders);
    }
}

public class RunMarkerTests
{
    private static string Folder()
    {
        var d = Path.Combine(Path.GetTempPath(), "shabpa-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void A_clean_exit_leaves_nothing_behind()
    {
        var d = Folder();
        try
        {
            Assert.Null(RunMarker.Begin(d, "9.9.9", () => "HomePage", startTimer: false));
            Assert.True(File.Exists(Path.Combine(d, RunMarker.FileName)));
            RunMarker.End();
            Assert.False(File.Exists(Path.Combine(d, RunMarker.FileName)));
        }
        finally { RunMarker.End(); Directory.Delete(d, true); }
    }

    [Fact]
    public void A_flag_left_by_a_dead_run_is_reported_with_its_last_page()
    {
        var d = Folder();
        try
        {
            File.WriteAllText(Path.Combine(d, RunMarker.FileName),
                """{"Version":"3.9.0","Pid":999999,"Started":"2026-10-22T13:00:00","Heartbeat":"2026-10-22T13:33:50","Page":"SettingsPage"}""");
            var report = RunMarker.Begin(d, "9.9.9", () => "HomePage", startTimer: false);
            Assert.NotNull(report);
            Assert.Contains("ended unexpectedly", report);
            Assert.Contains("3.9.0", report);
            Assert.Contains("13:33:50", report);
            Assert.Contains("SettingsPage", report);
        }
        finally { RunMarker.End(); Directory.Delete(d, true); }
    }

    [Fact]
    public void A_flag_of_a_run_that_is_still_alive_is_not_called_a_crash()
    {
        var mark = new RunMarker.Mark { Version = "3.9.0", Pid = Environment.ProcessId + 1, Started = DateTime.Now };
        Assert.Null(RunMarker.Describe(mark, _ => true));
        Assert.NotNull(RunMarker.Describe(mark, _ => false));
    }

    [Fact]
    public void The_heartbeat_writes_the_open_page()
    {
        var d = Folder();
        try
        {
            var page = "HomePage";
            RunMarker.Begin(d, "9.9.9", () => page, startTimer: false);
            page = "DnsPage";
            RunMarker.Beat();
            var mark = System.Text.Json.JsonSerializer.Deserialize<RunMarker.Mark>(File.ReadAllText(Path.Combine(d, RunMarker.FileName)))!;
            Assert.Equal("DnsPage", mark.Page);
            Assert.Equal(Environment.ProcessId, mark.Pid);
        }
        finally { RunMarker.End(); Directory.Delete(d, true); }
    }
}

public class BotBackoffTests
{
    [Fact]
    public void Waits_grow_and_stop_at_five_minutes()
    {
        Assert.Equal(5, BotBackoff.Wait(1).TotalSeconds);
        Assert.Equal(10, BotBackoff.Wait(2).TotalSeconds);
        Assert.Equal(40, BotBackoff.Wait(4).TotalSeconds);
        Assert.Equal(300, BotBackoff.Wait(7).TotalSeconds);
        Assert.Equal(300, BotBackoff.Wait(500).TotalSeconds);
    }

    [Fact]
    public void The_same_failure_is_logged_once_per_half_hour()
    {
        var b = new BotBackoff();
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(b.ShouldLog("HttpRequestException", t));
        Assert.False(b.ShouldLog("HttpRequestException", t.AddMinutes(5)));
        Assert.True(b.ShouldLog("TaskCanceledException", t.AddMinutes(6)));
        Assert.False(b.ShouldLog("TaskCanceledException", t.AddMinutes(20)));
        Assert.True(b.ShouldLog("TaskCanceledException", t.AddMinutes(40)));
    }

    [Fact]
    public async Task An_empty_answer_does_not_make_the_link_ask_in_a_tight_loop()
    {
        var calls = 0;
        var handler = new CountingEmptyHandler(() => Interlocked.Increment(ref calls));
        var settings = new Settings { BotEnabled = true, BotDeviceId = "dev1", BotSecretProtected = Secret.Protect("s3cret") };
        using var link = new BotLink(() => settings, () => { }, new BotHost { Execute = (_, _) => RemoteReply.Fail("x", "x") }, handler) { PollEvery = TimeSpan.FromMilliseconds(100) };
        link.Restart();
        await Task.Delay(1000);
        link.Stop();
        Assert.InRange(calls, 1, 20);
    }

    private sealed class CountingEmptyHandler(Action hit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            hit();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("") });
        }
    }
}

public class OffloadDepthTests
{
    [Fact]
    public void Slow_calls_cannot_pile_up_on_the_window_thread()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() => { dispatcher = Dispatcher.CurrentDispatcher; ready.Set(); Dispatcher.Run(); }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(ready.Wait(5000));
        var deepest = 0;
        var refused = 0;
        void Ask(int level)
        {
            try
            {
                Offload.KeepResponsive(() =>
                {
                    // while this runs on the pool, the window thread gets another request for the same slow thing
                    dispatcher!.BeginInvoke(() => Ask(level + 1));
                    Thread.Sleep(150);
                    return 0;
                }, dispatcher);
                deepest = Math.Max(deepest, level);
            }
            catch (OffloadBusyException) { refused++; }
        }
        dispatcher!.Invoke(() => Ask(1));
        Thread.Sleep(1500);
        dispatcher.InvokeShutdown();
        Assert.True(refused >= 1, "the fourth nested call should have been refused");
        Assert.True(deepest <= Offload.MaxDepth);
    }
}

public class StallStackThreadTests
{
    [Fact]
    public void The_stack_is_still_written_when_the_thread_pool_is_starved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shabpa-stall-" + Guid.NewGuid().ToString("N"));
        var oldFile = Log.File;
        Log.File = Path.Combine(folder, "shabpa.log");
        var release = new ManualResetEventSlim();
        var oldReader = UiWatchdog.StackReader;
        UiWatchdog.StackReader = _ => ["Fake.Frame"];
        try
        {
            // occupy the pool threads and queue more behind them
            ThreadPool.GetMinThreads(out var min, out _);
            for (var i = 0; i < Math.Max(min, Environment.ProcessorCount) * 4; i++) ThreadPool.QueueUserWorkItem(_ => release.Wait());
            Thread.Sleep(200);
            UiWatchdog.WriteStackForTest(1, 3000);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var text = "";
            while (DateTime.UtcNow < deadline && !text.Contains("Fake.Frame"))
            {
                Thread.Sleep(50);
                try { if (File.Exists(Log.File)) text = File.ReadAllText(Log.File); } catch (IOException) { }
            }
            Assert.Contains("Fake.Frame", text);
        }
        finally
        {
            release.Set();
            UiWatchdog.StackReader = oldReader;
            Log.File = oldFile;
            try { Directory.Delete(folder, true); } catch { }
        }
    }
}
