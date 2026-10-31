using System.Globalization;

namespace Shabpa.Core;

// Phase 1 of the growth plan: feathers («پَر»), Bidar's level, daily/weekly quests, the night streak and the 30 medals.
// Everything is computed and stored on this PC; nothing is sent anywhere. Feathers are only for fun and for learning the
// features, they are worth nothing outside the app (that is a later phase and needs a server).

public enum MedalTier { Bronze, Silver, Gold, Platinum, Diamond, Legendary, Special }

public enum QuestKind { Daily, Weekly, Once }

/// <summary>What the rewards system remembers (state.json → Rewards). Only counters and dates; no names, no paths.</summary>
public sealed class RewardsState
{
    public DateTime FirstRun { get; set; } = DateTime.Now;
    public int Feathers { get; set; }
    /// <summary>Everything ever earned (spending is a later phase); the level comes from this.</summary>
    public int TotalFeathers { get; set; }
    public Dictionary<string, DateTime> Medals { get; set; } = [];
    /// <summary>Days (yyyy-MM-dd) the app was running.</summary>
    public List<string> Days { get; set; } = [];
    /// <summary>Active minutes per day (the person was really at the PC); the streak counts these days.</summary>
    public Dictionary<string, int> Active { get; set; } = [];
    public List<string> Pages { get; set; } = [];
    public Dictionary<string, int> Counters { get; set; } = [];
    public string DayKey { get; set; } = "";
    public Dictionary<string, int> DayCounters { get; set; } = [];
    public List<string> DayPages { get; set; } = [];
    /// <summary>Quests and streak rewards already paid, so each is paid once.</summary>
    public List<string> Claimed { get; set; } = [];
    public List<string> DoneTasks { get; set; } = [];
    public List<string> Games { get; set; } = [];
    public bool TourDone { get; set; }
    /// <summary>The tour was shown once on its own; after that it is only opened from the medals page.</summary>
    public bool TourOffered { get; set; }
    public bool NightOwl { get; set; }
    public double LongestGameMinutes { get; set; }
    public int LastLevel { get; set; } = 1;
    /// <summary>The longest run of days in a row with real use (kept, because the daily list is trimmed).</summary>
    public int BestStreak { get; set; }
}

public sealed record RewardsContext(RewardsState R, AppState S, DateTime Now, int AllPages)
{
    public int Counter(string key) => R.Counters.GetValueOrDefault(key);
    public int DayCounter(string key) => R.DayCounters.GetValueOrDefault(key);
}

public sealed record MedalDef(string Id, string Icon, MedalTier Tier, bool Hidden, string NameFa, string NameEn, string DescFa, string DescEn,
    Func<RewardsContext, (double Cur, double Target)> Progress)
{
    public string Name => L.T(NameFa, NameEn);
    public string Desc => L.T(DescFa, DescEn);
    public int Reward => Tier switch { MedalTier.Bronze => 2, MedalTier.Silver => 5, MedalTier.Gold => 10, MedalTier.Platinum => 25, MedalTier.Diamond => 60, MedalTier.Legendary => 150, _ => 20 };
    public string TierName => Tier switch
    {
        MedalTier.Bronze => L.T("برنزی", "Bronze"),
        MedalTier.Silver => L.T("نقره‌ای", "Silver"),
        MedalTier.Gold => L.T("طلایی", "Gold"),
        MedalTier.Platinum => L.T("پلاتینی", "Platinum"),
        MedalTier.Diamond => L.T("الماسی", "Diamond"),
        MedalTier.Legendary => L.T("افسانه‌ای", "Legendary"),
        _ => L.T("ویژه", "Special"),
    };
}

public sealed record QuestDef(string Id, QuestKind Kind, string Fa, string En, int Reward, double Target, Func<RewardsContext, double> Value)
{
    public string Text => L.T(Fa, En);
}

public sealed record LevelDef(int Level, int From, string Fa, string En)
{
    public string Name => L.T(Fa, En);
}

public static class RewardsCatalog
{
    private static (double, double) Flag(bool b) => (b ? 1 : 0, 1);
    private static (double, double) Cnt(double cur, double target) => (Math.Min(cur, target), target);

