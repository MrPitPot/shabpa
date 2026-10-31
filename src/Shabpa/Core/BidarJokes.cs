namespace Shabpa.Core;

/// <summary>What Bidar says when asked for a joke, a fact, a tip or a pep talk, and how he decides the language of a chat.
/// Every list has its own Persian and English lines (not translations of each other), and a "bag" hands them out in a
/// shuffled order, so nothing repeats until the whole list has been told.</summary>
public static class BidarJokes
{
    private static readonly Random Rng = new();
    private static readonly Dictionary<string, Queue<int>> Bags = [];
    private static readonly Dictionary<string, int> Last = [];

    /// <summary>The next line of a list; never the same one twice in a row, and every line once per round.</summary>
    public static string Pick(string name, IReadOnlyList<string> lines)
    {
        if (lines.Count == 1) return lines[0];
        lock (Bags)
        {
            if (!Bags.TryGetValue(name, out var bag) || bag.Count == 0)
            {
                var order = Enumerable.Range(0, lines.Count).OrderBy(_ => Rng.Next()).ToList();
                // a new round never opens with the line the last one ended on
                if (Last.TryGetValue(name, out var last) && order[0] == last) (order[0], order[^1]) = (order[^1], order[0]);
                Bags[name] = bag = new Queue<int>(order);
            }
            var i = bag.Dequeue();
            Last[name] = i;
            return lines[i];
        }
    }

    public static string Joke(bool en) => en ? Pick("joke-en", JokesEn) : Pick("joke-fa", JokesFa);
    public static string Fact(bool en) => en ? Pick("fact-en", FactsEn) : Pick("fact-fa", FactsFa);
    public static string Tip(bool en) => en ? Pick("tip-en", TipsEn) : Pick("tip-fa", TipsFa);
    public static string Pep(bool en) => en ? Pick("pep-en", PepEn) : Pick("pep-fa", PepFa);
    public static string Hoot(bool en) => en ? Pick("hoot-en", HootEn) : Pick("hoot-fa", HootFa);

    // ---- which language is the person writing in? ------------------------------------------------------------------

    private static readonly HashSet<string> Finglish = new(StringComparer.OrdinalIgnoreCase)
    {
        "salam", "slm", "khoobi", "khobi", "chetori", "merci", "mamnoon", "mamnun", "mersi", "khodahafez", "shab", "bekhair", "bekheir", "jok", "joke-e", "mikham", "bokon", "khamoosh", "khamosh", "baz", "kon",
    };

