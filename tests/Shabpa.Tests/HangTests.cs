using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;

namespace Shabpa.Tests;

public class StallTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void Nothing_is_written_for_a_short_pause()
    {
        var t = new StallTracker();
        Assert.Empty(t.Observe(500, T0));
        Assert.Empty(t.Observe(1900, T0));
        Assert.False(t.Announced);
    }

    [Fact]
    public void A_stall_is_announced_at_two_seconds_while_it_is_still_going_on()
    {
        var t = new StallTracker();
        Assert.Equal([StallAction.Began], t.Observe(2000, T0));
        Assert.True(t.Announced);
        Assert.Empty(t.Observe(2250, T0));
    }

    [Fact]
    public void The_stack_is_taken_once_at_three_seconds()
    {
        var t = new StallTracker();
        t.Observe(2000, T0);
        Assert.Equal([StallAction.TakeStack], t.Observe(3000, T0));
        Assert.Empty(t.Observe(3250, T0));
        Assert.Empty(t.Observe(9000, T0));
    }

    [Fact]
    public void A_stall_that_jumps_past_both_limits_gets_both_actions()
    {
        var t = new StallTracker();
        Assert.Equal([StallAction.Began, StallAction.TakeStack], t.Observe(3100, T0));
    }

    [Fact]
    public void A_long_stall_reports_that_it_is_still_going_every_thirty_seconds()
    {
        var t = new StallTracker();
        t.Observe(3000, T0);
        Assert.Empty(t.Observe(20000, T0));
        Assert.Equal([StallAction.Still], t.Observe(33000, T0));
        Assert.Empty(t.Observe(40000, T0));
        Assert.Equal([StallAction.Still], t.Observe(63000, T0));
    }

    [Fact]
    public void Stacks_are_taken_at_most_once_in_ten_minutes()
    {
        var t = new StallTracker();
        Assert.Contains(StallAction.TakeStack, t.Observe(3000, T0));
        t.Reset();
        Assert.DoesNotContain(StallAction.TakeStack, t.Observe(3000, T0.AddMinutes(9)));
        t.Reset();
        Assert.Contains(StallAction.TakeStack, t.Observe(3000, T0.AddMinutes(10)));
    }

    [Fact]
    public void After_a_reset_the_next_stall_is_announced_again()
    {
        var t = new StallTracker();
        t.Observe(2500, T0);
        t.Reset();
        Assert.False(t.Announced);
        Assert.Contains(StallAction.Began, t.Observe(2100, T0.AddMinutes(1)));
    }
}

[Collection("log")]
public class UiWatchdogTests
{
    [Fact]
    public void A_blocked_window_is_written_to_the_log_with_its_page_and_stack_while_it_is_blocked()
    {
        var file = Path.Combine(Path.GetTempPath(), $"shabpa-watchdog-{Guid.NewGuid():N}.log");
        var oldFile = Log.File;
        Log.File = file;
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var uiThread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        Assert.True(ready.Wait(5000));
        var uiId = 0;
        dispatcher!.Invoke(() => uiId = Environment.CurrentManagedThreadId);

        var oldReader = UiWatchdog.StackReader;
        int? askedFor = null;
        UiWatchdog.StackReader = id => { askedFor = id; return ["Fake.Page.Build", "Fake.Driver.Call"]; };
        try
        {
            var watchdog = new Thread(() => UiWatchdog.Run(dispatcher!, () => "TestPage", uiId, 20)) { IsBackground = true };
            watchdog.Start();
            Thread.Sleep(300); // a few answered pings first
            dispatcher!.BeginInvoke(() => Thread.Sleep(3800)); // the window thread is busy for 3.8 s
            Thread.Sleep(5200);
            UiWatchdog.Stop();
            watchdog.Join(3000);
            var text = File.ReadAllText(file);
            Assert.Contains("not answering for", text);
            Assert.Contains("page: TestPage", text);
            Assert.Contains("over after", text);
            Assert.Contains("ui stall stack", text);
            Assert.Contains("at Fake.Page.Build", text);
            Assert.Equal(uiId, askedFor);
        }
        finally
        {
            UiWatchdog.StackReader = oldReader;
            Log.File = oldFile;
            dispatcher!.InvokeShutdown();
            try { File.Delete(file); } catch { /* temp */ }
        }
    }
}

public class OffloadTests
{
    [Fact]
    public void A_cached_value_answers_at_once_and_the_slow_read_arrives_later()
    {
        var gate = new ManualResetEventSlim();
        var cached = new Cached<int>(() => { gate.Wait(5000); return 42; }, TimeSpan.FromMinutes(1), initial: -1);
        var seen = new ManualResetEventSlim();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(-1, cached.Get(_ => seen.Set()));
        Assert.True(sw.ElapsedMilliseconds < 500, "the first call must not wait for the read");
        gate.Set();
        Assert.True(seen.Wait(5000));
        Assert.Equal(42, cached.Get());
    }