    public static readonly IReadOnlyList<MedalDef> Medals =
    [
        new("hello", "heart", MedalTier.Bronze, false, "سلام بیدار!", "Hello, Bidar!", "شبپا رو برای اولین بار باز کردی.", "You opened Shabpa for the first time.", _ => Flag(true)),
        new("tour", "star", MedalTier.Bronze, false, "راه‌بلد", "Pathfinder", "تور آشنایی رو تا آخر رفتی.", "You finished the intro tour.", c => Flag(c.R.TourDone)),
        new("curious", "globe", MedalTier.Bronze, false, "کنجکاو", "Curious", "۵ بخش مختلف برنامه رو باز کردی.", "You opened 5 different parts of the app.", c => Cnt(c.R.Pages.Count, 5)),
        new("explorer", "globe", MedalTier.Silver, false, "جهانگرد", "Globetrotter", "همه‌ی بخش‌های برنامه رو دیدی.", "You saw every part of the app.", c => Cnt(c.R.Pages.Count, c.AllPages)),
        new("tinker", "set", MedalTier.Bronze, false, "دست به آچار", "Tinkerer", "سری به تنظیمات زدی.", "You looked through the settings.", c => Flag(c.R.Pages.Contains("settings"))),
        new("firstgame", "pad", MedalTier.Bronze, false, "اولین دست", "First round", "اولین نوبت بازیت ثبت شد.", "Your first game session was logged.", c => Cnt(c.Counter("gameSessions"), 1)),

        new("gamenight", "pad", MedalTier.Silver, false, "گیم شب", "Game night", "۱۰ نوبت بازی ثبت شد.", "10 game sessions were logged.", c => Cnt(c.Counter("gameSessions"), 10)),
        new("allrounder", "mon", MedalTier.Silver, false, "همه‌فن‌حریف", "All-rounder", "۵ بازی مختلف بازی کردی.", "You played 5 different games.", c => Cnt(c.R.Games.Count, 5)),
        new("marathon", "clk", MedalTier.Gold, false, "ماراتن", "Marathon", "یه نوبت بازی بیشتر از ۳ ساعت.", "One game session longer than 3 hours.", c => Cnt(c.R.LongestGameMinutes, 180)),
        new("fiftyhours", "bolt", MedalTier.Gold, false, "پنجاه ساعت", "Fifty hours", "روی هم ۵۰ ساعت بازی ثبت شد.", "50 hours of games were logged in total.", c => Cnt(c.Counter("gameMinutes"), 3000)),
        new("realowl", "moon", MedalTier.Special, true, "جغد واقعی", "True owl", "بین ۲ تا ۵ صبح بازی رو شروع کردی.", "You started a game between 2 and 5 am.", c => Flag(c.R.NightOwl)),
        new("firsttick", "chk", MedalTier.Bronze, false, "اولین تیک", "First tick", "اولین کارت رو تموم کردی.", "You finished your first task.", c => Cnt(c.Counter("tasksDone"), 1)),

        new("doer", "tasks", MedalTier.Silver, false, "کاراَنداز", "Go-getter", "۲۵ کار تموم‌شده.", "25 tasks finished.", c => Cnt(c.Counter("tasksDone"), 25)),
        new("focus", "alarm", MedalTier.Bronze, false, "تمرکز", "Focus", "اولین نوبت تمرکز رو کامل کردی.", "You completed your first focus session.", c => Cnt(c.S.FocusLog.Count(f => f.Completed), 1)),
        new("calm", "sun", MedalTier.Gold, false, "ذهن آرام", "Calm mind", "روی ۱۰ ساعت تمرکز.", "10 hours of focus.", c => Cnt(c.S.FocusLog.Where(f => f.Completed).Sum(f => f.Minutes), 600)),
        new("broom", "brush", MedalTier.Bronze, false, "جاروی جادویی", "Magic broom", "اولین بار رم رو خالی کردی.", "You freed RAM for the first time.", c => Cnt(c.Counter("ramFreed"), 1)),
        new("housekeeper", "trash", MedalTier.Silver, false, "خونه‌تکونی", "Spring cleaning", "۲۵ بار رم رو خالی کردی.", "You freed RAM 25 times.", c => Cnt(c.Counter("ramFreed"), 25)),
        new("ontime", "pw", MedalTier.Bronze, false, "خاموشی به‌موقع", "Lights out on time", "اولین دستور زمان‌بندی‌شده رو گذاشتی.", "You set your first scheduled command.", c => Cnt(c.Counter("powerScheduled"), 1)),

        new("nightdl", "dl", MedalTier.Bronze, false, "دانلود شبانه", "Night download", "اولین برنامه‌ی دانلود رو ساختی.", "You made your first download plan.", c => Cnt(c.S.DownloadPlans.Count + c.Counter("downloadPlans"), 1)),
        new("teacher", "edit", MedalTier.Silver, false, "شاگرد زرنگ", "Quick student", "به شبپا یاد دادی یه لانچر رو باز کنه.", "You taught Shabpa to open a launcher.", c => Cnt(c.S.Macros.Count, 1)),
        new("careful", "folder", MedalTier.Bronze, false, "محتاط", "Careful", "اولین پشتیبان‌گیری رو تنظیم کردی.", "You set up your first backup.", c => Cnt(c.S.Backups.Count, 1)),
        new("overlay", "mon", MedalTier.Bronze, false, "چشم همیشه باز", "Always watching", "سری به اورلی بازی زدی.", "You visited the game overlay.", c => Flag(c.R.Pages.Contains("gaming"))),
        new("friend", "bell", MedalTier.Bronze, false, "رفیق بیدار", "Bidar's friend", "با بیدار حرف زدی.", "You talked to Bidar.", c => Cnt(c.Counter("bidar"), 1)),
        new("sevennights", "moon", MedalTier.Silver, false, "هفت شب", "Seven nights", "۷ روز مختلف با شبپا بودی.", "You were with Shabpa on 7 different days.", c => Cnt(c.R.Days.Count, 7)),

        new("companion", "moon", MedalTier.Gold, false, "یار همیشگی", "Always there", "۳۰ روز مختلف با شبپا بودی.", "You were with Shabpa on 30 different days.", c => Cnt(c.R.Days.Count, 30)),
        new("hundred", "heart", MedalTier.Special, true, "صدشب", "A hundred nights", "۱۰۰ روز مختلف با شبپا بودی.", "You were with Shabpa on 100 different days.", c => Cnt(c.R.Days.Count, 100)),
        new("midnight", "clk", MedalTier.Special, true, "سر ساعت دوازده", "On the stroke of twelve", "دقیقاً نیمه‌شب شبپا باز بود.", "Shabpa was running exactly at midnight.", c => Flag(c.R.Counters.ContainsKey("midnight"))),
        new("birthday", "heart", MedalTier.Special, true, "تولدت مبارک", "Happy birthday", "روز تولدت شبپا رو باز کردی.", "You opened Shabpa on your birthday.", c => Flag(c.R.Counters.ContainsKey("birthday"))),
        new("pumpkin", "moon", MedalTier.Special, true, "کدوی شب", "Night pumpkin", "هالووین با شبپا بودی.", "You were with Shabpa on Halloween.", c => Flag(c.R.Counters.ContainsKey("halloween"))),
        new("collector", "star", MedalTier.Gold, false, "کلکسیونر", "Collector", "۱۵ مدال گرفتی.", "You earned 15 medals.", c => Cnt(Earned(c), 15)),

        // ---- more to find: places, setups, streaks, levels, quests, chats ----------------------------------------------
        new("dnsvisit", "globe", MedalTier.Bronze, false, "نبض شبکه", "Network pulse", "سری به صفحه‌ی دی‌ان‌اس زدی.", "You visited the DNS page.", c => Flag(c.R.Pages.Contains("dns"))),
        new("healthvisit", "ecg", MedalTier.Bronze, false, "سلامت‌بین", "Health watcher", "از سلامت سیستم خبر گرفتی.", "You checked on the health of your PC.", c => Flag(c.R.Pages.Contains("health"))),
        new("weathervisit", "cloud", MedalTier.Bronze, false, "هواشناس", "Weatherwise", "هوای شهرت رو دیدی.", "You looked at the weather.", c => Flag(c.R.Pages.Contains("weather"))),
        new("micvisit", "mic", MedalTier.Bronze, false, "صدادار", "Sound check", "به میکروفون سر زدی.", "You visited the microphone page.", c => Flag(c.R.Pages.Contains("mic"))),
        new("playtimevisit", "pad", MedalTier.Bronze, false, "حسابدار بازی", "Game accountant", "گزارش زمان بازی رو دیدی.", "You looked at your play time.", c => Flag(c.R.Pages.Contains("playtime"))),
        new("colourful", "brush", MedalTier.Bronze, false, "رنگ‌به‌رنگ", "Colourful", "تم برنامه رو عوض کردی.", "You changed the theme.", c => Flag(c.S.Settings.Theme != Palette.Default)),
        new("backups3", "folder", MedalTier.Silver, false, "پشتیبان‌ساز", "Backup keeper", "۳ تا پشتیبان‌گیری تنظیم کردی.", "You set up 3 backups.", c => Cnt(c.S.Backups.Count, 3)),
        new("morningcall", "alarm", MedalTier.Bronze, false, "زنگ صبح", "Morning call", "اولین بیدارباش رو گذاشتی.", "You set your first wake-up.", c => Cnt(c.S.WakeSchedules.Count, 1)),
        new("telegram", "bell", MedalTier.Silver, false, "رفیق تلگرامی", "Telegram friend", "ربات تلگرام رو وصل کردی.", "You linked the Telegram bot.", c => Flag(c.S.Settings.BotEnabled)),
        new("nightnet", "moon", MedalTier.Silver, false, "اینترنت شبانه", "Night traffic", "پنجره‌ی اینترنت شبانه رو روشن کردی.", "You turned the night internet window on.", c => Flag(c.S.Settings.Overnight.InternetOn)),
        new("standby", "bolt", MedalTier.Bronze, false, "آماده‌باش", "On standby", "اولین قطعی برق رو ثبت کردی.", "You added your first power cut.", c => Cnt(c.S.Settings.Overnight.Outages.Count, 1)),
        new("showoff", "share", MedalTier.Bronze, false, "نمایش مدال", "Show-off", "یه مدال رو به‌صورت تصویر ساختی.", "You made an image of a medal.", c => Cnt(c.Counter("shared"), 1)),

        new("streak7", "flame", MedalTier.Silver, false, "هفته‌ی بی‌وقفه", "Unbroken week", "۷ روز پیاپی با شبپا بودی.", "You were with Shabpa 7 days in a row.", c => Cnt(c.R.BestStreak, 7)),
        new("streak30", "flame", MedalTier.Gold, false, "ماه بی‌وقفه", "Unbroken month", "۳۰ روز پیاپی با شبپا بودی.", "You were with Shabpa 30 days in a row.", c => Cnt(c.R.BestStreak, 30)),
        new("streak100", "flame", MedalTier.Platinum, false, "صد شب پیاپی", "A hundred nights straight", "۱۰۰ روز پیاپی با شبپا بودی.", "You were with Shabpa 100 days in a row.", c => Cnt(c.R.BestStreak, 100)),
        new("streak365", "flame", MedalTier.Diamond, false, "یک سال بی‌وقفه", "A year without a break", "۳۶۵ روز پیاپی با شبپا بودی.", "You were with Shabpa 365 days in a row.", c => Cnt(c.R.BestStreak, 365)),

        new("lvl10", "feather", MedalTier.Silver, false, "سطح ده", "Level ten", "به سطح ۱۰ رسیدی.", "You reached level 10.", c => Cnt(RewardsCatalog.LevelFor(c.R.TotalFeathers).Level, 10)),
        new("lvl25", "feather", MedalTier.Gold, false, "سطح بیست‌وپنج", "Level twenty-five", "به سطح ۲۵ رسیدی.", "You reached level 25.", c => Cnt(RewardsCatalog.LevelFor(c.R.TotalFeathers).Level, 25)),
        new("lvl50", "feather", MedalTier.Platinum, false, "نیمه‌ی راه", "Halfway", "به سطح ۵۰ رسیدی.", "You reached level 50.", c => Cnt(RewardsCatalog.LevelFor(c.R.TotalFeathers).Level, 50)),
        new("lvl75", "feather", MedalTier.Diamond, false, "اوج‌گیر", "High flyer", "به سطح ۷۵ رسیدی.", "You reached level 75.", c => Cnt(RewardsCatalog.LevelFor(c.R.TotalFeathers).Level, 75)),
        new("lvl100", "feather", MedalTier.Legendary, false, "بیدار بزرگ", "The great Bidar", "به سطح ۱۰۰ رسیدی.", "You reached level 100.", c => Cnt(RewardsCatalog.LevelFor(c.R.TotalFeathers).Level, 100)),

        new("quests10", "chk", MedalTier.Bronze, false, "کاربلد", "Quest rookie", "۱۰ مأموریت انجام دادی.", "You finished 10 quests.", c => Cnt(c.Counter("questsDone"), 10)),
        new("quests50", "chk", MedalTier.Silver, false, "مأموریت‌باز", "Quest hunter", "۵۰ مأموریت انجام دادی.", "You finished 50 quests.", c => Cnt(c.Counter("questsDone"), 50)),
        new("quests200", "chk", MedalTier.Gold, false, "قهرمان مأموریت", "Quest champion", "۲۰۰ مأموریت انجام دادی.", "You finished 200 quests.", c => Cnt(c.Counter("questsDone"), 200)),
        new("quests1000", "chk", MedalTier.Platinum, false, "افسانه‌ی مأموریت", "Quest legend", "۱٬۰۰۰ مأموریت انجام دادی.", "You finished 1,000 quests.", c => Cnt(c.Counter("questsDone"), 1000)),

        new("sessions10", "alarm", MedalTier.Silver, false, "ده نوبت", "Ten rounds", "۱۰ نوبت تمرکز رو کامل کردی.", "You completed 10 focus sessions.", c => Cnt(c.S.FocusLog.Count(f => f.Completed), 10)),
        new("sessions100", "alarm", MedalTier.Gold, false, "صد نوبت", "A hundred rounds", "۱۰۰ نوبت تمرکز رو کامل کردی.", "You completed 100 focus sessions.", c => Cnt(c.S.FocusLog.Count(f => f.Completed), 100)),

        new("chat25", "bell", MedalTier.Silver, false, "گپ‌دوست", "Chat buddy", "با بیدار ۲۵ بار حرف زدی.", "You talked to Bidar 25 times.", c => Cnt(c.Counter("bidar"), 25)),
        new("chat200", "bell", MedalTier.Gold, false, "همدم شب", "Night companion", "با بیدار ۲۰۰ بار حرف زدی.", "You talked to Bidar 200 times.", c => Cnt(c.Counter("bidar"), 200)),
        new("chat1000", "bell", MedalTier.Platinum, false, "من و بیدار", "Bidar and me", "با بیدار ۱٬۰۰۰ بار حرف زدی.", "You talked to Bidar 1,000 times.", c => Cnt(c.Counter("bidar"), 1000)),

        new("marathon6", "clk", MedalTier.Platinum, false, "ماراتن بزرگ", "Grand marathon", "یه نوبت بازی بیشتر از ۶ ساعت.", "One game session longer than 6 hours.", c => Cnt(c.R.LongestGameMinutes, 360)),
        new("games15", "mon", MedalTier.Gold, false, "گنجینه‌ی بازی", "Game treasury", "۱۵ بازی مختلف بازی کردی.", "You played 15 different games.", c => Cnt(c.R.Games.Count, 15)),

        // more secrets
        new("earlybird", "sun", MedalTier.Special, true, "سحرخیز", "Early bird", "ساعت ۵ تا ۶ صبح پشت سیستم بودی.", "You were at the PC between 5 and 6 am.", c => Flag(c.R.Counters.ContainsKey("early"))),
        new("nowruz", "star", MedalTier.Special, true, "نوروز مبارک", "Happy Nowruz", "نوروز با شبپا بودی.", "You were with Shabpa on Nowruz.", c => Flag(c.R.Counters.ContainsKey("nowruz"))),
        new("yalda", "moon", MedalTier.Special, true, "شب یلدا", "Yalda night", "شب یلدا با شبپا بودی.", "You were with Shabpa on Yalda night.", c => Flag(c.R.Counters.ContainsKey("yalda"))),

        // the upper rungs of the ladders: the last ones are meant to take years
        new("gamer100", "pad", MedalTier.Platinum, false, "کهنه‌کار", "Veteran", "۱۰۰ نوبت بازی ثبت شد.", "100 game sessions were logged.", c => Cnt(c.Counter("gameSessions"), 100)),
        new("gamer500", "pad", MedalTier.Diamond, false, "استاد میدان", "Arena master", "۵۰۰ نوبت بازی ثبت شد.", "500 game sessions were logged.", c => Cnt(c.Counter("gameSessions"), 500)),
        new("hours200", "bolt", MedalTier.Platinum, false, "دویست ساعت", "Two hundred hours", "روی هم ۲۰۰ ساعت بازی ثبت شد.", "200 hours of games were logged in total.", c => Cnt(c.Counter("gameMinutes"), 12000)),
        new("hours500", "bolt", MedalTier.Diamond, false, "پانصد ساعت", "Five hundred hours", "روی هم ۵۰۰ ساعت بازی ثبت شد.", "500 hours of games were logged in total.", c => Cnt(c.Counter("gameMinutes"), 30000)),
        new("hours2000", "bolt", MedalTier.Legendary, false, "ستون بازی", "Pillar of play", "روی هم ۲٬۰۰۰ ساعت بازی ثبت شد.", "2,000 hours of games were logged in total.", c => Cnt(c.Counter("gameMinutes"), 120000)),
        new("tasks100", "tasks", MedalTier.Gold, false, "دست‌به‌کار", "Hands on", "۱۰۰ کار تموم‌شده.", "100 tasks finished.", c => Cnt(c.Counter("tasksDone"), 100)),
        new("tasks500", "tasks", MedalTier.Platinum, false, "ماشین کار", "Work machine", "۵۰۰ کار تموم‌شده.", "500 tasks finished.", c => Cnt(c.Counter("tasksDone"), 500)),
        new("tasks2000", "tasks", MedalTier.Diamond, false, "کوه کار", "Mountain of work", "۲٬۰۰۰ کار تموم‌شده.", "2,000 tasks finished.", c => Cnt(c.Counter("tasksDone"), 2000)),
        new("focus50", "sun", MedalTier.Platinum, false, "ذهن روشن", "Clear mind", "روی ۵۰ ساعت تمرکز.", "50 hours of focus.", c => Cnt(FocusMinutes(c), 3000)),
        new("focus200", "sun", MedalTier.Diamond, false, "ذهن فولادی", "Mind of steel", "روی ۲۰۰ ساعت تمرکز.", "200 hours of focus.", c => Cnt(FocusMinutes(c), 12000)),
        new("focus1000", "sun", MedalTier.Legendary, false, "استاد تمرکز", "Focus sage", "روی ۱٬۰۰۰ ساعت تمرکز.", "1,000 hours of focus.", c => Cnt(FocusMinutes(c), 60000)),
        new("ram100", "brush", MedalTier.Gold, false, "نظافتچی", "Cleaner", "۱۰۰ بار رم رو خالی کردی.", "You freed RAM 100 times.", c => Cnt(c.Counter("ramFreed"), 100)),
        new("ram500", "brush", MedalTier.Platinum, false, "غبارروب", "Dust sweeper", "۵۰۰ بار رم رو خالی کردی.", "You freed RAM 500 times.", c => Cnt(c.Counter("ramFreed"), 500)),
        new("days200", "moon", MedalTier.Platinum, false, "دوست شبانه", "Night friend", "۲۰۰ روز مختلف با شبپا بودی.", "You were with Shabpa on 200 different days.", c => Cnt(c.R.Days.Count, 200)),
        new("days365", "moon", MedalTier.Diamond, false, "یک سال شبپا", "A year of Shabpa", "۳۶۵ روز مختلف با شبپا بودی.", "You were with Shabpa on 365 different days.", c => Cnt(c.R.Days.Count, 365)),
        new("days1000", "moon", MedalTier.Legendary, false, "هزار و یک شب", "A thousand and one nights", "۱٬۰۰۰ روز مختلف با شبپا بودی.", "You were with Shabpa on 1,000 different days.", c => Cnt(c.R.Days.Count, 1000)),
        new("coll30", "star", MedalTier.Platinum, false, "موزه‌دار", "Curator", "۳۰ مدال گرفتی.", "You earned 30 medals.", c => Cnt(Earned(c), 30)),
        new("coll45", "star", MedalTier.Diamond, false, "گنج‌دار", "Treasure keeper", "۴۵ مدال گرفتی.", "You earned 45 medals.", c => Cnt(Earned(c), 45)),
        new("allmedals", "medal", MedalTier.Legendary, false, "شاه مدال", "King of medals", "همه‌ی مدال‌ها رو گرفتی.", "You earned every medal.", c => Cnt(Earned(c), RewardsCatalog.CollectableCount)),
    ];

