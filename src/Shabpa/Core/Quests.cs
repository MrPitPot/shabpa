using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Shabpa.Core;

/// <summary>Things the app already notices that can move a quest forward. Each is reported once, where it happens.</summary>
public enum QuestEvent
{
    /// <summary>Minutes of a finished game session.</summary>
    GameMinutes,
    /// <summary>A scheduled power command (or the end of a plan) ran.</summary>
    PowerRan,
    /// <summary>A focus round was completed.</summary>
    FocusRound,
    BackupVerified,
    DnsTested,
    /// <summary>A health warning was fixed or marked as handled.</summary>
    HealthFixed,
    BotLinked,
    /// <summary>A command was run from the command box.</summary>
    AssistantCommand,
    /// <summary>A night-internet plan finished a night.</summary>
    NightPlanRan,
    /// <summary>The power-cut guard was switched on with at least one window.</summary>
    OutageGuardArmed,
}

public enum QuestScope { Daily, Weekly, Once }

/// <summary>One quest: do <see cref="Target"/> of <see cref="Event"/> in the period of its scope and the feathers are yours.
/// A daily quest counts what happened today, a weekly one since Saturday, a one-time one over the whole life of the install.</summary>
public sealed record QuestDef(string Id, QuestScope Scope, QuestEvent Event, double Target, int Feathers, string Fa, string En);

public sealed record BadgeDef(string Id, QuestEvent Event, double Target, string Fa, string En, string DescFa, string DescEn);

/// <summary>What the person has earned. Lives in its own file (quests.json) on this PC and is never sent anywhere; it only buys looks,
/// so editing it hurts nobody.</summary>
public sealed class QuestState
{
    public int Version { get; set; } = 1;
    public long Feathers { get; set; }
    /// <summary>Everything ever counted, per event name.</summary>
    public Dictionary<string, double> Lifetime { get; set; } = [];
    public string DayKey { get; set; } = "";
    public Dictionary<string, double> DayProgress { get; set; } = [];
    public string WeekKey { get; set; } = "";
    public Dictionary<string, double> WeekProgress { get; set; } = [];
    /// <summary>Quests finished in the current day or week, and the one-time ones finished ever.</summary>
    public List<string> Done { get; set; } = [];
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public string StreakDay { get; set; } = "";
    public List<string> Badges { get; set; } = [];
    /// <summary>The person turned the whole feature off: nothing is counted and the page hides.</summary>
    public bool Off { get; set; }
}

public sealed record QuestResult(List<QuestDef> Completed, List<BadgeDef> NewBadges, int Earned, int Level, bool LeveledUp, bool StreakBonus)
{
    public bool Any => Completed.Count > 0 || NewBadges.Count > 0 || LeveledUp || StreakBonus;
}

public static class Quests
{
    public const string FeatherFa = "پَر";
    public const int StreakBonusEvery = 7;
    public const int StreakBonusFeathers = 50;

    /// <summary>Quests that come back every day; three are drawn per day, never two for the same event.</summary>
    public static readonly IReadOnlyList<QuestDef> DailyPool =
    [
        new("d-game-30", QuestScope.Daily, QuestEvent.GameMinutes, 30, 15, "نیم ساعت بازی", "Half an hour of gaming"),
        new("d-game-60", QuestScope.Daily, QuestEvent.GameMinutes, 60, 20, "یک ساعت بازی", "One hour of gaming"),
        new("d-focus-1", QuestScope.Daily, QuestEvent.FocusRound, 1, 10, "یک دور تمرکز کامل", "Finish one focus round"),
        new("d-focus-2", QuestScope.Daily, QuestEvent.FocusRound, 2, 15, "دو دور تمرکز کامل", "Finish two focus rounds"),
        new("d-power-1", QuestScope.Daily, QuestEvent.PowerRan, 1, 15, "یک خاموشی یا خواب زمان‌بندی‌شده که اجرا بشه", "Let one scheduled shutdown or sleep run"),
        new("d-assist-1", QuestScope.Daily, QuestEvent.AssistantCommand, 1, 5, "یک دستور با جعبه‌ی فرمان بیدار", "Run one command from Bidar's command box"),
        new("d-health-1", QuestScope.Daily, QuestEvent.HealthFixed, 1, 20, "یک هشدار سلامت رو رفع کن", "Fix one health warning"),
        new("d-dns-1", QuestScope.Daily, QuestEvent.DnsTested, 1, 10, "یک تست دی‌ان‌اس", "Run one DNS test"),
    ];

