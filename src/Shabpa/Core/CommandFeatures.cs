using System.Text.RegularExpressions;

namespace Shabpa.Core;

/// <summary>The typed commands for everything in the app that is not power or time: open any page, the overlay, the theme, the lights (RGB), free the memory, Game
/// Mode, quick questions about the PC («دمای پردازنده چنده؟»), a short help and a little small talk. Persian and English, many ways to say each thing, typos and
/// the usual colloquial endings tolerated by the shared tokenizer. It only answers when one of its own nouns is in the text, so the power and time commands
/// keep reading exactly as before. Pure logic.</summary>
public static class CommandFeatures
{
    /// <summary>The lighting (RGB) page is not in this version of the app; its commands wait for the update that brings it back.</summary>
    public static readonly bool Lights = false;

    // ---- vocabulary ---------------------------------------------------------------------------------------------------

    /// <summary>Pages by every name a person might use. Keys are MainWindow's page keys ("system:tune" opens a tab).</summary>
    private static readonly (string key, string fa, string en, string[] words)[] Pages =
    [
        ("home", "خانه", "Home", ["خانه", "صفحه اصلی", "صفحه ی اصلی", "هوم", "داشبورد", "home", "main page", "dashboard", "start page"]),
        ("system", "کنترل سیستم", "System control", ["کنترل سیستم", "کنترل", "دستور برق", "صفحه برق", "system control", "power page", "control panel"]),
        ("system:tune", "بهینه‌سازی بازی", "Game tuning", ["بهینه سازی بازی", "بهینه سازی", "تیونینگ", "گیم تیونینگ", "بوست بازی", "بهینه کردن بازی", "game tuning", "game boost", "tuning", "tune", "optimize games", "optimise games", "game optimizer"]),
        ("system:wake", "بیدارباش", "Wake-up", ["بیدارباش", "بیدار باش", "ساعت بیدارباش", "wake page", "wake timers", "alarm page"]),
        ("system:memory", "حافظه", "Memory", ["حافظه", "صفحه رم", "مصرف رم", "memory page", "ram page"]),
        ("system:remote", "تلگرام", "Telegram", ["تلگرام", "ربات تلگرام", "کنترل از راه دور", "telegram", "remote control", "bot"]),
        ("comfort", "نور شب و استراحت", "Night light & rest", ["آسایش چشم", "استراحت", "نور شب و استراحت", "صفحه نور شب", "night light page", "eye comfort", "rest page", "breaks"]),
        ("profiles", "پروفایل‌ها", "Profiles", ["پروفایل*", "پروفایل ها", "profiles", "profile page", "routines"]),
        ("gaming", "اورلی بازی", "Game overlay", ["اورلی", "اورلی بازی", "هاد", "overlay", "game overlay", "hud", "fps counter"]),
        ("rgb", "نورپردازی", "Lighting", ["نورپردازی", "نور پردازی", "ار جی بی", "آر جی بی", "rgb", "lighting", "led page", "light show"]),
        ("playtime", "زمان بازی", "Playtime", ["زمان بازی", "ساعت بازی", "گزارش بازی", "تاریخچه بازی", "گیم لاگ", "playtime", "play time", "game log", "game history", "gaming stats"]),
        ("ping", "پایداری پینگ", "Ping stability", ["پایداری پینگ", "صفحه پینگ", "مصرف اینترنت", "چی اینترنت", "ping page", "ping stability", "network usage", "internet usage", "bandwidth"]),
        ("dns", "دی‌ان‌اس", "DNS", ["دی ان اس", "دیان اس", "دی ان‌اس", "dns", "dns page", "dns servers"]),
        ("download", "دانلود زمان‌بندی‌شده", "Scheduled download", ["دانلود زمان بندی", "دانلود زمانبندی", "صفحه دانلود", "دانلودها", "لانچر", "scheduled download", "download page", "downloads", "launcher"]),
        ("files", "فایل و پشتیبان", "Files & backup", ["فایل و پشتیبان", "فایل ها", "فایلها", "پشتیبان گیری", "بکاپ", "پشتیبان", "backup", "backups", "files", "duplicates", "disk space"]),
        ("health", "سلامت و امنیت", "Health & security", ["سلامت", "سلامت سیستم", "امنیت", "سلامت و امنیت", "health", "system health", "security", "health check", "antivirus"]),
        ("tasks", "کارها و تمرکز", "Tasks & focus", ["کارها", "لیست کار", "لیست کارها", "تسک ها", "تسک‌ها", "کارها و تمرکز", "tasks", "to-do list", "todo list", "task list", "focus page"]),
        ("mic", "میکروفون", "Microphone", ["میکروفون", "میکروفن", "میک", "نویزگیر", "noise suppression", "microphone", "mic", "noise filter"]),
        ("weather", "آب و هوا", "Weather", ["آب و هوا", "هوا", "وضع هوا", "وضعیت هوا", "weather", "forecast", "temperature outside"]),
        ("support", "پشتیبانی", "Support", ["پشتیبانی", "تیکت", "گزارش مشکل", "support", "ticket", "report a problem", "contact support"]),
        ("update", "به‌روزرسانی", "Updates", ["به روزرسانی", "بروزرسانی", "آپدیت", "نسخه جدید", "update", "updates", "new version", "check for updates", "upgrade"]),
        ("settings", "تنظیمات", "Settings", ["تنظیمات", "ستینگ", "settings", "preferences"]),
    ];

