using System.Globalization;

namespace Shabpa.Core;

/// <summary>The birthday wishes: a set of short, warm texts in Persian and English (some use the name, some do not). The one shown
/// is picked from the date, so it is the same every time the greeting opens that day and different on other days. Nothing here
/// says anything about age: the birth year is optional and a wish must never assume it.</summary>
public static class BirthdayWishes
{
    /// <summary>One wish in both languages. <c>{name}</c> is replaced by the person's name; wishes with it are used only when a name is saved.</summary>
    public sealed record Wish(string Fa, string En)
    {
        public bool UsesName => Fa.Contains("{name}") || En.Contains("{name}");
    }

    public static readonly IReadOnlyList<Wish> All =
    [
        // without a name
        new("امیدوارم سال تازه‌ات پر از خبرهای خوب، سلامتی و لحظه‌های شاد باشه. شب‌پا هم خوشحاله که امروز کنارته 🎈",
            "Wishing you a year full of good news, good health and good company. Shabpa is happy to be on your PC today 🎈"),
        new("امروز روز توئه؛ یه چایی خوب، یه آهنگ قشنگ و هرچی دلت می‌خواد 🎶",
            "Today is your day: a good cup of tea, a lovely song and whatever you feel like 🎶"),
        new("یه سال دیگه، یه عالمه اتفاق خوب. امیدوارم همه‌شون به دلت بشینه ✨",
            "Another year, plenty of good things ahead. May they all feel just right ✨"),
        new("شب‌پا می‌گه: امروز کار رو بذار کنار و یه کم از خودت لذت ببر 🍰",
            "Shabpa says: put work aside today and enjoy yourself a little 🍰"),
        new("روزت پر از لبخند، شبت پر از آرامش 🌟",
            "May your day be full of smiles and your night full of calm 🌟"),
        new("کیک رو فراموش نکن! شمع‌ها رو ولی من نمی‌تونم فوت کنم 🕯️",
            "Don't forget the cake! I can't blow out the candles for you, though 🕯️"),
        new("آرزو کن؛ شب‌پا قول می‌ده امروز مزاحمت نشه 🌠",
            "Make a wish; Shabpa promises not to bother you today 🌠"),
        new("بهترین‌ها رو برات آرزو می‌کنم، امروز و همهٔ روزهای بعدش 💐",
            "Wishing you the very best, today and every day after 💐"),
        // with the name
        new("{name} عزیز، تولدت مبارک! امیدوارم امروز پر از شادی باشه 🎉",
            "Dear {name}, happy birthday! May today be full of joy 🎉"),
        new("{name}، امروز روز توئه. هرچی دلت می‌خواد، همون! 🎁",
            "{name}, today is your day. Whatever you want, that's the plan! 🎁"),
        new("برای {name} یه سال پر از اتفاق‌های قشنگ آرزو می‌کنم ✨",
            "Wishing {name} a year full of lovely things ✨"),
        new("{name}، شب‌پا امروز بیشتر از همیشه خوشحاله که کنارته 🎈",
            "{name}, Shabpa is happier than ever to be on your PC today 🎈"),
        new("یه کیک بزرگ برای {name}! تولدت مبارک 🍰",
            "A big cake for {name}! Happy birthday 🍰"),
        new("{name} جان، امیدوارم امروز فقط خبرهای خوب بشنوی 💌",
            "{name}, I hope you hear nothing but good news today 💌"),
        new("سلامتی و خوشی برای تو، {name}. امروز حسابی بهت خوش بگذره 🌟",
            "Health and happiness to you, {name}. Have a wonderful day 🌟"),
        new("{name}، اگه امروز کسی کیک نیاورد، یه کیک مجازی از طرف من! 🎂",
            "{name}, if nobody brings cake today, here's a virtual one from me! 🎂"),
    ];

    /// <summary>A number that depends only on the text (string.GetHashCode differs between runs).</summary>
    internal static uint Seed(string key)
    {
        var h = 2166136261u;
        foreach (var ch in key) h = (h ^ ch) * 16777619u;
        return h;
    }

    /// <summary>The wish for this day. Same day, same wish; a wish that needs a name is skipped when there is none.</summary>
    public static Wish For(string? name, DateTime day)
    {
        var hasName = Birthday.CleanName(name).Length > 0;
        var start = (int)(Seed(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) % (uint)All.Count);
        for (var i = 0; i < All.Count; i++)
        {
            var wish = All[(start + i) % All.Count];
            if (hasName || !wish.UsesName) return wish;
        }
        return All[0];
    }

    /// <summary>The wish text in the chosen language with the name filled in.</summary>
    public static string Text(string? name, DateTime day, bool? english = null)
    {
        var clean = Birthday.CleanName(name);
        var wish = For(clean, day);
        return ((english ?? L.En) ? wish.En : wish.Fa).Replace("{name}", clean);
    }
}