    public static readonly IReadOnlyList<QuestDef> WeeklyPool =
    [
        new("w-game-300", QuestScope.Weekly, QuestEvent.GameMinutes, 300, 60, "پنج ساعت بازی این هفته", "Five hours of gaming this week"),
        new("w-focus-5", QuestScope.Weekly, QuestEvent.FocusRound, 5, 50, "پنج دور تمرکز این هفته", "Five focus rounds this week"),
        new("w-power-3", QuestScope.Weekly, QuestEvent.PowerRan, 3, 50, "سه خاموشی یا خواب زمان‌بندی‌شده این هفته", "Three scheduled shutdowns or sleeps this week"),
        new("w-night-3", QuestScope.Weekly, QuestEvent.NightPlanRan, 3, 60, "سه شب اینترنت شبانه", "Three nights of night internet"),
    ];

    /// <summary>One-time quests: each teaches a feature the first time it is used.</summary>
    public static readonly IReadOnlyList<QuestDef> OnceList =
    [
        new("o-backup", QuestScope.Once, QuestEvent.BackupVerified, 1, 50, "اولین پشتیبان‌گیری موفق", "Your first verified backup"),
        new("o-bot", QuestScope.Once, QuestEvent.BotLinked, 1, 50, "وصل کردن ربات تلگرام", "Link the Telegram bot"),
        new("o-night", QuestScope.Once, QuestEvent.NightPlanRan, 1, 40, "اولین شب اینترنت شبانه", "Your first night of night internet"),
        new("o-outage", QuestScope.Once, QuestEvent.OutageGuardArmed, 1, 30, "فعال کردن آماده‌باش قطعی برق", "Switch on the power-cut guard"),
        new("o-dns", QuestScope.Once, QuestEvent.DnsTested, 1, 20, "اولین تست دی‌ان‌اس", "Your first DNS test"),
        new("o-assist", QuestScope.Once, QuestEvent.AssistantCommand, 1, 10, "اولین دستور با بیدار", "Your first command to Bidar"),
    ];

    public static readonly IReadOnlyList<BadgeDef> Badges =
    [
        new("night-owl", QuestEvent.GameMinutes, 6000, "شب‌زنده‌دار", "Night owl", "صد ساعت بازی", "One hundred hours of gaming"),
        new("focused", QuestEvent.FocusRound, 50, "تمرکزی", "Focused", "پنجاه دور تمرکز", "Fifty focus rounds"),
        new("house-keeper", QuestEvent.HealthFixed, 10, "خانه‌تکان", "Spring cleaner", "ده هشدار سلامت رفع‌شده", "Ten health warnings fixed"),
        new("switch-off", QuestEvent.PowerRan, 20, "خاموشی‌چی", "Switch-off master", "بیست خاموشی یا خواب زمان‌بندی‌شده", "Twenty scheduled shutdowns or sleeps"),
        new("night-net", QuestEvent.NightPlanRan, 10, "شب‌دانلودی", "Night downloader", "ده شب اینترنت شبانه", "Ten nights of night internet"),
    ];

    /// <summary>Feathers needed for each level (level 1 starts at 0).</summary>
    public static readonly int[] LevelAt = [0, 60, 160, 320, 560, 900, 1400, 2100];

    public static readonly (string Fa, string En)[] LevelNames =
    [
        ("جوجه‌جغد", "Owlet"), ("بیدارِ تازه‌کار", "Fresh watcher"), ("نگهبان شب", "Night guard"), ("شب‌گرد", "Night walker"),
        ("جغد دانا", "Wise owl"), ("سرپرست شب", "Night warden"), ("استاد شب‌پا", "Shabpa master"), ("افسانه‌ی شب", "Legend of the night"),
    ];

    public static IEnumerable<QuestDef> All => DailyPool.Concat(WeeklyPool).Concat(OnceList);

    // ---- levels --------------------------------------------------------------------------------------------

    public static int Level(long feathers)
    {
        var level = 1;
        for (var i = 1; i < LevelAt.Length; i++) if (feathers >= LevelAt[i]) level = i + 1;
        return level;
    }

    /// <summary>Feathers still missing for the next level; null at the top.</summary>
    public static int? ToNext(long feathers) => Level(feathers) is var l && l < LevelAt.Length ? (int)(LevelAt[l] - feathers) : null;

    /// <summary>0..1 of the way from this level to the next (1 at the top).</summary>
    public static double LevelProgress(long feathers)
    {
        var l = Level(feathers);
        if (l >= LevelAt.Length) return 1;
        return Math.Clamp((double)(feathers - LevelAt[l - 1]) / (LevelAt[l] - LevelAt[l - 1]), 0, 1);
    }

    // ---- which quests are on today ----------------------------------------------------------------------------

    /// <summary>The Saturday that starts the week of <paramref name="day"/> (the Persian week).</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 1) % 7));