    /// <summary>True for English, false for Persian, null when there is nothing to go by (digits only): then the app's own language is used.
    /// The majority of the letters decides; Persian words written in Latin letters («salam», «khamosh kon») count as Persian.</summary>
    public static bool? IsEnglish(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int persian = 0, latin = 0;
        foreach (var ch in text)
        {
            if (ch is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿') persian++;
            else if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
        }
        if (persian == 0 && latin == 0) return null;
        if (persian == 0)
        {
            var words = text.Split([' ', '\t', ',', '.', '!', '?', '-'], StringSplitOptions.RemoveEmptyEntries);
            var fin = words.Count(w => Finglish.Contains(w));
            if (fin > 0 && fin * 2 >= words.Length) return false;
        }
        return latin > persian;
    }

    // ---- jokes ------------------------------------------------------------------------------------------------------

    public static readonly string[] JokesFa =
    [
        "ساعت ۳ شبه و تو داری «فقط یه قسمت دیگه» می‌بینی. من جغدم و بیداری کارمه؛ بهونه‌ی تو چیه؟ 🦉",
        "پینگم ۹۹۹ بود؛ بازی گفت: «بیا یه استراحت کوتاه کنیم.» 🎮",
        "خاموش کردن کامپیوتر دو مدل داره: یا من خاموشش می‌کنم، یا تو می‌گی «فقط یه دقیقه» و ساعت می‌شه ۵ صبح.",
        "دانلود ۹۹٪ مونده و ۴۰ دقیقه‌ست همون‌جاست. این دانلود نیست، یه رابطه‌ی عاطفیه. 💔",
        "ویندوز نوشت ۳۰٪. گفتم تا ۱۰۰ می‌رم یه چایی بخورم. برگشتم: ۳۱٪. چایی هم سرد شده بود. ☕",
        "ریستارت درمان همه‌چیزه. کاش برای بعضی دوشنبه‌ها هم داشتیم.",
        "کاربر: کامپیوترم چرا کنده؟ من: ۴۷ تا تب باز داری. کاربر: ولی همه‌شون مهمن! من: مثل ۴۷ تا برنامه‌ی «فردا می‌خونم». 🙃",
        "بهترین زمان پشتیبان‌گیری دیروز بود. دومین بهترینش همین الان.",
        "سی‌پی‌یو به رم گفت: تو چقدر زود فراموش می‌کنی! رم گفت: چی؟ 🧠",
        "اینترنت قطع شد و اهل خونه برای اولین بار با هم حرف زدن. تجربه‌ی عجیبی بود؛ پیشنهاد نمی‌کنم. 😅",
        "چرا برنامه‌نویس‌ها هالووین رو با کریسمس قاطی می‌کنن؟ چون Oct 31 == Dec 25. 🎃",
        "دکمه‌ی «بعداً یادآوری کن» تو ویندوز اسم دیگه‌ی «هیچ‌وقت»ـه.",
        "فایل پروژه: final.docx. بعدش: final_v2.docx. بعدش: final_v2_واقعی.docx. بعدش: final_v2_واقعی_این‌دیگه_آخریه.docx.",
        "من جغدم و با کامپیوتر دوستم؛ هر دو شب‌ها بهتر کار می‌کنیم و صبح‌ها هنگ می‌کنیم. ☀️",
        "به کامپیوتر گفتم یه جوک بگو. گفت: ارور ۴۰۴، شوخ‌طبعی پیدا نشد. پس خودم گفتم.",
        "«فقط ۵ دقیقه» تنها واحد زمانیه که معمولاً ۳ ساعت طول می‌کشه.",
        "موس بی‌سیم باتری‌ش رو درست وسط یه کار مهم تموم می‌کنه. این اتفاق نیست، هنره.",
        "سخت‌ترین کار دنیا: پیدا کردن اون فایلی که «همین‌جا گذاشتمش».",
        "مصرف رم: ۱۴ گیگ. برنامه‌ی در حال اجرا: مرورگر. همه‌چیز ظاهراً طبیعیه. 🌐",
        "تو اتاق تاریک فقط دو چیز روشنه: مانیتور و آرزوهای تو برای زودتر خوابیدن. 🌙",
        "پسورد قوی یعنی چیزی که فقط وقتی لازمش نداری یادته.",
        "گوشی ۲۰٪ باتری داره، ولی تا شارژر نباشه ۱٪ می‌شه. باتری‌ها بازیگرای ماهری‌ان. 🎭",
        "دمای پردازنده بالا رفته بود، گفتم آروم باش! گفت: تو جای من نیستی؛ ۴۷ تا تب باز رو من می‌کشم.",
        "من کی‌ام؟ جغدی که ساعت ۴ صبح از تو می‌پرسه «خوابت نمی‌بره؟»... و جوابش رو می‌دونه.",
        "آپدیت‌نوت ویندوز: «بهبود عملکرد و رفع اشکال». ترجمه: یه چیزی عوض کردیم، دعا کنید. 🤞",
        "کیبورد گفت: چرا همیشه فشارم می‌دی؟ گفتم تو دکمه‌ای، کارت همینه. گفت: ولی احساساتم؟",
        "آدم‌ها دو دسته‌ان: اونایی که فایل‌هاشون رو بک‌آپ می‌گیرن، و اونایی که هنوز اون «یه روز» رو ندیدن.",
        "هاردی که صدای عجیب می‌ده داره سعی می‌کنه یه چیزی بهت بگه. معمولاً: «خداحافظ». 💾",
        "یه کلمه‌ی عبور ۱۶ حرفی ساختم و ۳ ثانیه بعد یادم رفت. خوشبختانه مرورگر یادش بود و منم یادم رفت بهش اعتماد کنم.",
        "ساعت زنگ‌دار بهم گفت صبح بخیر. بهش گفتم اشتباه می‌کنی؛ اگه بخیر بود، من خواب بودم. ⏰",
    ];

    public static readonly string[] JokesEn =
    [
        "Why do programmers prefer dark mode? Because light attracts bugs. 🐛",
        "There are 10 kinds of people: those who understand binary and those who do not. 🦉",
        "Why did the PC go to sleep? It had too many tabs open and needed a break. 😴",
        "\"Have you tried turning it off and on again?\" The entire IT profession in one sentence.",
        "Why was the computer cold? It left its Windows open. 🪟",
        "A SQL query walks into a bar, sees two tables and asks: \"Can I join you?\"",
        "final.docx. final_v2.docx. final_v2_REAL.docx. final_v2_REAL_this_is_the_last_one.docx. We have all been there.",
        "Why do programmers mix up Halloween and Christmas? Because Oct 31 == Dec 25. 🎃",
        "A download stuck at 99% for 40 minutes is not a download, it is a relationship. 💔",
        "\"Just five more minutes\" are the last words before 3 a.m.",
        "The best time to back up was yesterday. The second best is right now.",
        "I am an owl: my sleep schedule is optional and my uptime is excellent. 🦉",
        "404: sense of humour not found. So I wrote this one myself.",
        "A wireless mouse always runs out of battery in the middle of an important task. That is not bad luck, it is craftsmanship.",
        "RAM: \"I forgot.\" CPU: \"You always do.\" 🧠",
        "My laptop fan sounds like a jet engine: it is ready for take-off, my files are not.",
        "Windows update says 30%. I make tea. I come back: 31%. The tea is cold. ☕",
        "Why did the developer go broke? He used up all his cache. 💸",
        "\"Bug fixes and improvements\" is what a developer writes when \"we changed something, please pray\" does not fit in the box. 🤞",
        "My browser has a tab, a bigger tab and a mega tab. It is a sandwich of regrets.",
        "Light mode at 2 a.m. should be classified as a crime. 🌙",
        "How do you comfort a JavaScript bug? You console it.",
        "Programming is 10% writing code and 90% wondering why it works.",
        "There is no place like 127.0.0.1. 🏠",
        "How many programmers does it take to change a light bulb? None, that is a hardware problem. 💡",
        "RAM usage: 14 GB. Running program: a browser. Everything is perfectly normal. 🌐",
        "I asked my computer for a joke. It said: \"Error 404: humour not found.\" So here I am.",
        "A password strong enough to be secure is a password you only remember when you do not need it.",
        "Hard drives that make strange noises are trying to tell you something. Usually: \"goodbye.\" 💾",
        "My alarm clock said good morning. I told it that it was mistaken: if it were good, I would still be asleep. ⏰",
    ];

    // ---- facts (all of them true) -------------------------------------------------------------------------------------

    public static readonly string[] FactsFa =
    [
        "جغدها می‌تونن سرشون رو تا حدود ۲۷۰ درجه بچرخونن. دلیلش اینه که چشم‌هاشون توی کاسه ثابته و نمی‌تونن بچرخونن‌شون. 🦉",
        "به یه گروه جغد می‌گن «پارلمان جغدها». حدس بزن چرا؟ چون همه نظر دارن و کسی عجله نداره. 🦉",
        "لبه‌ی پرهای جغد دندونه‌دندونه‌ست و همین باعث می‌شه تقریباً بی‌صدا پرواز کنه.",
        "کلمه‌ی «باگ» مال سال ۱۹۴۷ه: یه پروانه‌ی شب (موت) تو رله‌ی کامپیوتر هاروارد مارک ۲ گیر کرد و تیم اون رو توی دفترچه چسبوند. 🦋",
        "اولین وب‌کم دنیا (۱۹۹۱) تو دانشگاه کمبریج بود و فقط یه قهوه‌ساز رو نشون می‌داد تا کسی الکی تا آشپزخونه نره.",
        "وقتی به صفحه نگاه می‌کنی خیلی کمتر پلک می‌زنی؛ برای همین بعد از چند ساعت چشمات خشک می‌شه. 👀",
        "«وای‌فای» مخفف هیچ عبارتی نیست؛ فقط یه اسم تجاریه که انتخاب شده تا قشنگ و کوتاه باشه.",
        "ایمیل از وب قدیمی‌تره: اولین پیام‌هاش حدود سال ۱۹۷۱ فرستاده شد.",
        "یه پردازنده‌ی امروزی تو یک ثانیه میلیاردها دستور اجرا می‌کنه. و بازم گاهی منتظر مرورگر می‌مونه. 😄",
        "خواب بزرگ‌سال‌ها معمولاً به ۷ تا ۹ ساعت نیاز داره؛ «کم‌خوابی عادت می‌شه» بیشتر یه افسانه‌ست.",
    ];

    public static readonly string[] FactsEn =
    [
        "Owls can turn their heads about 270 degrees, because their eyes are fixed in their sockets and cannot move. 🦉",
        "A group of owls is called a parliament. Everyone has an opinion and nobody is in a hurry. 🦉",
        "The edges of an owl's feathers are fringed, which is why it flies almost silently.",
        "The word \"bug\" is from 1947: a moth got stuck in a relay of the Harvard Mark II and the team taped it into the log book. 🦋",
        "The first webcam (1991, Cambridge) pointed at a coffee pot so nobody walked to the kitchen for nothing. ☕",
        "You blink much less when you look at a screen, which is why your eyes dry out after a few hours. 👀",
        "\"Wi-Fi\" does not stand for anything; it is just a brand name picked to sound short and friendly.",
        "E-mail is older than the web: the first messages were sent around 1971.",
        "A modern processor runs billions of instructions every second, and still sometimes waits for a browser. 😄",
        "Most adults need 7 to 9 hours of sleep. \"You get used to less\" is mostly a myth.",
    ];

    public static readonly string[] TipsFa =
    [
        "قانون ۲۰-۲۰-۲۰: هر ۲۰ دقیقه، ۲۰ ثانیه به یه چیز دور (حدود ۶ متر اون‌طرف‌تر) نگاه کن. چشمات ممنون می‌شن. 👀",
        "شب‌ها «نور شب» رو روشن کن تا نور آبی صفحه خوابت رو کمتر به هم بزنه. بنویس «نور شب روشن».",
        "قبل از اینکه سرگرم بشی، بگو «یه ساعت دیگه خاموش کن». بعد با خیال راحت ادامه بده؛ قبلش شمارش معکوس می‌بینی و می‌تونی لغوش کنی.",
        "کار سنگینی داری؟ بنویس «۲۵ دقیقه تمرکز» و تایمر تمرکز رو شروع کن؛ بعدش ۵ دقیقه استراحت.",
        "اگه سیستم کند شده، «رم رو خالی کن» رو امتحان کن؛ چیزی بسته نمی‌شه.",
        "یه پشتیبان که هیچ‌وقت نگرفتی، یه پشتیبان نیست. از صفحه‌ی «فایل و پشتیبان» یکی تنظیم کن.",
        "اگه گیم می‌کنی و پینگت بالاست، صفحه‌ی «پایداری پینگ» نشون می‌ده مشکل از کجاست.",
        "آب بخور. جدی می‌گم؛ جغدها هم بالاخره آب می‌خورن. 💧",
    ];

    public static readonly string[] TipsEn =
    [
        "The 20-20-20 rule: every 20 minutes look at something far away (about 6 metres) for 20 seconds. Your eyes will thank you. 👀",
        "Turn night light on in the evening so the blue light of the screen disturbs your sleep less. Type \"night light on\".",
        "Before you get absorbed, say \"shut down in an hour\". Then carry on; you will see a countdown and can cancel it.",
        "Heavy work ahead? Type \"focus 25 minutes\", then take a 5-minute break.",
        "If the PC feels slow, try \"free ram\"; nothing is closed.",
        "A backup you never made is not a backup. Set one up on the Files & backup page.",
        "If you game and the ping is high, the Ping stability page shows where the problem is.",
        "Drink some water. I mean it; even owls do. 💧",
    ];

    public static readonly string[] PepFa =
    [
        "تا الان اومدی تا اینجا؛ این یعنی از اونی که فکر می‌کنی قوی‌تری. 💪",
        "کار بزرگ یعنی کلی کار کوچیک پشت سر هم. اولیش رو شروع کن؛ من کنارتم. 🦉",
        "اگه امروز خوب پیش نرفت، فردا یه روز تازه‌ست. من تا اون موقع بیدارم.",
        "یه نفس عمیق بکش، شونه‌هات رو ول کن. حالا ادامه بده. تو از پسش برمیای.",
        "قشنگ‌ترین کار دنیا اینه که کاری رو که پشت گوش انداختی، تموم کنی. یه قدم کوچیک بردار.",
        "استراحت هم جزئی از کاره. اگه خسته‌ای، اجازه داری بخوابی. 🌙",
    ];

    public static readonly string[] PepEn =
    [
        "You have come this far; that means you are stronger than you think. 💪",
        "Big work is just many small tasks in a row. Start the first one; I am right here. 🦉",
        "If today did not go well, tomorrow is a fresh day. I will be awake until then.",
        "Take a deep breath and drop your shoulders. Now carry on. You can do this.",
        "The best feeling in the world is finishing what you kept putting off. Take one small step.",
        "Rest is part of the work. If you are tired, you are allowed to sleep. 🌙",
    ];

    public static readonly string[] HootFa = ["هو هو! 🦉", "هووو... 🌙", "هو؟ هو! (یعنی: «چی شده؟») 🦉", "هو هو هو! یعنی «شب بخیر» به زبون جغدی."];
    public static readonly string[] HootEn = ["Hoo hoo! 🦉", "Hoooo... 🌙", "Hoo? Hoo! (that means \"what is it?\") 🦉", "Hoo hoo hoo! That is \"good night\" in owl."];
}
