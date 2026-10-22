using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Pages;
using Xunit;

namespace Shabpa.Tests;

/// <summary>The crash breadcrumb: what the next start learns about the page switch that was going on when the process died.</summary>
[Collection("Wpf")]
public class NavigationBreadcrumbTests
{
    private static string Folder()
    {
        var d = Path.Combine(Path.GetTempPath(), "shabpa-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static RunMarker.Mark Read(string d) => JsonSerializer.Deserialize<RunMarker.Mark>(File.ReadAllText(Path.Combine(d, RunMarker.FileName)))!;

    [Fact]
    public void The_last_step_and_the_tree_size_reach_the_flag()
    {
        var d = Folder();
        try
        {
            RunMarker.Begin(d, "9.9.9", () => "HealthPage", startTimer: false);
            RunMarker.Note("page health");
            RunMarker.NoteTree(31, 412);
            RunMarker.Beat(); // a heartbeat writes whatever the notes collected
            var mark = Read(d);
            Assert.Equal("page health", mark.Action);
            Assert.Equal(31, mark.TreeDepth);
            Assert.Equal(412, mark.Visuals);
            Assert.Equal(1, mark.Switches);
        }
        finally { RunMarker.End(); Directory.Delete(d, true); }
    }

    [Fact]
    public void The_next_start_logs_the_step_the_dead_run_was_on()
    {
        var old = new RunMarker.Mark
        {
            Version = "3.10.0", Pid = Environment.ProcessId + 1, Started = new DateTime(2026, 10, 1, 9, 0, 0), Heartbeat = new DateTime(2026, 10, 1, 9, 5, 0),
            Page = "TasksPage", Action = "tab TasksPage/TodoPage", ActionAt = new DateTime(2026, 10, 1, 9, 5, 3), Switches = 812, TreeDepth = 38, Visuals = 1500,
        };
        var line = RunMarker.Describe(old, _ => false)!;
        Assert.Contains("tab TasksPage/TodoPage", line);
        Assert.Contains("switch #812", line);
        Assert.Contains("38 deep", line);
        Assert.Contains("1500 visuals", line);
        // an old flag from before this field existed still reads fine
        var plain = RunMarker.Describe(new RunMarker.Mark { Version = "3.9.0", Pid = Environment.ProcessId + 1, Started = DateTime.Now }, _ => false)!;
        Assert.DoesNotContain("last step", plain);
    }

    [Fact]
    public void A_burst_of_notes_costs_a_write_at_most_every_quarter_second_and_the_last_one_still_lands()
    {
        var d = Folder();
        try
        {
            RunMarker.Begin(d, "9.9.9", () => "", startTimer: false);
            var path = Path.Combine(d, RunMarker.FileName);
            var writes = 0;
            var lastStamp = File.GetLastWriteTimeUtc(path);
            for (var i = 0; i < 500; i++)
            {
                RunMarker.Note("page p" + i);
                var stamp = File.GetLastWriteTimeUtc(path);
                if (stamp != lastStamp) { writes++; lastStamp = stamp; }
            }
            Assert.True(writes <= 3, $"{writes} file writes for a burst of 500 notes");
            SpinWait.SpinUntil(() => Read(d).Action == "page p499", 3000);
            Assert.Equal("page p499", Read(d).Action);
            Assert.Equal(500, Read(d).Switches);
        }
        finally { RunMarker.End(); Directory.Delete(d, true); }
    }

    [Fact]
    public void Notes_do_nothing_before_Begin_and_after_End()
    {
        RunMarker.End();
        RunMarker.Note("page home");
        RunMarker.NoteTree(1, 1);
        var d = Folder();
        try
        {
            RunMarker.Begin(d, "9.9.9", () => "", startTimer: false);
            Assert.Equal("", Read(d).Action);
        }
        finally { RunMarker.End(); Directory.Delete(d, true); }
    }
}

public static class StaRunner
{
    public static void Run(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
    }

    /// <summary>The application object lives on the first test thread; every later thread gets its own live brushes (the shared
    /// ones belong to a thread that is gone), the way the rest of the suite does it.</summary>
    public static void EnsureApp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(Ui.Resources(Palette.Find("night")));
        if (!app.Resources.Contains("SegTab"))
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Shabpa;component/Theme.xaml", UriKind.Absolute) });
    }

    /// <summary>Lets the dispatcher run for a while (timers, animations, idle work).</summary>
    public static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}

public class VisualStatsTests
{
    [Fact]
    public void A_very_deep_chain_is_measured_without_using_the_call_stack()
    {
        StaRunner.Run(() =>
        {
            const int depth = 1_500; // deep for a UI (WPF itself refuses 2047); the walk keeps no call-stack frame per level
            var root = new Border();
            var node = root;
            for (var i = 1; i < depth; i++)
            {
                var next = new Border();
                node.Child = next;
                node = next;
            }
            var r = VisualStats.Measure(root);
            Assert.Equal(depth, r.Depth);
            Assert.Equal(depth, r.Count);
        });
    }

