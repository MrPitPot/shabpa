using System.IO;

namespace Shabpa.Core;

// Phase 1 of the growth plan, items 15 and 16: «اینترنت شبانه» (a daily cheap-traffic window) and «آماده‌باش قطعی برق».
// Both are built from parts Shabpa already has (wake tasks, download plans, the command queue with its confirmation and
// countdown), so every rule that keeps those safe still applies: nothing fires late, nothing is silent.

public sealed class OutageSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Hour { get; set; }
    public int Minute { get; set; }
    public int DurationMinutes { get; set; } = 120;
    /// <summary>Empty = every day.</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    public string Source => $"outage:{Id:N}";
}

public sealed class NightSettings
{
    public bool InternetOn { get; set; }
    public int FromHour { get; set; } = 1;
    public int FromMinute { get; set; }
    public int ToHour { get; set; } = 7;
    public int ToMinute { get; set; }
    /// <summary>The first launcher is downloaded with the full help (resume button, quiet detection); the others are only opened.</summary>
    public List<string> Launchers { get; set; } = [];
    public PowerAction EndAction { get; set; } = PowerAction.Sleep;
    public Guid? WakeId { get; set; }
    public List<Guid> PlanIds { get; set; } = [];
    public List<Guid> CommandIds { get; set; } = [];

    public List<OutageSlot> Outages { get; set; } = [];
    /// <summary>"yyyyMMdd:slot" of the last warning, so each slot warns once a day.</summary>
    public string LastWarned { get; set; } = "";
}

public sealed class NightManager
{
    private readonly Hub _hub;
    private DateTime _lastBeat = DateTime.MinValue;

    public NightManager(Hub hub) => _hub = hub;

    private NightSettings S => _hub.Settings.Overnight;
    private static readonly List<DayOfWeek> Every = [];

    private string AliveFile => Path.Combine(_hub.Store.Folder, "alive.txt");

    // ---- night internet ---------------------------------------------------------------------------------------

    /// <summary>Sets up (or replaces) the daily window. Returns an error message or null.</summary>
    public string? EnableInternet(int fromHour, int fromMinute, int toHour, int toMinute, IReadOnlyList<LauncherInfo> launchers, PowerAction end)
    {
        if (launchers.Count == 0) return L.T("اول یه لانچر (استیم، اپیک...) انتخاب کن.", "Pick a launcher (Steam, Epic...) first.");
        if (fromHour == toHour && fromMinute == toMinute) return L.T("شروع و پایان نباید یکی باشن.", "Start and end cannot be the same.");
        DisableInternet(save: false);
        var now = DateTime.Now;

        var wake = new WakeSchedule { Hour = fromHour, Minute = fromMinute, Days = [] };
        if (_hub.AddWake(wake) is { } wakeError) return wakeError;
        S.WakeId = wake.Id;

        var start = Schedule.Next(fromHour, fromMinute, Every, now);
        // the helpers that only open a launcher go first: the main plan starts the one rule that may run at a time
        foreach (var info in launchers.Skip(1).Concat(launchers.Take(1)))
        {
            var main = ReferenceEquals(info, launchers[0]);
            var plan = new DownloadPlan
            {
                LauncherKey = info.Key, LauncherName = info.Name, ExePath = info.ExePath, Manual = info.Manual, StartAt = start, Action = end,
                OpenOnly = !main, RepeatDays = Enum.GetValues<DayOfWeek>().ToList(),
            };
            S.PlanIds.Add(plan.Id);
            _hub.State.DownloadPlans.Add(plan);
        }

        // the window ends at its end time whatever the downloads are doing; the usual countdown lets the person say "not yet"
        var cmd = _hub.Queue.Add(end, Schedule.Next(toHour, toMinute, Every, now), Enum.GetValues<DayOfWeek>(), "night");
        S.CommandIds.Add(cmd.Id);

        (S.InternetOn, S.FromHour, S.FromMinute, S.ToHour, S.ToMinute, S.EndAction) = (true, fromHour, fromMinute, toHour, toMinute, end);
        S.Launchers = launchers.Select(l => l.Key).ToList();
        _hub.Store.Save();
        _hub.RaiseChanged();
        return null;
    }

