#if DEBUG || SHABPA_TESTHOOKS
using System.IO;
using System.Text;
using Shabpa.Core;

namespace Shabpa.Pages;

/// <summary>Test aid (Debug / SHABPA_TESTHOOKS builds, screenshot runs only; <c>--rgb-bench report.txt --seconds 8</c>): starts the hidden lighting server, runs a rainbow on every
/// device for a few seconds and writes how long each device takes to show a frame, how steady that is, and how far apart the devices would be if they were sent one after the other
/// on one connection (the old way) compared with side by side (now). It really lights the PC's devices for those seconds.</summary>
public static class RgbBench
{
    public static async Task Run(string report, int seconds)
    {
        var text = new StringBuilder();
        void Say(string line) { text.AppendLine(line); File.WriteAllText(report, text.ToString()); }
        try { await Task.Run(() => Work(Say, seconds)); }
        catch (Exception ex) { Say($"failed: {ex.GetType().Name}: {ex.Message}"); }
        Say("done");
        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    }

    private static void Work(Action<string> say, int seconds)
    {
        const int port = 6742;
        if (!OpenRgbHost.Installed) { say("the lighting server is not installed here"); return; }
        var ours = !OpenRgbHost.IsListening(port);       // a server that is already running (the real Shabpa's) is only borrowed: it is never stopped here
        if (ours)
        {
            say($"starting the lighting server (admin={Elevation.IsElevated}) ...");
            OpenRgbHost.Start(port);
            for (var i = 0; i < 60 && !OpenRgbHost.IsListening(port); i++) Thread.Sleep(500);
        }
        using var client = new OpenRgbClient { Smooth = true };
        for (var i = 0; i < 40 && !client.Connect("127.0.0.1", port); i++) Thread.Sleep(500);
        if (!client.Connected) { say("could not connect: " + client.LastError); return; }
        // the server opens its port before it has found all the hardware: ask again until the number stops changing
        var steady = 0;
        for (var i = 0; i < 60 && steady < 6; i++) { Thread.Sleep(1000); steady = client.Reload() ? 0 : steady + 1; }
        say($"devices: {client.Devices.Count}");
        foreach (var d in client.Devices) say($"  #{d.Index} type {d.Type} {d.Name} · {d.Leds} LEDs");
        if (client.Devices.Count == 0) return;

        var look = new RgbLook(RgbEffects.Find("rainbow"), new Rgb(255, 0, 0), 1, 1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var late = new Dictionary<int, List<double>>();
        foreach (var d in client.Devices) late[d.Index] = [];
        var end = Environment.TickCount64 + seconds * 1000;
        while (Environment.TickCount64 < end)
        {
            var t = clock.Elapsed.TotalSeconds;
            client.UpdateAt((d, l) =>
            {
                lock (late) late[d.Index].Add(l);
                return RgbEngine.Frame(look, Math.Max(1, d.Leds), t + client.CostMs(d.Index) / 1000.0 + l, 0);
            }, wait: false);
            Thread.Sleep(20);
        }
        client.Flush(2000);
        say("");
        say("device                                        frames   delay avg / p95 / max (ms)   jitter (sd)   frame waited (avg ms)");
        var costs = new List<(int device, double avg)>();
        foreach (var (device, sent, delays) in client.LaneStats().OrderBy(x => x.Device))
        {
            if (delays.Length == 0) { say($"  #{device,-3}no frames"); continue; }
            var avg = delays.Average();
            var sorted = delays.OrderBy(x => x).ToArray();
            var sd = Math.Sqrt(delays.Select(x => (x - avg) * (x - avg)).Average());
            double waited; lock (late) waited = late[device].Count == 0 ? 0 : late[device].Average() * 1000;
            var name = client.Devices.FirstOrDefault(x => x.Index == device)?.Name ?? "?";
            say($"  #{device,-3}{(name.Length > 38 ? name[..38] : name),-40}{sent,6}   {avg,6:F1} / {sorted[(int)(sorted.Length * .95)],5:F1} / {sorted[^1],5:F1}   {sd,10:F1}   {waited,14:F1}");
            costs.Add((device, avg));
        }
        var ordered = costs.OrderBy(c => c.avg).ToList();
        say("");
        say($"Sent one after the other on one connection (the old way) the last device runs {ordered.Sum(c => c.avg) - ordered[^1].avg:F0} ms behind the first, and a frame takes {ordered.Sum(c => c.avg):F0} ms for all.");
        say($"Side by side (now) the slowest device decides: one frame takes {ordered[^1].avg:F0} ms, and each device is shown the effect ahead by its own delay, so they change together to within the delay jitter.");
        if (ours)
        {
            client.Update(_ => [Rgb.Black], force: true);
            client.Flush(1000);
            OpenRgbHost.Stop();
        }
        else say("(a lighting server was already running: it was left as it was, and a running Shabpa puts its own colours back within two seconds)");
    }
}
#endif