    private static readonly string[] Meta = ["collector", "coll30", "coll45", "allmedals"];

    /// <summary>Medals that are not about the number of medals themselves.</summary>
    public static int CollectableCount => Medals.Count(m => !Meta.Contains(m.Id));

    private static int Earned(RewardsContext c) => c.R.Medals.Count(m => !Meta.Contains(m.Key));

    private static double FocusMinutes(RewardsContext c) => c.S.FocusLog.Where(f => f.Completed).Sum(f => f.Minutes);

    public static MedalDef? Find(string id) => Medals.FirstOrDefault(m => m.Id == id);

    public const int MaxLevel = 100;

    /// <summary>Feathers needed in total to reach <paramref name="level"/>. Each level costs more than the one before:
    /// about a month of ordinary use reaches level 10, level 20 takes months and level 100 is a goal for years.</summary>
    public static int FeathersFor(int level) => level <= 1 ? 0 : (int)Math.Round(20 * Math.Pow(level - 1, 1.8));

    private static readonly (int From, string Fa, string En)[] Titles =
    [
        (1, "جوجه‌جغد", "Owlet"), (3, "شب‌نشین", "Night sitter"), (6, "پاسبان", "Sentry"), (10, "نگهبان شب", "Night watch"),
        (15, "ستاره‌شمر", "Stargazer"), (25, "جغد دانا", "Wise owl"), (40, "ماه‌نشین", "Moon-perched"), (60, "فرمانروای شب", "Night ruler"),
        (80, "اسطوره‌ی شب", "Night legend"), (100, "بیدار بزرگ", "The great Bidar"),
    ];