    private static readonly string[] OpenVerbs =
    [
        "برو", "برم", "بریم", "ببر", "ببرم", "باز", "بازکن", "بازش", "بیار", "بیاد", "نشون", "نشان", "نمایش", "ببینم", "ببین", "بنمای", "برگرد",
        "open*", "show*", "go", "goto", "navigate", "view", "display", "launch", "bring", "visit", "take",
    ];

    private static readonly string[] PowerWords =
    [
        "خاموش*", "بخاموش*", "shutdown*", "shut down", "turn off", "power off", "sleep", "بخواب*", "خوابون*", "خواب", "hibernate*", "restart*", "reboot*", "ریاستارت*", "ری استارت*", "ریستارت*", "قفل*", "lock*", "لغو*", "cancel*", "یادم بنداز*", "remind*", "بیدارم", "wake me", "وقتی", "when", "after", "بعد از", "تا", "in an hour", "دقیقه", "ساعت", "minutes", "hours",
    ];

    private static readonly string[] OnWords = ["روشن*", "فعال*", "بزن", "راه بنداز*", "استارت", "شروع", "بیار", "on", "enable*", "start*", "activate", "turn on", "switch on"];
    private static readonly string[] OffWords = ["خاموش*", "غیرفعال*", "غیر فعال*", "قطع*", "بردار*", "off", "disable*", "deactivate", "turn off", "switch off", "stop", "kill", "بسته", "ببند*"];

    private static readonly (string hex, string[] words)[] Colours =
    [
        ("FF2D2D", ["قرمز", "سرخ", "red", "crimson"]), ("2D7DFF", ["آبی", "ابی", "blue"]), ("14E0E0", ["فیروزه ای", "فیروزه", "آبی روشن", "آبی آسمانی", "سیان", "cyan", "turquoise", "aqua", "teal", "light blue"]),
        ("22E06B", ["سبز", "green", "lime"]), ("FFD60A", ["زرد", "yellow"]), ("FF8A00", ["نارنجی", "نارنجی", "orange"]), ("A855F7", ["بنفش", "ارغوانی", "purple", "violet", "lavender"]),
        ("FF4FA3", ["صورتی", "pink", "magenta", "rose"]), ("FFFFFF", ["سفید", "white"]), ("FFC857", ["طلایی", "gold", "golden", "amber"]),
    ];

    private static readonly (string key, string[] words)[] Themes =
    [
        ("night", ["تیره", "شب", "دارک", "آبی شب", "dark", "night", "night blue"]), ("black", ["مشکی", "سیاه", "black", "amoled", "oled"]), ("white", ["سفید", "روشن", "روز", "لایت", "white", "light", "day"]),
        ("halloween", ["هالووین", "هالوین", "halloween"]), ("pink", ["صورتی", "pink"]), ("purple", ["بنفش", "purple"]),
    ];

    // ---- text helpers -------------------------------------------------------------------------------------------------

