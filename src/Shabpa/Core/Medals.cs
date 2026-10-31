namespace Shabpa.Core;

/// <summary>How rare a medal is: the colour of its star (bronze, silver, gold) or the violet of the secret and seasonal ones.</summary>
public enum MedalTier { Bronze, Silver, Gold, Special }

/// <summary>One medal of the collection. <paramref name="Icon"/> is an icon name from Icons.xaml. A secret medal shows only
/// "?" until it is earned.</summary>
public sealed record Medal(string Id, MedalTier Tier, string Icon, string Fa, string En, string HowFa, string HowEn, bool Secret = false)
{
    public string Name => L.T(Fa, En);
    public string How => L.T(HowFa, HowEn);
}

/// <summary>What has been earned and the few counters that are not already in the app's own data. Local only, never sent.</summary>
public sealed class MedalState
{
    /// <summary>Medal id to the time it was earned.</summary>
    public Dictionary<string, DateTime> Earned { get; set; } = [];
    /// <summary>Counters for actions the rest of the app does not keep (memory cleans, settings visits...).</summary>
    public Dictionary<string, int> Counters { get; set; } = [];
    /// <summary>Number of different days Shabpa was open on, and the last one ("yyyy-MM-dd").</summary>
    public int Days { get; set; }
    public string LastDay { get; set; } = "";
    /// <summary>False until the first check after an update: medals already deserved then are given quietly, with one summary notice.</summary>
    public bool Primed { get; set; }
}

/// <summary>The numbers the medals are judged on, gathered once per check. Plain values so the rules are testable.</summary>
public sealed record MedalFacts
{
    public DateTime Now { get; init; }
    public int Games { get; init; }
    public double LongestGameMinutes { get; init; }
    public double GameHours { get; init; }
    public bool GameAtNight { get; init; }
    public int GamesPlayed { get; init; }
    public int TasksDone { get; init; }
    public int FocusSessions { get; init; }
    public double FocusHours { get; init; }
    public int PagesSeen { get; init; }
    public int PagesKnown { get; init; }
    public int DownloadPlans { get; init; }
    public int Scheduled { get; init; }
    public int Macros { get; init; }
    public int Backups { get; init; }
    public int Days { get; init; }
    public bool TourDone { get; init; }
    public bool Birthday { get; init; }
    public bool Halloween { get; init; }
    public int Earned { get; init; }
    public IReadOnlyDictionary<string, int> Counters { get; init; } = new Dictionary<string, int>();
    public int Count(string key) => Counters.TryGetValue(key, out var v) ? v : 0;
}

/// <summary>The medal collection and its rules. Pure: <see cref="Due"/> takes facts and returns ids, nothing else.</summary>
public static class Medals
{
    // counters kept in MedalState.Counters
    public const string CleanCounter = "clean", SettingsCounter = "page:settings", OverlayCounter = "page:gaming", AssistantCounter = "assistant";