    public static LevelDef LevelAt(int level)
    {
        level = Math.Clamp(level, 1, MaxLevel);
        var t = Titles.Last(x => level >= x.From);
        return new LevelDef(level, FeathersFor(level), t.Fa, t.En);
    }

    public static LevelDef LevelFor(int total)
    {
        var level = 1;
        while (level < MaxLevel && total >= FeathersFor(level + 1)) level++;
        return LevelAt(level);
    }

    // ---- quests ------------------------------------------------------------------------------------------

    private static readonly QuestDef[] DailyPool =
    [
        new("d.game", QuestKind.Daily, "یه ساعت بازی کن", "Play for an hour", 8, 60, c => c.DayCounter("gameMin")),
        new("d.focus", QuestKind.Daily, "یه نوبت تمرکز رو کامل کن", "Complete a focus session", 4, 1, c => c.S.FocusLog.Count(f => f.Completed && f.Start.Date == c.Now.Date)),
        new("d.task", QuestKind.Daily, "یه کار از لیستت رو تموم کن", "Finish one task from your list", 4, 1, c => c.DayCounter("task")),
        new("d.ram", QuestKind.Daily, "رم رو یه بار خالی کن", "Free up RAM once", 3, 1, c => c.DayCounter("ram")),
        new("d.power", QuestKind.Daily, "یه دستور زمان‌بندی‌شده بذار", "Set a scheduled command", 5, 1, c => c.DayCounter("power")),
        new("d.explore", QuestKind.Daily, "۴ بخش مختلف برنامه رو باز کن", "Open 4 different parts of the app", 3, 4, c => c.R.DayPages.Count),
    ];