    private static string Pad(IReadOnlyList<CommandToken> tokens) => " " + string.Join(' ', tokens.Select(t => t.Norm)) + " ";

    private static bool Contains(string padded, string phrase)
    {
        var p = CommandText.Normalize(phrase).Trim();
        if (p.Length == 0) return false;
        if (p.EndsWith('*')) return padded.Contains(" " + p[..^1], StringComparison.Ordinal);
        return padded.Contains(" " + p + " ", StringComparison.Ordinal);
    }

    private static bool Any(string padded, params string[] phrases) => phrases.Any(p => Contains(padded, p));

    private static bool Any(string padded, IEnumerable<string> phrases) => phrases.Any(p => Contains(padded, p));

    private static CommandResult Ready(AssistantCommand command) => new(CommandStatus.Ready, command, [], "", 1);

    private static readonly Regex Percent = new(@"(?<n>\d{1,3})\s*(%|درصد|percent|pct)", RegexOptions.Compiled);
    private static readonly Regex AfterWord = new(@"(?:روشنایی|روشنایی|brightness|شدت|اندازه|size|level|سطح|بزرگ\w*)\s*(?:رو|را|به|to|at|:)?\s*(?<n>\d{1,3})", RegexOptions.Compiled);

    private static int? Number(string text)
    {
        var m = Percent.Match(text);
        if (!m.Success) m = AfterWord.Match(text);
        return m.Success && int.TryParse(m.Groups["n"].Value, out var n) ? n : null;
    }

    // ---- the entry point ----------------------------------------------------------------------------------------------

    /// <summary>The command, or null when the text has nothing for this part of the app (the power and time reader then takes it).</summary>
    public static CommandResult? TryParse(IReadOnlyList<CommandToken> tokens)
    {
        if (tokens.Count == 0) return null;
        var padded = Pad(tokens);
        var flat = padded.Trim();

        // the lights
        if (Lights && Any(padded, "rgb", "ار جی بی", "آر جی بی", "نورپردازی", "نور پردازی", "ال ای دی", "led*", "lighting", "چراغ کیس", "نور کیس", "نور فن", "نور رم", "رنگ کیس", "نور قطعه*", "افکت نوری", "pc lights", "case lights", "rgb lights"))
            return Rgb(padded, flat);

        // the overlay
        if (Any(padded, "اورلی", "overlay", "هاد", "hud", "fps counter", "شمارنده فریم", "نمایشگر فریم", "نمایشگر fps"))
        {
            var overlay = OverlayCommand(padded, flat);
            if (overlay is not null) return overlay;
        }

        // the theme
        if (Any(padded, "تم", "theme", "ظاهر برنامه", "رنگ برنامه", "appearance", "dark mode", "light mode", "حالت تاریک", "حالت روشن"))
        {
            var theme = ThemeCommand(padded);
            if (theme is not null) return theme;
        }

        // free the memory
        if (Any(padded, "رم رو خالی", "رم خالی", "رم رو پاک", "رم رو آزاد", "خالی کردن رم", "پاکسازی رم", "آزاد کردن رم", "رم رو سبک", "رم رو تمیز", "free ram", "free up ram", "free memory", "clean ram", "clear ram", "clean memory", "clear memory", "empty ram", "flush ram", "boost ram"))
            return Ready(new AssistantCommand { Intent = CommandIntent.MemoryClean });

        // Windows' own game mode inside Shabpa
        if (Any(padded, "حالت بازی", "game mode", "gamemode") && !Any(padded, "ویندوز", "windows"))
        {
            var off = Any(padded, OffWords);
            var on = Any(padded, OnWords);
            if (on || off) return Ready(new AssistantCommand { Intent = CommandIntent.GameMode, On = !off });
        }

        // quick questions and small talk
        if (Info(padded, flat) is { } info) return info;

        // opening a page
        if (OpenPageCommand(tokens, padded) is { } open) return open;
        return null;
    }

    // ---- lights ---------------------------------------------------------------------------------------------------------

