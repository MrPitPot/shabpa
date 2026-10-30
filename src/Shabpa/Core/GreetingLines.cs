namespace Shabpa.Core;

public enum Season { Spring, Summer, Autumn, Winter }

/// <summary>What the greeting may know about the person's day: the game running now, the last one logged and how much has been played today.
/// Everything comes from the game log on this PC; nothing leaves it.</summary>
public sealed record GreetingContext(string Current, string Last, TimeSpan? SinceLast, double TodayMinutes, int GamesToday, double LastMaxGpuTemp = 0, double LastMinutes = 0)
{
    public static readonly GreetingContext None = new("", "", null, 0, 0);

    /// <summary>What the game log and the running game say right now. <paramref name="history"/> is newest last, like the saved list.</summary>
    public static GreetingContext From(IReadOnlyList<GameLogEntry> history, string currentGame, DateTime? currentSince, DateTime now)
    {
        var last = history.Count > 0 ? history[^1] : null;
        var today = history.Where(h => h.Start.Date == now.Date).Sum(h => h.Minutes);
        if (currentGame.Length > 0 && currentSince is { } since && since.Date == now.Date) today += Math.Max(0, (now - since).TotalMinutes);
        return new GreetingContext(currentGame, last?.Game ?? "", last is null ? null : now - last.Start.AddMinutes(last.Minutes), today,
            history.Count(h => h.Start.Date == now.Date), last?.MaxGpuTemp ?? 0, last?.Minutes ?? 0);
    }

    public bool Playing => Current.Length > 0;
    public bool PlayedRecently => Last.Length > 0 && SinceLast is { } s && s < TimeSpan.FromHours(8) && !Playing;
}

/// <summary>The words of the home-page greeting beyond the plain time of day: the season, the day of the week, special days, and lines about the game
/// the person is playing or has just logged. Persian and English go together (same count) so a language switch never changes how many there are.</summary>
public static class GreetingLines
{
    public static Season SeasonOf(DateTime d)
    {
        var md = d.Month * 100 + d.Day;
        return md switch
        {
            >= 321 and < 621 => Season.Spring,
            >= 621 and < 923 => Season.Summer,
            >= 923 and < 1221 => Season.Autumn,
            _ => Season.Winter,
        };
    }