    public static readonly IReadOnlyList<Medal> All =
    [
        // first steps
        new("hello", MedalTier.Bronze, "heart", "سلام بیدار!", "Hello, Bidar!", "شبپا رو برای اولین بار باز کردی.", "You opened Shabpa for the first time."),
        new("tour", MedalTier.Bronze, "star", "راه‌بلد", "Pathfinder", "تور آشنایی رو تا آخر رفتی.", "You finished the welcome tour."),
        new("curious", MedalTier.Bronze, "globe", "کنجکاو", "Curious", "۵ بخش مختلف برنامه رو باز کردی.", "You opened 5 different sections."),
        new("explorer", MedalTier.Silver, "globe", "جهانگرد", "Explorer", "همه‌ی بخش‌های برنامه رو دیدی.", "You visited every section of the app."),
        new("tinker", MedalTier.Bronze, "set", "دست به آچار", "Tinkerer", "سری به تنظیمات زدی.", "You looked around the settings."),
        // games
        new("game1", MedalTier.Bronze, "game", "اولین دست", "First round", "اولین نوبت بازیت ثبت شد.", "Your first game session was recorded."),
        new("game10", MedalTier.Silver, "game", "گیمر شب", "Night gamer", "۱۰ نوبت بازی ثبت شد.", "10 game sessions recorded."),
        new("game5titles", MedalTier.Silver, "mon", "همه‌فن‌حریف", "All-rounder", "۵ بازی مختلف بازی کردی.", "You played 5 different games."),
        new("marathon", MedalTier.Gold, "clk", "ماراتن", "Marathon", "یه نوبت بازی بیشتر از ۳ ساعت.", "One game session longer than 3 hours."),
        new("hours50", MedalTier.Gold, "bolt", "پنجاه ساعت", "Fifty hours", "روی هم ۵۰ ساعت بازی ثبت شد.", "50 hours of play recorded in total."),
        new("nightowl", MedalTier.Special, "moon", "جغد واقعی", "True night owl", "بین ۲ تا ۵ صبح بازی رو شروع کردی.", "You started a game between 2 and 5 am.", Secret: true),
        // tasks & focus
        new("task1", MedalTier.Bronze, "chk", "اولین تیک", "First tick", "اولین کارت رو تموم کردی.", "You finished your first task."),
        new("task25", MedalTier.Silver, "tasks", "کارراه‌انداز", "Getter of things done", "۲۵ کار تموم‌شده.", "25 tasks finished."),
        new("focus1", MedalTier.Bronze, "alarm", "تمرکز", "Focus", "اولین نوبت تمرکزت رو کامل کردی.", "You completed your first focus session."),
        new("focus10h", MedalTier.Gold, "sun", "ذهن آرام", "Calm mind", "روی هم ۱۰ ساعت تمرکز.", "10 hours of focus in total."),
        // system
        new("clean1", MedalTier.Bronze, "ram", "جاروی جادویی", "Magic broom", "اولین بار رم رو خالی کردی.", "You freed memory for the first time."),
        new("clean25", MedalTier.Silver, "trash", "خونه‌تکونی", "Spring cleaning", "۲۵ بار رم رو خالی کردی.", "You freed memory 25 times."),
        new("schedule", MedalTier.Bronze, "pw", "خاموشی به‌موقع", "Right on time", "اولین دستور زمان‌بندی‌شده رو گذاشتی.", "You scheduled your first command."),
        new("download", MedalTier.Bronze, "dl", "دانلود شبانه", "Night download", "اولین برنامه‌ی دانلود رو ساختی.", "You made your first download plan."),
        new("macro", MedalTier.Silver, "edit", "شاگرد زرنگ", "Quick learner", "به شبپا یاد دادی یه لانچر رو باز کنه.", "You taught Shabpa to open a launcher."),
        new("backup", MedalTier.Bronze, "folder", "محتاط", "Careful one", "اولین پشتیبان‌گیری رو تنظیم کردی.", "You set up your first backup."),
        new("overlay", MedalTier.Bronze, "mon", "چشم همیشه باز", "Always watching", "سری به اورلی بازی زدی.", "You opened the game overlay page."),
        new("assistant", MedalTier.Bronze, "bell", "رفیق بیدار", "Bidar's friend", "با بیدار حرف زدی.", "You talked to Bidar."),
        // loyalty
        new("days7", MedalTier.Silver, "moon", "هفت شب", "Seven nights", "۷ روز مختلف با شبپا بودی.", "Shabpa was with you on 7 different days."),
        new("days30", MedalTier.Gold, "moon", "یار همیشگی", "Old friend", "۳۰ روز مختلف با شبپا بودی.", "Shabpa was with you on 30 different days."),
        new("days100", MedalTier.Special, "heart", "صدشب", "A hundred nights", "۱۰۰ روز مختلف با شبپا بودی.", "Shabpa was with you on 100 different days."),
        // secret & seasonal
        new("midnight", MedalTier.Special, "clk", "سر ساعت دوازده", "Stroke of midnight", "دقیقاً نیمه‌شب شبپا باز بود.", "Shabpa was open at exactly midnight.", Secret: true),
        new("birthday", MedalTier.Special, "heart", "تولدت مبارک", "Happy birthday", "روز تولدت شبپا رو باز کردی.", "You opened Shabpa on your birthday.", Secret: true),
        new("halloween", MedalTier.Special, "moon", "کدوی شب", "Pumpkin night", "هالووین با شبپا بودی.", "You spent Halloween with Shabpa.", Secret: true),
        new("collector", MedalTier.Gold, "star", "کلکسیونر", "Collector", "۱۵ مدال گرفتی.", "You earned 15 medals."),
    ];

    private static readonly Dictionary<string, Medal> ById = All.ToDictionary(m => m.Id);
    public static Medal? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>Whether a medal's rule holds for these facts.</summary>
    public static bool Holds(string id, MedalFacts f) => id switch
    {
        "hello" => true,
        "tour" => f.TourDone,
        "curious" => f.PagesSeen >= 5,
        "explorer" => f.PagesKnown > 0 && f.PagesSeen >= f.PagesKnown,
        "tinker" => f.Count(SettingsCounter) >= 1,
        "game1" => f.Games >= 1,
        "game10" => f.Games >= 10,
        "game5titles" => f.GamesPlayed >= 5,
        "marathon" => f.LongestGameMinutes >= 180,
        "hours50" => f.GameHours >= 50,
        "nightowl" => f.GameAtNight,
        "task1" => f.TasksDone >= 1,
        "task25" => f.TasksDone >= 25,
        "focus1" => f.FocusSessions >= 1,
        "focus10h" => f.FocusHours >= 10,
        "clean1" => f.Count(CleanCounter) >= 1,
        "clean25" => f.Count(CleanCounter) >= 25,
        "schedule" => f.Scheduled >= 1,
        "download" => f.DownloadPlans >= 1,
        "macro" => f.Macros >= 1,
        "backup" => f.Backups >= 1,
        "overlay" => f.Count(OverlayCounter) >= 1,
        "assistant" => f.Count(AssistantCounter) >= 1,
        "days7" => f.Days >= 7,
        "days30" => f.Days >= 30,
        "days100" => f.Days >= 100,
        "midnight" => f.Now is { Hour: 0, Minute: 0 },
        "birthday" => f.Birthday,
        "halloween" => f.Halloween,
        "collector" => f.Earned >= 15,
        _ => false,
    };

    /// <summary>The medals these facts earn that are not in <paramref name="earned"/> yet, in catalogue order. The collector
    /// medal counts the ones found in the same pass.</summary>
    public static List<string> Due(MedalFacts f, IReadOnlyDictionary<string, DateTime> earned)
    {
        var fresh = new List<string>();
        foreach (var m in All)
        {
            if (earned.ContainsKey(m.Id)) continue;
            var facts = m.Id == "collector" ? f with { Earned = earned.Count + fresh.Count } : f;
            if (Holds(m.Id, facts)) fresh.Add(m.Id);
        }
        return fresh;
    }

    /// <summary>Counts a new day of use; true when the day changed.</summary>
    public static bool TouchDay(MedalState s, DateTime now)
    {
        var day = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (s.LastDay == day) return false;
        s.LastDay = day;
        s.Days++;
        return true;
    }
}
