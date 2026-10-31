using System.IO;
using System.Windows;
using Shabpa.Core;

namespace Shabpa;

// ---- feathers and quests, the power-cut guard and night internet ---------------------------------------------------
// Everything here is local: the counters live in quests.json next to the other data and nothing is sent anywhere.
public sealed partial class Hub
{
    /// <summary>What the person has earned (feathers, level, streak, badges). Loaded once at start.</summary>
    public QuestState Quest { get; private set; } = new();

    /// <summary>Feathers, quests or badges changed (the quests page and the home card redraw).</summary>
    public event Action? QuestsChanged;

    /// <summary>The state of each health check the last time the health page looked (memory only), to notice a warning that went away.</summary>
    public Dictionary<string, CheckState> HealthSeen { get; } = [];

    private string QuestFile => Path.Combine(Store.Folder, "quests.json");
    private DateTime _questSaved = DateTime.MinValue;
    private bool _questDirty;
    private DateTime _lastOutageTick = DateTime.Now;

    private void LoadQuests()
    {
        if (Demo) { Quest = DemoQuests(); return; }
        Quest = Quests.Load(QuestFile);
        Quest.Off = Settings.QuestsOff;
    }

    private static QuestState DemoQuests()
    {
        var s = new QuestState();
        var now = DateTime.Now;
        Quests.Record(s, QuestEvent.GameMinutes, 140, now);
        Quests.Record(s, QuestEvent.FocusRound, 2, now);
        Quests.Record(s, QuestEvent.BotLinked, 1, now);
        Quests.Record(s, QuestEvent.PowerRan, 1, now);
        s.Streak = s.BestStreak = 4;
        return s;
    }

    private void SaveQuests(bool force = false)
    {
        if (Store.ReadOnly || Demo) return;
        var now = DateTime.Now;
        if (!force && now - _questSaved < TimeSpan.FromSeconds(20)) { _questDirty = true; return; }
        _questSaved = now;
        _questDirty = false;
        Quests.Save(QuestFile, Quest);
    }