    public static readonly IReadOnlyDictionary<Season, (string[] fa, string[] en)> Seasons = new Dictionary<Season, (string[], string[])>
    {
        [Season.Spring] = (
            ["بهار اومده و حتی سیستمت هم یه کم سرحال‌تره 🌸", "بوی بهار میاد؛ پنجره رو باز کن یه نفس تازه بگیر 🌷", "بهار قشنگه، فقط مواظب حساسیتت باش 🤧🌼", "شکوفه‌ها باز شدن، تو هم باز شو! 🌸",
             "بهاره؛ وقتشه یه پیاده‌روی کوتاه بین دو تا بازی بذاری 🚶", "روزهای بهاری بلندتر شدن، بازی‌های بیشتری جا می‌شه 😉", "بهار بخیر! دنیا سبز شده، چشمات رو از مانیتور بردار 🌿",
             "بارون بهاری می‌اد؟ یه چای و یه فیلم می‌چسبه 🌦️", "ماه‌های بهاری، حال خوب و هوای خوب 🍃", "بهار یعنی شروع دوباره؛ امروز رو هم از اول شروع کن 🌱",
             "این روزها هوا بهاریه، فنِ سیستمت هم یه نفس راحت می‌کشه 😌", "لاله‌ها دارن می‌شکفن، ایستگاه بعدی: تعطیلات 🌷"],
            ["Spring is here, even your PC feels fresher 🌸", "Smells like spring; open a window and take a fresh breath 🌷", "Spring is lovely, mind the allergies 🤧🌼", "The blossoms are open, open up too! 🌸",
             "It is spring; a short walk between two games is a good idea 🚶", "Spring days are longer, more games fit in 😉", "Happy spring! The world turned green, look away from the screen 🌿",
             "Spring rain on the way? Tea and a movie would be perfect 🌦️", "Spring months, good mood and good weather 🍃", "Spring means starting over; start today over too 🌱",
             "The weather is mild, your PC fans get a break too 😌", "Tulips are blooming, next stop: holidays 🌷"]),
        [Season.Summer] = (
            ["تابستون داغه، یه لیوان آب خنک بخور 🧊", "گرمای تابستون؛ مواظب دمای سیستمت هم باش 🌡️", "تابستونه؛ کولر روشن، بازی روشن 😎", "هوا گرمه، فنِ سیستمت داره عرق می‌ریزه 💦",
             "تابستون بخیر! هندونه یادت نره 🍉", "روزهای بلند تابستون یعنی شب‌های بلندِ بازی 🌙", "گرما که زیاده، یه بستنی بد نیست 🍦", "تابستون شده؛ پرده رو بکش که مانیتور برق نزنه ☀️",
             "با این گرما یه سایه و یه نوشیدنی خنک دیگه چی می‌خوای؟ 🥤", "تابستونه و ساعت‌ها طولانی؛ یادت باشه کش‌وقوس بدی 🤸", "گرما شدید شد؟ شب‌پا دمای قطعه‌هات رو زیر نظر داره 🔍", "عصرهای تابستون با یه شربت خنک عالیه 🍹"],
            ["Summer is hot, have a cold glass of water 🧊", "Summer heat; watch your PC temperatures too 🌡️", "It is summer; AC on, game on 😎", "It is hot, your PC fans are sweating 💦",
             "Happy summer! Do not forget watermelon 🍉", "Long summer days mean long gaming nights 🌙", "Too hot? An ice cream would not hurt 🍦", "Summer is here; draw the curtain so the monitor does not glare ☀️",
             "A bit of shade and a cold drink, what else do you need? 🥤", "Long summer hours; remember to stretch 🤸", "Heat wave? Shabpa keeps an eye on your parts' temperatures 🔍", "Summer evenings are made for a cold sherbet 🍹"]),
        [Season.Autumn] = (
            ["پاییزه؛ برگ‌ها می‌ریزن و چای دم می‌کشه 🍂", "پاییز بخیر! هوای دلنشین، بازی دلنشین 🍁", "بارون پاییزی یعنی یه گوشه، یه پتو و یه بازی 🌧️", "پاییز شد، وقتشه چایی داغ بیاد وسط 🍵",
             "برگ‌ها رنگ عوض کردن، تو هم یه تم جدید امتحان کن 🎨", "پاییز یعنی فصل بازگشت: به درس، به کار، به بازی 📚", "هوا خنک شده و سیستمت هم خوشحاله 😌", "پاییز و بوی خاک بارون خورده 🌦️",
             "غروب‌های پاییز زودتر میان؛ حواست به زمان باشه 🌆", "یه انار و یه چای، پاییز یعنی همین 🍎", "پاییز آهنگ‌های آرومش رو می‌خونه 🎧", "پاییزه؛ از این‌به‌بعد شب‌ها بلندتر می‌شن 🌙"],
            ["It is autumn; leaves fall and the tea steeps 🍂", "Happy autumn! Gentle weather, gentle games 🍁", "Autumn rain means a corner, a blanket and a game 🌧️", "Autumn is here, hot tea belongs on the table 🍵",
             "The leaves changed colour, try a new theme too 🎨", "Autumn is the season of returns: to study, to work, to games 📚", "It is cooler and your PC is happier 😌", "Autumn and the smell of rain-soaked earth 🌦️",
             "Autumn sunsets come earlier; keep an eye on the time 🌆", "A pomegranate and a tea: that is autumn 🍎", "Autumn hums its quiet songs 🎧", "It is autumn; nights get longer from now on 🌙"]),
        [Season.Winter] = (
            ["زمستونه؛ یه پتو و یه چای داغ، همه‌چی تمومه 🧣", "سرده؟ سیستمت حداقل گرمه 🔥", "زمستون بخیر! کرسی نداری؟ حداقل یه جوراب گرم بپوش 🧦", "برف می‌آد؟ بهترین بهانه برای یه بازی طولانی ❄️",
             "زمستون یعنی شب‌های بلند و فیلم و بازی 🍿", "هوا سرد شده؛ فنِ سیستمت امروز راحته 😌", "زمستون؛ فصل سوپ داغ و کیبورد گرم ☕", "یه لیوان شیرکاکائو و بازی مورد علاقه‌ات ☕🎮",
             "زمستونه؛ یادت نره پنجره رو بسته نگه داری 🪟", "ای بابا، سرما؛ بیا تو لاک خودمون 🐢", "شبای زمستون برای یه فیلم خوب ساخته شدن 🎬", "برف و بوران بیرون، آرامش داخل ☃️"],
            ["It is winter; a blanket and hot tea, all set 🧣", "Cold? At least your PC is warm 🔥", "Happy winter! Wear warm socks at least 🧦", "Snowing? The best excuse for a long game ❄️",
             "Winter means long nights, movies and games 🍿", "It is cold; your PC fans have an easy day 😌", "Winter: the season of hot soup and warm keyboards ☕", "A cup of hot chocolate and your favourite game ☕🎮",
             "It is winter; keep the window shut 🪟", "Brr, the cold; let us stay in our shell 🐢", "Winter nights were made for a good film 🎬", "Snow outside, calm inside ☃️"]),
    };