    [Fact]
    public void Only_one_read_runs_at_a_time()
    {
        var running = 0;
        var maxRunning = 0;
        var done = new ManualResetEventSlim();
        var cached = new Cached<int>(() =>
        {
            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            Thread.Sleep(150);
            Interlocked.Decrement(ref running);
            done.Set();
            return 1;
        }, TimeSpan.Zero);
        for (var i = 0; i < 20; i++) cached.Get();
        Assert.True(done.Wait(5000));
        Thread.Sleep(300);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public void A_failing_read_does_not_throw_or_run_again_at_once()
    {
        var calls = 0;
        var cached = new Cached<int>(() => { Interlocked.Increment(ref calls); throw new InvalidOperationException("driver"); }, TimeSpan.FromMinutes(1), initial: 7);
        for (var i = 0; i < 10; i++) Assert.Equal(7, cached.Get());
        Thread.Sleep(300);
        for (var i = 0; i < 10; i++) cached.Get();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void A_set_value_is_returned_without_a_read()
    {
        var calls = 0;
        var cached = new Cached<bool>(() => { Interlocked.Increment(ref calls); return false; }, TimeSpan.FromMinutes(1));
        cached.Set(true);
        Assert.True(cached.Get());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void A_hung_call_gives_the_fallback_after_the_wait_not_never()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = Offload.Bounded(() => { Thread.Sleep(5000); return 5; }, -1, 200);
        Assert.Equal(-1, result);
        Assert.True(sw.ElapsedMilliseconds < 2000);
        Assert.Equal(9, Offload.Bounded(() => 9, -1, 2000));
        Assert.Equal(-1, Offload.Bounded<int>(() => throw new InvalidOperationException(), -1, 2000));
    }

    [Fact]
    public void Without_a_window_thread_the_work_just_runs()
    {
        Assert.Equal(3, Offload.KeepResponsive(() => 3));
    }

    [Fact]
    public void The_window_thread_keeps_handling_messages_while_slow_work_runs()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(ready.Wait(5000));

        var handled = 0;
        var result = 0;
        dispatcher!.Invoke(() =>
        {
            // a timer-like stream of messages that must be handled while the work below is running on the pool
            var stop = false;
            _ = Task.Run(() =>
            {
                while (!Volatile.Read(ref stop)) { dispatcher.BeginInvoke(() => Interlocked.Increment(ref handled)); Thread.Sleep(20); }
            });
            result = Offload.KeepResponsive(() => { Thread.Sleep(600); return 11; }, dispatcher);
            Volatile.Write(ref stop, true);
        });
        dispatcher.InvokeShutdown();
        Assert.Equal(11, result);
        Assert.True(handled >= 5, $"only {handled} messages were handled while waiting");
    }

    [Fact]
    public void An_error_in_the_slow_work_reaches_the_caller()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() => { dispatcher = Dispatcher.CurrentDispatcher; ready.Set(); Dispatcher.Run(); }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(ready.Wait(5000));
        Assert.Throws<InvalidOperationException>(() => dispatcher!.Invoke(() => Offload.KeepResponsive<int>(() => throw new InvalidOperationException("x"), dispatcher)));
        dispatcher!.InvokeShutdown();
    }
}

public class AdapterGuardTests
{
    private sealed class FakeProps : IPInterfaceProperties
    {
        public override bool IsDnsEnabled => false;
        public override string DnsSuffix => "";
        public override bool IsDynamicDnsEnabled => false;
        public override UnicastIPAddressInformationCollection UnicastAddresses => throw new NotSupportedException();
        public override MulticastIPAddressInformationCollection MulticastAddresses => throw new NotSupportedException();
        public override IPAddressInformationCollection AnycastAddresses => throw new NotSupportedException();
        public override IPAddressCollection DnsAddresses => throw new NetworkInformationException(10043);
        public override GatewayIPAddressInformationCollection GatewayAddresses => throw new NetworkInformationException(10043);
        public override IPAddressCollection DhcpServerAddresses => throw new NotSupportedException();
        public override IPAddressCollection WinsServersAddresses => throw new NotSupportedException();
        public override IPv4InterfaceProperties GetIPv4Properties() => throw new NetworkInformationException(10043);
        public override IPv6InterfaceProperties GetIPv6Properties() => throw new NotSupportedException();
    }