    [Fact]
    public void Depth_counts_the_ancestors_and_count_counts_the_subtree()
    {
        StaRunner.Run(() =>
        {
            var leaf = new Border();
            var mid = new Border { Child = leaf };
            var other = new Border();
            var grid = new Grid();
            grid.Children.Add(mid);
            grid.Children.Add(other);
            var top = new Border { Child = grid };
            Assert.Equal(new VisualStats.Result(4, 5), VisualStats.Measure(top));     // top, grid, mid, leaf = 4 deep; five visuals
            Assert.Equal(new VisualStats.Result(4, 1), VisualStats.Measure(leaf));    // three ancestors above, itself alone
            Assert.Equal(new VisualStats.Result(4, 2), VisualStats.Measure(mid));
        });
    }

    [Fact]
    public void The_walk_stops_at_the_limit()
    {
        StaRunner.Run(() =>
        {
            var grid = new Grid();
            for (var i = 0; i < 100; i++) grid.Children.Add(new Border());
            Assert.True(VisualStats.Measure(grid, limit: 10).Count <= 10);
        });
    }
}

public class StackGaugeTests
{
    [Fact]
    public void It_records_nothing_while_off_and_the_deepest_sample_while_on()
    {
        StackGauge.Reset();
        StackGauge.Enabled = false;
        StackGauge.Sample("t.off");
        Assert.Equal(0, StackGauge.Peak("t.off"));
        StackGauge.Enabled = true;
        try
        {
            static void Deeper(int n) { if (n == 0) StackGauge.Sample("t.on"); else Deeper(n - 1); }
            Deeper(0);
            var shallow = StackGauge.Peak("t.on");
            Deeper(40);
            var deep = StackGauge.Peak("t.on");
            Deeper(5);
            Assert.True(deep >= shallow + 40);
            Assert.Equal(deep, StackGauge.Peak("t.on")); // a shallower sample never lowers the peak
            Assert.Contains("t.on=", StackGauge.Report());
        }
        finally { StackGauge.Enabled = false; StackGauge.Reset(); }
    }
}

/// <summary>The page-exit animation and the tab strip under hammering: nothing grows, nothing loops, nothing waits forever.</summary>
[Collection("Wpf")]
public class NavigationStressTests
{
    private sealed class Dummy(string title) : PageBase
    {
        public override string PageTitle => title;
        public int Refreshed;
        public override void Refresh() => Refreshed++;
        public Dummy Init()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title });
            panel.Children.Add(new Border { Child = new TextBlock { Text = title + "2" } });
            Content = panel;
            return this;
        }
    }

    private sealed class Tabs : TabsPage
    {
        public readonly string[] Names = ["a", "b", "c", "d"];
        public Tabs()
        {
            foreach (var n in Names) Add(n, n.ToUpperInvariant(), new Dummy(n).Init());
        }
    }

    private static void EnsureApp() => StaRunner.EnsureApp();

    private static Window OffScreen(UIElement content) => new()
    {
        Content = content, Width = 700, Height = 400, ShowActivated = false, ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
    };

    [Fact]
    public void A_thousand_rapid_tab_switches_do_not_grow_the_tree_loop_the_layout_or_deepen_the_stack()
    {
        StaRunner.Run(() =>
        {
            EnsureApp();
            _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-navstress-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: true);
            Motion.TestOverride = true;
            StackGauge.Reset();
            StackGauge.Enabled = true;
            var tabs = new Tabs();
            var window = OffScreen(tabs);
            window.Show();
            window.UpdateLayout();
            try
            {
                StaRunner.Pump(300);
                var before = VisualStats.Measure(window);
                var rng = new Random(11);
                for (var i = 0; i < 1000; i++)
                {
                    tabs.Select(tabs.Names[rng.Next(tabs.Names.Length)]);
                    if (i % 3 == 0) StaRunner.Pump(1);
                }
                tabs.Select("a");
                StaRunner.Pump(1500);
                var after = VisualStats.Measure(window);
                Assert.Equal(before.Depth, after.Depth);               // no layer is added per switch
                Assert.InRange(after.Count, before.Count - 40, before.Count + 40);

                // settled: the strip must not keep laying itself out (the thumb healing itself in a loop would show here)
                var passes = 0;
                window.LayoutUpdated += (_, _) => passes++;
                StaRunner.Pump(1000);
                Assert.True(passes <= 3, $"{passes} layout passes in one idle second");

                Assert.True(StackGauge.PeakAll() < 120, $"deepest sample {StackGauge.PeakAll()} frames: {StackGauge.Report()}");
            }
            finally
            {
                StackGauge.Enabled = false;
                Motion.TestOverride = null;
                window.Close();
            }
        });
    }

    [Fact]
    public void The_page_exit_reports_back_once_and_leaves_the_host_plain()
    {
        StaRunner.Run(() =>
        {
            Motion.TestOverride = true;
            try
            {
                var host = new Border();
                var window = OffScreen(host);
                window.Show();
                var done = 0;
                Motion.Leave(host, () => done++);
                StaRunner.Pump(900);
                Assert.Equal(1, done);
                Assert.Equal(1.0, host.Opacity);
                Assert.Equal(Transform.Identity, host.RenderTransform);
                window.Close();
            }
            finally { Motion.TestOverride = null; }
        });
    }

    [Fact]
    public void The_page_exit_still_reports_back_when_the_clock_is_late()
    {
        StaRunner.Run(() =>
        {
            Motion.TestOverride = true;
            var saved = Motion.LeaveSafety;
            try
            {
                var host = new Border();
                var window = OffScreen(host);
                window.Show();
                var done = 0;
                // the safety timer is set to fire before the animation could (as if its clock never reported back)
                Motion.LeaveSafety = TimeSpan.FromMilliseconds(-150);
                Motion.Leave(host, () => done++);
                StaRunner.Pump(120);
                Assert.Equal(1, done);
                Assert.Equal(1.0, host.Opacity);
                StaRunner.Pump(600);
                Assert.Equal(1, done); // the clock reporting afterwards does not run it twice
                window.Close();
            }
            finally { Motion.LeaveSafety = saved; Motion.TestOverride = null; }
        });
    }

    [Fact]
    public void An_exit_that_is_replaced_by_a_newer_one_does_not_reset_the_newer_fade()
    {
        StaRunner.Run(() =>
        {
            Motion.TestOverride = true;
            var saved = Motion.LeaveSafety;
            try
            {
                var host = new Border();
                var window = OffScreen(host);
                window.Show();
                int first = 0, second = 0;
                Motion.LeaveSafety = TimeSpan.FromMilliseconds(-150);
                Motion.Leave(host, () => first++);     // its safety fires at ~20 ms
                Motion.LeaveSafety = TimeSpan.FromMilliseconds(300);
                Motion.Leave(host, () => second++);    // the newer exit owns the fade
                StaRunner.Pump(100);
                Assert.Equal(1, first);
                Assert.Equal(0, second);
                Assert.True(host.Opacity < 1.0 || DependencyPropertyHelper.GetValueSource(host, UIElement.OpacityProperty).IsAnimated);
                StaRunner.Pump(800);
                Assert.Equal(1, second);
                Assert.Equal(1.0, host.Opacity);
                window.Close();
            }
            finally { Motion.LeaveSafety = saved; Motion.TestOverride = null; }
        });
    }
}