    /// <summary>A few lines for each day of the week (the Persian week ends on Friday).</summary>
    public static readonly IReadOnlyDictionary<DayOfWeek, (string[] fa, string[] en)> Weekdays = new Dictionary<DayOfWeek, (string[], string[])>
    {
        [DayOfWeek.Saturday] = (["شنبه‌ست! شروع هفته، شروع خوب 💪", "شنبه بخیر؛ هفته‌ی جدید، شانس جدید 🌱", "اولین روز هفته؛ یواش‌یواش، عجله نداریم ☕", "شنبه‌ها بدون چایی شروع نمی‌شن 🍵"],
            ["It is Saturday! A good week starts now 💪", "Saturday: a new week, a new chance 🌱", "First day of the week; slowly, no rush ☕", "Saturdays do not begin without tea 🍵"]),
        [DayOfWeek.Sunday] = (["یکشنبه بخیر! هفته راه افتاد 🚀", "یکشنبه‌ست؛ یه قدم جلوتر از شنبه 😄", "یکشنبه؛ ریتم هفته داره جا می‌افته 🎵", "امروز یکشنبه‌ست، به خودت یه چای بدهکاری ☕"],
            ["Happy Sunday! The week is rolling 🚀", "It is Sunday; one step ahead of Saturday 😄", "Sunday: the rhythm of the week is settling 🎵", "Sunday: you owe yourself a tea ☕"]),
        [DayOfWeek.Monday] = (["دوشنبه‌ست، ولی ما از پسش برمیایم 😤", "دوشنبه بخیر؛ نصف هفته‌ی کاری نزدیکه 💪", "دوشنبه‌ها فقط یه اسمن، بریم جلو ✨", "دوشنبه؛ قهوه یا چایی؟ فرقی نداره، مهم اینه که شروع کنی ☕"],
            ["It is Monday, but we can handle it 😤", "Happy Monday; the middle of the week is near 💪", "Mondays are only a name, let us go ✨", "Monday: coffee or tea? It does not matter, just start ☕"]),
        [DayOfWeek.Tuesday] = (["سه‌شنبه‌ست! هفته داره می‌گذره 🏃", "سه‌شنبه بخیر؛ ریتم هفته گرفتی 🎶", "سه‌شنبه؛ نه اولِ هفته‌ای نه آخرش، آرامش 😌", "سه‌شنبه‌ست، یه استراحت کوتاه حقته 🛋️"],
            ["It is Tuesday! The week is moving 🏃", "Happy Tuesday; you caught the rhythm 🎶", "Tuesday: neither start nor end, calm 😌", "It is Tuesday; a short rest is your right 🛋️"]),
        [DayOfWeek.Wednesday] = (["چهارشنبه‌ست! نصف راه رو رفتی 🎯", "چهارشنبه بخیر؛ فردا پنجشنبه‌ست، یه ذره دیگه 😉", "چهارشنبه؛ وسط هفته و وسط سرحالی 🌟", "چهارشنبه‌سوری نیست، ولی دلت می‌خواد بپری؟ 🔥"],
            ["It is Wednesday! You are halfway there 🎯", "Happy Wednesday; tomorrow is Thursday, almost 😉", "Wednesday: mid-week, mid-mood 🌟", "Midweek already, nearly the weekend 🔥"]),
        [DayOfWeek.Thursday] = (["پنجشنبه‌ست! بوی آخر هفته میاد 🎉", "پنجشنبه بخیر؛ فردا جمعه‌ست، یه ذره دیگه 😍", "پنجشنبه‌ها شب بازی و فیلمن 🎮🍿", "پنجشنبه‌ی خوشحال! امشب بیشتر بیدار می‌مونی؟ 🌙"],
            ["It is Thursday! The weekend is in the air 🎉", "Happy Thursday; tomorrow is Friday 😍", "Thursdays are game and movie nights 🎮🍿", "Happy Thursday! Staying up later tonight? 🌙"]),
        [DayOfWeek.Friday] = (["جمعه‌ست! امروز به هیچ‌کس جواب پس نمی‌دی 😎", "جمعه بخیر؛ صبحونه‌ی دیرهنگام و یه روز تنبل 🥞", "جمعه‌ی عزیز! هر چی دلت می‌خواد بازی کن 🎮", "جمعه یعنی استراحت؛ سیستم هم باهات استراحت می‌کنه 😴"],
            ["It is Friday! You owe nobody anything today 😎", "Happy Friday; a late breakfast and a lazy day 🥞", "Dear Friday! Play whatever you like 🎮", "Friday means rest; the PC rests with you 😴"]),
    };