    private static readonly QuestDef[] WeeklyPool =
    [
        new("w.focus", QuestKind.Weekly, "این هفته ۵ نوبت تمرکز رو کامل کن", "Complete 5 focus sessions this week", 15, 5,
            c => c.S.FocusLog.Count(f => f.Completed && f.Start.Date >= WeekStart(c.Now))),
        new("w.days", QuestKind.Weekly, "این هفته ۵ روز مختلف شبپا رو باز کن", "Open Shabpa on 5 different days this week", 15, 5,
            c => c.R.Days.Count(d => DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) && t >= WeekStart(c.Now))),
    ];

    private static readonly QuestDef[] OncePool =
    [
        new("o.backup", QuestKind.Once, "اولین پشتیبان‌گیری رو تنظیم کن", "Set up your first backup", 20, 1, c => c.S.Backups.Count > 0 ? 1 : 0),
        new("o.download", QuestKind.Once, "یه برنامه‌ی دانلود شبانه بساز", "Make a night download plan", 12, 1, c => c.S.DownloadPlans.Count + c.Counter("downloadPlans") > 0 ? 1 : 0),
        new("o.bot", QuestKind.Once, "ربات تلگرام رو وصل کن", "Link the Telegram bot", 20, 1, c => c.S.Settings.BotEnabled ? 1 : 0),
    ];

    /// <summary>Saturday is the first day of the Persian week; the English interface uses Monday.</summary>
    public static DateTime WeekStart(DateTime now)
    {
        var back = L.En ? ((int)now.DayOfWeek + 6) % 7 : ((int)now.DayOfWeek + 1) % 7;
        return now.Date.AddDays(-back);
    }

    public static string WeekKey(DateTime now) => WeekStart(now).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Three daily quests, picked from the date so they are the same all day and after a restart.</summary>
    public static IReadOnlyList<QuestDef> DailyFor(DateTime now)
    {
        var seed = (int)(now.Date.Ticks / TimeSpan.TicksPerDay);
        var picks = new List<QuestDef>();
        for (var i = 0; picks.Count < 3; i++)
        {
            var q = DailyPool[(seed * 5 + i * 2 + i / 3) % DailyPool.Length];
            if (!picks.Contains(q)) picks.Add(q);
            if (i > 20) break;
        }
        return picks;
    }

    public static QuestDef WeeklyFor(DateTime now) => WeeklyPool[(int)((WeekStart(now).Ticks / TimeSpan.TicksPerDay) / 7 % WeeklyPool.Length)];

    public static IReadOnlyList<QuestDef> Once => OncePool;

    public static string ClaimKey(QuestDef q, DateTime now) => q.Kind switch
    {
        QuestKind.Daily => $"{q.Id}:{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}",
        QuestKind.Weekly => $"{q.Id}:{WeekKey(now)}",
        _ => q.Id,
    };
}

