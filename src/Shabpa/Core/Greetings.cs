namespace Shabpa.Core;

/// <summary>The line at the top of the home page. One is picked at random each time the app starts (and again when
/// the part of the day changes), so it is not the same plain "good morning" every day.</summary>
public static class Greetings
{
    public enum Part { Dawn, Morning, Noon, Afternoon, Evening, Night, Late }

    private static readonly Random Dice = new();
    private static (Part part, int index)? _chosen;

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

    /// <summary>Same line for the whole session while the part of the day stays the same.</summary>
    public static string Pick(DateTime now)
    {
        var part = PartOf(now);
        var lines = L.En ? English[part] : Persian[part];
        if (_chosen is not { } c || c.part != part) _chosen = c = (part, Dice.Next(Persian[part].Length));
        return (L.En ? English[part] : Persian[part])[c.index % lines.Length];
    }

    /// <summary>For tests and the self-screenshots: start over so the next Pick is a fresh draw.</summary>
    public static void Reset() => _chosen = null;

    public static readonly IReadOnlyDictionary<Part, string[]> Persian = new Dictionary<Part, string[]>
    {
        [Part.Dawn] =
        [
            "سحرخیز شدی! صبحت بخیر ☀️", "صبح زود بخیر، قهرمان! 🌅", "هنوز خورشید خواب‌آلوده‌ست، تو بیداری! 🐓",
            "سلام سحرخیز! چایی دم کردی؟ ☕", "صبحِ زود و آرومت بخیر 🌄", "جوجه‌ها هم هنوز بیدار نشدن، تو بیداری! 🐣",
            "سپیده زده، روزت قشنگ شروع شد ✨", "صبح بخیر؛ دنیا مال سحرخیزهاست 🌍",
        ],
        [Part.Morning] =
        [
            "صبحت بخیر! ☀️", "صبح عالیِ پرتقالی! 🍊", "صبح بخیر؛ امروز قراره روز خوبی باشه 🌼", "سلام! یه صبح خوشمزه‌ی دیگه 🥐",
            "صبح بخیر، یه لیوان چایی و شروع کنیم ☕", "صبحت به‌رنگ نارنجی خورشید 🌞", "روز بخیر! آماده‌ی ماجراجویی‌ای؟ 🚀",
            "صبح قشنگت بخیر، انرژی‌ت رو شارژ کن 🔋", "سلام سلام! صبح شد و دنیا منتظرته 🌈", "صبح بخیر؛ لبخندت رو از یادت نبر 😊",
            "یه صبح تازه، یه شروع تازه 🌱", "صبح بخیر، امروز مال توئه 💪", "نسیم صبح بخیر! 🍃", "صبح شیرین و پر از انرژی 🍯",
        ],
        [Part.Noon] =
        [
            "ظهرت بخیر! ناهار یادت نره 🍽️", "ظهر بخیر؛ وقت یه استراحت کوچیکه 😌", "نصف روز گذشت، دمت گرم! 🌤️",
            "ظهر بخیر، یه چیزی بخور، باتری‌ت کمه 🍛", "ظهر شد! خورشید همین بالاست ☀️", "وقت ناهاره، آهنگ مورد علاقه‌ت رو بذار 🎧",
            "ظهر بخیر، کمی آب بنوش 💧", "نیمه‌ی روز بخیر! 🌞",
        ],
        [Part.Afternoon] =
        [
            "بعدازظهرت بخیر! 🌤️", "عصر نزدیکه، یه چایی دیگه؟ ☕", "بعدازظهر بخیر؛ یه کش‌وقوس به بدنت بده 🤸", "سلام! نصف بیشتر روز رو رد کردی 👏",
            "بعدازظهر آروم و دل‌نشین 🍃", "ساعت چایی شده! 🫖", "یه استراحت کوتاه بد نیست، چشمات رو هم بده به خدا 👀",
            "بعدازظهر بخیر، قلب‌آفتابی! 🌻",
        ],
        [Part.Evening] =
        [
            "عصرت بخیر! 🌇", "عصر بخیر؛ خورشید داره خداحافظی می‌کنه 🌆", "عصر قشنگ و آروم 🌙", "عصر بخیر، بریم سراغ بازی‌ها؟ 🎮",
            "عصر بخیر؛ وقت تماشای غروبه 🌅", "سلام! امیدوارم روز خوبی داشتی 🧡", "عصر بخیر، یه چایی داغ می‌چسبه ☕",
            "غروب بخیر، دنیا طلایی شده ✨",
        ],
        [Part.Night] =
        [
            "شبت بخیر! 🌙", "شب بخیر؛ ستاره‌ها بیدارن ⭐", "شب‌بخیر، شبِ آروم و قشنگی داشته باشی 🌃", "سلام شب‌زنده‌دار! 🦉",
            "شب بخیر؛ یه فیلم خوب بد نیست 🍿", "شب شد؛ وقت استراحت مغز و چشم 😴", "شب بخیر، ماه امشب خیلی خوشگله 🌕",
            "شب به‌خیر، یه چای دارچینی بخور 🍵", "شب بخیر! به فکر اخراج خستگی باش 🛋️",
        ],
        [Part.Late] =
        [
            "هنوز بیداری؟! 🦉 آفرین به این شب‌زنده‌داری", "نصف‌شبه؛ بدنت می‌گه بخواب 😴", "ساعت خواب گذشته، فردا رو هم دوست داشته باش 🌙",
            "نیمه‌شب بخیر! یه لیوان آب بخور و بخواب 💧", "دیروقته؛ شب‌زنده‌داری مبارک، ولی چشمات رو هم دریاب 👀", "سلام جغد شبانه! 🦉✨",
            "این وقت شب فقط شب‌پا و تو بیدارید 🌌", "خسته‌ای؟ شب‌پا می‌تونه کامپیوتر رو برات خاموش کنه 😉",
        ],
    };