    private static CommandResult Rgb(string padded, string flat)
    {
        var off = Any(padded, OffWords) && !Any(padded, "جز", "except");
        var on = Any(padded, OnWords);
        var open = Any(padded, OpenVerbs) && !on && !off;
        // an effect named in either language (the longest name wins)
        var effect = RgbEffects.All
            .SelectMany(e => new[] { (e, name: CommandText.Normalize(e.Fa).Trim()), (e, name: CommandText.Normalize(e.En).Trim()), (e, name: e.Key) })
            .Where(x => x.name.Length >= 3 && Contains(padded, x.name))
            .OrderByDescending(x => x.name.Length).Select(x => x.e).FirstOrDefault();
        // plain words for the basic effects
        effect ??= Any(padded, "رنگین کمان", "رنگینکمان", "rainbow") ? RgbEffects.Find("rainbow")
            : Any(padded, "تنفس*", "breath*") ? RgbEffects.Find("breathe")
            : Any(padded, "موزیک", "موسیقی", "آهنگ", "music", "audio", "beat*") ? RgbEffects.Find("music")
            : Any(padded, "چشمک*", "فلش", "flash*", "blink*", "strobe") ? RgbEffects.Find("flash")
            : Any(padded, "تصادفی", "رندوم", "random") ? RgbEffects.Find("random")
            : Any(padded, "دمای پردازنده", "دما", "temperature", "cpu temp") ? RgbEffects.Find("temp")
            : Any(padded, "ثابت", "static", "solid") ? RgbEffects.Find("static") : null;
        var colour = Colours.Where(c => Any(padded, c.words)).Select(c => c.hex).FirstOrDefault();
        var level = Number(flat);
        if (open && effect is null && colour is null && level is null) return Ready(new AssistantCommand { Intent = CommandIntent.OpenPage, Page = "rgb" });
        if (off && effect is null && colour is null)
            return Ready(new AssistantCommand { Intent = CommandIntent.Rgb, On = false, Value = "off" });
        if (effect is null && colour is null && level is null)
            return Ready(new AssistantCommand { Intent = CommandIntent.Rgb, On = !off, Value = off ? "off" : "on" });
        return Ready(new AssistantCommand { Intent = CommandIntent.Rgb, On = true, Value = effect?.Key ?? "", Color = colour ?? "", Level = level is { } l ? Math.Clamp(l, 0, 100) : -1 });
    }

    // ---- overlay ---------------------------------------------------------------------------------------------------------

    private static CommandResult? OverlayCommand(string padded, string flat)
    {
        var layout = Any(padded, "نوار", "bar", "افقی", "horizontal") ? "bar"
            : Any(padded, "کارت", "card") ? "card"
            : Any(padded, "فشرده", "compact", "کوچیک", "small") ? "compact"
            : Any(padded, "مینیمال", "minimal", "ساده", "simple") ? "minimal"
            : Any(padded, "حلقه*", "rings", "ring", "گرد", "round") ? "rings"
            : Any(padded, "نمودار", "graph", "chart") ? "graph" : null;
        if (layout is not null && Any(padded, "طرح", "مدل", "حالت", "نوع", "layout", "style", "mode", "type", "look", "کن", "بذار", "بزار", "بزن", "set", "use", "switch")) return Ready(new AssistantCommand { Intent = CommandIntent.Overlay, Value = "layout:" + layout });
        if (Any(padded, "اندازه", "size", "بزرگ*", "کوچ*", "bigger", "larger", "smaller", "scale") && Number(flat) is { } size) return Ready(new AssistantCommand { Intent = CommandIntent.Overlay, Value = "size:" + Math.Clamp(size, 25, 200) });
        if (Any(padded, "فقط موقع بازی", "موقع بازی", "تو بازی", "توی بازی", "حین بازی", "in game", "in-game", "while gaming", "while playing", "only in game", "only when gaming"))
            return Ready(new AssistantCommand { Intent = CommandIntent.Overlay, Value = "mode:InGame" });
        if (Any(padded, "همیشه", "always")) return Ready(new AssistantCommand { Intent = CommandIntent.Overlay, Value = "mode:Always" });
        var off = Any(padded, OffWords);
        var on = Any(padded, OnWords);
        if (off) return Ready(new AssistantCommand { Intent = CommandIntent.Overlay, Value = "mode:Off", On = false });
        if (on) return Ready(new AssistantCommand { Intent = CommandIntent.Overlay, Value = "mode:Always", On = true });
        return Any(padded, OpenVerbs) || flat.Split(' ').Length <= 2 ? Ready(new AssistantCommand { Intent = CommandIntent.OpenPage, Page = "gaming" }) : null;
    }