/// <summary>Watches what the person does, unlocks medals, pays feathers and shows the notices.</summary>
public sealed class RewardsEngine
{
    private readonly Hub _hub;
    private readonly Func<DateTime> _now;
    private readonly List<(MedalDef medal, DateTime at)> _toasts = [];
    private int _tick;
    private bool _dirty;

    public RewardsEngine(Hub hub, Func<DateTime>? now = null)
    {
        _hub = hub;
        _now = now ?? (() => DateTime.Now);
        State.Pages.RemoveAll(p => p == "kit");
    }

    public RewardsState State => _hub.State.Rewards;

    /// <summary>A medal was earned, a quest was paid or the level changed (the page redraws).</summary>
    public event Action? Changed;
    /// <summary>A new medal, to show on the desktop.</summary>
    public event Action<MedalDef>? MedalEarned;

    public static int PageCount { get; set; } = 16;

    public RewardsContext Context => new(State, _hub.State, _now(), PageCount);

    public void Start()
    {
        _hub.Second += OnSecond;
        _hub.MemoryCleaned += (_, _) => Count("ramFreed", day: "ram");
        _hub.Tasks.Changed += OnTasksChanged;
        _hub.Queue.Added += _ => Count("powerScheduled", day: "power");
        Evaluate();
    }