    public void DisableInternet(bool save = true)
    {
        foreach (var id in S.PlanIds) _hub.State.DownloadPlans.RemoveAll(p => p.Id == id);
        foreach (var id in S.CommandIds) _hub.Queue.Remove(id);
        if (S.WakeId is { } wake) _hub.RemoveWake(wake);
        (S.InternetOn, S.WakeId) = (false, null);
        S.PlanIds.Clear();
        S.CommandIds.Clear();
        if (save) { _hub.Store.Save(); _hub.RaiseChanged(); }
    }

    // ---- power-cut standby --------------------------------------------------------------------------------------

    public static readonly TimeSpan WarnBefore = TimeSpan.FromMinutes(10), HibernateBefore = TimeSpan.FromMinutes(2);

    public void AddOutage(OutageSlot slot)
    {
        S.Outages.Add(slot);
        // hibernate two minutes before the cut: open work is saved to disk and the drive stops cleanly
        var days = slot.Days.Count == 0 ? Enum.GetValues<DayOfWeek>() : slot.Days.ToArray();
        _hub.Queue.Add(PowerAction.Hibernate, NextOccurrence(slot, DateTime.Now).Subtract(HibernateBefore), days, slot.Source);
        _hub.Store.Save();
        _hub.RaiseChanged();
    }

    public void RemoveOutage(Guid id)
    {
        if (S.Outages.FirstOrDefault(o => o.Id == id) is not { } slot) return;
        S.Outages.Remove(slot);
        foreach (var c in _hub.State.Queue.Where(c => c.Source == slot.Source).ToList()) _hub.Queue.Remove(c.Id);
        _hub.Store.Save();
        _hub.RaiseChanged();
    }

    /// <summary>The next start of the slot on one of its days (the hibernate time is derived from it).</summary>
    public static DateTime NextOccurrence(OutageSlot slot, DateTime after) => Schedule.Next(slot.Hour, slot.Minute, slot.Days, after);

    /// <summary>Once a second: the ten-minute warning before a slot.</summary>
    public void Tick(DateTime now)
    {
        if (S.Outages.Count > 0)
            foreach (var slot in S.Outages)
            {
                if (slot.Days.Count > 0 && !slot.Days.Contains(now.DayOfWeek)) continue;
                var start = now.Date.AddHours(slot.Hour).AddMinutes(slot.Minute);
                var warnAt = start - WarnBefore;
                if (now < warnAt || now > warnAt.AddMinutes(1)) continue;
                var mark = $"{now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}:{slot.Id:N}";
                if (S.LastWarned == mark) continue;
                S.LastWarned = mark;
                _hub.Notify(L.T("قطعی برق نزدیکه", "Power cut soon"),
                    L.T($"ساعت {Fa.Clock(slot.Hour, slot.Minute)} برق می‌ره. کارت رو ذخیره کن؛ دو دقیقه قبلش سیستم هایبرنیت می‌شه (از شمارش معکوس می‌تونی لغوش کنی).",
                        $"Power goes out at {Fa.Clock(slot.Hour, slot.Minute)}. Save your work; the PC hibernates two minutes before (you can cancel from the countdown)."));
                _hub.Store.Save();
            }
        // a heartbeat once a minute, so the next start can say how long the PC was off
        if (!_hub.Store.ReadOnly && now - _lastBeat >= TimeSpan.FromMinutes(1))
        {
            _lastBeat = now;
            try { File.WriteAllText(AliveFile, now.Ticks.ToString()); } catch { /* the heartbeat is a nicety */ }
        }
    }

    /// <summary>At start: if the PC was off during a power-cut slot, say how long it was off.</summary>
    public void ReportGap()
    {
        try
        {
            if (S.Outages.Count == 0 || !File.Exists(AliveFile) || !long.TryParse(File.ReadAllText(AliveFile), out var ticks)) return;
            var last = new DateTime(ticks);
            var now = DateTime.Now;
            var gap = now - last;
            if (gap < TimeSpan.FromMinutes(5) || gap > TimeSpan.FromDays(2)) return;
            foreach (var slot in S.Outages)
            {
                var start = last.Date.AddHours(slot.Hour).AddMinutes(slot.Minute);
                if (last < start.AddHours(-1) || last > start.AddMinutes(slot.DurationMinutes + 20)) continue;
                _hub.Notify(L.T("برق برگشت", "Power is back"),
                    L.T($"سیستم از ساعت {Fa.Clock(last)} تا الان، یعنی {Fa.Duration(gap)} خاموش بود.", $"The PC was off from {Fa.Clock(last)} until now: {Fa.Duration(gap)}."));
                return;
            }
        }
        catch { /* nothing to report */ }
    }
}
