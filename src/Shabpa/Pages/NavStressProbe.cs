using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--nav-stress &lt;report&gt; [rounds]</c> (needs <c>--screenshots &lt;folder&gt;</c>; the window sits off-screen): switches
/// pages and tabs over and over, at several speeds, and writes after every few rounds how the process looks: the deepest
/// visual-tree chain, number of visuals, handles, GDI and USER objects, threads, memory, the deepest call stack seen on the
/// window thread and the longest gap between two dispatcher beats. A leak, a growing tree or a stall shows as a trend.</summary>
internal static class NavStressProbe
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    public static async Task Run(MainWindow window, string report, int rounds)
    {
        Hub.I.Settings.Animations = true;
        StackGauge.Enabled = true;
        var sb = new System.Text.StringBuilder();
        void Say(string t) { sb.AppendLine(t); File.AppendAllText(report, t + Environment.NewLine); }
        File.WriteAllText(report, "");
        // the crash breadcrumb, as in a normal run (a screenshot run does not start it): the report ends with what it would leave behind
        var flagFolder = Path.Combine(Path.GetTempPath(), "shabpa-navstress-flag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(flagFolder);
        RunMarker.Begin(flagFolder, "probe", () => window.CurrentPage, startTimer: false);
        await Task.Delay(2500);

        var last = Stopwatch.GetTimestamp();
        double worstGap = 0, worstEver = 0;
        var deepestStack = 0;
        var beat = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(10) };
        beat.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var gap = (now - last) * 1000.0 / Stopwatch.Frequency;
            worstGap = Math.Max(worstGap, gap);
            worstEver = Math.Max(worstEver, gap);
            last = now;
            deepestStack = Math.Max(deepestStack, new StackTrace().FrameCount);
            StackGauge.Sample("beat");
        };
        beat.Start();

        string Metrics(string phase, int n)
        {
            var (depth, count) = VisualStats.Measure(window.CaptureRoot);
            using var self = Process.GetCurrentProcess();
            var line = $"{phase} #{n} page={window.CurrentPage} depth={depth} visuals={count} handles={self.HandleCount} gdi={GetGuiResources(self.Handle, 0)} user={GetGuiResources(self.Handle, 1)} " +
                       $"threads={self.Threads.Count} privMB={self.PrivateMemorySize64 >> 20} managedMB={GC.GetTotalMemory(false) >> 20} stack={deepestStack} sites=[{StackGauge.Report()}] gap={worstGap:0}ms";
            worstGap = 0;
            return line;
        }

        var args = Environment.GetCommandLineArgs();
        var only = Array.IndexOf(args, "--keys") is var k and >= 0 && k + 1 < args.Length ? args[k + 1].Split(',') : null;
        var keys = window.CaptureKeys.Where(x => only is null || only.Contains(x)).ToList();
        // idle layout: a page that has settled must stop laying itself out. Count layout passes over one second per page.
        var passes = 0;
        window.CaptureRoot.LayoutUpdated += (_, _) => passes++;
        foreach (var key in keys)
        {
            window.Navigate(key);
            await Task.Delay(1800);
            var seconds = only is null ? 1 : 8;
            for (var sec = 0; sec < seconds; sec++)
            {
                passes = 0;
                await Task.Delay(1000);
                Say($"idle-layout {key}: {passes} layout passes in 1 s");
            }
        }
        var rng = new Random(7);
        Say(Metrics("start", 0));
        // (phase, delay between switches in ms, switches per round)
        foreach (var (phase, gap) in new[] { ("slow-250ms", 250), ("rapid-30ms", 30), ("burst-0ms", 0), ("rapid-5ms", 5) })
        {
            if (rounds <= 0) break;
            var total = phase.StartsWith("slow") ? Math.Min(rounds, 150) : rounds;
            for (var i = 1; i <= total; i++)
            {
                var key = keys[rng.Next(keys.Count)];
                window.Navigate(key);
                if (gap > 0) await Task.Delay(gap);
                else if (i % 25 == 0) await Task.Delay(1);
                if (i % Math.Max(1, total / 10) == 0) Say(Metrics(phase, i));
            }
            await Task.Delay(2500);
            Say(Metrics(phase + "-settled", total));
        }
        // the tabs inside a page (live / history, ...): hammered with no pause, on every page that has them
        if (rounds > 0)
            foreach (var key in keys)
            {
                window.Navigate(key);
                await Task.Delay(400);
                if (window.CurrentPageObject is not TabsPage tabsPage) continue;
                var tabKeys = tabsPage.Keys;
                var worstBefore = worstEver;
                for (var i = 0; i < rounds; i++)
                {
                    tabsPage.Select(tabKeys[rng.Next(tabKeys.Count)]);
                    if (i % 3 == 0) await Task.Delay(i % 2 == 0 ? 0 : 5);
                }
                await Task.Delay(1500);
                Say($"tabs {key}: {tabKeys.Count} tabs x {rounds} quick switches, worst gap {worstEver:0} ms (before {worstBefore:0}), {Metrics("tabs", rounds)}");
            }
        // the sidebar one by one, the way a hand does it, through the radio buttons themselves
        Say($"worst gap over the whole run: {worstEver:0} ms");
        await Task.Delay(400);
        RunMarker.Beat();
        Say("running.flag at the end: " + File.ReadAllText(Path.Combine(flagFolder, RunMarker.FileName)));
        RunMarker.End();
        try { Directory.Delete(flagFolder, true); } catch { /* temp */ }
        beat.Stop();
        Say("done");
        Application.Current.Shutdown(0);
    }
}