    // ---- theme ----------------------------------------------------------------------------------------------------------

    private static CommandResult? ThemeCommand(string padded)
    {
        if (Any(padded, "حالت تاریک", "dark mode")) return Ready(new AssistantCommand { Intent = CommandIntent.Theme, Value = "night" });
        if (Any(padded, "حالت روشن", "light mode")) return Ready(new AssistantCommand { Intent = CommandIntent.Theme, Value = "white" });
        foreach (var p in Palette.All)
            if (Any(padded, CommandText.Normalize(p.Fa), CommandText.Normalize(p.En)))
                return Ready(new AssistantCommand { Intent = CommandIntent.Theme, Value = p.Key });
        foreach (var (key, words) in Themes)
            if (Any(padded, words)) return Ready(new AssistantCommand { Intent = CommandIntent.Theme, Value = key });
        return null;
    }

    // ---- questions and small talk -------------------------------------------------------------------------------------

    private static CommandResult? Info(string padded, string flat)
    {
        string? kind = null;
        if (Any(padded, "چه کارایی بلدی", "چه کارهایی بلدی", "چیکار میتونی", "چی کار میتونی", "چی بلدی", "کمک", "راهنما", "راهنمایی", "کمکم کن", "help", "what can you do", "what do you do", "commands", "how do i use you", "how to use you", "capabilities", "abilities"))
            kind = "help";
        else if (Any(padded, "ساعت چنده", "ساعت چند", "چه ساعتیه", "ساعت چنده الان", "زمان الان", "what time is it", "what's the time", "whats the time", "current time", "time now", "tell me the time", "time please"))
            kind = "time";
        else if (Any(padded, "امروز چندمه", "تاریخ امروز", "تاریخ", "امروز چه روزیه", "امروز چه روزیست", "چندم", "what day is it", "what's the date", "whats the date", "today's date", "todays date", "what date is it", "current date", "date today"))
            kind = "date";
        else if (Any(padded, "دمای پردازنده", "دمای سی پی یو", "دمای cpu", "cpu temp*", "processor temp*", "how hot is my cpu", "cpu heat") && !Any(padded, "افکت", "نورپردازی", "rgb", "effect"))
            kind = "cpu";
        else if (Any(padded, "دمای گرافیک", "دمای کارت گرافیک", "gpu temp*", "graphics temp*", "how hot is my gpu"))
            kind = "gpu";
        else if (Any(padded, "مصرف رم", "رم چقدره", "رم چقدر", "رم پره", "ram usage", "how much ram", "memory usage", "ram used", "is my ram full"))
            kind = "ram";
        else if (Any(padded, "پینگ چنده", "پینگم", "پینگ من", "my ping", "what's my ping", "whats my ping", "ping now", "how is my ping", "current ping"))
            kind = "ping";
        else if (Any(padded, "وضعیت سیستم", "وضعیت کامپیوتر", "سیستم چطوره", "سیستم چطوره", "حال سیستم", "system status", "how is my pc", "how's my pc", "hows my pc", "pc status", "system report", "sysinfo"))
            kind = "status";
        else if (Any(padded, "سلام", "درود", "های", "هلو", "صبح بخیر", "عصر بخیر", "شب بخیر", "hello", "hi", "hey", "good morning", "good evening", "good night", "yo", "sup") && flat.Split(' ').Length <= 4 && !Any(padded, PowerWords))
            kind = Any(padded, "شب بخیر", "good night") ? "goodnight" : "hello";
        else if (Any(padded, "مرسی", "ممنون", "دمت گرم", "تشکر", "thanks", "thank you", "thx", "ty", "cheers") && flat.Split(' ').Length <= 4 && !Any(padded, PowerWords))
            kind = "thanks";
        else if (Any(padded, "خوبی", "حالت چطوره", "چطوری", "how are you", "how r u", "how are u", "whats up", "what's up") && flat.Split(' ').Length <= 4 && !Any(padded, PowerWords))
            kind = "howareyou";
        else if (Any(padded, "کی هستی", "تو کی هستی", "اسمت چیه", "اسمت چی", "who are you", "what is your name", "what's your name", "your name"))
            kind = "who";
        else if (Any(padded, "جوک", "بگو خنده", "شوخی", "joke", "tell me a joke", "make me laugh", "funny", "یه خنده", "بخندون"))
            kind = "joke";
        else if (Any(padded, "فکت", "چیز جالب", "اطلاعات جالب", "میدونستی", "fun fact", "interesting fact", "tell me a fact", "did you know", "random fact"))
            kind = "fact";
        else if (Any(padded, "یه نکته", "نکته بگو", "نکته ای", "نصیحت", "یه توصیه", "tip", "give me a tip", "any advice", "advice", "a tip"))
            kind = "tip";
        else if (Any(padded, "انگیزه", "روحیه بده", "دلگرمم کن", "دلم گرفته", "motivate me", "cheer me up", "encourage me", "i'm sad"))
            kind = "pep";
        else if (Any(padded, "هو هو", "هوهو", "hoot", "hoo hoo", "هوووو"))
            kind = "hoot";
        else if (Any(padded, "سطحم", "سطح من", "لول من", "مدالام", "مدالهام", "مدال هام", "مدالای من", "پرهام", "چند تا پر", "شب پیاپی", "امتیازم", "my level", "my medals", "my feathers", "my streak", "how many medals", "show my progress"))
            kind = "medals";
        else if (Any(padded, "کارای امروز", "کارهای امروز", "کار امروز", "چه کاری دارم", "چه کارایی دارم", "لیست کارها", "تسکام", "my tasks", "my to do", "what do i have today", "tasks today", "todo list"))
            kind = "tasks";
        else if (Any(padded, "چند وقته روشنه", "چند ساعته روشنه", "آپتایم", "uptime", "how long has my pc been on", "how long on"))
            kind = "uptime";
        else if (Any(padded, "فضای دیسک", "فضای خالی", "چقدر جا دارم", "هارد چقدر", "disk space", "free space", "storage left", "how much space"))
            kind = "disk";
        else if (Any(padded, "شیر یا خط", "سکه بنداز", "flip a coin", "coin flip", "heads or tails"))
            kind = "coin";
        else if (Any(padded, "تاس بنداز", "تاس", "roll a dice", "roll a die", "roll the dice", "dice"))
            kind = "dice";
        else if (Any(padded, "نسخه برنامه", "نسخه ی برنامه", "نسخه شب پا", "چه نسخه", "ورژن", "version", "which version", "what version", "about shabpa") && !Any(padded, "جدید", "new", "latest", "update", "آپدیت", "به روز*"))
            kind = "version";
        return kind is null ? null : Ready(new AssistantCommand { Intent = CommandIntent.Info, Value = kind });
    }

