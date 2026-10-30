namespace Shabpa.Core;

/// <summary>The line at the top of the home page. One is picked at random each time the app starts (and again when
/// the part of the day changes), so it is not the same plain "good morning" every day. The tone is friendly and a
/// little cheeky on purpose.</summary>
public static class Greetings
{
    public enum Part { Dawn, Morning, Noon, Afternoon, Evening, Night, Late }

    private static readonly Random Dice = new();
    private static string? _chosenKey;
    private static string _chosenText = "";

    public static Part PartOf(DateTime now) => now.Hour switch
    {
        >= 4 and < 7 => Part.Dawn,
        >= 7 and < 12 => Part.Morning,
        >= 12 and < 14 => Part.Noon,
        >= 14 and < 17 => Part.Afternoon,
        >= 17 and < 21 => Part.Evening,
        >= 21 or < 1 => Part.Night,
        _ => Part.Late,
    };

    /// <summary>The line of the home page. It stays the same while nothing it depends on changes (the part of the day, the day, the game being played
    /// or last logged, the hours played today, the language), and is a fresh draw when one of those changes.</summary>
    public static string Pick(DateTime now, GreetingContext? context = null) => Pick(now, context ?? GreetingContext.None, Dice);

    internal static string Pick(DateTime now, GreetingContext ctx, Random dice)
    {
        var english = L.En;
        var key = $"{PartOf(now)}|{now:yyyyMMdd}|{ctx.Current}|{ctx.Last}|{ctx.PlayedRecently}|{(int)(ctx.TodayMinutes / 60)}|{english}";
        if (key == _chosenKey) return _chosenText;
        _chosenKey = key;
        _chosenText = Draw(now, ctx, english, dice);
        return _chosenText;
    }

    /// <summary>For tests and the self-screenshots: start over so the next Pick is a fresh draw.</summary>
    public static void Reset() => _chosenKey = null;

    private static string Pick1(string[] lines, Random dice) => lines[dice.Next(lines.Length)];

    /// <summary>Every kind of line that fits this moment, with how much weight each kind has: most of the time a line about the hour, often one about
    /// the season or the day of the week, on a special day that first, and when a game is on or was just played, one about it.</summary>
    internal static List<(string kind, string[] lines, int weight)> Pools(DateTime now, GreetingContext ctx, bool english)
    {
        var pools = new List<(string, string[], int)>
        {
            ("time", (english ? English : Persian)[PartOf(now)], 40),
            ("season", english ? GreetingLines.Seasons[GreetingLines.SeasonOf(now)].en : GreetingLines.Seasons[GreetingLines.SeasonOf(now)].fa, 20),
            ("weekday", english ? GreetingLines.Weekdays[now.DayOfWeek].en : GreetingLines.Weekdays[now.DayOfWeek].fa, 12),
        };
        if (GreetingLines.SpecialDay(now) is { } special)
            pools.Add(("special", english ? GreetingLines.Special[special].en : GreetingLines.Special[special].fa, 60));
        var game = ctx.Playing ? ctx.Current : ctx.PlayedRecently ? ctx.Last : "";
        if (ctx.Playing) pools.Add(("playing", Fill(english ? GreetingLines.PlayingNow.en : GreetingLines.PlayingNow.fa, ctx.Current), 50));
        if (ctx.PlayedRecently) pools.Add(("after", Fill(english ? GreetingLines.AfterGame.en : GreetingLines.AfterGame.fa, ctx.Last), 45));
        if (game.Length > 0 && PerGameLines(game, english) is { Length: > 0 } own) pools.Add(("game", own, 40));
        if (ctx.TodayMinutes >= 120)
        {
            var hours = Math.Max(2, (int)Math.Round(ctx.TodayMinutes / 60.0, MidpointRounding.AwayFromZero));
            pools.Add(("longday", Fill(english ? GreetingLines.LongDay.en : GreetingLines.LongDay.fa, english ? hours.ToString() : Fa.Digits(hours.ToString())), 25));
        }
        if (ctx.PlayedRecently && ctx.LastMaxGpuTemp >= 82)
            pools.Add(("hot", Fill(english ? GreetingLines.HotGame.en : GreetingLines.HotGame.fa, ctx.Last), 18));
        return pools;
    }

    private static string[] Fill(string[] templates, string value) => [.. templates.Select(t => t.Replace("{0}", value))];

    internal static string[] PerGameLines(string game, bool english)
    {
        var lower = game.ToLowerInvariant();
        foreach (var (keys, fa, en) in GreetingLines.PerGame)
            if (keys.Any(k => lower.Contains(k))) return english ? en : fa;
        return [];
    }

    private static string Draw(DateTime now, GreetingContext ctx, bool english, Random dice)
    {
        var pools = Pools(now, ctx, english).Where(p => p.lines.Length > 0).ToList();
        var total = pools.Sum(p => p.weight);
        var roll = dice.Next(total);
        foreach (var (_, lines, weight) in pools)
        {
            if (roll < weight) return Pick1(lines, dice);
            roll -= weight;
        }
        return Pick1(pools[0].lines, dice);
    }

