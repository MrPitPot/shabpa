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
        "به کامپیوتر گفتم «بخواب.» گفت: «باشه، فقط اول ۱۴۷ تا به‌روزرسانی.» 😴",
        "آنتی‌ویروس: «هیچ تهدیدی پیدا نشد.» پوشه‌ی دانلودها: «۳۷۰ فایل به اسم “جدید (۳)”.» تهدید واقعی همین‌جاست.",
        "وای‌فای همیشه ضعیف‌ترین سیگنالش رو دقیقاً همون‌جایی می‌ذاره که تو نشستی. 📶",
        "ویندوز سه ساعته نوشته «در حال تمیزکاری...». من ظرفای دیروز رو هنوز نشستم و اون داره تمیزکاری می‌کنه.",
        "پسورد جدید ساختم. حالا سه تا پسورد دارم: اونی که یادمه، اونی که ذخیره شده، و اونی که «اشتباهه» ولی همیشه همونو تایپ می‌کنم. 🔑",
        "لپ‌تاپ رو گذاشتم روی پا: هم کار می‌کنم هم بخاری روشنه. بهره‌وری یعنی این. 🔥",
        "«ریستارت کن» تنها دستور فنی‌ایه که همه بلدن و هیچ‌کس نمی‌دونه چرا جواب می‌ده.",
        "باتری لپ‌تاپ ۱۰٪. من: «تا ۵ دقیقه‌ی دیگه کارم تمومه.» باتری: «من ۵ ثانیه‌ی دیگه تموم می‌شم.» 🔋",
        "اکسل نوشت #REF!. منم همین حس رو دارم، فقط کسی بهم خطا نمی‌ده.",
        "Ctrl+S تنها عادتیه که بعد از اولین باری که یه فایل رو از دست دادی یاد می‌گیری. 💾",
        "تو پوشه‌ی «مهم» هیچی مهم نیست. مهم‌ها توی پوشه‌ی «جدید»ن.",
        "بلوتوث: «در حال جستجو...» برای همیشه. درست مثل من توی سوپرمارکت.",
        "سرعت اینترنت ۱۰۰ مگابیت، سرعت دانلود ۲۰۰ کیلوبایت. ریاضیات اینترنت قانون‌های خودش رو داره. 🧮",
        "ساعت ۲ شب: «فقط یه نگاه به ایمیل.» ساعت ۴ صبح: سه تا مقاله‌ی ویکی‌پدیا، دو تا ویدیوی ماهی‌گیری و یه جغد که می‌پرسه «خب؟» 🦉",
        "تب‌های مرورگرم مثل کتابخونه‌ست: همه‌ی کتاب‌ها رو باز کردم و هیچ‌کدوم رو نخوندم.",
        "هدفون سیمی: توی کیف گره می‌خوره. هدفون بی‌سیم: توی کیف گم می‌شه. 🎧",
        "رمز رو یادداشت کردم و گذاشتم «جای امن». حالا دارم دنبال جای امن می‌گردم.",
        "نسخه‌ی پشتیبانِ فایل مهم همیشه دقیقاً یه نسخه قدیمی‌تر از چیزیه که لازم داری.",
        "کامپیوتر من دو سرعت داره: وقتی عجله ندارم فوری، وقتی عجله دارم یخ‌زده.",
        "بازی گفت «اتصال شما ناپایداره.» گفتم: «منم همین‌طور.» 🎮",
        "ویندوز: «برای به‌روزرسانی آماده‌ایم.» من: «من نیستم.» ویندوز: «نپرسیده بودیم.»",
        "کارت گرافیک داغ شد و پنکه‌ها شروع کردن به خوندن. کنسرتش رایگانه، ولی گوش‌هات هزینه‌ش رو می‌دن. 🔊",
        "یکی از رازهای بزرگ دنیا: چرا پرینتر دقیقاً وقتی لازمش داری خرابه و وقتی نیازی بهش نداری کار می‌کنه؟ 🖨️",
        "همه می‌گن «دو دقیقه‌ی دیگه آنلاینم». توی ساعت من، دو دقیقه یعنی ۴۵ دقیقه.",
        "«دانلود کامل شد.» فایل: ۰ بایت. 🙃",
        "گوشی ۱۲٪. شارژر خونه‌ی دوستمه. دوستم خونه‌ی خودشه. من وسط راهم.",
        "جدول ضرب یادم رفته ولی ۳۰ تا میان‌بر کیبورد حفظم. اولویت‌ها.",
        "از ربات خواستم کارهام رو انجام بده. ربات گرفتم که کارهام رو یادم می‌ندازه. من هنوز انجام نمی‌دم. 🤖",
        "جغد ساعت ۴ صبح: «هنوز بیداری؟» تو: «تو چی؟» جغد: «من جغدم. بهونه‌ی تو چیه؟» 🦉",
        "دیباگ کردن یعنی کارآگاه یه فیلم جنایی باشی که قاتلش هم خودتی. 🔍",
        "آخرین نسخه‌ی پروژه‌م اسمش «نهایی_نهایی_درست_اصلی» بود. نسخه‌ی بعدی رو دیگه اسم نذاشتم، ترسیدم.",
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
        "I told my PC to sleep. It said: \"Sure, after 147 updates.\" 😴",
        "Antivirus: \"No threats found.\" Downloads folder: 370 files named \"New (3)\". The real threat is there.",
        "Wi-Fi always puts its weakest signal exactly where you sit. 📶",
        "Windows has said \"Cleaning up...\" for three hours. I have not even washed yesterday's dishes.",
        "I made a new password. Now I have three: the one I remember, the one that is saved, and the \"wrong\" one I always type. 🔑",
        "Laptop on my lap: I am working and heating at the same time. That is what productivity means. 🔥",
        "\"Restart it\" is the only tech command everyone knows and nobody can explain.",
        "Laptop battery: 10%. Me: \"Five more minutes of work.\" Battery: \"I have five seconds.\" 🔋",
        "Excel said #REF!. Same, Excel. Same.",
        "Ctrl+S is the only habit you learn after losing a file once. 💾",
        "The folder called \"Important\" contains nothing important. The important stuff is in \"New folder\".",
        "Bluetooth: \"Searching...\" Forever. Like me in a supermarket.",
        "Internet speed: 100 Mbps. Download speed: 200 KB/s. Internet maths has its own rules. 🧮",
        "2 a.m.: \"just a quick look at my mail.\" 4 a.m.: three wiki pages, two fishing videos and an owl asking \"well?\" 🦉",
        "My browser tabs are a library: I opened every book and read none.",
        "Wired headphones: tangled in the bag. Wireless headphones: lost in the bag. 🎧",
        "I wrote the password on a note and put it in a \"safe place\". Now I am looking for the safe place.",
        "The backup of an important file is always exactly one version older than the one you need.",
        "My computer has two speeds: instant when I am not in a hurry, frozen when I am.",
        "The game said \"Your connection is unstable.\" I said: \"Same.\" 🎮",
        "Windows: \"We are ready to update.\" Me: \"I am not.\" Windows: \"We were not asking.\"",
        "The GPU heated up and the fans started singing. The concert is free but your ears pay for it. 🔊",
        "One of life's great mysteries: why does the printer fail exactly when you need it and work exactly when you do not? 🖨️",
        "Everyone says \"online in two minutes\". On my clock, two minutes means 45.",
        "\"Download complete.\" The file: 0 bytes. 🙃",
        "Phone: 12%. Charger: at my friend's house. Friend: at their house. Me: in the middle.",
        "I forgot the multiplication table but I remember 30 keyboard shortcuts. Priorities.",
        "I asked for a robot to do my chores. I got one that reminds me of my chores. I still do not do them. 🤖",
        "Owl at 4 a.m.: \"Still awake?\" You: \"And you?\" Owl: \"I am an owl. What is your excuse?\" 🦉",
        "Debugging is being the detective in a crime movie where you are also the murderer. 🔍",
        "My latest project file is called final_final_real_this_one. I did not name the next version; I was afraid.",
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
        "جغدها بعد از خوردن شکار، چیزهای هضم‌نشدنی مثل استخوون و پر رو به شکل یه گلوله‌ی کوچیک بالا میارن. دانشمندها همین گلوله‌ها رو باز می‌کنن تا بفهمن جغد چی خورده. 🦉",
        "جغدها سه تا پلک دارن: یکی بالا، یکی پایین و یه پلک نازک که از کنار چشم رد می‌شه و چشم رو تمیز و مرطوب نگه می‌داره.",
        "جغدها تقریباً همه‌جای دنیا زندگی می‌کنن؛ فقط تو قطب جنوب نیستن. 🌍",
        "جغد انباری (Barn owl) تو تاریکی کامل هم می‌تونه فقط با گوش‌دادن به صدای موش جاش رو پیدا کنه و بگیرتش.",
        "کوچک‌ترین جغد دنیا، جغد جن (Elf owl)، فقط حدود ۳۰ تا ۴۰ گرم وزن داره؛ یعنی از یه گوشی کوچیک هم سبک‌تره.",
        "بعضی جغدهای بزرگ، مثل جغد شاخ‌دار اوراسیایی، دو بال بازشون به حدود ۱٫۸ متر می‌رسه.",
        "گوش‌های بعضی جغدها یکی بالاتر از اون یکی قرار گرفته؛ همین باعث می‌شه بفهمن صدا از بالاست یا پایین.",
        "جغد انگشت بیرونی پاش رو می‌تونه به عقب بچرخونه و شکار رو با دو انگشت جلو و دو انگشت عقب محکم بگیره.",
        "جغدها ۱۴ تا مهره‌ی گردن دارن، دو برابر آدم؛ برای همین سرشون رو این‌قدر می‌چرخونن. 🦉",
        "صورت جغد مثل یه دیش بشقابیه: پرهای دور صورتش صدا رو جمع می‌کنن و به سمت گوش‌هاش می‌فرستن.",
        "اولین پیامک (SMS) دنیا ۳ دسامبر ۱۹۹۲ فرستاده شد و متنش «Merry Christmas» بود. 📱",
        "اولین موشواره‌ی کامپیوتر رو داگلاس انگلبارت ساخت و بدنه‌اش چوبی بود.",
        "اولین هارد دیسک تجاری، آی‌بی‌ام ۳۵۰ (سال ۱۹۵۶)، بزرگ‌تر از یه یخچال بود و کمتر از ۵ مگابایت جا داشت. 💾",
        "اولین دامنه‌ی اینترنتی ثبت‌شده‌ی دنیا symbolics.com بود، در سال ۱۹۸۵.",
        "تیم برنرز-لی وب جهانی رو سال ۱۹۸۹ پیشنهاد داد و اولین سایت سال ۱۹۹۱ راه افتاد. 🌐",
        "علامت @ رو برای ایمیل ری تاملینسون انتخاب کرد، همون که اولین ایمیل بین دو کامپیوتر رو فرستاد.",
        "کامپیوتر ENIAC، یکی از اولین کامپیوترهای بزرگ، نزدیک ۳۰ تن وزن داشت.",
        "اولین تماس با گوشی موبایل سال ۱۹۷۳ انجام شد و گوشی موتورولای اون روز حدود یه کیلو وزن داشت. 📞",
        "ویندوز ۱.۰ نوامبر ۱۹۸۵ عرضه شد؛ یعنی ویندوز از ۴۰ سال بیشتر عمر داره.",
        "لینوس توروالدز لینوکس رو سال ۱۹۹۱ شروع کرد؛ اولش یه پروژه‌ی شخصی بود.",
        "اولین ویدیوی یوتیوب «Me at the zoo» نام داشت و حدود ۱۹ ثانیه بود. 🐘",
        "کلمه‌ی «پیکسل» یعنی «عنصر تصویر» و از ترکیب Picture و Element ساخته شده.",
        "کلمه‌ی «الگوریتم» از نام خوارزمی، دانشمند ایرانی، گرفته شده؛ کلمه‌ی «جبر» هم از کتاب اونه. 🇮🇷",
        "گوگل سال ۱۹۹۸ تأسیس شد.",
        "اولین ایموجی‌ها سال ۱۹۹۹ توسط شیگتاکا کوریتا تو ژاپن طراحی شدن. 🙂",
        "مغز فقط حدود ۲٪ وزن بدنه، ولی حدود ۲۰٪ انرژی بدن رو مصرف می‌کنه. 🧠",
        "قلب آدم روزی حدود صد هزار بار می‌زنه.",
        "آدم‌ها به‌طور معمول در دقیقه ۱۵ تا ۲۰ بار پلک می‌زنن، ولی پشت صفحه این عدد تقریباً نصف می‌شه. 👀",
        "کافئین بسته به آدم معمولاً بین ۳ تا ۷ ساعت طول می‌کشه تا نصفش از بدن بره؛ قهوه‌ی عصر ممکنه شب هم بیدارت نگه داره. ☕",
        "دمای مناسب اتاق خواب برای اکثر آدم‌ها بین ۱۶ تا ۱۹ درجه‌ست.",
        "آدم‌ها حدود یک‌سوم عمرشون رو می‌خوابن.",
        "نور روشن و نور آبی صفحه‌ها شب‌ها ترشح ملاتونین، هورمون خواب، رو کم می‌کنه. 🌙",
        "کم‌آبی حتی در حد کم می‌تونه تمرکز و حال آدم رو پایین بیاره؛ آب بخور. 💧",
        "موقع خواب مغز خاطره‌های روز رو مرتب و ذخیره می‌کنه؛ بی‌خوابی یادگیری رو سخت می‌کنه.",
        "بلند شدن و چند دقیقه راه رفتن بعد از یه ساعت نشستن پشت کامپیوتر، برای کمر و تمرکز خوبه.",
        "نور در یک ثانیه حدود ۷ بار و نیم دور زمین می‌چرخه. ⚡",
        "نور خورشید حدود ۸ دقیقه و ۲۰ ثانیه طول می‌کشه تا به زمین برسه. ☀️",
        "روز ناهید (ونوس) از سالش درازتره: یه دور چرخیدن به دور خودش حدود ۲۴۳ روز زمینی طول می‌کشه، ولی دور خورشید ۲۲۵ روز.",
        "یه روز مریخی حدود ۲۴ ساعت و ۳۷ دقیقه‌ست.",
        "ماه هر سال حدود ۳٫۸ سانتی‌متر از زمین دورتر می‌شه. 🌕",
        "اختاپوس سه تا قلب داره. 🐙",
        "زرافه هم مثل آدم فقط ۷ مهره‌ی گردن داره، ولی هر کدوم خیلی بلندترن.",
        "عسل تقریباً هیچ‌وقت خراب نمی‌شه؛ عسل‌های هزاران ساله هم گاهی هنوز خوردنی بودن. 🍯",
        "حدود ۷۰٪ سطح زمین رو آب پوشونده. 🌊",
        "یلدا شب انقلاب زمستونیه و تو نیم‌کره‌ی شمالی بلندترین شب سال؛ بعدش روزها کم‌کم بلندتر می‌شن.",
        "لحظه‌ی تحویل سال نو ایرانی دقیقاً لحظه‌ی اعتدال بهاری نجومیه.",
        "برج میلاد تهران ۴۳۵ متر ارتفاع داره و جزو بلندترین برج‌های مخابراتی دنیاست. 🏙️",
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
        "After eating, owls cough up the parts they cannot digest, such as bones and fur, as a small pellet. Scientists open these pellets to learn what the owl ate. 🦉",
        "Owls have three eyelids: one above, one below and a thin one that sweeps sideways to keep the eye clean and moist.",
        "Owls live on every continent except Antarctica. 🌍",
        "A barn owl can find and catch a mouse in total darkness by listening alone.",
        "The smallest owl, the elf owl, weighs only about 30 to 40 grams; that is lighter than a small phone.",
        "Some big owls, like the Eurasian eagle-owl, have a wingspan of about 1.8 metres.",
        "In some owls one ear sits higher than the other, which helps them tell if a sound is above or below them.",
        "An owl can turn its outer toe backwards and grip prey with two toes in front and two behind.",
        "Owls have 14 neck vertebrae, twice as many as a human, which is why they can turn their heads so far. 🦉",
        "An owl's face is like a satellite dish: the feathers around it collect sound and send it to the ears.",
        "The first SMS ever was sent on 3 December 1992 and said \"Merry Christmas\". 📱",
        "The first computer mouse was built by Douglas Engelbart and had a wooden shell.",
        "The first commercial hard disk, the IBM 350 (1956), was bigger than a fridge and held less than 5 megabytes. 💾",
        "The first registered internet domain name was symbolics.com, in 1985.",
        "Tim Berners-Lee proposed the World Wide Web in 1989 and the first website went live in 1991. 🌐",
        "Ray Tomlinson, who sent the first e-mail between two computers, picked the @ sign for e-mail addresses.",
        "The ENIAC, one of the first big computers, weighed nearly 30 tonnes.",
        "The first mobile phone call was made in 1973, and that Motorola phone weighed about a kilogram. 📞",
        "Windows 1.0 came out in November 1985, so Windows is more than 40 years old.",
        "Linus Torvalds started Linux in 1991 as a personal project.",
        "The first YouTube video, \"Me at the zoo\", is about 19 seconds long. 🐘",
        "\"Pixel\" means \"picture element\": a blend of Picture and Element.",
        "The word \"algorithm\" comes from the name of al-Khwarizmi, a Persian scholar; the word \"algebra\" comes from his book. 🇮🇷",
        "Google was founded in 1998.",
        "The first emoji were drawn in 1999 by Shigetaka Kurita in Japan. 🙂",
        "The brain is only about 2% of your body weight but uses about 20% of your energy. 🧠",
        "A human heart beats about a hundred thousand times a day.",
        "People normally blink 15 to 20 times a minute, but in front of a screen that roughly halves. 👀",
        "Depending on the person, caffeine takes about 3 to 7 hours to drop by half, so an evening coffee can still keep you awake. ☕",
        "For most people the best bedroom temperature is between 16 and 19 degrees Celsius.",
        "People spend about a third of their lives asleep.",
        "Bright light, and the blue light of screens, at night lowers melatonin, the sleep hormone. 🌙",
        "Even mild dehydration can lower your focus and mood; drink some water. 💧",
        "While you sleep your brain sorts and stores the memories of the day; poor sleep makes learning harder.",
        "Standing up and walking for a few minutes after an hour at the computer is good for your back and your focus.",
        "Light travels around the Earth about 7.5 times in one second. ⚡",
        "Sunlight takes about 8 minutes and 20 seconds to reach the Earth. ☀️",
        "A day on Venus is longer than its year: one spin takes about 243 Earth days, while its orbit takes 225.",
        "A day on Mars lasts about 24 hours and 37 minutes.",
        "The Moon moves about 3.8 centimetres further from the Earth every year. 🌕",
        "An octopus has three hearts. 🐙",
        "A giraffe has only 7 neck vertebrae, just like a human, but each one is much longer.",
        "Honey almost never spoils; honey thousands of years old has sometimes still been edible. 🍯",
        "About 70% of the Earth's surface is covered by water. 🌊",
        "Yalda is the winter solstice night, the longest night of the year in the northern hemisphere; after it the days slowly get longer.",
        "The moment of the Persian new year, Nowruz, is exactly the astronomical spring equinox.",
        "Tehran's Milad Tower is 435 metres tall and one of the tallest telecommunication towers in the world. 🏙️",
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