    // ---- opening pages ----------------------------------------------------------------------------------------------

    private static CommandResult? OpenPageCommand(IReadOnlyList<CommandToken> tokens, string padded)
    {
        var verb = Any(padded, OpenVerbs);
        var best = Pages.Where(p => p.key != "rgb" || Lights).Select(p => (p, hit: p.words.Where(w => Contains(padded, w)).OrderByDescending(w => w.Length).FirstOrDefault()))
            .Where(x => x.hit is not null).OrderByDescending(x => x.hit!.Length).FirstOrDefault();
        if (best.hit is null) return null;
        var content = tokens.Count(t => t.Norm.Length > 1);
        // power and time words, or any number, mean a power / time command that merely mentions a page («restart after update»)
        if (tokens.Any(t => t.Norm.Any(char.IsDigit)) || Any(padded, PowerWords)) return null;
        // «تنظیمات» alone, or «برو به تنظیمات»: a page name with an opening word, or very little else
        if (!verb && content > 3) return null;
        if (!verb && Any(padded, OnWords) || !verb && Any(padded, OffWords)) return null;     // «میکروفون رو روشن کن» is not «open the microphone page»
        return Ready(new AssistantCommand { Intent = CommandIntent.OpenPage, Page = best.p.key });
    }

    // ---- helpers for the rest of the app --------------------------------------------------------------------------------