    public static readonly IReadOnlyDictionary<Part, string[]> English = new Dictionary<Part, string[]>
    {
        [Part.Dawn] =
        [
            "Up with the sun! Good morning ☀️", "Early bird! 🐓", "The sun is still yawning and you are up 🌅", "Fresh tea and a quiet dawn ☕",
            "Good morning, early riser 🌄", "Even the chickens are asleep 🐣", "The sky is turning gold ✨", "The world belongs to early risers 🌍",
        ],
        [Part.Morning] =
        [
            "Good morning! ☀️", "Have an orange-bright morning! 🍊", "Good morning, today will be a good one 🌼", "Hello! Another tasty morning 🥐",
            "Tea first, then the world ☕", "Morning sunshine to you 🌞", "Ready for an adventure? 🚀", "Charge your batteries, it is morning 🔋",
            "Rise and shine! 🌈", "Don't forget to smile 😊", "A fresh morning, a fresh start 🌱", "Today is yours 💪", "Good morning breeze 🍃", "Sweet and energetic morning 🍯",
        ],
        [Part.Noon] =
        [
            "Good noon! Don't skip lunch 🍽️", "Midday break time 😌", "Half the day is done, well played! 🌤️", "Eat something, your battery is low 🍛",
            "The sun is right above you ☀️", "Lunch time, put on your favourite song 🎧", "Drink some water 💧", "Happy midday! 🌞",
        ],
        [Part.Afternoon] =
        [
            "Good afternoon! 🌤️", "Evening is near, another tea? ☕", "Stretch a little 🤸", "Most of the day is behind you 👏",
            "A calm, cosy afternoon 🍃", "Teatime! 🫖", "Rest your eyes for a minute 👀", "Sunny afternoon to you 🌻",
        ],
        [Part.Evening] =
        [
            "Good evening! 🌇", "The sun is saying goodbye 🌆", "A calm evening 🌙", "Shall we play something? 🎮",
            "Sunset time 🌅", "Hope the day was kind to you 🧡", "A hot tea would be lovely ☕", "Golden hour ✨",
        ],
        [Part.Night] =
        [
            "Good night! 🌙", "The stars are awake ⭐", "Have a calm, lovely night 🌃", "Hello night owl! 🦉",
            "A good movie would fit 🍿", "Time to rest your eyes 😴", "The moon is beautiful tonight 🌕", "Cinnamon tea is calling 🍵", "Let tiredness go 🛋️",
        ],
        [Part.Late] =
        [
            "Still awake?! 🦉", "It's the middle of the night; your body says sleep 😴", "Past bedtime — tomorrow will love you too 🌙",
            "A glass of water and then bed 💧", "Late night salute — but look after your eyes 👀", "Hello night owl! 🦉✨",
            "Only Shabpa and you are awake 🌌", "Tired? Shabpa can switch the PC off for you 😉",
        ],
    };
}