/// <summary>Friendly lines that use the person's name (only when one is saved). Purely local text for the home page and the
/// on-screen notices; the name is never put into anything that leaves the PC (bot, support, logs).</summary>
public static class Friendly
{
    private static readonly Random Dice = new();
    private static (Greetings.Part part, int index)? _chosen;

    private static readonly IReadOnlyDictionary<Greetings.Part, (string Fa, string En)[]> Lines = new Dictionary<Greetings.Part, (string, string)[]>
    {
        [Greetings.Part.Dawn] = [("سحر بخیر {0}! ☀️", "Good early morning, {0}! ☀️"), ("{0}، چه سحرخیز! 🌄", "{0}, what an early bird! 🌄"), ("بیداری {0}؟ صبح زود بخیر ☕", "Up already, {0}? Early morning to you ☕")],
        [Greetings.Part.Morning] = [("صبح بخیر {0}! ☀️", "Good morning, {0}! ☀️"), ("سلام {0}، امروز روز خوبیه 🌼", "Hi {0}, today looks like a good one 🌼"), ("{0}، چایی‌ت رو خوردی؟ ☕", "{0}, had your tea yet? ☕")],
        [Greetings.Part.Noon] = [("ظهر بخیر {0}! ☀️", "Good noon, {0}! ☀️"), ("سلام {0}، وقت ناهاره 🍽️", "Hi {0}, lunch time 🍽️"), ("{0}، یه نفس تازه کن 🍃", "{0}, take a fresh breath 🍃")],
        [Greetings.Part.Afternoon] = [("بعدازظهر بخیر {0}! 🌻", "Good afternoon, {0}! 🌻"), ("سلام {0}، یه استراحت کوچیک چطوره؟ 👀", "Hi {0}, how about a small break? 👀"), ("{0}، بعدازظهرت خوش! 🍃", "{0}, enjoy your afternoon! 🍃")],
        [Greetings.Part.Evening] = [("عصر بخیر {0}! 🌇", "Good evening, {0}! 🌇"), ("سلام {0}، خسته نباشی 🌆", "Hi {0}, well done today 🌆"), ("{0}، غروب قشنگیه 🌅", "{0}, what a lovely sunset 🌅")],
        [Greetings.Part.Night] = [("شب بخیر {0}! 🌙", "Good night, {0}! 🌙"), ("سلام {0}، شبت خوش ✨", "Hi {0}, have a nice night ✨"), ("{0}، شب آرومیه 🍵", "{0}, a calm night 🍵")],
        [Greetings.Part.Late] = [("{0}، هنوز بیداری؟ 🌙", "{0}, still awake? 🌙"), ("سلام {0}، دیروقته‌ها 😴", "Hi {0}, it's getting late 😴"), ("{0}، شب‌پا هم کنارته 🌌", "{0}, Shabpa is here with you 🌌")],
    };

    public static int VariantCount => Lines[Greetings.Part.Morning].Length;

    /// <summary>The home-page greeting with the name; <paramref name="variant"/> picks one of the lines of that part of the day.</summary>
    public static string Greeting(Greetings.Part part, string name, bool english, int variant)
    {
        var lines = Lines[part];
        var line = lines[Math.Abs(variant) % lines.Length];
        return string.Format(CultureInfo.InvariantCulture, english ? line.En : line.Fa, Birthday.CleanName(name));
    }

    /// <summary>Same line for the whole session while the part of the day stays the same (like <see cref="Greetings.Pick"/>).
    /// Null when there is no name: the caller then uses the usual lines.</summary>
    public static string? Greeting(DateTime now, string? name)
    {
        var clean = Birthday.CleanName(name);
        if (clean.Length == 0) return null;
        var part = Greetings.PartOf(now);
        if (_chosen is not { } c || c.part != part) _chosen = c = (part, Dice.Next(VariantCount));
        return Greeting(part, clean, L.En, c.index);
    }

    /// <summary>"Ali, all is calm" for the home page when nothing is scheduled. Null without a name.</summary>
    public static string? Calm(string? name, bool? english = null)
    {
        var clean = Birthday.CleanName(name);
        if (clean.Length == 0) return null;
        return (english ?? L.En) ? $"All calm, {clean}" : $"{clean}، همه‌چیز آرومه";
    }

    /// <summary>The title of a notice with the name when there is one (otherwise the plain title).</summary>
    public static string Notice(string? name, string plainFa, string plainEn, string namedFa, string namedEn, bool? english = null)
    {
        var clean = Birthday.CleanName(name);
        var en = english ?? L.En;
        if (clean.Length == 0) return en ? plainEn : plainFa;
        return (en ? namedEn : namedFa).Replace("{name}", clean);
    }
}