    private sealed class FakeNic(string name, Func<IPInterfaceProperties> props, bool throwsStatus = false) : NetworkInterface
    {
        public override string Id => name;
        public override string Name => name;
        public override string Description => name;
        public override IPInterfaceProperties GetIPProperties() => props();
        public override IPv4InterfaceStatistics GetIPv4Statistics() => throw new NotSupportedException();
        public override IPInterfaceStatistics GetIPStatistics() => throw new NotSupportedException();
        public override bool Supports(NetworkInterfaceComponent networkInterfaceComponent) => false;
        public override PhysicalAddress GetPhysicalAddress() => PhysicalAddress.None;
        public override NetworkInterfaceType NetworkInterfaceType => NetworkInterfaceType.Ethernet;
        public override OperationalStatus OperationalStatus => throwsStatus ? throw new NetworkInformationException(10043) : OperationalStatus.Up;
        public override long Speed => 0;
        public override bool IsReceiveOnly => false;
        public override bool SupportsMulticast => false;
    }

    [Fact]
    public void An_adapter_without_ipv4_error_10043_is_skipped_not_fatal()
    {
        var list = DnsConfig.Adapters(() => [new FakeNic("tunnel", () => new FakeProps())]);
        Assert.Empty(list);
    }

    [Fact]
    public void An_adapter_whose_status_cannot_be_read_is_skipped()
    {
        var list = DnsConfig.Adapters(() => [new FakeNic("vanishing", () => new FakeProps(), throwsStatus: true)]);
        Assert.Empty(list);
    }

    [Fact]
    public void A_list_that_cannot_be_had_at_all_is_empty()
    {
        Assert.Empty(DnsConfig.Adapters(() => throw new NetworkInformationException(10043)));
        Assert.Empty(DnsConfig.Adapters(() => throw new SocketException(10043)));
    }

    [Fact]
    public void One_bad_adapter_does_not_hide_the_others_and_the_real_list_works()
    {
        // the real machine: whatever it has, asking never throws
        _ = DnsConfig.Adapters();
        var list = DnsConfig.Adapters(() => [new FakeNic("a", () => new FakeProps()), new FakeNic("b", () => throw new NetworkInformationException(10043))]);
        Assert.Empty(list);
    }
}

public class FontGuardTests
{
    private static string Src(params string[] parts) => Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shabpa", .. parts]));

    [Fact]
    public void No_font_family_in_the_xaml_or_code_is_empty()
    {
        var root = Src();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("FontFamily=\"\"") || text.Contains("new FontFamily(\"\")") || text.Contains("FontFamily(string.Empty)")) offenders.Add(file);
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_shared_font_resources_are_not_empty()
    {
        var theme = File.ReadAllText(Src("Theme.xaml"));
        foreach (var key in new[] { "Ui", "Num", "Icons" })
        {
            var m = System.Text.RegularExpressions.Regex.Match(theme, "<FontFamily x:Key=\"" + key + "\">([^<]*)</FontFamily>");
            Assert.True(m.Success, key);
            Assert.False(string.IsNullOrWhiteSpace(m.Groups[1].Value), key);
        }
    }
}

public class ProcessLookupTests
{
    [Fact]
    public void An_unknown_process_answers_pending_at_once_even_when_the_lookup_is_slow()
    {
        var gate = new ManualResetEventSlim();
        var resolver = new ProcessResolver(pid => { gate.Wait(5000); return new ProcInfo("game", "Game", @"C:\g.exe"); });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Same(ProcessResolver.Pending, resolver.Resolve(1234));
        Assert.True(sw.ElapsedMilliseconds < 500);
        gate.Set();
        ProcInfo? info = null;
        for (var i = 0; i < 100 && (info = resolver.Resolve(1234)) is { } p && ReferenceEquals(p, ProcessResolver.Pending); i++) Thread.Sleep(20);
        Assert.Equal("game", info!.Key);
    }

    [Fact]
    public void Bytes_of_a_process_still_being_looked_up_wait_and_are_counted_once_it_is_known()
    {
        var known = false;
        var meter = new TrafficMeter(pid => known ? new ProcInfo("chrome", "Chrome", "") : ProcessResolver.Pending);
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        meter.Record(10, 1000, upload: false);
        Assert.Empty(meter.Snapshot(1, now)); // nothing is attributed yet, nothing is lost
        meter.Record(10, 500, upload: false);
        known = true;
        var apps = meter.Snapshot(1, now.AddSeconds(1));
        var chrome = Assert.Single(apps);
        Assert.Equal("chrome", chrome.Key);
        Assert.Equal(1500, chrome.DownBytes);
    }

    [Fact]
    public void A_lookup_that_never_finishes_ends_up_as_other_after_a_few_rounds()
    {
        var meter = new TrafficMeter(pid => ProcessResolver.Pending);
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        meter.Record(99, 800, upload: true);
        IReadOnlyList<AppTraffic> apps = [];
        for (var i = 0; i < 8 && apps.Count == 0; i++) apps = meter.Snapshot(1, now.AddSeconds(i));
        var other = Assert.Single(apps);
        Assert.Equal(TrafficMeter.Other, other.Key);
        Assert.Equal(800, other.UpBytes);
    }
}
