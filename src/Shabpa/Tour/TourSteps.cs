using Shabpa.Core;

namespace Shabpa.Tour;

/// <summary>One stop of the guided tour. <see cref="Target"/> names what gets the spotlight (see <see cref="TourTargets"/>);
/// <see cref="Page"/> is the page that must be open for it ("" = leave as it is). When <see cref="ClickPage"/> is set the
/// person is asked to click the target (a sidebar item) and the tour goes on by itself once that page is open.</summary>
public sealed record TourStep(string Id, string Target, string Page, string TitleFa, string TitleEn, string TextFa, string TextEn,
    string ClickPage = "", string ClickFa = "", string ClickEn = "")
{
    public string Title => L.T(TitleFa, TitleEn);
    public string Text => L.T(TextFa, TextEn);
    public string Click => L.T(ClickFa, ClickEn);
    public bool NeedsClick => ClickPage.Length > 0;
}

/// <summary>The kinds of spotlight target a step can name. The window resolves them against its real controls
/// (<c>MainWindow.Tour.cs</c>); the tests check every name against MainWindow.xaml so a renamed menu item cannot silently break the tour.</summary>
public static class TourTargets
{
    public const string None = "";                // centred, no spotlight
    public const string Sidebar = "sidebar";      // the whole menu column
    public const string Theme = "theme";          // the dark / light button in the title bar
    public const string Language = "lang";        // the language chip under the menu
    public const string NavPrefix = "nav:";       // nav:system = the menu item that opens the page "system"
    public const string PagePrefix = "page:";     // page:1 = the second block of the open page
    public const string IdPrefix = "id:";         // id:tour.replay = the element of the open page with that AutomationId

    public static bool IsValidShape(string target) =>
        target is None or Sidebar or Theme or Language
        || target.StartsWith(NavPrefix, StringComparison.Ordinal) && target.Length > NavPrefix.Length
        || target.StartsWith(PagePrefix, StringComparison.Ordinal) && int.TryParse(target.AsSpan(PagePrefix.Length), out var n) && n >= 0
        || target.StartsWith(IdPrefix, StringComparison.Ordinal) && target.Length > IdPrefix.Length;
}

/// <summary>The tour itself, in order. The pages and menu items are the real ones of MainWindow (names in MainWindow.xaml, page keys in
/// MainWindow.xaml.cs); nothing here touches any UI, so the order and the texts are unit-tested.</summary>
public static class TourSteps
{
    /// <summary>The automation id of the "see the tour again" button on the settings page.</summary>
    public const string ReplayId = "tour.replay";