    /// <summary>The lines of each part of the day (the first set is the original, the second set the newer additions; both languages have the same count).</summary>
    private static IReadOnlyDictionary<Part, string[]>? _persian, _english;

    public static IReadOnlyDictionary<Part, string[]> Persian => _persian ??= Merge(PersianBase, GreetingMore.Persian);

    public static IReadOnlyDictionary<Part, string[]> English => _english ??= Merge(EnglishBase, GreetingMore.English);

    private static IReadOnlyDictionary<Part, string[]> Merge(IReadOnlyDictionary<Part, string[]> a, IReadOnlyDictionary<Part, string[]> b) =>
        a.ToDictionary(p => p.Key, p => (string[])[.. p.Value, .. b[p.Key].Where(x => !p.Value.Contains(x))]);

    public static readonly IReadOnlyDictionary<Part, string[]> PersianBase = new Dictionary<Part, string[]>
    {
        [Part.Dawn] =
        [
            "به‌به! سحرخیز شدی، آفتاب هنوز تو رخت‌خوابه ☀️", "این وقت صبح؟ تو یا قهرمانی یا هنوز نخوابیدی 😄", "صبح زود بخیر! خروس‌ها هم هنوز خوابن 🐓",
            "سحرخیزی؟ پس چایی‌ت رو دم کن که دنیا مال توئه ☕", "هنوز هوا گرگ‌ومیشه، ولی تو آماده‌ای! 🌄", "صبح بخیر، جوجه‌ها هم هنوز چشماشونو باز نکردن 🐣",
            "سپیده زد! روزت رو با یه لبخند شروع کن ✨", "چه صبحی! فقط من و تو و خورشیدِ خواب‌آلود 🌅",
        ],
        [Part.Morning] =
        [
            "صبحت بخیر! ☀️", "صبح بخیر! چایی‌ت رو خوردی یا هنوز مغزت بوت نشده؟ ☕", "سلام صبحی! امروز قراره روز خوبی باشه 🌼",
            "صبح عالیِ پرتقالی! یه لیوان چایی و راه بیفتیم 🍊", "صبح شد، دنیا منتظره تو رو ببینه 🌈", "بیدار شو قهرمان، امروز نوبت توئه 💪",
            "سلام! یه صبح خوشمزه‌ی دیگه، مثل نون تازه 🥐", "صبح بخیر؛ لبخندت رو یادت نره، مفته 😊", "یه صبح تازه، یه شروع تازه 🌱",
            "نسیم صبح بخیر! 🍃", "صبح بخیر! باتری‌ت رو شارژ کن، کلی کار داریم 🔋", "ببین کی اومده! صبحت بخیر 😎",
            "صبح بخیر، امروز هم ما می‌ترکونیم 🚀", "عسل صبح بخیر 🍯",
        ],
        [Part.Noon] =
        [
            "ظهرت بخیر! ناهار یادت نره، شکمت داره سیگنال می‌ده 🍽️", "ظهر شد! یه نفس بگیر و یه چیزی بخور 😌", "نصف روز رفت، دمت گرم که هنوز سر پایی 🌤️",
            "وقت ناهاره! آهنگ مورد علاقه‌ت رو بذار 🎧", "ظهر بخیر؛ یه لیوان آب بخور، گیاه نیستی که نشکنی 💧", "خورشید درست بالای سرته، کولر رو روشن کن ☀️",
            "ظهر بخیر! غذا نخوری باتری‌ت زود خالی می‌شه 🍛", "نیمه‌ی روز بخیر، چشماتو از مانیتور بردار یه لحظه 👀",
        ],
        [Part.Afternoon] =
        [
            "عصر نزدیکه، یه چایی دیگه؟ ☕", "وقت چای خوردنه! 🫖", "بعدازظهر بخیر! یه کش‌وقوس به کمرت بده 🤸", "سلام! نصف بیشتر روز رو رد کردی، آفرین 👏",
            "بعدازظهرِ آروم و دل‌نشین 🍃", "یه استراحت کوتاه بد نیست، چشمات رو هم بده به خدا 👀", "بعدازظهر بخیر، آفتاب‌پرست! 🌻",
            "این ساعت یه چایی و یه خرما می‌چسبه 🍵", "ساعت خواب‌آلودگیه؛ یه آب به صورتت بزن 💦",
        ],
        [Part.Evening] =
        [
            "عصرت بخیر! 🌇", "عصر بخیر؛ خورشید داره خداحافظی می‌کنه 🌆", "غروب بخیر، دنیا طلایی شده ✨", "عصر بخیر، بریم سراغ بازی؟ 🎮",
            "وقت تماشای غروبه، یه نگاه به پنجره بنداز 🌅", "سلام! امیدوارم روز خوبی داشته باشی 🧡", "عصر بخیر، یه چایی داغ می‌چسبه ☕",
            "کارای امروز تموم شد؟ حالا وقت خودته 🎧", "عصر بخیر! شام چی داریم؟ 🍲",
        ],
        [Part.Night] =
        [
            "شبت بخیر! 🌙", "شب بخیر؛ ستاره‌ها بیدارن، تو هم هستی 🌟", "سلام شب‌زنده‌دار! 🦉", "شب بخیر؛ یه فیلم خوب بد نیست 🍿",
            "شب شد؛ وقت استراحت مغز و چشمه 😴", "ماه امشب خیلی خوشگله، یه نگاه بهش بنداز 🌕", "یه چای دارچینی بخور و آروم بگیر 🍵",
            "شب بخیر! خستگی رو بنداز دور 🛋️", "شب شد و شب‌پا بیداره، تو هم راحت باش 🌃",
        ],
        [Part.Late] =
        [
            "هنوز بیداری؟! 🦉 به این شب‌زنده‌داری آفرین", "نصف‌شبه؛ بدنت داره التماس می‌کنه که بخوابی 😴", "ساعت خواب گذشته، فردا رو هم دوست داشته باش 🌙",
            "یه لیوان آب بخور و برو بخواب، فردا هم روزه 💧", "دیروقته! چشمات رو هم دریاب 👀", "سلام جغد شبانه! 🦉✨",
            "این وقت شب فقط من و تو بیداریم 🌌", "خسته‌ای؟ شب‌پا می‌تونه کامپیوتر رو برات خاموش کنه، شب بخیر 😉",
        ],
    };