    public int Streak
    {
        get
        {
            var day = _now().Date;
            if (State.Active.GetValueOrDefault(Key(day)) < 1) day = day.AddDays(-1); // today may not have started yet
            var n = 0;
            while (State.Active.GetValueOrDefault(Key(day)) >= 1) { n++; day = day.AddDays(-1); }
            return n;
        }
    }

    public LevelDef Level => RewardsCatalog.LevelFor(State.TotalFeathers);
    public LevelDef? NextLevel => Level.Level >= RewardsCatalog.MaxLevel ? null : RewardsCatalog.LevelAt(Level.Level + 1);

    private static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- events -------------------------------------------------------------------------------------------

    public void Visit(string page)
    {
        var key = page.Split(':')[0];
        if (key == "kit") return;
        var changed = false;
        if (!State.Pages.Contains(key)) { State.Pages.Add(key); changed = true; }
        RollDay();
        if (!State.DayPages.Contains(key)) { State.DayPages.Add(key); changed = true; }
        if (changed) { _dirty = true; Evaluate(); }
    }

    public void Count(string key, int by = 1, string? day = null)
    {
        RollDay();
        State.Counters[key] = State.Counters.GetValueOrDefault(key) + by;
        if (day is not null) State.DayCounters[day] = State.DayCounters.GetValueOrDefault(day) + by;
        _dirty = true;
        Evaluate();
    }

    /// <summary>The person talked to Bidar (command box / voice).</summary>
    public void BidarSpoke() => Count("bidar", day: "bidar");

    public void FinishTour()
    {
        if (State.TourDone) return;
        State.TourDone = true;
        _dirty = true;
        Evaluate();
    }

    public void GameFinished(string game, double minutes, DateTime start)
    {
        RollDay();
        Count("gameSessions");
        State.Counters["gameMinutes"] = State.Counters.GetValueOrDefault("gameMinutes") + (int)Math.Round(minutes);
        State.DayCounters["gameMin"] = State.DayCounters.GetValueOrDefault("gameMin") + (int)Math.Round(minutes);
        if (game.Length > 0 && !State.Games.Contains(game) && State.Games.Count < 60) State.Games.Add(game);
        State.LongestGameMinutes = Math.Max(State.LongestGameMinutes, minutes);
        if (start.Hour is >= 2 and < 5) State.NightOwl = true;
        _dirty = true;
        Evaluate();
    }

    private void OnTasksChanged()
    {
        var changed = false;
        foreach (var t in _hub.State.Tasks.Where(t => t.Done))
        {
            var id = t.Id.ToString("N");
            if (State.DoneTasks.Contains(id)) continue;
            State.DoneTasks.Add(id);
            if (State.DoneTasks.Count > 400) State.DoneTasks.RemoveAt(0);
            // a task that was already done when this started does not count as new work today
            State.Counters["tasksDone"] = State.Counters.GetValueOrDefault("tasksDone") + 1;
            State.DayCounters["task"] = State.DayCounters.GetValueOrDefault("task") + 1;
            changed = true;
        }
        if (changed) { _dirty = true; Evaluate(); }
    }

    // ---- the clock ------------------------------------------------------------------------------------------

    private void OnSecond()
    {
        if (++_tick % 30 != 0) { FlushToasts(); return; }
        var now = _now();
        // active minutes: the person is really there (typed or moved the mouse in the last two minutes)
        if (_tick % 60 == 0 && UserIdle.Since() < TimeSpan.FromMinutes(2))
        {
            var day = Key(now);
            State.Active[day] = State.Active.GetValueOrDefault(day) + 1;
            _dirty = true;
        }
        Evaluate();
    }

    private void RollDay()
    {
        var now = _now();
        var today = Key(now);
        if (State.DayKey == today) return;
        State.DayKey = today;
        State.DayCounters.Clear();
        State.DayPages.Clear();
        _dirty = true;
    }