    /// <summary>Counts something that happened and tells the person what it finished. Safe from any thread.</summary>
    public void Earn(QuestEvent e, double amount = 1)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess()) { d.BeginInvoke(() => Earn(e, amount)); return; }
        Quest.Off = Settings.QuestsOff;
        var result = Quests.Record(Quest, e, amount, DateTime.Now);
        if (!result.Any)
        {
            _questDirty = true; // counted, nothing finished: written with the next batch
            QuestsChanged?.Invoke();
            return;
        }
        SaveQuests(force: true);
        QuestsChanged?.Invoke();
        foreach (var q in result.Completed)
            Notify(L.T("مأموریت انجام شد", "Quest done"),
                L.T($"«{q.Fa}»: {Fa.Digits("+" + q.Feathers)} {Quests.FeatherFa}", $"“{q.En}”: +{q.Feathers} feathers"), "quests");
        foreach (var b in result.NewBadges)
            Notify(L.T("نشان تازه", "New badge"), L.T($"«{b.Fa}» ({b.DescFa})", $"“{b.En}” ({b.DescEn})"), "quests");
        if (result.StreakBonus)
            Notify(L.T("هفت شب پیاپی", "Seven days in a row"), L.T($"{Fa.Digits("+" + Quests.StreakBonusFeathers)} {Quests.FeatherFa} جایزه‌ی زنجیره", $"+{Quests.StreakBonusFeathers} feathers for the streak"), "quests");
        if (result.LeveledUp)
        {
            var (fa, en) = Quests.LevelNames[result.Level - 1];
            Notify(L.T("سطح تازه برای بیدار", "Bidar levelled up"), L.T($"سطح {Fa.Digits(result.Level.ToString())}: {fa}", $"Level {result.Level}: {en}"), "quests");
        }
    }

    /// <summary>Counts once: for things that are seen many times but should count the first time only (the bot being linked).</summary>
    private void EarnOnce(QuestEvent e, string questId)
    {
        if (!Quest.Done.Contains(questId)) Earn(e);
    }

    public void SetQuestsOff(bool off)
    {
        Settings.QuestsOff = off;
        Quest.Off = off;
        Store.Save();
        QuestsChanged?.Invoke();
    }

    /// <summary>Clears feathers, levels, streak and badges.</summary>
    public void ResetQuests()
    {
        Quest = new QuestState { Off = Settings.QuestsOff };
        SaveQuests(force: true);
        QuestsChanged?.Invoke();
    }

    // ---- power-cut guard --------------------------------------------------------------------------------------------

    /// <summary>Called every second: warns before an expected cut, hibernates through the usual countdown, and says so when the PC is back.</summary>
    private void OutageTick(DateTime now)
    {
        var s = Settings.Outage;
        var gap = now - _lastOutageTick;
        _lastOutageTick = now;
        if (OutageGuard.ReturnReportDue(s, now, gap))
        {
            s.ReportedStart = s.ActedStart;
            Store.Save();
            Notify(L.T("آماده‌باش قطعی برق", "Power-cut guard"),
                L.T($"سیستم قبل از قطعی ساعت {Fa.Clock(s.ActedStart!.Value)} هایبرنیت شد و الان برگشتی. کارهات سر جاشونه.",
                    $"The PC hibernated before the {Fa.Clock(s.ActedStart!.Value)} cut and you are back. Your work is where you left it."), "system:outage");
        }
        if (OutageGuard.Check(s, now, Snapshot.GameRunning) is not { } due) return;
        var clock = Fa.Clock(due.Start);
        switch (due.Step)
        {
            case OutageStep.Warn:
                s.WarnedStart = due.Start;
                Store.Save();
                Notify(L.T("قطعی برق نزدیکه", "Power cut soon"),
                    L.T($"برق ساعت {clock} قطع می‌شه. کارت رو ذخیره کن؛ {Fa.Digits(s.HibernateMinutes.ToString())} دقیقه قبلش سیستم هایبرنیت می‌شه.",
                        $"Power is due to go at {clock}. Save your work; the PC hibernates {s.HibernateMinutes} minutes before."), "system:outage");
                break;
            case OutageStep.Hibernate:
                if (RequestAction(PowerAction.Hibernate, L.T($"قطعی برق ساعت {clock}", $"Power cut at {clock}"), Math.Clamp(Settings.CountdownSeconds, 10, 60), "outage"))
                {
                    s.ActedStart = due.Start;
                    s.ActedAt = now;
                    Store.Save();
                }
                break; // another countdown on screen: tried again next second
            case OutageStep.SkippedForGame:
                s.ActedStart = due.Start;
                s.ActedAt = null;
                Store.Save();
                Notify(L.T("هایبرنیت نشد", "Not hibernated"),
                    L.T($"بازی در جریانه، پس برای قطعی ساعت {clock} هایبرنیت نکردم. اگه می‌خوای، خودت ذخیره کن و خاموش کن.",
                        $"A game is running, so I did not hibernate for the {clock} cut. Save and switch off yourself if you want to."), "system:outage");
                break;
        }
    }

    /// <summary>The outage page changed the schedule or the switch.</summary>
    public void OutageChanged()
    {
        Store.Save();
        Changed?.Invoke();
        if (Settings.Outage.Enabled && Settings.Outage.Windows.Count > 0) Earn(QuestEvent.OutageGuardArmed);
    }

    // ---- night internet -------------------------------------------------------------------------------------------------

    /// <summary>The weekly plan made by the night-internet card, if any.</summary>
    public DownloadPlan? NightPlan => State.DownloadPlans.FirstOrDefault(p => p.Night);

    /// <summary>Turns the night window into a weekly plan: open the launcher at the start (waking the PC if it sleeps), stop at the end,
    /// and do <paramref name="then"/> when the download is over or the window closes. Replaces the previous night plan.</summary>
    public bool EnableNightInternet(LauncherInfo launcher, int startMinute, int endMinute, PowerAction then)
    {
        if (startMinute == endMinute) return false;
        DisableNightInternet(save: false);
        var now = DateTime.Now;
        var plan = new DownloadPlan
        {
            LauncherKey = launcher.Key, LauncherName = launcher.Name, ExePath = launcher.ExePath, Manual = launcher.Manual,
            StartAt = Schedule.Next(startMinute / 60, startMinute % 60, [], now),
            RepeatDays = [.. Enum.GetValues<DayOfWeek>()],
            EndMinute = endMinute, ActAtEnd = true, WakeUp = true, Action = then, QuietMinutes = 10, Night = true,
        };
        State.DownloadPlans.Add(plan);
        Settings.Night.StartMinute = startMinute;
        Settings.Night.EndMinute = endMinute;
        Settings.Night.Enabled = true;
        Store.Save();
        Changed?.Invoke();
        return true;
    }

    public void DisableNightInternet(bool save = true)
    {
        foreach (var p in State.DownloadPlans.Where(p => p.Night).ToList())
        {
            if (p.State == PlanState.Running && State.ActiveRule is not null) DeactivateRule();
            State.DownloadPlans.Remove(p);
        }
        Settings.Night.Enabled = false;
        if (!save) return;
        Store.Save();
        Changed?.Invoke();
    }

    /// <summary>What came in during the night window that ended on this morning, from the hourly usage Shabpa records (needs administrator rights).</summary>
    public (long down, long up) NightUsage(DateOnly morning) =>
        NightWindow.Night(day => Usage.Hourly(day), morning, Settings.Night.StartMinute, Settings.Night.EndMinute);

    /// <summary>The first tick after the window ends: tells the person how much the night brought.</summary>
    private void NightMorningReport(DateTime now)
    {
        var n = Settings.Night;
        if (!n.Enabled || !n.MorningReport) return;
        var today = DateOnly.FromDateTime(now);
        var key = today.ToString("yyyy-MM-dd");
        if (n.ReportedMorning == key) return;
        var end = now.Date.AddMinutes(n.EndMinute);
        if (now < end.AddMinutes(10) || now > end.AddHours(8)) return;
        n.ReportedMorning = key;
        Store.Save();
        var (down, up) = NightUsage(today);
        if (down + up == 0) return; // no measurement (no administrator rights) or a quiet night
        Notify(L.T("اینترنت شبانه", "Night internet"),
            L.T($"دیشب {Fa.Bytes(down)} دانلود و {Fa.Bytes(up)} آپلود شد.", $"Last night brought {Fa.Bytes(down)} down and {Fa.Bytes(up)} up."), "download");
    }

    /// <summary>The per-second work of this file.</summary>
    private void QuestsSecond(DateTime now)
    {
        OutageTick(now);
        NightMorningReport(now);
        if (_questDirty && now - _questSaved > TimeSpan.FromSeconds(20)) SaveQuests(force: true);
    }
}
