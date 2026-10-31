using Shabpa.Core;

namespace Shabpa;

/// <summary>Medals: judged on the app's own local data every 30 seconds (and right after the actions that earn them), never
/// sent anywhere. A new medal raises <see cref="MedalEarned"/>; the window shows it in the corner above the taskbar.</summary>
public sealed partial class Hub
{
    private DateTime _lastMedalCheck = DateTime.MinValue;
    private bool _medalDirty = true;

    /// <summary>A medal was just earned (UI thread).</summary>
    public event Action<Medal>? MedalEarned;
    /// <summary>The first check after this feature arrived found several medals already deserved; they are listed once, together.</summary>
    public event Action<IReadOnlyList<Medal>>? MedalsCaughtUp;
    /// <summary>The collection changed (for the medals page).</summary>
    public event Action? MedalsChanged;

    public MedalState MedalBook => State.Medals;

    /// <summary>Something that may earn a medal just happened: the next tick checks instead of waiting for the 30 s pass.</summary>
    public void MedalSoon() => _medalDirty = true;

    /// <summary>Adds to one of the medal counters (memory cleans, page visits...).</summary>
    public void MedalCount(string counter, int by = 1)
    {
        var c = State.Medals.Counters;
        c[counter] = c.GetValueOrDefault(counter) + by;
        _medalDirty = true;
    }

    /// <summary>Opened a page: the settings and overlay visits count for their medals.</summary>
    public void MedalPage(string key)
    {
        if (key is "settings" or "gaming") MedalCount("page:" + key);
        else _medalDirty = true;
    }

    private void MedalSecond(DateTime now)
    {
        if (Demo) return;
        var newDay = Medals.TouchDay(State.Medals, now);
        var midnight = now is { Hour: 0, Minute: 0 };
        if (!_medalDirty && !newDay && !midnight && now - _lastMedalCheck < TimeSpan.FromSeconds(30)) return;
        _medalDirty = false;
        _lastMedalCheck = now;
        CheckMedals(now);
    }

    internal MedalFacts MedalFactsNow(DateTime now)
    {
        var sessions = GameLog.Games.SelectMany(g => g.Sessions.Select(s => (g.Key, s.Start, Minutes: s.Seconds / 60.0))).ToList();
        if (sessions.Count == 0) sessions = State.GameHistory.Select(h => (h.Game, h.Start, h.Minutes)).ToList();
        var known = PageUse.Known.Select(k => k.Key).ToHashSet();
        return new MedalFacts
        {
            Now = now,
            Games = sessions.Count,
            GamesPlayed = sessions.Select(s => s.Item1).Distinct().Count(),
            LongestGameMinutes = sessions.Count == 0 ? 0 : sessions.Max(s => s.Minutes),
            GameHours = sessions.Sum(s => s.Minutes) / 60,
            GameAtNight = sessions.Any(s => s.Start.Hour is >= 2 and < 5),
            TasksDone = State.Tasks.Count(t => t.Done),
            FocusSessions = State.FocusLog.Count(f => f.Completed),
            FocusHours = State.FocusLog.Sum(f => f.Minutes) / 60,
            PagesSeen = State.PageVisits.Keys.Count(known.Contains),
            PagesKnown = known.Count,
            DownloadPlans = State.DownloadPlans.Count,
            Scheduled = State.Queue.Count,
            Macros = State.Macros.Count,
            Backups = State.Backups.Count,
            Days = State.Medals.Days,
            TourDone = Settings.TourStatus == Tour.TourState.Done,
            Birthday = BirthdayToday,
            Halloween = now is { Month: 10, Day: 31 },
            Earned = State.Medals.Earned.Count,
            Counters = State.Medals.Counters,
        };
    }

    private void CheckMedals(DateTime now)
    {
        List<string> due;
        try { due = Medals.Due(MedalFactsNow(now), State.Medals.Earned); }
        catch (Exception ex) { Log.Write($"medals check failed: {ex.Message}"); return; }
        var firstPass = !State.Medals.Primed;
        State.Medals.Primed = true;
        if (due.Count == 0) { if (firstPass) Store.Save(); return; }
        foreach (var id in due) State.Medals.Earned[id] = now;
        Store.Save();
        Log.Write($"medals earned: {string.Join(", ", due)}");
        var medals = due.Select(Medals.Find).OfType<Medal>().ToList();
        MedalsChanged?.Invoke();
        // an update that brings medals to someone who has used Shabpa for a while: one notice for the lot, not a dozen
        if (firstPass && medals.Count > 2)
        {
            MedalsCaughtUp?.Invoke(medals);
            return;
        }
        foreach (var m in medals) MedalEarned?.Invoke(m);
    }
}