    private static int Mix(int a, int b)
    {
        unchecked
        {
            var h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }

    /// <summary>Three daily quests for <paramref name="day"/>: the same three all day, different tomorrow, never two on the same event.</summary>
    public static List<QuestDef> Today(DateOnly day)
    {
        var order = DailyPool.Select((q, i) => (q, key: Mix(day.DayNumber, i + 1))).OrderBy(x => x.key).Select(x => x.q);
        var picked = new List<QuestDef>();
        foreach (var q in order)
        {
            if (picked.Any(p => p.Event == q.Event)) continue;
            picked.Add(q);
            if (picked.Count == 3) break;
        }
        return picked;
    }

    public static QuestDef Weekly(DateOnly day) => WeeklyPool[Mix(WeekStart(day).DayNumber, 7) % WeeklyPool.Count];

    /// <summary>Everything on the board today: the daily three, this week's, and the one-time ones.</summary>
    public static List<QuestDef> Board(DateOnly day) => [.. Today(day), Weekly(day), .. OnceList];

    // ---- counting -------------------------------------------------------------------------------------------

    private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Starts a new day or week when the date changed: yesterday's counters and finished daily/weekly quests are dropped.</summary>
    public static void Roll(QuestState s, DateTime now)
    {
        var day = DateOnly.FromDateTime(now);
        if (s.DayKey != Key(day))
        {
            s.DayKey = Key(day);
            s.DayProgress = [];
            s.Done.RemoveAll(id => id.StartsWith("d-", StringComparison.Ordinal));
        }
        var week = Key(WeekStart(day));
        if (s.WeekKey != week)
        {
            s.WeekKey = week;
            s.WeekProgress = [];
            s.Done.RemoveAll(id => id.StartsWith("w-", StringComparison.Ordinal));
        }
    }

    public static double Progress(QuestState s, QuestDef q) => q.Scope switch
    {
        QuestScope.Daily => s.DayProgress.GetValueOrDefault(q.Event.ToString()),
        QuestScope.Weekly => s.WeekProgress.GetValueOrDefault(q.Event.ToString()),
        _ => s.Lifetime.GetValueOrDefault(q.Event.ToString()),
    };

    /// <summary>Counts <paramref name="amount"/> of <paramref name="e"/>, finishes whatever it completes and pays the feathers.</summary>
    public static QuestResult Record(QuestState s, QuestEvent e, double amount, DateTime now)
    {
        var none = new QuestResult([], [], 0, Level(s.Feathers), false, false);
        if (s.Off || amount <= 0) return none;
        var before = Level(s.Feathers);
        Roll(s, now);
        var day = DateOnly.FromDateTime(now);
        var earned = 0;

        // the streak: one more day in a row on the first thing done that day
        var streakBonus = false;
        if (s.StreakDay != Key(day))
        {
            s.Streak = s.StreakDay == Key(day.AddDays(-1)) ? s.Streak + 1 : 1;
            s.StreakDay = Key(day);
            s.BestStreak = Math.Max(s.BestStreak, s.Streak);
            if (s.Streak % StreakBonusEvery == 0) { earned += StreakBonusFeathers; streakBonus = true; }
        }

        var name = e.ToString();
        s.Lifetime[name] = s.Lifetime.GetValueOrDefault(name) + amount;
        s.DayProgress[name] = s.DayProgress.GetValueOrDefault(name) + amount;
        s.WeekProgress[name] = s.WeekProgress.GetValueOrDefault(name) + amount;

        var completed = new List<QuestDef>();
        foreach (var q in Board(day))
        {
            if (q.Event != e || s.Done.Contains(q.Id) || Progress(s, q) < q.Target) continue;
            s.Done.Add(q.Id);
            completed.Add(q);
            earned += q.Feathers;
        }

        var badges = new List<BadgeDef>();
        foreach (var b in Badges)
            if (b.Event == e && !s.Badges.Contains(b.Id) && s.Lifetime.GetValueOrDefault(name) >= b.Target)
            {
                s.Badges.Add(b.Id);
                badges.Add(b);
            }

        s.Feathers += earned;
        var level = Level(s.Feathers);
        return new QuestResult(completed, badges, earned, level, level > before, streakBonus);
    }

    // ---- file ---------------------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Reads the file; a missing or broken one gives an empty state (the earnings are only looks, so nothing is worth a crash).</summary>
    public static QuestState Load(string file)
    {
        try
        {
            if (File.Exists(file)) return JsonSerializer.Deserialize<QuestState>(File.ReadAllText(file), Json) ?? new QuestState();
        }
        catch { /* a bad file starts over */ }
        return new QuestState();
    }

    public static void Save(string file, QuestState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, file, true);
        }
        catch { /* best effort */ }
    }
}

/// <summary>Notices a health warning that went away between two looks at the health page (a real fix, not a click on "ignore").
/// The first look only remembers the states; nothing counts until a warning seen earlier is gone.</summary>
public static class HealthWatch
{
    public static int Fixed(Dictionary<string, CheckState> seen, IEnumerable<HealthCheck> now)
    {
        var count = 0;
        foreach (var c in now)
        {
            if (c.State == CheckState.Good && seen.TryGetValue(c.Id, out var before) && before is CheckState.Attention or CheckState.Problem) count++;
            if (c.State != CheckState.Unknown) seen[c.Id] = c.State;
        }
        return count;
    }
}
