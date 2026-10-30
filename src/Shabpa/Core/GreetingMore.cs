namespace Shabpa.Core;

/// <summary>More lines for every part of the day, added to the original ones by <see cref="Greetings"/>. Both languages have the same number of lines
/// per part and none repeats an original.</summary>
internal static class GreetingMore
{
    public static readonly IReadOnlyDictionary<Greetings.Part, string[]> Persian = new Dictionary<Greetings.Part, string[]>
    {
        [Greetings.Part.Dawn] =
        [
            "ساعت اینقدر زوده که حتی گوشی‌ات هم هنوز خوابه 📱", "سحرِ آروم؛ بهترین وقت برای فکرهای تازه 💡", "سلام سحرخیز! امروز یه قدم از همه جلوتری 🏁", "هوا هنوز تاریک روشنه؛ چایی‌ات رو آروم بخور ☕",
            "صدای پرنده‌ها بلند شد؛ دنیا داره بیدار می‌شه 🐦", "سحر بخیر! اگه هنوز نخوابیدی، یه چیز گرم بخور و بخواب 🛏️", "اولین نور آفتاب، اولین لبخند امروز ☀️", "قبل از همه بیداری؛ سکوت صبح مال توئه 🤫",
            "امروز سحرخیزی؛ فردا خمیازه‌ی بعدازظهر 😴", "هنوز شهر خوابه و فقط من و تو بیداریم 🌆", "صبح سپیده بخیر؛ یه نفس عمیق بکش 🌬️", "به‌به! چه کسی اینقدر زود بیدار شده؟ 👀",
            "چشمات هنوز خوابن ولی همتت بیداره 💪", "سحر شد؛ مغزت داره بوت می‌شه، صبر کن 🔄",
        ],
        [Greetings.Part.Morning] =
        [
            "صبح بخیر! یه صبحانه‌ی درست‌وحسابی فراموش نشه 🍳", "صبح بخیر؛ یه کار کوچیک رو همین اول انجام بده، بقیه‌ش آسون‌تره ✅", "سلام صبحی! امروز چی توی برنامه‌ست؟ 📝", "صبح قشنگ، نور از پنجره و یه چای تازه‌دم 🪟",
            "صبح شد؛ آب بخور، کش‌وقوس بیا، شروع کن 🧘", "سلام! هر چی کوچیک شروع بشه، بزرگ می‌شه 🌳", "صبح بخیر! لیست کارهات رو یه نگاه بنداز 📋", "شب‌پا هم بیدار شده و آماده‌ست 🦉☀️",
            "صبح بخیر! امروز رو سبک بگیر، نه خیلی هم سخت 😌", "سلام؛ خورشید بالا اومد، سیستم هم بالا اومد 🌞", "یه صبح دیگه، یه فرصت دیگه 🎯", "صبح بخیر؛ لبخند بزن، دوربین مخفی نیست ولی مفته 😁",
            "قهوه یا چای؟ انتخاب با خودته، صبح بخیر ☕", "امروز تا غروب کلی کار داری؛ با انرژی شروع کن ⚡",
        ],
        [Greetings.Part.Noon] =
        [
            "ظهر بخیر! نمک‌گیر نشی، یه کم نون و پنیر هم بد نیست 🥖", "وسط روزی؛ یه نفس عمیق و یه لیوان آب 💧", "ظهر شد، سیستم هم یه نفس تازه می‌خواد؛ چشمات رو ببند یه دقیقه 😌", "ناهارت چیه؟ شب‌پا فقط می‌پرسه 🍽️",
            "ظهر بخیر! گرسنگی با مانیتور حل نمی‌شه 😄", "نیمه‌ی روز؛ از اول تا الان چه کار خوبی کردی؟ 🌟", "ظهر بخیر؛ بعد از ناهار یه چایی و آروم‌آروم 🍵", "یه کم قدم بزن، پاهات هم حق دارن 🚶",
            "ظهر آفتابی یا ابری، حال تو مهمه 🌤️", "وقت استراحت وسط روزه؛ نذار خستگی جمع بشه 🛋️", "ظهرت پر از انرژی؛ یه ساعت دیگه کار یه ساعت دیگه استراحت ⏱️", "غذا یادت نره؛ سیستم هم برق می‌خوره، تو هم غذا 🔌🍛",
            "ظهر بخیر! بوی غذا از یه جایی میاد؟ 😋", "نصف روز رو رد کردی؛ دم‌ت گرم ✌️",
        ],
        [Greetings.Part.Afternoon] =
        [
            "بعدازظهر بخیر! یه کم میوه بخور، قند خونت بالا بره 🍎", "ساعت پنج نزدیکه؛ آخرین تلاش و بعد استراحت 💪", "بعدازظهرها آروم‌ترن، یه آهنگ ملایم بذار 🎶", "سلام! خواب‌آلودی؟ ده قدم راه برو 🚶",
            "وقت یه فنجون چای و یه کار کوچیکه ☕", "بعدازظهر بخیر؛ پنجره رو باز کن هوای تازه بیاد 🪟", "نصف دوم روز شروع شد؛ می‌شه از اول شروع کرد 🔄", "سیستم هم این ساعت یه کم کند می‌شه، تو بهش وقت بده 😉",
            "بعدازظهر یعنی یه چای، یه شیرینی و یه خنده 🍪", "ساعت‌ها دارن می‌رن؛ یه کار مهم رو الان تموم کن ✅", "بعدازظهر بخیر! چشمات از مانیتور خسته نشدن؟ 👀", "نور کم‌کم نارنجی می‌شه؛ خوشگله 🍊",
            "چند ساعت دیگه شب‌پا می‌تونه برات خاموشی تنظیم کنه 🌙", "بعدازظهر تنبل بخیر 😴",
        ],
        [Greetings.Part.Evening] =
        [
            "غروب شد؛ چراغ‌ها رو روشن کن و یه چای دم کن 💡", "عصر بخیر! امروز چه کار قشنگی کردی؟ 🌟", "وقت آرامه؛ یه آهنگ خوب و یه بازی آروم 🎧", "غروب‌ها کمی دلتنگی داره، یه زنگ به یه دوست بزن 📞",
            "عصر بخیر؛ بعد از یه روز طولانی یه استراحت لایقی 🛋️", "هوا تاریک می‌شه؛ نور شب رو یادت نره 🌆", "شب‌پا دقیقاً می‌دونه این ساعت چی لازمه: چای 🍵", "تا شام یه چیزی بخور، ولی زیاد نه! 🍪",
            "غروب بخیر! امروز رو چطور دیدی؟ 🌅", "کارت تموم شد؟ حالا نوبت بازی و تفریحه 🎮", "عصر بخیر؛ آسمون رنگین‌کمانیه 🌈", "الان وقت پروفایل «تفریح» رو روشن کردنه 😎",
            "عصر یعنی نصف کار تموم، نصف شادی شروع 🎉", "چایی داغ، کیبورد گرم و یه بازی خوب 🔥",
        ],
        [Greetings.Part.Night] =
        [
            "شب بخیر! چشمات رو از نور آبی دور کن، نور شب رو روشن کن 🌙", "شبه؛ سیستم و تو هر دو لایق یه استراحت‌اید 💤", "شب خوش؛ فردا هم روزه، بذار با انرژی بیدار شی 🔋", "شب‌ها همه‌چی آرومه؛ حتی فنِ سیستمت 🤫",
            "شب بخیر! یه تاریخچه از امروز بنویس، یادت می‌مونه 📖", "ستاره‌ها بیرون اومدن؛ یه نگاه بنداز ✨", "شب شد؛ برای فردا یه تصمیم خوب بگیر 💭", "یه آهنگ آروم و یه چای گرم، شب کامل می‌شه 🎶",
            "شب بخیر؛ شب‌پا آماده‌ست خاموشی رو سر وقت انجام بده ⏰", "امشب زود بخواب، فردا ممنونت می‌شه 😴", "شب‌ها فکرها بلندترن؛ ولی خواب بهتره 🛌", "شب بخیر! چراغ‌ها رو کم کن 💡",
            "هنوز بیداری؟ بیداری خوبه، ولی بدن هم صدا داره 👂", "شب؛ بازی‌ها قشنگ‌ترن و پینگ پایین‌تر 😉",
        ],
        [Greetings.Part.Late] =
        [
            "ساعت از نیمه‌شب گذشته؛ فردا رو دریاب 🌌", "دیروقته؛ شب‌پا پیشنهاد می‌ده این بازی رو ذخیره کنی و بخوابی 💾", "هنوز بیداری؟ حداقل یه لیوان شیر گرم بخور 🥛", "این ساعت فقط جغدها بیدارن 🦉",
            "ساعت خوابه؛ چشمات خسته‌ان، حتی اگه نگی 🥱", "شب‌پا می‌تونه ده دقیقه دیگه سیستم رو بخوابونه، بگو 💤", "تاریک‌ترین ساعت شبه؛ و شب‌پا کنارته 🕯️", "دیروقت بخیر! آب بخور، نور مانیتور رو کم کن 💡",
            "این ساعت کارهای خوب کم انجام می‌شه؛ بخواب 😴", "خوابت نمیاد؟ بگو «حالت شب‌خواب» رو برات روشن کنم 🌙", "نیمه‌شب‌ها هم می‌شه پیروز شد، ولی فردا خسته‌ای 😅", "ساعت بدنت می‌گه بخواب؛ بهش گوش بده ⏰",
            "جغد کوچولو، وقتشه بری لونه‌ت 🦉🛏️", "یه شب دیگه؛ ولی بدنت بخش مهمی از ماجراست 🫶",
        ],
    };

