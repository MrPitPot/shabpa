namespace Shabpa.Core;

// 3.13.0 shipped a «شب و قطعی برق» page (night-internet window and power-cut standby). It was taken out again in 3.13.1.
// These two classes only keep the shape of what that page saved, so that an upgrade can remove the wake task, the download
// plans and the queued commands it may have created. Nothing here is shown or offered any more.

public sealed class OutageSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Hour { get; set; }
    public int Minute { get; set; }
    public int DurationMinutes { get; set; } = 120;
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
    public List<string> Launchers { get; set; } = [];
    public PowerAction EndAction { get; set; } = PowerAction.Sleep;
    public Guid? WakeId { get; set; }
    public List<Guid> PlanIds { get; set; } = [];
    public List<Guid> CommandIds { get; set; } = [];
    public List<OutageSlot> Outages { get; set; } = [];
    public string LastWarned { get; set; } = "";
}

public static class LegacyNight
{
    /// <summary>True when 3.13.0 left something behind.</summary>
    public static bool HasLeftovers(NightSettings s) => s.InternetOn || s.WakeId is not null || s.PlanIds.Count > 0 || s.CommandIds.Count > 0 || s.Outages.Count > 0;

    /// <summary>Removes exactly what the old page created (by the ids it saved) and forgets it. The person's own plans, commands and
    /// wake-ups are never touched. Returns true when something was cleaned up.</summary>
    public static bool Cleanup(Hub hub)
    {
        var s = hub.Settings.Overnight;
        if (!HasLeftovers(s)) return false;
        foreach (var id in s.PlanIds) hub.State.DownloadPlans.RemoveAll(p => p.Id == id);
        foreach (var id in s.CommandIds) hub.Queue.Remove(id);
        foreach (var slot in s.Outages)
            foreach (var c in hub.State.Queue.Where(c => c.Source == slot.Source).ToList()) hub.Queue.Remove(c.Id);
        if (s.WakeId is { } wake) hub.RemoveWake(wake);
        hub.Settings.Overnight = new NightSettings();
        hub.Store.Save();
        hub.RaiseChanged();
        Log.Write("removed what the night page of 3.13.0 had set up");
        return true;
    }
}