    public static readonly IReadOnlyList<TourStep> All =
    [
        new("welcome", TourTargets.None, "home",
            "خوش اومدی به شب‌پا", "Welcome to Shabpa",
            "یه تور کوتاه (حدود یک دقیقه) می‌گیریم تا ببینی هر چیزی کجاست. هر لحظه با Esc می‌تونی ردش کنی؛ بعداً هم از «تنظیمات» دوباره پیداش می‌کنی.",
            "A short tour (about a minute) to show you where everything is. Press Esc any time to skip it; you can find it again in Settings."),

        new("sidebar", TourTargets.Sidebar, "home",
            "منوی کناری، همه‌ی بخش‌ها", "The side menu, every section",
            "همه‌چیز از همین‌جا باز می‌شه. بخش‌ها گروه‌بندی شدن: سیستم و برق، بازی و شبکه، نگهداری و روزمره. با Ctrl و یه عدد هم می‌تونی سریع بپری.",
            "Everything opens from here. Sections are grouped: system and power, gaming and network, maintenance and everyday. Ctrl plus a number jumps there fast."),

        new("home", TourTargets.PagePrefix + "0", "home",
            "خانه، یک نگاه به همه‌چیز", "Home, everything at a glance",
            "کارت اصلی خونه: کار زمان‌بندی‌شده‌ی بعدی و شمارش معکوسش. پایین‌تر مصرف زنده‌ی سخت‌افزار و اقدام‌های سریع رو می‌بینی.",
            "The main card of Home: the next scheduled task and its countdown. Below it: live hardware load and quick actions."),

        new("system", TourTargets.NavPrefix + "system", "home",
            "کنترل سیستم، خاموشی زمان‌بندی‌شده", "System control, scheduled shutdown",
            "مهم‌ترین بخش شب‌پا اینه: بذاری سیستم خودش خاموش بشه، بخوابه یا ری‌استارت بشه.",
            "The heart of Shabpa: let the PC shut down, sleep or restart by itself.",
            ClickPage: "system", ClickFa: "روی «کنترل سیستم» کلیک کن", ClickEn: "Click “System control”"),

        new("system-tabs", TourTargets.PagePrefix + "0", "system",
            "تب‌های کنترل سیستم", "The System control tabs",
            "دستور برق (همین حالا یا هر شب)، خاموشی شرطی (مثلاً وقتی دانلود تموم شد)، مصرف برق، خواب و بیداری، بیداریاش، حافظه و تلگرام. هر کدوم یه ابزار جداست.",
            "Power command (now or every night), conditional shutdown (say, when a download ends), energy, sleep and wake, wake alarms, memory and Telegram. Each is its own tool."),

        new("system-power", TourTargets.PagePrefix + "1", "system",
            "همین حالا یا هر شب سر ساعت", "Right now, or every night",
            "اول کار رو انتخاب کن: خاموشی، خواب، قفل یا ری‌استارت. بعد زمانش: همین حالا، چند دقیقه‌ی دیگه یا هر شب سر ساعت. قبل از اجرا همیشه مهلت لغو داری.",
            "Pick the job first: shutdown, sleep, lock or restart. Then when: right now, in a few minutes or every night. You always get a moment to cancel."),

        new("comfort", TourTargets.PagePrefix + "0", "comfort",
            "نور شب و استراحت", "Night light & rest",
            "نور صفحه رو شب‌ها گرم و کم‌نور کن، هر چند وقت یه بار برای استراحت یادآوری بگیر و حالت شب‌خواب رو روشن کن. چشمات ممنونت می‌شن.",
            "Make the screen warm and dim at night, get break reminders and switch on sleep mode. Your eyes will thank you."),

        new("gaming", TourTargets.NavPrefix + "gaming", "comfort",
            "اورلی بازی", "Game overlay",
            "اطلاعات زنده‌ی سیستم (فریم، دما، مصرف) روی خود بازی؛ هر طور که دوست داری.",
            "Live system info (FPS, temperatures, load) drawn over your game, the way you like it.",
            ClickPage: "gaming", ClickFa: "روی «اورلی بازی» کلیک کن", ClickEn: "Click “Game overlay”"),

        new("gaming-page", TourTargets.PagePrefix + "0", "gaming",
            "ظاهر و جای اورلی", "Look and place of the overlay",
            "این‌جا انتخاب می‌کنی چی نشون داده بشه، کجای صفحه بشینه و چه رنگی داشته باشه. یه کلید میان‌بر هم برای روشن و خاموش کردنش داری.",
            "Choose what it shows, where it sits and its colour. There is also a hotkey to switch it on and off."),

        new("playtime", TourTargets.PagePrefix + "1", "playtime",
            "زمان بازی", "Playtime",
            "هر نوبت بازی با مدت، دما و مصرفش ثبت می‌شه؛ این کارت‌ها جمع زمان، تعداد، پربازی‌ترین و اینترنت مصرفی رو نشون می‌دن. بالاشون هم امروز، هفته یا ماه رو انتخاب می‌کنی. همه‌چیز فقط روی همین کامپیوتر می‌مونه.",
            "Every session is logged with its length, temperatures and load; these cards show total time, number of games, the most played and the internet used. Above them you pick today, week or month. It all stays on this PC."),

        new("dns", TourTargets.PagePrefix + "0", "dns",
            "دی‌ان‌اس", "DNS",
            "یه دی‌ان‌اس مناسب از لیست انتخاب کن، پینگش رو ببین و با یه کلیک روی کارت شبکه‌ت ثبتش کن. برای بازی و اینترنت سریع‌تر به درد می‌خوره.",
            "Pick a DNS from the list, check its ping and set it on your network card in one click. Handy for games and faster browsing."),

        new("health", TourTargets.PagePrefix + "0", "health",
            "سلامت و امنیت", "Health & security",
            "سلامت سیستم، امنیت و استارتاپ، به‌روزرسانی برنامه‌ها، درایورها و گزارش برای پشتیبانی؛ همه به زبان ساده. فقط نشون می‌ده و چیزی رو بی‌خبر تغییر نمی‌ده.",
            "System health, security and startup, app updates, drivers and a report for support, in plain words. It only shows; it never changes anything behind your back."),

        new("mic", TourTargets.PagePrefix + "0", "mic",
            "میکروفون", "Microphone",
            "نویزگیر زنده: صدای میکروفون رو همین الان تمیز می‌کنه (خش‌خش، فن، کیبورد) و چیزی ذخیره نمی‌شه. کنارش سطح صدا، قطع و وصل و تست نویز هست.",
            "The live noise suppressor cleans your microphone right now (hiss, fans, keyboard) and nothing is stored. Next to it: level, mute and a noise test."),

        new("weather", TourTargets.PagePrefix + "0", "weather",
            "آب و هوا", "Weather",
            "اسم شهرت رو بنویس و هوای الان و روزهای آینده رو با یه صحنه‌ی زنده ببین.",
            "Type your city and see the weather now and for the coming days, with a live scene."),

        new("theme", TourTargets.Theme, "weather",
            "تیره یا روشن", "Dark or light",
            "با این دکمه ظاهر برنامه همین الان عوض می‌شه، بدون ری‌استارت. زبان رو هم از دکمه‌ی پایین منو می‌تونی عوض کنی.",
            "This button switches the look right now, no restart. The language button under the menu changes the language."),

        new("settings", TourTargets.NavPrefix + "settings", "weather",
            "تنظیمات", "Settings",
            "زبان، تم، انیمیشن‌ها، شروع با ویندوز و خیلی چیزهای دیگه. این تور هم همین‌جا دوباره در دسترسه.",
            "Language, theme, animations, starting with Windows and much more. This tour is here too, whenever you want it again.",
            ClickPage: "settings", ClickFa: "روی «تنظیمات» کلیک کن", ClickEn: "Click “Settings”"),

        new("replay", TourTargets.IdPrefix + ReplayId, "settings",
            "دیدن دوباره‌ی تور", "See the tour again",
            "اگه یه جایی رو فراموش کردی، این دکمه تور رو از اول نشونت می‌ده.",
            "If you forget something, this button plays the tour again from the start."),

        new("support", TourTargets.PagePrefix + "0", "support",
            "پشتیبانی، و تمام!", "Support, and that is it!",
            "مشکل یا پیشنهادی داشتی همین‌جا بنویس و جوابش رو ببین. تورمون تموم شد؛ برو سراغ اولین زمان‌بندیت. شب‌ات به‌خیر.",
            "Got a problem or an idea? Write it here and read the reply. That was the tour: go set up your first schedule. Good night."),
    ];

    /// <summary>The step wants a page that is not the open one.</summary>
    public static bool NeedsPage(TourStep step, string activeKey) => step.Page.Length > 0 && !string.Equals(activeKey, step.Page, StringComparison.Ordinal);

    /// <summary>The page keys of MainWindow the steps may open or ask to be clicked.</summary>
    public static IEnumerable<string> PagesUsed => All.SelectMany(s => new[] { s.Page, s.ClickPage }).Where(p => p.Length > 0).Distinct();
}
