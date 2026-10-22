using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa.Pages;

/// <summary>Test aid for <c>--tabs-test &lt;report&gt;</c> (needs <c>--screenshots &lt;folder&gt;</c>): switches every tab of every tabbed page the way a click does
/// and reports, per switch, how long the click blocked the UI thread, the longest gap between two dispatcher beats afterwards, and
/// where the selection thumb is drawn compared with the chosen tab (it must end exactly on it). Frames of the strip are saved too.</summary>
internal static class TabsProbe
{
    public static async Task Run(MainWindow window, string report, string folder, Action<FrameworkElement, string> save)
    {
        Directory.CreateDirectory(folder);
        Hub.I.Settings.Animations = true;
        var sb = new StringBuilder();
        void Mark(string t) => File.AppendAllText(report + ".progress", $"{DateTime.Now:HH:mm:ss.fff} {t}{Environment.NewLine}");
        sb.AppendLine($"lang={(L.En ? "en" : "fa")}");
        var bad = 0;
        await Task.Delay(2500);

        // a heartbeat on the UI thread: any gap far above its interval is a stall someone would feel as a hang
        var last = Stopwatch.GetTimestamp();
        var worstGap = 0.0;
        var beat = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(10) };
        beat.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            worstGap = Math.Max(worstGap, (now - last) * 1000.0 / Stopwatch.Frequency);
            last = now;
        };
        beat.Start();

        foreach (var page in new[] { "health", "ping" })
        {
            Mark($"navigate {page}");
            window.Navigate(page);
            await Task.Delay(1500);
            if (MotionAudit.All(window).OfType<TabsPage>().FirstOrDefault() is not { } tabs) { sb.AppendLine($"{page}: no tabs"); continue; }
            var keys = tabs.Keys;
            // deep links from another page (the way Home and the notifications open a tab): the thumb must land on the tab
            foreach (var deep in new[] { keys[1], keys[^1], keys[0] })
            {
                window.Navigate("home");
                await Task.Delay(700);
                window.Navigate($"{page}:{deep}");
                await Task.Delay(1600);
                if (MotionAudit.All(window).OfType<TabsPage>().FirstOrDefault() is not { } t2) continue;
                var d = t2.ProbeThumb();
                var off = Math.Abs(d.thumbX - d.tabX) > 1.5 || Math.Abs(d.thumbW - d.tabW) > 1.5;
                sb.AppendLine($"deep {page}:{deep} thumb={d.thumbX:0}/{d.thumbW:0} tab={d.tabX:0}/{d.tabW:0}{(off ? " **MISPLACED**" : "")}");
                if (off) bad++;
                if (page == "health") save(window.CaptureRoot, Path.Combine(folder, $"deep-{page}-{deep}.png"));
            }
            window.Navigate(page);
            await Task.Delay(1000);
            tabs = MotionAudit.All(window).OfType<TabsPage>().First();
            // forward, then back to the first, then jump to the last and back: the jumps are where a stuck thumb showed
            // rapid clicks, faster than the animation, ending on the second tab (the one reported stuck)
            foreach (var gap in new[] { 30, 90, 200 })
            {
                foreach (var k in new[] { keys[1], keys[Math.Min(2, keys.Count - 1)], keys[1], keys[0], keys[^1], keys[1] }) { Mark($"rapid select {k} gap={gap}"); tabs.Select(k); Mark("selected"); await Task.Delay(gap); }
                await Task.Delay(1500);
                var r = tabs.ProbeThumb();
                var off = Math.Abs(r.thumbX - r.tabX) > 1.5 || Math.Abs(r.thumbW - r.tabW) > 1.5;
                sb.AppendLine($"rapid {page} gap={gap}ms thumb={r.thumbX:0}/{r.thumbW:0} tab={r.tabX:0}/{r.tabW:0}{(off ? " **MISPLACED**" : "")}");
                if (off) bad++;
            }
            var order = keys.Skip(1).Concat([keys[0]]).Concat([keys[^1], keys[0]]).ToList();
            foreach (var key in order)
            {
                worstGap = 0;
                last = Stopwatch.GetTimestamp();
                var sw = Stopwatch.StartNew();
                tabs.Select(key);
                var blocked = sw.Elapsed.TotalMilliseconds;
                var line = new StringBuilder($"{page}:{key} blocked={blocked:0}ms");
                foreach (var at in new[] { 80, 250, 700, 1500 })
                {
                    await Task.Delay(at - (int)sw.ElapsedMilliseconds < 1 ? 1 : at - (int)sw.ElapsedMilliseconds);
                    var t = tabs.ProbeThumb();
                    line.Append($" | {at}ms thumb={t.thumbX:0}/{t.thumbW:0} tab={t.tabX:0}/{t.tabW:0}{(t.animatingX ? " animX" : "")}{(t.animatingW ? " animW" : "")}");
                    if (at == 1500 && (Math.Abs(t.thumbX - t.tabX) > 1.5 || Math.Abs(t.thumbW - t.tabW) > 1.5)) { line.Append(" **MISPLACED**"); bad++; }
                }
                line.Append($" | worst-gap={worstGap:0}ms");
                if (blocked > 120 || worstGap > 250) line.Append(" **STALL**");
                sb.AppendLine(line.ToString());
                sb.AppendLine("    " + tabs.ProbeDump());
                if (page == "health") save(window.CaptureRoot, Path.Combine(folder, $"tabs-{page}-{key}-{order.IndexOf(key)}.png"));
            }
        }
        beat.Stop();
        sb.AppendLine($"misplaced={bad}");
        File.WriteAllText(report, sb.ToString());
        Application.Current.Shutdown(0);
    }
}