    public static readonly IReadOnlyDictionary<Part, string[]> EnglishBase = new Dictionary<Part, string[]>
    {
        [Part.Dawn] =
        [
            "Up with the sun! Good morning ☀️", "Early bird! The sun is still in bed 🐓", "Even the chickens are asleep, and you're up 🌅", "Fresh tea and a quiet dawn ☕",
            "Good morning, early riser 🌄", "Even the chicks haven't opened their eyes yet 🐣", "The sky is turning gold ✨", "Just you, me and a sleepy sun 🌍",
        ],
        [Part.Morning] =
        [
            "Good morning! ☀️", "Have an orange-bright morning! 🍊", "Good morning, today will be a good one 🌼", "Another tasty morning, like fresh bread 🥐",
            "Tea first, then the world ☕", "Rise and shine, champ 🌈", "Ready for an adventure? 🚀", "Charge your batteries, we have plans 🔋",
            "Don't forget to smile, it's free 😊", "A fresh morning, a fresh start 🌱", "Today is yours 💪", "Look who's up! 😎", "Sweet and energetic morning 🍯", "Good morning, we are going to crush it today 🚀",
        ],
        [Part.Noon] =
        [
            "Good noon! Lunch won't eat itself 🍽️", "Midday break time 😌", "Half the day is done, well played! 🌤️", "Eat something, your battery is low 🍛",
            "The sun is right above you ☀️", "Lunch time, put on your favourite song 🎧", "Drink some water, you're not a plant 💧", "Look away from the screen for a sec 👀",
        ],
        [Part.Afternoon] =
        [
            "Evening is near, another tea? ☕", "Tea time! 🫖", "Stretch a little 🤸", "Most of the day is behind you, nice 👏",
            "A calm, cosy afternoon 🍃", "Rest your eyes for a minute 👀", "Sunny afternoon to you 🌻", "Splash some water on your face, sleepyhead 💦", "Tea and a date is the right snack for this hour 🍵",
        ],
        [Part.Evening] =
        [
            "Good evening! 🌇", "The sun is saying goodbye 🌆", "Golden hour ✨", "Shall we play something? 🎮",
            "Sunset time, look out the window 🌅", "Hope the day was kind to you 🧡", "A hot tea would be lovely ☕", "Work's done, now it's your time 🎧", "What is for dinner? 🍲",
        ],
        [Part.Night] =
        [
            "Good night! 🌙", "The stars are awake, and so are you 🌟", "Hello night owl! 🦉", "A good movie would fit 🍿",
            "Time to rest your eyes 😴", "The moon is beautiful tonight 🌕", "Cinnamon tea is calling 🍵", "Let tiredness go 🛋️", "Shabpa is awake, take it easy 🌃",
        ],
        [Part.Late] =
        [
            "Still awake?! 🦉", "It's the middle of the night; your body is begging for sleep 😴", "Past bedtime, tomorrow will love you too 🌙",
            "A glass of water and then bed 💧", "Late night salute, but look after your eyes 👀", "Hello night owl! 🦉✨",
            "Only you and I are awake 🌌", "Tired? Shabpa can switch the PC off for you 😉",
        ],
    };
}
