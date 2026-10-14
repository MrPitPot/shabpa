using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;

namespace Shabpa.Core;

public enum PowerEventKind { Boot, Shutdown, Restart, Sleep, Hibernate, Wake, Unexpected }

public sealed record PowerEvent(DateTime Time, PowerEventKind Kind, string Detail = "")
{
    /// <summary>Counted for on-time but not shown on the timeline.</summary>
    public bool Hidden { get; init; }

    public bool TurnsOn => Kind is PowerEventKind.Boot or PowerEventKind.Wake;
    public bool TurnsOff => Kind is PowerEventKind.Shutdown or PowerEventKind.Restart or PowerEventKind.Sleep or PowerEventKind.Hibernate;

    public string Title => Kind switch
    {
        PowerEventKind.Boot => L.T("روشن شد", "Turned on"),
        PowerEventKind.Shutdown => L.T("خاموش شد", "Shut down"),
        PowerEventKind.Restart => L.T("ری‌استارت شد", "Restarted"),
        PowerEventKind.Sleep => L.T("خوابید", "Went to sleep"),
        PowerEventKind.Hibernate => L.T("هایبرنیت شد", "Hibernated"),
        PowerEventKind.Wake => L.T("بیدار شد", "Woke up"),
        _ => L.T("ناگهانی خاموش شد", "Unexpected shutdown"),
    };
}

/// <summary>Reads power transitions from the Windows System event log (no admin needed).</summary>
public static class PowerHistory
{
    private const string KernelGeneral = "Microsoft-Windows-Kernel-General";
    private const string KernelPower = "Microsoft-Windows-Kernel-Power";
    private const string Troubleshooter = "Microsoft-Windows-Power-Troubleshooter";