    /// <summary>Re-check everything. Cheap (a few dozen comparisons), so it also runs after every event.</summary>
    public void Evaluate()
    {
        var now = _now();
        RollDay();
        var today = Key(now);
        if (!State.Days.Contains(today)) { State.Days.Add(today); _dirty = true; }
        if (State.Days.Count > 800) State.Days.RemoveRange(0, State.Days.Count - 800);
        if (State.Active.Count > 400)
            foreach (var old in State.Active.Keys.OrderBy(k => k).Take(State.Active.Count - 400).ToList()) State.Active.Remove(old);

        // hidden medals tied to the date
        if (now.Hour == 0 && now.Minute <= 1 && !State.Counters.ContainsKey("midnight")) { State.Counters["midnight"] = 1; _dirty = true; }
        if (now.Hour == 5 && !State.Counters.ContainsKey("early") && UserIdle.Since() < TimeSpan.FromMinutes(5)) { State.Counters["early"] = 1; _dirty = true; }
        var pcal = new PersianCalendar();
        if (pcal.GetMonth(now) == 1 && pcal.GetDayOfMonth(now) <= 3 && !State.Counters.ContainsKey("nowruz")) { State.Counters["nowruz"] = 1; _dirty = true; }
        if (pcal.GetMonth(now) == 9 && pcal.GetDayOfMonth(now) == 30 && !State.Counters.ContainsKey("yalda")) { State.Counters["yalda"] = 1; _dirty = true; }
        if (now.Month == 10 && now.Day == 31 && !State.Counters.ContainsKey("halloween")) { State.Counters["halloween"] = 1; _dirty = true; }
        if (!State.Counters.ContainsKey("birthday") && _hub.Settings.Birthday is { HasDate: true } b)
        {
            var (month, day) = b.Jalali ? (new PersianCalendar().GetMonth(now), new PersianCalendar().GetDayOfMonth(now)) : (now.Month, now.Day);
            if (b.Month == month && b.Day == day) { State.Counters["birthday"] = 1; _dirty = true; }
        }

        var changed = false;
        for (var pass = 0; pass < 3; pass++) // the collector medal may follow another one
        {
            var ctx = new RewardsContext(State, _hub.State, now, PageCount);
            var any = false;
            foreach (var m in RewardsCatalog.Medals)
            {
                if (State.Medals.ContainsKey(m.Id)) continue;
                var (cur, target) = m.Progress(ctx);
                if (cur < target) continue;
                State.Medals[m.Id] = now;
                Pay(m.Reward);
                _toasts.Add((m, now));
                _hub.Notify(L.T("مدال جدید!", "New medal!"), $"{m.Name} · +{Fa.N(m.Reward)} {L.T("پَر", "feathers")}");
                any = changed = true;
            }
            if (!any) break;
        }

        var ctxQ = new RewardsContext(State, _hub.State, now, PageCount);
        foreach (var q in RewardsCatalog.DailyFor(now).Append(RewardsCatalog.WeeklyFor(now)).Concat(RewardsCatalog.Once))
        {
            var key = RewardsCatalog.ClaimKey(q, now);
            if (State.Claimed.Contains(key) || q.Value(ctxQ) < q.Target) continue;
            State.Claimed.Add(key);
            if (State.Claimed.Count > 300) State.Claimed.RemoveAt(0);
            Pay(q.Reward);
            State.Counters["questsDone"] = State.Counters.GetValueOrDefault("questsDone") + 1;
            _hub.Notify(L.T("مأموریت انجام شد", "Quest done"), $"{q.Text} · +{Fa.N(q.Reward)} {L.T("پَر", "feathers")}");
            changed = true;
        }

        // a whole week of nights: a bonus every seven days of the streak
        var streak = Streak;
        if (streak > State.BestStreak) { State.BestStreak = streak; changed = true; }
        if (streak >= 7 && streak % 7 == 0)
        {
            var mark = $"streak:{today}";
            if (!State.Claimed.Contains(mark))
            {
                State.Claimed.Add(mark);
                Pay(10);
                _hub.Notify(L.T("هفت شب پیاپی", "Seven nights in a row"), L.T("۱۰ پَر هدیه گرفتی.", "You got 10 feathers."));
                changed = true;
            }
        }

        var level = Level.Level;
        if (level > State.LastLevel)
        {
            State.LastLevel = level;
            _hub.Notify(L.T("سطح تازه!", "New level!"), L.T($"بیدار شد «{Level.Name}».", $"Bidar is now “{Level.Name}”."));
            changed = true;
        }
        else if (level < State.LastLevel) State.LastLevel = level;

        if (changed || _dirty)
        {
            if (!_hub.Demo) _hub.Store.Save();
            _dirty = false;
            Changed?.Invoke();
        }
        FlushToasts();
    }

    private void Pay(int feathers)
    {
        State.Feathers += feathers;
        State.TotalFeathers += feathers;
    }

    /// <summary>Medal notices wait while a game is running in quiet mode and show afterwards.</summary>
    private void FlushToasts()
    {
        if (_toasts.Count == 0 || MedalEarned is null) return;
        if (_hub.GameModeActive && _hub.Settings.Comfort.GameModeQuiet) return;
        var list = _toasts.ToList();
        _toasts.Clear();
        foreach (var (medal, _) in list) MedalEarned?.Invoke(medal);
    }

    /// <summary>Screenshots and demos: a believable half-full cabinet that is never saved.</summary>
    public void SeedDemo()
    {
        var now = _now();
        var r = State;
        foreach (var id in new[] { "hello", "tour", "curious", "tinker", "firstgame", "gamenight", "allrounder", "marathon", "firsttick", "focus", "broom", "ontime", "nightdl", "careful", "sevennights", "realowl", "midnight" })
            r.Medals[id] = now.AddDays(-3);
        r.Feathers = r.TotalFeathers = 640;
        r.Pages.AddRange(["home", "system", "gaming", "tasks", "settings", "dns", "files", "health", "weather"]);
        r.Counters["gameSessions"] = 12; r.Counters["gameMinutes"] = 1730; r.Counters["ramFreed"] = 9; r.Counters["tasksDone"] = 17;
        r.Games.AddRange(["Valorant", "Dota 2", "Elden Ring", "CS2", "Minecraft"]);
        r.LongestGameMinutes = 214;
        for (var i = 0; i < 6; i++) { r.Days.Add(Key(now.AddDays(-i))); r.Active[Key(now.AddDays(-i))] = 40; }
        r.DayKey = Key(now);
        r.DayCounters["ram"] = 1;
        r.LastLevel = Level.Level;
    }
}