    public static readonly IReadOnlyDictionary<Greetings.Part, string[]> English = new Dictionary<Greetings.Part, string[]>
    {
        [Greetings.Part.Dawn] =
        [
            "So early that even your phone is still asleep 📱", "A quiet dawn; the best time for fresh ideas 💡", "Hello early riser! You are a step ahead of everyone today 🏁", "Still half dark; sip your tea slowly ☕",
            "The birds have started; the world is waking up 🐦", "Good dawn! If you have not slept yet, have something warm and go to bed 🛏️", "The first light of the sun, the first smile of the day ☀️", "You are up before everyone; the quiet morning is yours 🤫",
            "An early riser today, a yawn this afternoon 😴", "The city is asleep and only you and I are awake 🌆", "Good morning; take a deep breath 🌬️", "Well well, who got up this early? 👀",
            "Your eyes are asleep but your will is awake 💪", "Dawn; your brain is booting, hold on 🔄",
        ],
        [Greetings.Part.Morning] =
        [
            "Good morning! Do not forget a proper breakfast 🍳", "Good morning; do one small task first, the rest gets easier ✅", "Hello! What is on the plan today? 📝", "A lovely morning, light through the window and fresh tea 🪟",
            "Morning; drink water, stretch, begin 🧘", "Hi! Whatever starts small grows big 🌳", "Good morning! Glance over your to-do list 📋", "Shabpa is awake and ready too 🦉☀️",
            "Good morning! Take today lightly, not too hard 😌", "Hello; the sun is up, so is the PC 🌞", "Another morning, another chance 🎯", "Good morning; smile, there is no hidden camera but it is free 😁",
            "Coffee or tea? Your choice, good morning ☕", "There is plenty to do before sunset; start with energy ⚡",
        ],
        [Greetings.Part.Noon] =
        [
            "Good noon! A bit of bread and cheese would not hurt 🥖", "Midday; a deep breath and a glass of water 💧", "It is noon and the PC wants a breath too; close your eyes for a minute 😌", "What is for lunch? Shabpa only asks 🍽️",
            "Good noon! Hunger is not solved with a monitor 😄", "Half the day; what good thing have you done so far? 🌟", "Good noon; after lunch, a tea and take it slow 🍵", "Take a little walk, your legs have rights too 🚶",
            "Sunny or cloudy, your mood matters 🌤️", "Midday rest time; do not let tiredness pile up 🛋️", "Full of energy at noon; an hour of work, an hour of rest ⏱️", "Do not forget food; the PC eats power, you eat food 🔌🍛",
            "Good noon! Is that food I smell? 😋", "You passed half the day; well done ✌️",
        ],
        [Greetings.Part.Afternoon] =
        [
            "Good afternoon! Some fruit will lift your energy 🍎", "Five o'clock is near; a last effort and then rest 💪", "Afternoons are calmer, put on a soft song 🎶", "Hello! Sleepy? Walk ten steps 🚶",
            "Time for a cup of tea and a small task ☕", "Good afternoon; open the window for fresh air 🪟", "The second half of the day has started; you may start over 🔄", "The PC slows a little at this hour too, give it time 😉",
            "Afternoon means tea, a sweet and a laugh 🍪", "The hours are leaving; finish one important thing now ✅", "Good afternoon! Are your eyes tired of the screen? 👀", "The light slowly turns orange; lovely 🍊",
            "In a few hours Shabpa can schedule your shutdown 🌙", "Good lazy afternoon 😴",
        ],
        [Greetings.Part.Evening] =
        [
            "Sunset; turn the lights on and brew a tea 💡", "Good evening! What nice thing did you do today? 🌟", "A calm hour; a good song and a gentle game 🎧", "Evenings carry a little longing; call a friend 📞",
            "Good evening; after a long day you deserve a rest 🛋️", "It is getting dark; do not forget night light 🌆", "Shabpa knows exactly what this hour needs: tea 🍵", "Eat something before dinner, but not too much! 🍪",
            "Good evening! How was your day? 🌅", "Work done? Now it is game and fun time 🎮", "Good evening; the sky is full of colours 🌈", "Now is the time to switch on your “fun” profile 😎",
            "Evening: half the work done, half the joy begins 🎉", "Hot tea, a warm keyboard and a good game 🔥",
        ],
        [Greetings.Part.Night] =
        [
            "Good night! Keep your eyes from blue light, turn night light on 🌙", "It is night; you and the PC both deserve a rest 💤", "Good night; tomorrow is a day too, wake up with energy 🔋", "Everything is calm at night; even your PC's fan 🤫",
            "Good night! Write a note about today, you will remember it 📖", "The stars are out; take a look ✨", "Night; make one good decision for tomorrow 💭", "A soft song and a warm tea complete the night 🎶",
            "Good night; Shabpa is ready to switch off on time ⏰", "Sleep early tonight, tomorrow will thank you 😴", "Thoughts are louder at night; sleep is better 🛌", "Good night! Dim the lights 💡",
            "Still awake? That is fine, but your body has a voice too 👂", "Night; games feel better and the ping is lower 😉",
        ],
        [Greetings.Part.Late] =
        [
            "Past midnight; look after tomorrow 🌌", "It is late; Shabpa suggests saving this game and going to bed 💾", "Still awake? At least have a glass of warm milk 🥛", "At this hour only owls are awake 🦉",
            "Bedtime; your eyes are tired even if you do not say so 🥱", "Shabpa can put the PC to sleep in ten minutes, just say 💤", "The darkest hour of the night; Shabpa is with you 🕯️", "Good late night! Drink water, dim the monitor 💡",
            "Few good things happen at this hour; sleep 😴", "Cannot sleep? Say “sleep mode” and I will turn it on 🌙", "You can win at midnight too, but tomorrow you will be tired 😅", "Your body clock says sleep; listen to it ⏰",
            "Little owl, time to go to your nest 🦉🛏️", "One more night; but your body is an important part of the story 🫶",
        ],
    };
}