    public static List<PowerEvent> Read(DateTime from)
    {
        var raw = new List<PowerEvent>();
        try
        {
            var ms = (long)(DateTime.Now - from).TotalMilliseconds;
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[(EventID=1 or EventID=12 or EventID=13 or EventID=41 or EventID=42 or EventID=107 or EventID=1074 or EventID=6008) and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
            using var reader = new EventLogReader(query);
            while (reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    if (record.TimeCreated is not { } time) continue;
                    var ev = Map(record.ProviderName ?? "", record.Id, time, () => Data(record));
                    if (ev is not null) raw.Add(ev);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"event log read failed: {ex.Message}");
        }
        return Tidy(raw);
    }

    private static Dictionary<string, string> Data(EventRecord record)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var index = 0;
            foreach (var data in XDocument.Parse(record.ToXml()).Descendants().Where(e => e.Name.LocalName == "Data"))
                result[data.Attribute("Name")?.Value ?? $"param{++index}"] = data.Value;
        }
        catch { /* malformed event; treat as no data */ }
        return result;
    }

    internal static PowerEvent? Map(string provider, int id, DateTime time, Func<Dictionary<string, string>> data)
    {
        switch (provider, id)
        {
            case (KernelGeneral, 12): return new PowerEvent(time, PowerEventKind.Boot);
            case (KernelGeneral, 13): return new PowerEvent(time, PowerEventKind.Shutdown);
            case (KernelPower, 41): return new PowerEvent(time, PowerEventKind.Unexpected);
            case ("EventLog", 6008): return new PowerEvent(time, PowerEventKind.Unexpected);
            case (KernelPower, 107): return new PowerEvent(time, PowerEventKind.Wake);
            case (KernelPower, 42):
            {
                // SYSTEM_POWER_STATE: 5 = S4 (hibernate), 2..4 = S1..S3 (sleep).
                var hibernate = data().TryGetValue("TargetState", out var state) && state.Trim() == "5";
                return new PowerEvent(time, hibernate ? PowerEventKind.Hibernate : PowerEventKind.Sleep);
            }
            case (Troubleshooter, 1):
            {
                var d = data();
                d.TryGetValue("WakeSourceText", out var source);
                return new PowerEvent(time, PowerEventKind.Wake, WakeSource(source));
            }
            case ("User32", 1074):
            {
                var d = data();
                var process = d.GetValueOrDefault("param1", "");
                var type = d.GetValueOrDefault("param5", "").ToLowerInvariant();
                var restart = type.Contains("restart") || type.Contains("راه");
                return new PowerEvent(time, restart ? PowerEventKind.Restart : PowerEventKind.Shutdown, Initiator(process));
            }
            default: return null;
        }
    }

    internal static string WakeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";
        if (source.Contains("Shabpa", StringComparison.OrdinalIgnoreCase)) return L.T("بیدارباش شب‌پا", "Shabpa wake timer");
        return source.Trim();
    }

    internal static string Initiator(string process)
    {
        var p = process.ToLowerInvariant();
        if (p.Contains("explorer.exe") || p.Contains("startmenuexperiencehost")) return L.T("از منوی استارت", "From the Start menu");
        if (p.Contains("shabpa")) return L.T("با شب‌پا", "By Shabpa");
        if (p.Contains("shutdown.exe")) return L.T("با دستور shutdown", "By the shutdown command");
        if (p.Contains("mousocoreworker") || p.Contains("trustedinstaller") || p.Contains("usoclient")) return L.T("به‌روزرسانی ویندوز", "Windows Update");
        if (p.Contains("winlogon") || p.Contains("wininit") || p.Contains("svchost") || p.Contains("runtimebroker") || p.Contains("logonui")) return L.T("ویندوز", "Windows");
        var name = process.Split(' ')[0].Split('\\', '/')[^1];
        return name.Length == 0 ? "" : L.T($"با {name}", $"By {name}");
    }

    /// <summary>Collapses the several log entries one transition produces into a single timeline entry.</summary>
    internal static List<PowerEvent> Tidy(IEnumerable<PowerEvent> raw)
    {
        var result = new List<PowerEvent>();
        foreach (var ev in raw.OrderBy(e => e.Time))
        {
            if (result.Count == 0) { result.Add(ev); continue; }
            var prev = result[^1];
            var gap = ev.Time - prev.Time;

            if (ev.Kind == PowerEventKind.Unexpected)
            {
                if (result.TakeLast(2).Any(e => e.Kind == PowerEventKind.Unexpected && ev.Time - e.Time < TimeSpan.FromMinutes(10)))
                    continue;
                // Windows only learns about a crash on the next boot, so the entry lands just after the
                // boot entry. Put it before the boot, where it belongs on a timeline.
                if (prev.Kind == PowerEventKind.Boot && gap < TimeSpan.FromMinutes(10))
                    result.Insert(result.Count - 1, ev with { Time = prev.Time });
                else
                    result.Add(ev);
                continue;
            }

            // A shutdown/restart request is followed by the kernel's own shutdown (or, with fast
            // startup, hibernate) entry.
            if (ev.TurnsOff && prev.Kind is PowerEventKind.Shutdown or PowerEventKind.Restart && gap < TimeSpan.FromMinutes(10))
                continue;

            if (ev.TurnsOn && prev.TurnsOn && gap < TimeSpan.FromMinutes(2))
            {
                if (prev.Kind == PowerEventKind.Wake && ev.Detail.Length > 0) result[^1] = prev with { Detail = ev.Detail };
                continue;
            }

            if (ev.TurnsOn && prev.Kind == PowerEventKind.Restart && gap < TimeSpan.FromMinutes(10))
            {
                // The boot that completes a restart is part of the restart: keep it for the on-time
                // maths but leave it off the timeline.
                result.Add(new PowerEvent(ev.Time, PowerEventKind.Boot) { Hidden = true });
                continue;
            }

            // Fast startup resumes a hibernated kernel, but to the user that is a boot.
            if (ev.Kind == PowerEventKind.Wake && prev.Kind == PowerEventKind.Shutdown)
            {
                result.Add(new PowerEvent(ev.Time, PowerEventKind.Boot));
                continue;
            }

            result.Add(ev);
        }
        return result;
    }

    /// <summary>How long the machine was on during each of the days in [from, now].</summary>
    public static Dictionary<DateOnly, TimeSpan> OnTime(IReadOnlyList<PowerEvent> events, DateTime from, DateTime now)
    {
        var result = new Dictionary<DateOnly, TimeSpan>();
        for (var d = DateOnly.FromDateTime(from); d <= DateOnly.FromDateTime(now); d = d.AddDays(1))
            result[d] = TimeSpan.Zero;

        // If the first thing we see is the machine turning off, it was on before that.
        var first = events.FirstOrDefault(e => e.TurnsOn || e.TurnsOff || e.Kind == PowerEventKind.Unexpected);
        DateTime? onSince = first is null || first.TurnsOff ? from : null;
        var lastSeen = from;

        foreach (var ev in events)
        {
            if (ev.TurnsOn)
            {
                // Still "on" from an earlier marker means the off transition was never logged (a crash).
                if (onSince is { } stale) AddSpan(result, stale, lastSeen);
                onSince = ev.Time;
            }
            else if (ev.TurnsOff)
            {
                if (onSince is { } start) AddSpan(result, start, ev.Time);
                onSince = null;
            }
            if (ev.Kind != PowerEventKind.Unexpected) lastSeen = ev.Time;
        }
        if (onSince is { } open) AddSpan(result, open, now);
        return result;
    }

    private static void AddSpan(Dictionary<DateOnly, TimeSpan> result, DateTime start, DateTime end)
    {
        while (start < end)
        {
            var dayEnd = start.Date.AddDays(1);
            var until = end < dayEnd ? end : dayEnd;
            var key = DateOnly.FromDateTime(start);
            if (result.ContainsKey(key)) result[key] += until - start;
            start = until;
        }
    }
}