    // ---- special days ---------------------------------------------------------------------------------------------------

    public static string? SpecialDay(DateTime d) => (d.Month, d.Day) switch
    {
        (3, >= 19 and <= 25) => "nowruz",
        (12, 20 or 21) => "yalda",
        (12, 31) or (1, 1 or 2) => "newyear",
        (10, >= 28) or (11, 1) => "halloween",
        _ => null,
    };

    public static readonly IReadOnlyDictionary<string, (string[] fa, string[] en)> Special = new Dictionary<string, (string[], string[])>
    {
        ["nowruz"] = (["عیدتون مبارک! سال نو پر از بازی‌های خوب و پینگ پایین 🌷", "نوروزت پیروز! هفت‌سین چیدی یا هنوز؟ 🌱", "سال نو مبارک! امیدوارم امسال هیچ‌وقت بازی‌ات وسط راه هنگ نکنه 🎊", "عید نوروز بخیر؛ شب‌پا هم به شما تبریک می‌گه 🐟", "نوروز شد! عیدی‌ات رو بگیر، یه گیم جدید بخر 😄", "سال نو، سیستم نو، حال نو 🎉"],
            ["Happy Nowruz! May the year bring great games and low ping 🌷", "Nowruz is here! Did you set the Haft-sin yet? 🌱", "Happy new year! May no game freeze on you this year 🎊", "Nowruz greetings from Shabpa too 🐟", "It is Nowruz! Take your gift money and buy a new game 😄", "New year, new rig, new mood 🎉"]),
        ["yalda"] = (["شب یلدا مبارک! هندونه و انار حاضره؟ 🍉", "یلدا بخیر؛ بلندترین شب سال، بهترین بهانه برای یه بازی طولانی 🌙", "یلدا مبارک! حافظ بخون و بعدش گیم 📖🎮", "امشب شب یلداست؛ شب‌پا هم تا صبح بیداره 🦉", "یلدا مبارک؛ آجیل، انار و یه بازی دوستانه 🥜🍎", "طولانی‌ترین شب سال؛ مبارکت باشه 🔴"],
            ["Happy Yalda! Are the watermelon and pomegranate ready? 🍉", "Yalda greetings; the longest night is the best excuse for a long game 🌙", "Happy Yalda! Read Hafez and then game 📖🎮", "It is Yalda night; Shabpa stays up till morning too 🦉", "Happy Yalda; nuts, pomegranate and a friendly game 🥜🍎", "The longest night of the year; enjoy it 🔴"]),
        ["newyear"] = (["سال نوی میلادی مبارک! 🎆", "ساعت‌ها داره عوض می‌شه؛ سال نو مبارک 🥳", "اول ژانویه‌ست! تصمیم گرفتی کمتر بازی کنی؟ 😏", "سال نو مبارک؛ بذار این سال هم با هم باشیم 🎇", "تبریک سال جدید! پینگت همیشه زیر ۳۰ 🎉"],
            ["Happy New Year! 🎆", "The clocks are changing; happy new year 🥳", "It is the first of January! Resolved to play less? 😏", "Happy new year; let us spend this one together too 🎇", "Congratulations on the new year! Ping always under 30 🎉"]),
        ["halloween"] = (["هالووین مبارک! امشب هر صدایی از پشت سرت اومد، فقط فن سیستمه 🎃", "هالووین شد؛ امشب بازی ترسناک می‌کنی؟ 👻", "شب هالووین؛ خیلی نترس، شب‌پا باهاته 🦇", "هالووین بخیر! آب‌نبات یا شیطنت؟ 🍬", "کدو تنبل‌ها بیدارن؛ تو هم بیدار بمون 🎃", "اگه صدای خش‌خش اومد، حتماً فنِ پردازنده‌ست 😈"],
            ["Happy Halloween! Any noise behind you tonight is only the PC fan 🎃", "It is Halloween; playing a horror game tonight? 👻", "Halloween night; do not be scared, Shabpa is with you 🦇", "Happy Halloween! Trick or treat? 🍬", "The pumpkins are awake; so stay awake 🎃", "If you hear a rustle, it is surely the CPU fan 😈"]),
    };