[Collection("Wpf")]
public class NavigationLeakAndPumpTests
{
    private static void EnsureApp() => StaRunner.EnsureApp();

    private sealed class Quiet : PageBase { }

    private sealed class Strip : TabsPage
    {
        public Strip() { Add("a", "A", new Quiet { Content = new TextBlock { Text = "a" } }); Add("b", "B", new Quiet { Content = new TextBlock { Text = "b" } }); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndDrop()
    {
        var strip = new Strip();
        var window = new Window { Content = strip, Width = 400, Height = 200, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        window.Show();
        window.UpdateLayout();
        StaRunner.Pump(50);
        window.Content = null;   // the page leaves the window (Unloaded)
        window.Close();
        StaRunner.Pump(100);
        return new WeakReference(strip);
    }

    [Fact]
    public void A_tab_page_that_left_the_window_can_be_collected()
    {
        StaRunner.Run(() =>
        {
            EnsureApp();
            _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-leak-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: true);
            var weak = ShowAndDrop();
            for (var i = 0; i < 8 && weak.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                StaRunner.Pump(30);
            }
            Assert.False(weak.IsAlive, "the thrown-away tab page is still held (a global layout event keeps it)");
        });
    }

    [Fact]
    public void The_dns_page_refreshes_without_waiting_for_a_slow_adapter_read()
    {
        StaRunner.Run(() =>
        {
            EnsureApp();
            _ = new Hub(new Store(Path.Combine(Path.GetTempPath(), "shabpa-dns-" + Guid.NewGuid().ToString("N"))), dryRun: true, demo: false);
            using var release = new ManualResetEventSlim();
            var reads = 0;
            var page = new DnsPage
            {
                AdapterSource = () =>
                {
                    Interlocked.Increment(ref reads);
                    release.Wait(10_000);
                    return [new DnsAdapter("Ethernet", 3, "id-3", ["1.1.1.1"], false, true)];
                },
            };
            var window = new Window { Content = page, Width = 900, Height = 600, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
            window.Show();
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var nested = 0;
                // any message loop pushed on top of the refresh would let this timer-less probe run inside it
                void Probe() => nested++;
                page.Dispatcher.BeginInvoke(Probe, DispatcherPriority.Send);
                for (var i = 0; i < 5; i++) page.Refresh();      // the one-second clock asks again and again while the read is slow
                Assert.True(sw.ElapsedMilliseconds < 1500, $"Refresh waited {sw.ElapsedMilliseconds} ms for the adapters");
                Assert.Equal(0, nested);                          // nothing was dispatched inside Refresh: no nested message loop
                Assert.Equal(1, reads);                           // and only one read runs at a time
                release.Set();
                SpinWait.SpinUntil(() => { StaRunner.Pump(20); return FindText(page, "1.1.1.1"); }, 5000);
                Assert.True(FindText(page, "1.1.1.1"), "the page did not refresh itself when the read finished");
            }
            finally { release.Set(); window.Close(); }
        });
    }

    private static bool FindText(DependencyObject root, string text)
    {
        if (root is TextBlock t && t.Text.Contains(text)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindText(VisualTreeHelper.GetChild(root, i), text)) return true;
        return false;
    }
}