    public static string PageName(string key) => Pages.FirstOrDefault(p => p.key == key) is { key: not null } p ? L.T(p.fa, p.en) : key;

    public static IEnumerable<string> PageKeys => Pages.Where(p => p.key != "rgb" || Lights).Select(p => p.key);

    /// <summary>What Bidar can do, one line each, for «چه کارایی بلدی؟».</summary>
    public static string HelpText(bool english) => english
        ? string.Join("\n", "• Power: “shut down in an hour”, “sleep at 2:30”, “restart in 10 minutes”, “cancel”",
            "• Conditions: “shut down when the download ends”, “when Steam closes”, “if I'm idle for 20 minutes”",
            "• Reminders and focus: “remind me at 5pm to call mom”, “focus 25 minutes”",
            "• Screen: “night light on”, “night light 40%”, “sleep mode”",
            "• Pages: “open settings”, “go to DNS”, “show game tuning”",
            "• Overlay: “overlay on”, “overlay only in game”, “overlay bar”, “overlay size 120%”",
            "• Theme: “theme halloween”, “dark mode”, “light mode”",
            "• PC: “free ram”, “game mode on”, “cpu temp?”, “ram usage?”, “my ping?”, “system status”",
            "• Fun and facts: “tell me a joke”, “tell me a fact”, “give me a tip”, “cheer me up”, “calc 12*3”, “flip a coin”, “roll a dice”",
            "• About you: “my level”, “my tasks”, “how long has my PC been on?”, “disk space”",
            "• Handy: “make me a strong password”, “convert 5 km to miles”, “100 c to f”, “time in tokyo”, “days until nowruz”, “when should I sleep if I wake up at 7”, “random number between 1 and 50”, “choose between tea or coffee”, “battery level”",
            "• Anything else: “what time is it?”")
        : string.Join("\n", "• برق: «یه ساعت دیگه خاموش کن»، «ساعت ۲:۳۰ بخوابون»، «ده دقیقه دیگه ری‌استارت»، «لغو»",
            "• شرطی: «وقتی دانلود تموم شد خاموش کن»، «وقتی استیم بسته شد»، «اگه ۲۰ دقیقه بیکار بودم»",
            "• یادآوری و تمرکز: «ساعت ۵ یادم بنداز به مامان زنگ بزنم»، «۲۵ دقیقه تمرکز»",
            "• صفحه: «نور شب روشن»، «نور شب ۴۰٪»، «حالت شب‌خواب»",
            "• صفحه‌ها: «برو تنظیمات»، «دی‌ان‌اس رو باز کن»، «بهینه‌سازی بازی رو نشون بده»",
            "• اورلی: «اورلی رو روشن کن»، «اورلی فقط موقع بازی»، «اورلی نوار»، «اورلی اندازه ۱۲۰٪»",
            "• تم: «تم هالووین»، «حالت تاریک»، «حالت روشن»",
            "• سیستم: «رم رو خالی کن»، «حالت بازی روشن»، «دمای پردازنده چنده؟»، «مصرف رم؟»، «پینگم؟»، «وضعیت سیستم»",
            "• سرگرمی و اطلاعات: «یه جوک بگو»، «یه فکت بگو»، «یه نکته بگو»، «روحیه بده»، «۱۲*۳ حساب کن»، «شیر یا خط»، «تاس بنداز»",
            "• خودت: «سطحم چنده؟»، «کارای امروزم»، «سیستم چند وقته روشنه؟»، «فضای دیسک»",
            "• کارهای دم‌دستی: «یه پسورد قوی بساز»، «۵ کیلومتر چند مایله؟»، «۱۰۰ سانتیگراد به فارنهایت»، «ساعت توکیو چنده؟»، «تا نوروز چند روز مونده؟»، «اگه ۷ بیدار شم کی بخوابم؟»، «عدد تصادفی بین ۱ تا ۵۰»، «بین چای یا قهوه انتخاب کن»، «باتری چنده؟»",
            "• بقیه: «ساعت چنده؟»");
}