    // ---- games ----------------------------------------------------------------------------------------------------------

    /// <summary>After a game has been logged (the name is put in {0}).</summary>
    public static readonly (string[] fa, string[] en) AfterGame = (
        ["تازه «{0}» بازی کردی؛ دستت درد نکنه، حالا یه استراحت 🎮", "«{0}» تموم شد؟ حالا چشمات رو هم دریاب 👀", "بازی «{0}» ثبت شد؛ حالا یه لیوان آب بخور 💧", "«{0}» رو که تموم کردی، یه کش‌وقوس به کمرت بده 🤸",
         "آخرین بازی‌ات «{0}» بود؛ بریم ببینیم گزارشش چی می‌گه؟ 📊", "«{0}» چطور بود؟ شب‌پا همه‌چی رو نوشته 📝", "بعد از «{0}» یه چایی لازمی ☕", "گزارش «{0}» آماده‌ست؛ می‌خوای ببینی؟ 📈",
         "«{0}» تموم شد، حالا نوبت یه کم هوای آزاده 🌤️", "بازی «{0}» رو تموم کردی و سیستم هم خسته‌ست، بذار خاموشش کنم؟ 😉"],
        ["You just played “{0}”; good job, now a short rest 🎮", "Done with “{0}”? Now look after your eyes 👀", "“{0}” is logged; now drink some water 💧", "After “{0}”, stretch your back a little 🤸",
         "Your last game was “{0}”; shall we look at its report? 📊", "How was “{0}”? Shabpa wrote it all down 📝", "After “{0}” you deserve a tea ☕", "The report of “{0}” is ready; want to see it? 📈",
         "“{0}” is over; time for some fresh air 🌤️", "You finished “{0}” and the PC is tired; shall I switch it off? 😉"]);

    /// <summary>While a game is running (the name is put in {0}).</summary>
    public static readonly (string[] fa, string[] en) PlayingNow = (
        ["«{0}» در جریانه؛ شب‌پا ساکت و مراقبه 🎮", "موفق باشی توی «{0}»! 🏆", "«{0}» رو داری می‌ترکونی؟ دمای سیستم رو من می‌پایم 🔍", "وسط «{0}» هم یه جرعه آب بخور 💧",
         "بازی «{0}» داره پیش می‌ره، منم حواسم هست 🦉", "«{0}»؛ امیدوارم پینگت پایین بمونه 📶", "هر وقت بازی «{0}» تموم شد، گزارشش رو آماده می‌کنم 📝", "«{0}»؛ بزن بریم! ⚡"],
        ["“{0}” is on; Shabpa is quiet and watching 🎮", "Good luck in “{0}”! 🏆", "Crushing “{0}”? I keep an eye on the temperatures 🔍", "Sip some water in the middle of “{0}” 💧",
         "“{0}” is going on and I am watching 🦉", "“{0}”; hoping your ping stays low 📶", "When “{0}” ends, I will prepare its report 📝", "“{0}”; let us go! ⚡"]);

    /// <summary>A long day of gaming ({0} = hours, such as "۳").</summary>
    public static readonly (string[] fa, string[] en) LongDay = (
        ["امروز {0} ساعت بازی کردی؛ یه استراحت کوتاه بد نیست 🧘", "{0} ساعت گیم؟ آفرین، ولی چشمات هم حق دارن 👀", "تا الان {0} ساعت پای بازی بودی؛ بلند شو یه آب بخور 💧", "{0} ساعت بازی؛ یه کش‌وقوس و یه پیاده‌روی کوچیک؟ 🚶",
         "امروز کلی بازی کردی ({0} ساعت)؛ یه چای و یه استراحت 🍵"],
        ["{0} hours of gaming today; a short break would not hurt 🧘", "{0} hours of games? Well done, but your eyes have rights too 👀", "{0} hours at the game so far; stand up and drink some water 💧", "{0} hours of play; a stretch and a small walk? 🚶",
         "A lot of gaming today ({0} hours); a tea and a rest 🍵"]);

