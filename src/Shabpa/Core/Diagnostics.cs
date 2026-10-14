using System.Text;

namespace Shabpa.Core;

/// <summary>Checks every Windows integration against the real machine (<c>Shabpa.exe --diagnose report.txt</c>).
/// Nothing here changes power state; the wake task it creates is removed again.</summary>
public static class Diagnostics
{
    /// <summary>Sends data to itself over 127.0.0.1 with and without a limit on this very process and
    /// measures what actually got through. Needs admin (the driver).</summary>
    private static string LoopbackTest()
    {
        var self = System.Diagnostics.Process.GetCurrentProcess().ProcessName.ToLowerInvariant();

        static (double mbps, string error) Transfer(int bytes, int timeoutMs)
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var sender = Task.Run(() =>
            {
                using var server = listener.AcceptTcpClient();
                using var stream = server.GetStream();
                var chunk = new byte[16 * 1024];
                for (var sent = 0; sent < bytes; sent += chunk.Length) stream.Write(chunk);
            });
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                if (!client.ConnectAsync(System.Net.IPAddress.Loopback, port).Wait(3000)) return (0, "connect timed out");
                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                var buffer = new byte[64 * 1024];
                var watch = System.Diagnostics.Stopwatch.StartNew();
                long received = 0;
                while (received < bytes)
                {
                    var n = stream.Read(buffer);
                    if (n <= 0) break;
                    received += n;
                }
                return (received * 8 / 1e6 / watch.Elapsed.TotalSeconds, "");
            }
            catch (Exception ex)
            {
                return (0, ex.GetType().Name);
            }
            finally
            {
                listener.Stop();
                try { sender.Wait(2000); } catch { /* the sender fails when the connection is cut */ }
            }
        }

        using var shaper = new Shaper();
        var results = new List<string>();
        var (free, _) = Transfer(16 << 20, 5000);
        results.Add($"unlimited {free:0} Mbit/s");

        shaper.Apply([new EffectiveLimit(self, 16, 0, false, false)]);
        if (!shaper.Running) return $"driver did not start: {shaper.Error}";
        Thread.Sleep(200);
        var (down, e1) = Transfer(4 << 20, 8000);
        results.Add($"download cap 16 -> {down:0.0} Mbit/s{(e1.Length > 0 ? " " + e1 : "")}");

        shaper.Apply([new EffectiveLimit(self, 0, 8, false, false)]);
        Thread.Sleep(200);
        var (up, e2) = Transfer(2 << 20, 8000);
        results.Add($"upload cap 8 -> {up:0.0} Mbit/s{(e2.Length > 0 ? " " + e2 : "")}");

        shaper.Apply([new EffectiveLimit(self, 0, 0, true, false)]);
        Thread.Sleep(200);
        var (blocked, e3) = Transfer(1 << 20, 2000);
        results.Add($"blocked -> {blocked:0.0} Mbit/s ({(e3.Length > 0 ? e3 : "data got through")}); dropped {shaper.Dropped}");

        shaper.Apply([]);
        var (after, _) = Transfer(16 << 20, 5000);
        results.Add($"after removing limits {after:0} Mbit/s");
        return string.Join("; ", results);
    }

    public static string Run()
    {
        var report = new StringBuilder();
        void Line(string name, Func<string> check)
        {
            try { report.AppendLine($"{name}: {check()}"); }
            catch (Exception ex) { report.AppendLine($"{name}: FAILED {ex.GetType().Name}: {ex.Message}"); }
        }

        using var sensors = new SensorHub(() => new Settings());
        sensors.Sample();
        Thread.Sleep(2000);
        var s = sensors.Sample();
        Line("sensor tick cost", () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            for (var i = 0; i < 10; i++) sensors.Sample();
            var used = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu;
            return $"{sw.ElapsedMilliseconds / 10.0:0.0} ms wall, {used.TotalMilliseconds / 10:0.0} ms cpu per tick";
        });
        Line("cpu load %", () => s.CpuLoad.ToString("0.0"));
        Line("gpu load %", () => s.GpuLoad.ToString("0.0"));
        Line("cpu watts", () => $"{s.CpuWatts:0.0} ({(s.CpuMeasured ? "measured" : "estimated")})");
        Line("total watts", () => s.TotalWatts.ToString("0.0"));
        Line("network Mbit/s", () => $"down {s.DownMbps:0.00} up {s.UpMbps:0.00}");
        Line("idle seconds", () => s.IdleSeconds.ToString("0"));
        Line("processes seen", () => s.Processes.Count.ToString());
        Line("game / recording", () => $"{s.GameRunning} / {s.Recording}");

        Line("power history (7 days)", () =>
        {
            var events = PowerHistory.Read(DateTime.Now.AddDays(-7));
            var onTime = PowerHistory.OnTime(events, DateTime.Now.Date.AddDays(-6), DateTime.Now);
            return $"{events.Count} events; " + string.Join(", ", events.Where(e => !e.Hidden).TakeLast(8).Select(e => $"{e.Time:MM-dd HH:mm} {e.Kind}{(e.Detail.Length > 0 ? $" [{e.Detail}]" : "")}"))
                   + "; on-time hours: " + string.Join(" ", onTime.OrderBy(d => d.Key).Select(d => d.Value.TotalHours.ToString("0.0")));
        });

        Line("sleep blockers", () =>
        {
            var result = SleepBlockers.Read();
            return result.NeedsAdmin ? "needs admin (expected when not elevated)" : $"{result.Items.Count} item(s)";
        });

        Line("wake timers enabled", () => WakeScheduler.WakeTimersEnabled()?.ToString() ?? "unknown");

        Line("wake task round trip", () =>
        {
            // A weekly task on a day far from now, removed right away.
            var schedule = new WakeSchedule { Hour = 4, Minute = 44, Days = [DateTime.Now.AddDays(3).DayOfWeek] };
            var error = WakeScheduler.Register(schedule, Environment.ProcessPath ?? "Shabpa.exe");
            if (error is not null) return $"register FAILED: {error}";
            var exists = WakeScheduler.Exists(schedule.Id);
            var removed = WakeScheduler.Unregister(schedule.Id);
            return $"registered, exists={exists}, removed={removed}, still there={WakeScheduler.Exists(schedule.Id)}";
        });

        Line("elevated", () => Elevation.IsElevated.ToString());

        Line("per-app traffic (ETW)", () =>
        {
            var meter = new TrafficMeter(new ProcessResolver().Resolve);
            using var monitor = NetMonitor.TryStart(meter, out var error);
            if (monitor is null) return error == "needs-admin" ? "needs admin (expected when not elevated)" : $"FAILED: {error}";
            Thread.Sleep(3000);
            var apps = meter.Snapshot(3, DateTime.Now);
            return $"{apps.Count} app(s): " + string.Join(", ", apps.OrderByDescending(a => a.DownBytes + a.UpBytes).Take(6)
                .Select(a => $"{a.Key} ↓{Fa.Bytes(a.DownBytes)} ↑{Fa.Bytes(a.UpBytes)}"));
        });

        Line("limiter driver files", () => Shaper.DriverPresent ? "present" : "MISSING (WinDivert.dll / WinDivert64.sys not next to the app)");

        Line("limiter driver start", () =>
        {
            // A limit for an app that does not exist: every packet passes straight through. Without admin
            // this proves the driver library loads and refuses politely; with admin it is a live run.
            using var shaper = new Shaper();
            shaper.Apply([new EffectiveLimit("shabpa-diagnostics-no-such-app", 1, 1, false, false)]);
            if (!shaper.Running) return $"not started: {shaper.Error}";
            Thread.Sleep(3000);
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = ping.Send("8.8.8.8", 1500);
            shaper.Apply([]);
            return $"ran 3 s and stopped cleanly; dropped {shaper.Dropped}; network during the run: ping {reply.Status}";
        });

        Line("limiter loopback test", () => Elevation.IsElevated ? LoopbackTest() : "skipped (needs admin)");

        Line("port table", () =>
        {
            var table = PortTable.Read();
            var tcp = table.Keys.Count(k => (k & 0x10000) != 0);
            return $"{tcp} tcp + {table.Count - tcp} udp ports, {table.Values.Distinct().Count()} processes";
        });

        Line("memory", () =>
        {
            var info = MemoryCleaner.Read();
            return $"{Fa.Bytes(info.Used)} used of {Fa.Bytes(info.Total)} ({info.UsedPercent:0}%), cache {Fa.Bytes(info.Standby)}";
        });

        Line("trim own working set", () =>
        {
            // Only this process, so the check costs the user nothing.
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            var before = self.WorkingSet64;
            var trimmed = MemoryCleaner.TrimEach(self.Id);
            self.Refresh();
            return $"trimmed {trimmed} process, working set {Fa.Bytes(before)} -> {Fa.Bytes(self.WorkingSet64)}";
        });

        Line("purge cache", () => Elevation.IsElevated ? "skipped (would really purge)" : $"refused as expected: {MemoryCleaner.PurgeCache().Error}");

        Line("ping 8.8.8.8", () =>
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = ping.Send("8.8.8.8", 1500);
            return $"{reply.Status} {reply.RoundtripTime} ms";
        });

        Line("autostart registered",() => Autostart.IsEnabled().ToString());
        Line("token encryption", () => TelegramBot.Unprotect(TelegramBot.Protect("123456789:test")) == "123456789:test" ? "ok" : "FAILED");
        return report.ToString();
    }
}
