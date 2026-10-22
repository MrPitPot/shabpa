using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--page-timing &lt;report&gt; [seconds]</c> (needs <c>--screenshots &lt;folder&gt;</c>, best with <c>--live</c>; the window sits off-screen):
/// opens every page, keeps it open for a few seconds of real ticks, and writes the longest gap of the window's dispatcher plus the
/// worst and average time of every page step (Refresh, OnTick, OnSecond, the swap). Anything over 50 ms is marked.</summary>
internal static class PageTimingProbe
{
    public static async Task Run(MainWindow window, string report, int seconds)
    {
        Hub.I.Settings.Animations = false;
        File.WriteAllText(report, $"live={Hub.I.Demo is false} seconds-per-page={seconds}{Environment.NewLine}");
        void Say(string t) => File.AppendAllText(report, t + Environment.NewLine);
        var last = Stopwatch.GetTimestamp();
        double worstGap = 0;
        var beat = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(10) };
        beat.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            worstGap = Math.Max(worstGap, (now - last) * 1000.0 / Stopwatch.Frequency);
            last = now;
        };
        beat.Start();
        var passes = 0;
        window.CaptureRoot.LayoutUpdated += (_, _) => passes++;
        await Task.Delay(3000);
        var args = Environment.GetCommandLineArgs();
        var only = Array.IndexOf(args, "--keys") is var k and >= 0 && k + 1 < args.Length ? args[k + 1].Split(',') : null;
        var offenders = new List<string>();
        var all = args.Contains("--steps-all");
        foreach (var key in window.CaptureKeys.Where(x => only is null || only.Contains(x)))
        {
            PageTiming.Reset();
            worstGap = 0;
            passes = 0;
            var sw = Stopwatch.StartNew();
            window.Navigate(key);
            window.UpdateLayout();
            var navMs = sw.Elapsed.TotalMilliseconds;
            await Task.Delay(seconds * 1000);
            var line = $"{key,-22} navigate+layout {navMs,6:0} ms | worst dispatcher gap {worstGap,6:0} ms | layout passes/s {passes / (double)seconds,5:0.0} | steps >{(all ? 0 : 50)} ms: {PageTiming.Report(all ? 0 : 50)}";
            Say((navMs > 100 || worstGap > 150 || passes / (double)seconds > 30 || PageTiming.Report().Length > 0 ? "SLOW " : "ok   ") + line);
            if (navMs > 50 || PageTiming.Report().Length > 0) offenders.Add(key);
        }
        Say("done; pages with a step over 50 ms: " + string.Join(", ", offenders));
        Application.Current.Shutdown(0);
    }
}