    /// <summary>When the last game ran hot ({0} = game name).</summary>
    public static readonly (string[] fa, string[] en) HotGame = (
        ["«{0}» سیستمت رو حسابی داغ کرده بود؛ یه نگاه به گرد و خاک فن‌ها بنداز 🌡️", "دمای کارت گرافیک توی «{0}» بالا رفته بود؛ شاید وقتشه کیس رو تمیز کنی 🧹"],
        ["“{0}” made your PC run hot; check the fans for dust 🌡️", "The graphics card got hot in “{0}”; maybe it is time to clean the case 🧹"]);

    /// <summary>Lines for particular games: the first keyword found in the lower-case game name picks the set ({0} = the name).</summary>
    public static readonly (string[] keys, string[] fa, string[] en)[] PerGame =
    [
        (["valorant"], ["والورنت بازی کردی؟ امیدوارم کلاچ‌ها به نفعت بوده باشن 🎯", "یه دست والورنت دیگه؟ ضربه‌ی سر یادت نره 🔫"], ["Played Valorant? Hope the clutches went your way 🎯", "One more Valorant round? Do not forget headshots 🔫"]),
        (["counter-strike", "cs2", "csgo", "counter strike"], ["سی‌اس بازی کردی؟ امیدوارم رنک پایین نیومده باشه 😅", "رانند بعدی CS رو برنده می‌شی، مطمئنم 💣"], ["Played CS? Hope the rank did not drop 😅", "You will win the next CS round, I am sure 💣"]),
        (["dota"], ["دوتا دو؟ ببین دوباره تا صبح بیدار نمونی 🌙", "یه دست دوتا یعنی یه ساعت و نیم، قبول؟ 😏"], ["Dota 2? Do not stay up till morning again 🌙", "One Dota game means an hour and a half, right? 😏"]),
        (["league of legends", "leagueoflegends", "league"], ["لیگ لجندز؟ امیدوارم هم‌تیمی‌ها خوب بودن 🙃", "رنک‌بندی بعدی رو با آرامش بازی کن 🧘"], ["League of Legends? Hope your teammates were kind 🙃", "Play the next ranked game calmly 🧘"]),
        (["elden ring", "eldenring"], ["الدن رینگ؟ دمت گرم که هنوز نذاشتی کنار، «You Died» رو هم قبول کن 💀", "الدن رینگ و بالاخره بزرگ‌ترین اربابا؟ مراقب باش 🗡️"], ["Elden Ring? Respect for not quitting yet, accept the “You Died” 💀", "Elden Ring and the great bosses? Be careful 🗡️"]),
        (["minecraft"], ["ماینکرفت؟ امیدوارم کریپر خونه‌ات رو منفجر نکرده باشه 💥", "ماینکرفت بازی می‌کنی؟ یه معدن دیگه بکن ⛏️"], ["Minecraft? Hope no creeper blew up your house 💥", "Playing Minecraft? Dig one more mine ⛏️"]),
        (["fortnite"], ["فورتنایت؟ ساختن یادت نره 🏗️", "فورتنایت و پیروزی آخر بازی؟ امیدوارم 👑"], ["Fortnite? Do not forget to build 🏗️", "Fortnite and a final victory? Hoping so 👑"]),
        (["gta", "grand theft"], ["جی‌تی‌ای؟ پلیس‌ها دنبالت بودن؟ 🚓", "لس‌سانتوس بدون تو شلوغ نبود 😄"], ["GTA? Were the police after you? 🚓", "Los Santos was not busy without you 😄"]),
        (["cyberpunk"], ["سایبرپانک؟ نایت‌سیتی بدون تو چراغون نیست 🌃", "سایبرپانک؛ رام کردن گرافیک این بازی یه هنره 🎨"], ["Cyberpunk? Night City is not lit without you 🌃", "Cyberpunk; taming its graphics is an art 🎨"]),
        (["witcher"], ["ویچر؟ امیدوارم گرالت پول کافی جمع کرده باشه 🪙", "ویچر سه؛ هیچ‌وقت کهنه نمی‌شه 🐺"], ["The Witcher? Hope Geralt earned enough coin 🪙", "Witcher 3 never gets old 🐺"]),
        (["apex"], ["اپکس لجندز؟ امیدوارم کامیون‌ها دنبالت نبودن 🚛", "اپکس بازی کردی؟ نفر آخر موندی؟ 🏆"], ["Apex Legends? Hope nobody chased you with a truck 🚛", "Played Apex? Last squad standing? 🏆"]),
        (["pubg", "battlegrounds"], ["پابجی؟ امیدوارم شام مرغ خورده باشی 🍗", "پابجی و منطقه‌ی آبی؛ همیشه دیر می‌رسی! 🏃"], ["PUBG? Hope you got a chicken dinner 🍗", "PUBG and the blue zone; you are always late! 🏃"]),
        (["overwatch"], ["اورواچ؟ مراقب بمب‌ها باش 💥", "اورواچ و هم‌تیمی‌های بی‌دقت 😅"], ["Overwatch? Mind the bombs 💥", "Overwatch and its careless teammates 😅"]),
        (["genshin"], ["جنشین؟ رزین‌هات رو خرج کردی؟ 🌸", "جنشین ایمپکت؛ ماجراجویی بعدی منتظرته ✨"], ["Genshin? Did you spend your resin? 🌸", "Genshin Impact; the next adventure awaits ✨"]),
        (["warzone", "call of duty", "cod"], ["کال آو دیوتی؟ امیدوارم کیل‌ها زیاد بوده 💥", "وارزون؛ لوت‌ها رو یادت نره 🎒"], ["Call of Duty? Hope there were many kills 💥", "Warzone; do not forget the loot 🎒"]),
        (["rocket league", "rocketleague"], ["راکت لیگ؟ دیگه این بار گل زدی؟ ⚽", "ماشین فوتبالی؛ همیشه سرگرم‌کننده 🚗"], ["Rocket League? Did you score this time? ⚽", "Cars playing football; always fun 🚗"]),
        (["fc 24", "fc24", "fc 25", "fc25", "fifa", "ea sports fc", "eafc"], ["فوتبال بازی کردی؟ امیدوارم برد رسیده باشه ⚽", "ای‌اف‌سی؛ هم‌تیمی‌ها رو هم بذار توی ترکیب 😄"], ["Played football? Hope you got the win ⚽", "EA FC; put your friends in the line-up too 😄"]),
        (["among us", "amongus"], ["اموگ آس؟ مشکوکی! 🚀", "امپاستر که نبودی؟ 😏"], ["Among Us? You look suspicious! 🚀", "You were not the impostor, were you? 😏"]),
        (["red dead"], ["رد دد؟ مراقب اسبت باش 🐴", "وحشی غرب منتظر برگشتنته 🤠"], ["Red Dead? Look after your horse 🐴", "The wild west awaits your return 🤠"]),
        (["hollow knight"], ["هالو نایت؟ دمت گرم که ادامه می‌دی، سخته 💪", "هالو نایت، یه شاهکار 🐞"], ["Hollow Knight? Respect for continuing, it is hard 💪", "Hollow Knight, a masterpiece 🐞"]),
        (["dark souls", "darksouls"], ["دارک سولز؟ لیاقت صبرت رو داری 🔥", "«Praise the Sun!» 🌞"], ["Dark Souls? You have earned your patience 🔥", "“Praise the Sun!” 🌞"]),
        (["stardew"], ["استاردیو ولی؟ مزرعه‌ات رو آب دادی؟ 🌾", "استاردیو؛ آرامش یه بازی 🐔"], ["Stardew Valley? Did you water the farm? 🌾", "Stardew; a game of calm 🐔"]),
        (["terraria"], ["تراریا؟ امیدوارم باس رو شکست داده باشی ⚔️", "تراریا؛ کندن و ساختن 🌳"], ["Terraria? Hope you beat the boss ⚔️", "Terraria; digging and building 🌳"]),
        (["rainbow six", "siege"], ["رینبو سیکس؟ دیوارها رو هم بشکن 🧱", "سیج؛ دقت و آرامش 🎯"], ["Rainbow Six? Break the walls too 🧱", "Siege; precision and calm 🎯"]),
    ];
}
